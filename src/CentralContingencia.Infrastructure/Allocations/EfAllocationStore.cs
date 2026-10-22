using CentralContingencia.Application.Allocations;
using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using CentralContingencia.Domain.Audit;
using System.Linq.Expressions;

namespace CentralContingencia.Infrastructure.Allocations;

public sealed class EfAllocationStore(DbContextOptions<CentralContingenciaDbContext> options, TimeProvider clock, ReservationOptions? reservationOptions = null) : IAllocationStore
{
    private readonly AllocationDomainService domain = new();
    private readonly ReservationOptions policy = reservationOptions ?? new();
    // Reserved system principal, not an Identity account. No credentials or impersonation.
    private static readonly Guid ExpiryActor = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public async Task<ProxyAllocation?> FindAsync(Guid id, CancellationToken token)
    {
        await using var context = new CentralContingenciaDbContext(options);
        try { return await context.ProxyAllocations.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, token); }
        catch (NpgsqlException error) { throw AllocationPersistenceErrors.Translate(error); }
    }
    public async Task<IReadOnlyList<ProxyAllocation>> HistoryAsync(Guid profileId, CancellationToken token)
    {
        await using var context = new CentralContingenciaDbContext(options);
        try { return await context.ProxyAllocations.AsNoTracking().Where(a => a.ProfileId == profileId).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToArrayAsync(token); }
        catch (NpgsqlException error) { throw AllocationPersistenceErrors.Translate(error); }
    }
    public async Task<AllocationPage> QueryAsync(AllocationQuery query, CancellationToken token)
    {
        await using var context = new CentralContingenciaDbContext(options);
        try
        {
            var rows = context.ProxyAllocations.AsNoTracking();
            if (query.ProfileId.HasValue) rows = rows.Where(a => a.ProfileId == query.ProfileId);
            if (query.ProxyId.HasValue) rows = rows.Where(a => a.ProxyId == query.ProxyId);
            if (query.Status is not null) { var status = Enum.Parse<AllocationStatus>(query.Status); rows = rows.Where(a => a.Status == status); }
            var total = await rows.CountAsync(token);
            IOrderedQueryable<ProxyAllocation> Order<T>(Expression<Func<ProxyAllocation,T>> key) => query.Direction == "asc" ? rows.OrderBy(key) : rows.OrderByDescending(key);
            var sorted = query.Sort switch { "status" => Order(a => a.Status), "updatedAt" => Order(a => a.UpdatedAt), _ => Order(a => a.CreatedAt) };
            var items = await sorted.ThenBy(a => a.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(token);
            return new(items.Select(OperationalManagementService.Response).ToArray(), query.Page, query.PageSize, total);
        }
        catch (NpgsqlException error) { throw AllocationPersistenceErrors.Translate(error); }
    }
    public Task<ProxyAllocation> ReserveAsync(AllocationReservation request, CancellationToken token, OperationalAuditContext? audit = null) => Execute(async context =>
    {
        var profile = await LockProfile(context, request.ProfileId, token);
        var proxy = await LockProxy(context, request.ProxyId, token);
        var now = clock.GetUtcNow();
        var deadline = policy.Deadline(now, proxy.ExpiresAt, request.ReservationExpiresAt);
        await RecoverExpired(context, now, request.ProfileId, request.ProxyId, token);
        // Free the partial unique indexes before inserting; both saves share the transaction.
        await context.SaveChangesAsync(token);
        var snapshot = await context.ProxyAllocations.Where(a => a.ProfileId == request.ProfileId || a.ProxyId == request.ProxyId ||
            a.Id == request.Id || a.ActorId == request.ActorId && a.IdempotencyKey == request.IdempotencyKey).ToArrayAsync(token);
        var allocation = AllocationPersistenceErrors.Domain(() => domain.Reserve(request.Id, profile, proxy, request.ActorId,
            request.IdempotencyKey, now, deadline, snapshot));
        context.ProxyAllocations.Add(allocation);
        if (audit is not null) context.OperationalAuditEvents.Add(new(audit.ActorId, allocation.ProfileId, allocation.Id, OperationalAction.AllocationReserved, allocation.CreatedAt, audit.CorrelationId));
        return allocation;
    }, token);
    public Task ConfirmAsync(Guid id, CancellationToken token, OperationalAuditContext? audit = null) => Change(id, AllocationChange.Confirm, token, audit);
    public Task ReleaseAsync(Guid id, CancellationToken token, OperationalAuditContext? audit = null) => Change(id, AllocationChange.Release, token, audit);
    public Task ExpireAsync(Guid id, CancellationToken token) => Change(id, AllocationChange.Expire, token, null);
    public async Task CloseProfileAsync(Guid profileId, CancellationToken token, OperationalAuditContext? audit = null)
    {
        await Execute(async context =>
        {
            var profile = await LockProfile(context, profileId, token);
            var now = clock.GetUtcNow();
            await RecoverExpired(context, now, profileId, null, token);
            var history = await context.ProxyAllocations.Where(a => a.ProfileId == profileId).ToArrayAsync(token);
            var active = history.Where(a => a.IsActive).ToArray(); var wasActive = profile.Status == ProfileStatus.Active;
            AllocationPersistenceErrors.Domain(() => domain.CloseProfile(profile, history, now));
            if (audit is not null && wasActive)
            {
                context.OperationalAuditEvents.Add(new(audit.ActorId, profileId, null, OperationalAction.ProfileClosed, now, audit.CorrelationId));
                foreach (var allocation in active) context.OperationalAuditEvents.Add(new(audit.ActorId, profileId, allocation.Id, OperationalAction.AllocationReleased, now, audit.CorrelationId));
            }
            return true;
        }, token);
    }
    private async Task Change(Guid id, AllocationChange change, CancellationToken token, OperationalAuditContext? audit)
    {
        var accepted = await Execute(async context =>
        {
            var identity = await context.ProxyAllocations.Where(a => a.Id == id).Select(a => new { a.ProfileId, a.ProxyId }).SingleOrDefaultAsync(token)
                ?? throw new AllocationPersistenceException(AllocationPersistenceFailure.NotFound);
            var profile = await LockProfile(context, identity.ProfileId, token);
            var proxy = await LockProxy(context, identity.ProxyId, token);
            var now = clock.GetUtcNow();
            await RecoverExpired(context, now, identity.ProfileId, identity.ProxyId, token);
            // Reload only after acquiring resource locks; never mutate a stale tracked snapshot.
            var allocation = await context.ProxyAllocations.SingleAsync(a => a.Id == id, token);
            if (allocation.Status == AllocationStatus.Expired)
                return change == AllocationChange.Expire;
            var previouslyConfirmed = allocation.Status == AllocationStatus.Confirmed;
            AllocationPersistenceErrors.Domain(() =>
            {
                switch (change)
                {
                    case AllocationChange.Confirm: domain.Confirm(allocation, profile, proxy, now); break;
                    case AllocationChange.Release: domain.Release(allocation, now); break;
                    case AllocationChange.Expire: domain.ExpireReservation(allocation, now); break;
                }
            });
            if (audit is not null && !(change == AllocationChange.Confirm && previouslyConfirmed)) context.OperationalAuditEvents.Add(new(audit.ActorId, allocation.ProfileId, id,
                change == AllocationChange.Confirm ? OperationalAction.AllocationConfirmed : change == AllocationChange.Release ? OperationalAction.AllocationReleased : OperationalAction.AllocationExpired, now, audit.CorrelationId));
            return true;
        }, token);
        // An expired confirmation is rejected AFTER committing its recovery and audit.
        if (!accepted) throw new AllocationPersistenceException(AllocationPersistenceFailure.Conflict);
    }
    internal static async Task RecoverExpired(CentralContingenciaDbContext context, DateTimeOffset now, Guid profileId, Guid? proxyId, CancellationToken token)
    {
        var expired = await context.ProxyAllocations.Where(a => (a.ProfileId == profileId || a.ProxyId == proxyId) &&
            a.Status == AllocationStatus.Reserved && a.ReservationExpiresAt <= now).ToArrayAsync(token);
        foreach (var allocation in expired)
        {
            AllocationPersistenceErrors.Domain(() => new AllocationDomainService().ExpireReservation(allocation, now));
            context.OperationalAuditEvents.Add(new(ExpiryActor, allocation.ProfileId, allocation.Id,
                OperationalAction.AllocationExpired, now, "reservation-expiry"));
        }
    }
    private async Task<T> Execute<T>(Func<CentralContingenciaDbContext, Task<T>> action, CancellationToken token)
    {
        // Dedicated context prevents saving unrelated pending proxy/Identity changes.
        await using var context = new CentralContingenciaDbContext(options);
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", token);
            // Serializes recovery/mutation before resource locks, including cross-profile reuse.
            // Readers remain nonblocking; unique partial indexes remain the final guarantee.
            await context.Database.ExecuteSqlRawAsync("LOCK TABLE allocations.\"ProxyAllocations\" IN SHARE ROW EXCLUSIVE MODE", token);
            var result = await action(context);
            await context.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return result;
        }
        catch (Exception error) when (error is DbUpdateException or NpgsqlException) { throw AllocationPersistenceErrors.Translate(error); }
    }
    internal static async Task<OperationalProfile> LockProfile(CentralContingenciaDbContext context, Guid id, CancellationToken token)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM profiles.\"OperationalProfiles\" WHERE \"Id\" = {id} FOR UPDATE", token);
        return await context.OperationalProfiles.SingleOrDefaultAsync(p => p.Id == id, token)
            ?? throw new AllocationPersistenceException(AllocationPersistenceFailure.NotFound);
    }
    internal static async Task<Proxy> LockProxy(CentralContingenciaDbContext context, Guid id, CancellationToken token)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM proxies.\"Proxies\" WHERE \"Id\" = {id} FOR UPDATE", token);
        return await context.Proxies.SingleOrDefaultAsync(p => p.Id == id, token)
            ?? throw new AllocationPersistenceException(AllocationPersistenceFailure.NotFound);
    }
    private enum AllocationChange { Confirm, Release, Expire }
}
