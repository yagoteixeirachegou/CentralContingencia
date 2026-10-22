using System.Text.Json;
using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Domain.WorkQueue;
using Microsoft.Extensions.Logging;

namespace CentralContingencia.Worker;

/// <summary>
/// Executa UMA tarefa já reservada: heartbeat em paralelo, execução, e relato do desfecho provando o lease.
/// Nunca registra payload, resultado nem mensagens de exceção (apenas ids, tipo, tentativa e códigos).
/// </summary>
public sealed class TaskRunner(IWorkQueueService queue, IReadOnlyDictionary<WorkTaskType, ITaskExecutor> executors, string workerId, WorkerOptions options,
    TimeProvider clock, ILogger logger)
{
    public const int MaxResultBytes = 4096;
    private static readonly JsonSerializerOptions ResultJson = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(ClaimedWorkTask task, CancellationToken stopping)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["TaskId"] = task.Id, ["CorrelationId"] = task.CorrelationId, ["WorkerId"] = workerId });
        logger.LogInformation("Tarefa {Type} iniciada (tentativa {Attempt}/{MaxAttempts}).", task.Type, task.Attempt, task.MaxAttempts);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var state = new HeartbeatState();
        using var heartbeatStop = new CancellationTokenSource();
        var heartbeat = HeartbeatLoopAsync(task, state, execution, heartbeatStop.Token);

        TaskOutcome? outcome = null;
        try
        {
            outcome = executors.TryGetValue(task.Type, out var executor)
                ? await executor.ExecuteAsync(new(task.Id, task.Type, task.PayloadVersion, task.Payload, task.Attempt, task.MaxAttempts, task.CorrelationId), execution.Token)
                : new TaskOutcome.Failure("executor_missing", "Sem executor para o tipo.", false);
        }
        catch (OperationCanceledException) when (execution.IsCancellationRequested) { }
        catch (Exception error)
        {
            logger.LogWarning("Executor falhou com {ErrorType}.", error.GetType().Name);
            outcome = new TaskOutcome.Failure("executor_error", "Erro inesperado no executor.", true);
        }
        finally
        {
            await heartbeatStop.CancelAsync();
            try { await heartbeat; } catch (OperationCanceledException) { }
        }
        await ReportAsync(task, outcome, state, stopping.IsCancellationRequested);
    }

    private async Task ReportAsync(ClaimedWorkTask task, TaskOutcome? outcome, HeartbeatState state, bool shuttingDown)
    {
        if (state.LeaseLost)
        {
            logger.LogWarning("Lease perdido: o resultado desta tentativa foi descartado.");
            return;
        }
        // O relato usa um prazo próprio: o desligamento já cancelou o token do executor, mas o desfecho precisa ser gravado.
        using var deadline = new CancellationTokenSource(options.ShutdownGrace);
        try
        {
            await WithRetry(async () =>
            {
                switch (outcome)
                {
                    case TaskOutcome.Success success:
                        if (success.Result is null || Serialize(success.Result) is not null)
                        {
                            var json = success.Result is null ? null : Serialize(success.Result);
                            await queue.CompleteAsync(workerId, task.Id, task.Attempt, json, deadline.Token);
                            logger.LogInformation("Tarefa concluída com sucesso.");
                        }
                        else
                        {
                            var status = await queue.FailAsync(workerId, task.Id, task.Attempt, "result_too_large", "Resultado acima do limite.", false, deadline.Token);
                            logger.LogWarning("Resultado rejeitado; tarefa em {Status}.", status);
                        }
                        break;
                    case null when state.CancelRequested:
                        await queue.ConfirmCancelledAsync(workerId, task.Id, task.Attempt, deadline.Token);
                        logger.LogInformation("Tarefa cancelada a pedido.");
                        break;
                    case null when shuttingDown:
                        await queue.ReleaseAsync(workerId, task.Id, task.Attempt, deadline.Token);
                        logger.LogInformation("Tarefa devolvida à fila no desligamento.");
                        break;
                    case TaskOutcome.Failure failure when state.CancelRequested:
                        await queue.ConfirmCancelledAsync(workerId, task.Id, task.Attempt, deadline.Token);
                        logger.LogInformation("Tarefa cancelada a pedido (falha de código {Code} ignorada).", failure.Code);
                        break;
                    case TaskOutcome.Failure failure:
                        var next = await queue.FailAsync(workerId, task.Id, task.Attempt, failure.Code, failure.Message, failure.Transient, deadline.Token);
                        logger.LogWarning("Tarefa falhou com código {Code}; novo estado {Status}.", failure.Code, next);
                        break;
                    default:
                        // Sem desfecho nem motivo conhecido: devolve sem contar a tentativa.
                        await queue.ReleaseAsync(workerId, task.Id, task.Attempt, deadline.Token);
                        break;
                }
            }, deadline.Token);
        }
        catch (WorkQueueException error) when (error.Failure is WorkQueueFailure.LeaseLost or WorkQueueFailure.Conflict)
        {
            logger.LogWarning("Lease perdido ao relatar o desfecho; a fila fará a recuperação.");
        }
        catch (Exception error) when (error is WorkQueueException or OperationCanceledException)
        {
            // O lease vencerá e a recuperação devolve a tarefa à fila (entrega ao-menos-uma-vez).
            logger.LogError("Não foi possível relatar o desfecho ({ErrorType}); a recuperação por lease tratará a tarefa.", error.GetType().Name);
        }
    }

    private async Task HeartbeatLoopAsync(ClaimedWorkTask task, HeartbeatState state, CancellationTokenSource execution, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try { await Task.Delay(options.Heartbeat, clock, stop); }
            catch (OperationCanceledException) { return; }
            try
            {
                if (await queue.HeartbeatAsync(workerId, task.Id, task.Attempt, stop) && !state.CancelRequested)
                {
                    state.CancelRequested = true;
                    logger.LogInformation("Cancelamento solicitado; interrompendo a execução.");
                    await execution.CancelAsync();
                }
            }
            catch (OperationCanceledException) { return; }
            catch (WorkQueueException error) when (error.Failure is WorkQueueFailure.LeaseLost or WorkQueueFailure.Conflict or WorkQueueFailure.NotFound)
            {
                state.LeaseLost = true;
                await execution.CancelAsync();
                return;
            }
            catch (WorkQueueException error)
            {
                // Banco indisponível: tenta de novo no próximo ciclo; se o lease vencer, o relato final será recusado.
                logger.LogWarning("Heartbeat não registrado ({Failure}).", error.Failure);
            }
        }
    }

    private static async Task WithRetry(Func<Task> action, CancellationToken token)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { await action(); return; }
            catch (WorkQueueException error) when (error.Failure == WorkQueueFailure.Unavailable && attempt < 4)
            {
                // Retry-After do servidor (limitado a 10 s aqui) ou backoff curto com jitter.
                var wait = error.RetryAfter is { } asked ? TimeSpan.FromSeconds(Math.Min(10, asked.TotalSeconds)) : TimeSpan.FromMilliseconds(200 * attempt * (0.8 + Random.Shared.NextDouble() * 0.4));
                await Task.Delay(wait, token);
            }
        }
    }

    private static string? Serialize(object? result)
    {
        if (result is null) return null;
        var json = JsonSerializer.Serialize(result, ResultJson);
        return System.Text.Encoding.UTF8.GetByteCount(json) <= MaxResultBytes ? json : null;
    }

    private sealed class HeartbeatState
    {
        public volatile bool CancelRequested;
        public volatile bool LeaseLost;
    }
}
