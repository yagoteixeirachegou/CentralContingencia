using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Domain.WorkQueue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CentralContingencia.Worker;

/// <summary>
/// Laço do worker: (remoto) obtém a configuração do servidor e devolve o que ficou preso de uma execução anterior; depois recupera leases vencidos
/// (modo local), reserva tarefas até o limite de concorrência e entrega cada uma a um <see cref="TaskRunner"/>.
/// Falhas de transporte/banco não derrubam o processo: espera com backoff exponencial, jitter e Retry-After e tenta de novo.
/// No desligamento para de reservar, cancela as execuções (que devolvem a tarefa à fila), aguarda o prazo de graça e avisa o servidor.
/// </summary>
public sealed class WorkerService(IServiceScopeFactory scopes, IEnumerable<ITaskExecutor> executors, WorkerOptions options, TimeProvider clock, ILogger<WorkerService> logger) : BackgroundService
{
    private readonly IReadOnlyDictionary<WorkTaskType, ITaskExecutor> executorsByType = executors.ToDictionary(e => e.Type);
    private string workerId = options.ResolveWorkerId();
    private int active;
    // Chave do claim em andamento: se a resposta se perder (falha de transporte), a próxima tentativa REUSA a chave e recebe de volta as tarefas já reservadas.
    private Guid? pendingClaimKey;
    private volatile bool stopping;
    private static readonly string Version = typeof(WorkerService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        logger.LogInformation("Worker iniciado (modo {Mode}, concorrência {MaxConcurrency}, tipos {Types}).", options.Mode, options.MaxConcurrency, string.Join(",", executorsByType.Keys));
        using var slots = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);
        var running = new List<Task>();
        var presenceInterval = options.Heartbeat;
        var failures = 0;

