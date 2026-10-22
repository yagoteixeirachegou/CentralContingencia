using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.Domain.Allocations;

public enum AllocationStatus { Reserved, Confirmed, Released, Expired }
public enum AllocationEndReason { Released, ProfileClosed, ReservationExpired }

public sealed class ProxyAllocation
{
    public Guid Id { get; private set; }
    public Guid ProxyId { get; private set; }
    public Guid ProfileId { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public AllocationStatus Status { get; private set; } = AllocationStatus.Reserved;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset ReservationExpiresAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public AllocationEndReason? EndReason { get; private set; }
    // Expiration is an explicit transition, never an implicit release of exclusivity.
    public bool IsActive => Status is AllocationStatus.Reserved or AllocationStatus.Confirmed;
    private ProxyAllocation() { }
    internal ProxyAllocation(Guid id, Guid proxyId, Guid profileId, Guid actorId, Guid key, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        if (id == Guid.Empty || proxyId == Guid.Empty || profileId == Guid.Empty || actorId == Guid.Empty || key == Guid.Empty)
            throw new ArgumentException("Identificadores de alocação inválidos.");
        ProxyRules.ValidateTimestamp(now); ProxyRules.ValidateTimestamp(expiresAt);
        if (expiresAt <= now) throw new ArgumentException("Expiração de reserva inválida.");
        Id = id; ProxyId = proxyId; ProfileId = profileId; ActorId = actorId; IdempotencyKey = key;
        CreatedAt = UpdatedAt = now.ToUniversalTime(); ReservationExpiresAt = expiresAt.ToUniversalTime();
    }
    internal void ValidateTime(DateTimeOffset now)
    {
        ProxyRules.ValidateTimestamp(now);
        if (now < UpdatedAt) throw new ArgumentException("Timestamp de alocação inválido.");
    }
    internal void Confirm(DateTimeOffset now)
    {
        ValidateTime(now);
        if (Status != AllocationStatus.Reserved || now >= ReservationExpiresAt)
            throw new InvalidOperationException("Reserva não pode ser confirmada.");
        Status = AllocationStatus.Confirmed; ConfirmedAt = UpdatedAt = now.ToUniversalTime();
    }
    internal void End(DateTimeOffset now, AllocationEndReason reason)
    {
        ValidateTime(now);
        if (!IsActive) throw new InvalidOperationException("Alocação já finalizada.");
        if (reason == AllocationEndReason.ReservationExpired && (Status != AllocationStatus.Reserved || now < ReservationExpiresAt))
            throw new InvalidOperationException("Reserva ainda não pode expirar.");
        Status = reason == AllocationEndReason.ReservationExpired ? AllocationStatus.Expired : AllocationStatus.Released;
        EndReason = reason; EndedAt = UpdatedAt = now.ToUniversalTime();
    }
    public override string ToString() => $"ProxyAllocation {Id}";
}
