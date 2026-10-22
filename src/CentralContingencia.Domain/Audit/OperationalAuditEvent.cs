using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.Domain.Audit;

public enum OperationalAction { ProfileCreated, ProfileUpdated, ProfileClosed, AllocationReserved, AllocationConfirmed, AllocationReleased, AllocationExpired }
public sealed class OperationalAuditEvent
{
    public Guid Id { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid ProfileId { get; private set; }
    public Guid? AllocationId { get; private set; }
    public OperationalAction Action { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string CorrelationId { get; private set; } = "";
    private OperationalAuditEvent() { }
    public OperationalAuditEvent(Guid actorId, Guid profileId, Guid? allocationId, OperationalAction action, DateTimeOffset occurredAt, string correlationId)
    {
        if (actorId == Guid.Empty || profileId == Guid.Empty || allocationId == Guid.Empty || !Enum.IsDefined(action) ||
            (action is OperationalAction.AllocationReserved or OperationalAction.AllocationConfirmed or OperationalAction.AllocationReleased or OperationalAction.AllocationExpired) != allocationId.HasValue)
            throw new ArgumentException("Evento operacional inválido.");
        ProxyRules.ValidateTimestamp(occurredAt); ProxyRules.ValidateText(correlationId, 128);
        Id = Guid.NewGuid(); ActorId = actorId; ProfileId = profileId; AllocationId = allocationId;
        Action = action; OccurredAt = occurredAt.ToUniversalTime(); CorrelationId = correlationId;
    }
    public override string ToString() => $"OperationalAuditEvent {Id}";
}