        // Handshake: sem configuração aceita pelo servidor o worker não reserva nada.
        while (!stop.IsCancellationRequested)
        {
            try { presenceInterval = await StartAsync(stop) ?? presenceInterval; break; }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                failures++;
                var delay = Backoff(failures, error);
                logger.LogWarning("Handshake com a API falhou ({Failure}); nova tentativa em {DelaySeconds:0}s.", Describe(error), delay.TotalSeconds);
                try { await Task.Delay(delay, clock, stop); } catch (OperationCanceledException) { break; }
            }
        }
        failures = 0;

        var presence = PresenceLoopAsync(presenceInterval, stop);
        var nextRecovery = clock.GetUtcNow();
        while (!stop.IsCancellationRequested)
        {
            var delay = options.PollInterval;
            try
            {
                if (options.Mode == WorkerMode.LocalDatabase && clock.GetUtcNow() >= nextRecovery)
                {
                    await using var recoveryScope = scopes.CreateAsyncScope();
                    var recovered = await recoveryScope.ServiceProvider.GetRequiredService<IWorkQueueService>().RecoverExpiredAsync(stop);
                    if (recovered > 0) logger.LogWarning("{Count} tarefa(s) com lease vencido recuperada(s).", recovered);
                    nextRecovery = clock.GetUtcNow() + options.RecoveryInterval;
                }
                running.RemoveAll(t => t.IsCompleted);
                var free = slots.CurrentCount;
                if (free > 0)
                {
                    await using var claimScope = scopes.CreateAsyncScope();
                    var claimKey = pendingClaimKey ??= Guid.NewGuid();
                    var claimed = await claimScope.ServiceProvider.GetRequiredService<IWorkQueueService>().ClaimAsync(workerId, executorsByType.Keys.ToList(), free, claimKey, stop);
                    pendingClaimKey = null;
                    foreach (var task in claimed)
                    {
                        await slots.WaitAsync(stop);
                        Interlocked.Increment(ref active);
                        running.Add(RunAsync(task, slots, stop));
                    }
                    if (claimed.Count > 0) delay = TimeSpan.Zero;
                }
                failures = 0;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                // Falha indeterminada (a reserva pode ter ocorrido): mantém a chave; qualquer outra resposta definitiva descarta.
                if (error is not WorkQueueException { Failure: WorkQueueFailure.Unavailable }) pendingClaimKey = null;
                failures++;
                delay = Backoff(failures, error);
                // Só o tipo/código: mensagens de exceção de rede ou banco podem conter host e usuário.
                logger.LogWarning("Falha ao consultar a fila ({Failure}); nova tentativa em {DelaySeconds:0}s.", Describe(error), delay.TotalSeconds);
            }
            try { if (delay > TimeSpan.Zero) await Task.Delay(delay, clock, stop); }
            catch (OperationCanceledException) { break; }
        }

        stopping = true;
        logger.LogInformation("Worker encerrando; aguardando {Count} tarefa(s) em andamento.", running.Count(t => !t.IsCompleted));
        try { await Task.WhenAll(running).WaitAsync(options.ShutdownGrace); }
        catch (TimeoutException) { logger.LogWarning("Prazo de desligamento esgotado; tarefas restantes serão recuperadas pelo vencimento do lease."); }
        catch (Exception error) { logger.LogWarning("Falha ao aguardar tarefas ({ErrorType}).", error.GetType().Name); }
        try { await presence.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        await GoodbyeAsync();
    }

    /// <returns>Intervalo de presença acordado com o servidor (remoto) ou null (local).</returns>
    private async Task<TimeSpan?> StartAsync(CancellationToken stop)
    {
        await using var scope = scopes.CreateAsyncScope();
        var config = await Session(scope).GetConfigAsync(stop);
        TimeSpan? interval = null;
        if (config is not null)
        {
            workerId = config.ExecutorId.ToString("D");
            // O servidor manda: o heartbeat de tarefa nunca passa de um terço do lease que ele concede.
            options.HeartbeatSeconds = Math.Min(options.HeartbeatSeconds, Math.Max(1, config.LeaseSeconds / 3));
            options.MaxConcurrency = Math.Min(options.MaxConcurrency, Math.Max(1, config.MaxClaim));
            interval = TimeSpan.FromSeconds(Math.Max(1, config.HeartbeatSeconds));
            logger.LogInformation("Executor {ExecutorName} ({ExecutorId}) autenticado; lease {LeaseSeconds}s, heartbeat de tarefa {HeartbeatSeconds}s.", config.Name, workerId, config.LeaseSeconds, options.HeartbeatSeconds);
        }
        // Reinício: o que este executor ainda segura pertence a uma execução anterior que não existe mais. Devolve sem consumir tentativa.
        var queue = scope.ServiceProvider.GetRequiredService<IWorkQueueService>();
        if (config is not null)
            foreach (var held in await queue.HeldAsync(workerId, stop))
            {
                try { await queue.ReleaseAsync(workerId, held.Id, held.Attempt, stop); logger.LogInformation("Tarefa {TaskId} devolvida à fila após reinício.", held.Id); }
                catch (WorkQueueException error) when (error.Failure is WorkQueueFailure.LeaseLost or WorkQueueFailure.Conflict or WorkQueueFailure.NotFound) { }
            }
        return interval;
    }

    private async Task PresenceLoopAsync(TimeSpan interval, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await Session(scope).HeartbeatAsync(stopping ? ExecutorState.Draining : Volatile.Read(ref active) == 0 ? ExecutorState.Idle : ExecutorState.Busy, Version, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogWarning("Heartbeat do executor não registrado ({Failure}).", Describe(error)); }
            try { await Task.Delay(Jitter(interval, 0.1), clock, stop); } catch (OperationCanceledException) { return; }
        }
    }

    private async Task GoodbyeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await Session(scope).ShutdownAsync(deadline.Token);
        }
        catch (Exception error) { logger.LogWarning("Encerramento não comunicado à API ({Failure}).", Describe(error)); }
    }

    private async Task RunAsync(ClaimedWorkTask task, SemaphoreSlim slots, CancellationToken stop)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var runner = new TaskRunner(scope.ServiceProvider.GetRequiredService<IWorkQueueService>(), executorsByType, workerId, options, clock, logger);
            await runner.RunAsync(task, stop);
        }
        catch (Exception error)
        {
            logger.LogError("Falha inesperada ao processar a tarefa {TaskId} ({ErrorType}).", task.Id, error.GetType().Name);
        }
        finally { Interlocked.Decrement(ref active); slots.Release(); }
    }

    private static IExecutorSession Session(AsyncServiceScope scope) => scope.ServiceProvider.GetService<IExecutorSession>() ?? new NullExecutorSession();

    /// <summary>Backoff exponencial limitado, com jitter de ±20%; respeita Retry-After do servidor e espera o máximo quando a credencial é recusada.</summary>
    private TimeSpan Backoff(int failures, Exception error)
    {
        var max = TimeSpan.FromSeconds(options.ErrorBackoffMaxSeconds);
        var delay = TimeSpan.FromSeconds(Math.Min(max.TotalSeconds, Math.Pow(2, Math.Min(failures, 10))));
        if (error is WorkQueueException { Failure: WorkQueueFailure.Unauthorized }) delay = max;
        if (error is WorkQueueException { RetryAfter: { } retryAfter } && retryAfter > delay) delay = retryAfter;
        return Jitter(delay, 0.2);
    }

    private static TimeSpan Jitter(TimeSpan value, double fraction) => value * (1 + (Random.Shared.NextDouble() * 2 - 1) * fraction);

    private static string Describe(Exception error) => error is WorkQueueException known ? known.Detail is null ? known.Failure.ToString() : $"{known.Failure}/{known.Detail}" : error.GetType().Name;
}
