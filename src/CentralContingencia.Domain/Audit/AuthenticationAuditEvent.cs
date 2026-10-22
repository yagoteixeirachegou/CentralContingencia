namespace CentralContingencia.Domain.Audit;

public enum AuthenticationAction { LoginSucceeded, LoginFailed, LockedOut, Logout, PasswordChanged, UserCreated, AdminProvisioned }

public sealed class AuthenticationAuditEvent
{
    public Guid Id { get; private set; }
    public Guid? ActorId { get; private set; }
    public Guid? ResourceId { get; private set; }
    public AuthenticationAction Action { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string CorrelationId { get; private set; } = "";
    private AuthenticationAuditEvent() { }
    public AuthenticationAuditEvent(AuthenticationAction action, Guid? actorId, Guid? resourceId, string correlationId)
    {
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 128)
            throw new ArgumentException("Correlation ID inválido.", nameof(correlationId));
        Id = Guid.NewGuid();
        Action = action;
        ActorId = actorId;
        ResourceId = resourceId;
        OccurredAt = DateTimeOffset.UtcNow;
        CorrelationId = correlationId;
    }
}
