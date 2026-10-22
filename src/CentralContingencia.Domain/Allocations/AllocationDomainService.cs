using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.Domain.Allocations;

// The caller must supply the complete relevant snapshot and persist changes atomically.
// Database uniqueness is still required to protect concurrent transactions.
public sealed class AllocationDomainService
{
    public ProxyAllocation Reserve(Guid id, OperationalProfile profile, Proxy proxy, Guid actorId, Guid key,
        DateTimeOffset now, DateTimeOffset reservationExpiresAt, IReadOnlyCollection<ProxyAllocation> allocations)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(proxy); ArgumentNullException.ThrowIfNull(allocations);
        profile.ValidateTime(now);
        if (allocations.Any(a => (a.ProfileId == profile.Id || a.ProxyId == proxy.Id) && now < a.UpdatedAt))
            throw new ArgumentException("Timestamp anterior ao histórico de alocações.");
        if (now < proxy.UpdatedAt || profile.Status != ProfileStatus.Active || !proxy.IsAvailable(now, allocations.Any(a => a.ProxyId == proxy.Id && a.IsActive)))
            throw new InvalidOperationException("Perfil ou proxy indisponível para alocação.");
        if (allocations.Any(a => a.ProfileId == profile.Id && a.IsActive)) throw new InvalidOperationException("Perfil já possui alocação ativa.");
        if (allocations.Any(a => a.Id == id || a.ActorId == actorId && a.IdempotencyKey == key))
            throw new InvalidOperationException("Identificador ou chave de alocação já utilizado.");
        if (reservationExpiresAt > proxy.ExpiresAt) throw new ArgumentException("Reserva ultrapassa validade da proxy.");
        return new(id, proxy.Id, profile.Id, actorId, key, now, reservationExpiresAt);
    }
    public void Confirm(ProxyAllocation allocation, OperationalProfile profile, Proxy proxy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(allocation); ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(proxy);
        profile.ValidateTime(now);
        allocation.ValidateTime(now);
        if (allocation.ProfileId != profile.Id || allocation.ProxyId != proxy.Id || profile.Status != ProfileStatus.Active)
            throw new InvalidOperationException("Recursos da alocação inválidos.");
        // A replay acknowledges the existing confirmation; it does not allocate again.
        if (allocation.Status == AllocationStatus.Confirmed) return;
        if (now < proxy.UpdatedAt || !proxy.IsAvailable(now, false))
            throw new InvalidOperationException("Recursos da alocação inválidos ou indisponíveis.");
        allocation.Confirm(now);
    }
    public void Release(ProxyAllocation allocation, DateTimeOffset now)
    { ArgumentNullException.ThrowIfNull(allocation); allocation.End(now, AllocationEndReason.Released); }
    public void ExpireReservation(ProxyAllocation allocation, DateTimeOffset now)
    { ArgumentNullException.ThrowIfNull(allocation); allocation.End(now, AllocationEndReason.ReservationExpired); }
    public void CloseProfile(OperationalProfile profile, IReadOnlyCollection<ProxyAllocation> allocations, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(allocations);
        profile.ValidateTime(now);
        var related = allocations.Where(a => a.ProfileId == profile.Id).ToArray();
        var active = related.Where(a => a.IsActive).ToArray();
        if (active.Length > 1 || profile.Status == ProfileStatus.Closed && active.Length != 0)
            throw new InvalidOperationException("Estado de alocações do perfil inconsistente.");
        // Validate all changes before mutating either entity.
        foreach (var allocation in related) allocation.ValidateTime(now);
        foreach (var allocation in active) allocation.End(now, AllocationEndReason.ProfileClosed);
        profile.Close(now);
    }
}
