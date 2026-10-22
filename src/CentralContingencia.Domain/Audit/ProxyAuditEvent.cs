namespace CentralContingencia.Domain.Audit;

public enum ProxyAuditAction { Created, Updated, Revealed }

public sealed class ProxyAuditEvent
{
    public Guid Id { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid ProxyId { get; private set; }
    public ProxyAuditAction Action { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string CorrelationId { get; private set; } = "";
    private ProxyAuditEvent() { }
    public ProxyAuditEvent(Guid actorId, Guid proxyId, ProxyAuditAction action, DateTimeOffset now, string correlationId)
    {
        if (actorId == Guid.Empty || proxyId == Guid.Empty || !Enum.IsDefined(action) ||
            string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 128)
            throw new ArgumentException("Evento de auditoria inválido.");
        Id = Guid.NewGuid(); ActorId = actorId; ProxyId = proxyId; Action = action;
        OccurredAt = now.ToUniversalTime(); CorrelationId = correlationId;
    }
}
