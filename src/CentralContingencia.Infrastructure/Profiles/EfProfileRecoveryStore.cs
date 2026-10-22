using CentralContingencia.Application.Profiles.Recovery;
using CentralContingencia.Application.Allocations;
using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Audit;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Infrastructure.Allocations;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CentralContingencia.Infrastructure.Profiles;

public sealed class EfProfileRecoveryStore(DbContextOptions<CentralContingenciaDbContext> options, TimeProvider clock, ReservationOptions policy) : IProfileRecoveryStore
{
    public async Task<RecoverySnapshot> ReadAsync(IReadOnlyCollection<RecoveryCandidate> candidates, CancellationToken token)
    {
        await using var context = new CentralContingenciaDbContext(options);
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, token);
            await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token);
            return await Read(context, candidates, token);
        }
        catch (NpgsqlException) { throw new RecoveryException(RecoveryFailure.Persistence); }
    }
    private static async Task<RecoverySnapshot> Read(CentralContingenciaDbContext context, IReadOnlyCollection<RecoveryCandidate> candidates, CancellationToken token)
    {
        if (candidates.Count == 0) return new([], [], []);
        var hosts = candidates.Select(c => c.Host).Distinct().ToArray(); var ports = candidates.Select(c => c.Port).Distinct().ToArray();
        var names = candidates.Select(c => c.Name.ToUpperInvariant()).Distinct().ToArray();
        // Value Objects remain inside the domain; fixed parameterized SQL avoids unsupported .Value translations.
        var proxies = await context.Proxies.FromSqlInterpolated($"SELECT *, xmin FROM proxies.\"Proxies\" WHERE \"Host\" = ANY({hosts}) AND \"Port\" = ANY({ports})").AsNoTracking().Take(10001).ToArrayAsync(token);
        var profiles = await context.OperationalProfiles.FromSqlInterpolated($"SELECT *, xmin FROM profiles.\"OperationalProfiles\" WHERE upper(\"Name\") = ANY({names})").AsNoTracking().Take(10001).ToArrayAsync(token);
        if (proxies.Length > 10000 || profiles.Length > 10000) throw new RecoveryException(RecoveryFailure.Validation);
        var proxyIds = proxies.Select(p => p.Id).ToArray(); var profileIds = profiles.Select(p => p.Id).ToArray();
        var allocations = await context.ProxyAllocations.AsNoTracking().Where(a => proxyIds.Contains(a.ProxyId) || profileIds.Contains(a.ProfileId)).Take(10001).ToArrayAsync(token);
        if (allocations.Length > 10000) throw new RecoveryException(RecoveryFailure.Validation);
        return new(proxies, profiles, allocations);
    }
    public async Task<IProfileRecoveryTransaction> BeginAsync(CancellationToken token)
    {
        var context = new CentralContingenciaDbContext(options); IDbContextTransaction? transaction = null;
        try
        {
            transaction = await context.Database.BeginTransactionAsync(token);
            await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", token);
            // Same first lock as allocation mutations. Readers remain nonblocking; bound lock waits.
            await context.Database.ExecuteSqlRawAsync("LOCK TABLE allocations.\"ProxyAllocations\" IN SHARE ROW EXCLUSIVE MODE", token);
            await context.Database.ExecuteSqlRawAsync("LOCK TABLE profiles.\"OperationalProfiles\", proxies.\"Proxies\", audit.\"ProfileRecoveryBatches\" IN SHARE ROW EXCLUSIVE MODE", token);
            return new Transaction(context, transaction, clock, policy);
        }
        catch (Exception error) when (error is NpgsqlException or DbUpdateException)
        { if (transaction is not null) await transaction.DisposeAsync(); await context.DisposeAsync(); throw Translate(error); }
        catch { if (transaction is not null) await transaction.DisposeAsync(); await context.DisposeAsync(); throw; }
    }
    private static RecoveryException Translate(Exception error)
    {
        var sql = error is PostgresException postgres ? postgres.SqlState : (error as DbUpdateException)?.InnerException is PostgresException inner ? inner.SqlState : null;
        return new(sql is "23505" or "55P03" or "40P01" or "40001" ? RecoveryFailure.Conflict : RecoveryFailure.Persistence);
    }
    private sealed class Transaction(CentralContingenciaDbContext context, IDbContextTransaction transaction, TimeProvider clock, ReservationOptions policy) : IProfileRecoveryTransaction
    {
        public async Task<ProfileRecoveryBatch?> FindAsync(Guid actor, Guid key, CancellationToken token)
        {
            try { return await context.ProfileRecoveryBatches.AsNoTracking().Include(b => b.Lines).SingleOrDefaultAsync(b => b.ActorId == actor && b.IdempotencyKey == key, token); }
            catch (NpgsqlException error) { throw Translate(error); }
        }
        public async Task<RecoverySnapshot> ReadAsync(IReadOnlyCollection<RecoveryCandidate> candidates, CancellationToken token)
        { try { return await Read(context, candidates, token); } catch (NpgsqlException error) { throw Translate(error); } }
        public async Task SaveAsync(ProfileRecoveryBatch batch, IReadOnlyList<RecoveryCandidate> candidates, CancellationToken token)
        {
            try
            {
                var domain = new AllocationDomainService();
                foreach (var line in batch.Lines.Where(l => l.Outcome == "Recovered"))
                {
                    var candidate = candidates.Single(c => c.Line == line.Line); var now = clock.GetUtcNow();
                    var proxy = await context.Proxies.SingleAsync(p => p.Id == line.ProxyId, token);
                    await EfAllocationStore.RecoverExpired(context, now, line.ProfileId!.Value, proxy.Id, token);
                    await context.SaveChangesAsync(token); // Free partial uniqueness indexes; still within this transaction.
                    var profile = OperationalProfile.Create(line.ProfileId.Value, ProfileName.Create(candidate.Name), null, now, candidate.Platform1, candidate.Platform2);
                    var history = await context.ProxyAllocations.Where(a => a.ProxyId == proxy.Id || a.ProfileId == profile.Id).ToArrayAsync(token);
                    var allocation = domain.Reserve(line.AllocationId!.Value, profile, proxy, batch.ActorId, Guid.NewGuid(), now, policy.Deadline(now, proxy.ExpiresAt, null), history);
                    domain.Confirm(allocation, profile, proxy, now);
                    context.OperationalProfiles.Add(profile); context.ProxyAllocations.Add(allocation);
                    foreach (var action in new[] { OperationalAction.ProfileCreated, OperationalAction.AllocationReserved, OperationalAction.AllocationConfirmed })
                        context.OperationalAuditEvents.Add(new(batch.ActorId, profile.Id, action == OperationalAction.ProfileCreated ? null : allocation.Id, action, now, batch.CorrelationId));
                }
                context.ProfileRecoveryBatches.Add(batch); await context.SaveChangesAsync(token); await transaction.CommitAsync(token);
            }
            catch (Exception error) when (error is DbUpdateException or NpgsqlException or ArgumentException or InvalidOperationException)
            {
                await transaction.RollbackAsync(CancellationToken.None); context.ChangeTracker.Clear();
                if (error is ArgumentException or InvalidOperationException) throw new RecoveryException(RecoveryFailure.Conflict);
                throw Translate(error);
            }
        }
        public async ValueTask DisposeAsync() { await transaction.DisposeAsync(); await context.DisposeAsync(); }
    }
}
