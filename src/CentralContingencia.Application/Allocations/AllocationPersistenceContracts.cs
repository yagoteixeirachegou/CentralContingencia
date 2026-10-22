using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Profiles;

namespace CentralContingencia.Application.Allocations;

public enum AllocationPersistenceFailure { NotFound, Conflict, Validation, Unavailable, Forbidden, AuditFailure }
public sealed record OperationalAuditContext(Guid ActorId, string CorrelationId);
public sealed class AllocationPersistenceException(AllocationPersistenceFailure failure)
    : Exception("Persistência de perfis/alocações recusada.")
{
    public AllocationPersistenceFailure Failure { get; } = failure;
}
public interface IOperationalProfileStore
{
    Task AddAsync(OperationalProfile profile, CancellationToken token, OperationalAuditContext? audit = null);
    Task<OperationalProfile?> FindAsync(Guid id, CancellationToken token);
    Task<ProfilePage> QueryAsync(ProfileQuery query, CancellationToken token);
    Task<OperationalProfile> UpdateAsync(Guid id, string? name, string? platform, CancellationToken token, OperationalAuditContext audit,
        bool platform1Specified = false, string? platform1 = null, bool platform2Specified = false, string? platform2 = null);
}
public sealed record AllocationReservation(Guid Id, Guid ProfileId, Guid ProxyId, Guid ActorId,
    Guid IdempotencyKey, DateTimeOffset? ReservationExpiresAt);
// Internal persistence boundary; authorization and HTTP use cases are not implemented here.
public interface IAllocationStore
{
    Task<ProxyAllocation?> FindAsync(Guid id, CancellationToken token);
    Task<IReadOnlyList<ProxyAllocation>> HistoryAsync(Guid profileId, CancellationToken token);
    Task<AllocationPage> QueryAsync(AllocationQuery query, CancellationToken token);
    Task<ProxyAllocation> ReserveAsync(AllocationReservation reservation, CancellationToken token, OperationalAuditContext? audit = null);
    Task ConfirmAsync(Guid id, CancellationToken token, OperationalAuditContext? audit = null);
    Task ReleaseAsync(Guid id, CancellationToken token, OperationalAuditContext? audit = null);
    Task ExpireAsync(Guid id, CancellationToken token);
    Task CloseProfileAsync(Guid profileId, CancellationToken token, OperationalAuditContext? audit = null);
}
