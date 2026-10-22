namespace CentralContingencia.Domain.WorkQueue;

public enum WorkTaskAction { Created, Claimed, Succeeded, Failed, RetryScheduled, DeadLettered, CancelRequested, Cancelled, LeaseExpired, Released }

/// <summary>Auditoria da fila. Guarda apenas ids, ação e código sanitizado: nunca payload, resultado ou mensagem livre. Heartbeats não geram evento.</summary>
public sealed class WorkTaskEvent
{
    /// <summary>Ordem total dos eventos (identidade do banco); OccurredAt pode empatar.</summary>
    public long Sequence { get; private set; }
    public Guid Id { get; private set; }
    public Guid TaskId { get; private set; }
    public WorkTaskAction Action { get; private set; }
    public Guid? ActorId { get; private set; }
    public string? WorkerId { get; private set; }
    public int Attempt { get; private set; }
    public string? Code { get; private set; }
    public string CorrelationId { get; private set; } = "";
    public DateTimeOffset OccurredAt { get; private set; }
    private WorkTaskEvent() { }
    public static WorkTaskEvent Create(WorkTask task, WorkTaskAction action, Guid? actorId, string? workerId, DateTimeOffset now, string? code = null) => new()
    {
        Id = Guid.NewGuid(), TaskId = task.Id, Action = action, ActorId = actorId, WorkerId = workerId, Attempt = task.AttemptCount,
        Code = code is null ? null : WorkErrorSanitizer.Code(code), CorrelationId = task.CorrelationId, OccurredAt = now.ToUniversalTime(),
    };
    public override string ToString() => $"WorkTaskEvent {Id}";
}
