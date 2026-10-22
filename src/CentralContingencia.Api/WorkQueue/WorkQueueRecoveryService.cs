using CentralContingencia.Application.WorkQueue;

namespace CentralContingencia.Api.WorkQueue;

/// <summary>
/// Recupera tarefas cujo executor sumiu (lease vencido). Com workers remotos, que não têm acesso ao banco, esta é a única fonte de recuperação.
/// Vários processos da API podem rodar em paralelo: a recuperação usa SKIP LOCKED. Desligável com <c>WorkQueue:RecoveryIntervalSeconds</c> = 0.
/// </summary>
public sealed class WorkQueueRecoveryService(IServiceScopeFactory scopes, TimeSpan interval, ILogger<WorkQueueRecoveryService> logger, TimeSpan? runTimeout = null) : BackgroundService
{
    public const string Setting = "WorkQueue:RecoveryIntervalSeconds";
    public const int DefaultSeconds = 15;
    public static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stopping))
            {
                try
                {
                    // Prazo por rodada: um banco pendurado não pode travar o laço para sempre.
                    using var run = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                    run.CancelAfter(runTimeout ?? RunTimeout);
                    await using var scope = scopes.CreateAsyncScope();
                    var recovered = await scope.ServiceProvider.GetRequiredService<IWorkQueueService>().RecoverExpiredAsync(run.Token);
                    if (recovered > 0) logger.LogWarning("{Count} tarefa(s) com lease vencido recuperada(s).", recovered);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return; }
                catch (OperationCanceledException) { logger.LogWarning("Recuperação de leases excedeu {Seconds}s; tentando de novo no próximo ciclo.", (runTimeout ?? RunTimeout).TotalSeconds); }
                catch (Exception error) { logger.LogWarning("Recuperação de leases falhou ({Failure}).", error is WorkQueueException known ? known.Failure.ToString() : error.GetType().Name); }
            }
        }
        catch (OperationCanceledException) { }
    }
}
