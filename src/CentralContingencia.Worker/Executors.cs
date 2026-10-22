using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Domain.WorkQueue;

namespace CentralContingencia.Worker;

public sealed record TaskExecutionContext(Guid TaskId, WorkTaskType Type, int PayloadVersion, string Payload, int Attempt, int MaxAttempts, string CorrelationId);

/// <summary>Resultado de uma execução. Falhas carregam código estável e mensagem curta (ambos sanitizados antes de persistir).</summary>
public abstract record TaskOutcome
{
    public sealed record Success(object? Result) : TaskOutcome;
    public sealed record Failure(string Code, string Message, bool Transient) : TaskOutcome;
}

public interface ITaskExecutor
{
    WorkTaskType Type { get; }
    /// <summary>Deve respeitar o token: ele é cancelado em cancelamento solicitado, lease perdido e desligamento do worker.</summary>
    Task<TaskOutcome> ExecuteAsync(TaskExecutionContext context, CancellationToken token);
}

public sealed record SimulatedResult(int StepsCompleted, int Attempt, string Behavior);

/// <summary>
/// Executor fictício: espera, produz resultado tipado e simula falhas (transitória, permanente, até certa tentativa).
/// Não integra AdsPower, não abre navegador, não executa agentes externos nem toca em redes sociais.
/// </summary>
public sealed class SimulatedProcessingExecutor(TimeProvider clock) : ITaskExecutor
{
    public WorkTaskType Type => WorkTaskType.SimulatedProcessing;

    public async Task<TaskOutcome> ExecuteAsync(TaskExecutionContext context, CancellationToken token)
    {
        SimulatedPayload payload;
        try { payload = SimulatedPayload.FromJson(context.Payload); }
        catch (WorkTaskPayloadException) { return new TaskOutcome.Failure("invalid_payload", "Payload inválido.", false); }

        var completed = 0;
        for (var step = 0; step < payload.Steps; step++)
        {
            token.ThrowIfCancellationRequested();
            if (payload.StepDelayMs > 0) await Task.Delay(TimeSpan.FromMilliseconds(payload.StepDelayMs), clock, token);
            completed++;
        }
        return payload.Behavior switch
        {
            SimulatedBehavior.FailTransient => new TaskOutcome.Failure("simulated_transient", "Falha transitória simulada.", true),
            SimulatedBehavior.FailPermanent => new TaskOutcome.Failure("simulated_permanent", "Falha permanente simulada.", false),
            SimulatedBehavior.FailUntilAttempt when context.Attempt < payload.SucceedOnAttempt => new TaskOutcome.Failure("simulated_transient", "Falha transitória simulada.", true),
            _ => new TaskOutcome.Success(new SimulatedResult(completed, context.Attempt, payload.Behavior.ToString())),
        };
    }
}
