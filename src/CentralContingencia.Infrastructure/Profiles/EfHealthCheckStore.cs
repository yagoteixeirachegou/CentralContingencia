using CentralContingencia.Application.Warmup;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CentralContingencia.Infrastructure.Profiles;

public sealed class EfHealthCheckStore(DbContextOptions<CentralContingenciaDbContext> options) : IHealthCheckStore
{
    public async Task<IReadOnlyList<ProfileHealthCheck>> ListByProfileAsync(string adsPowerProfileId, CancellationToken token)
    {
        await using var context = new CentralContingenciaDbContext(options);
        return await Guard(() => context.ProfileHealthChecks.AsNoTracking().Where(c => c.AdsPowerProfileId == adsPowerProfileId).ToListAsync(token));
    }

    public async Task<IReadOnlyList<ProfileHealthCheck>> ListByProfilesAsync(IReadOnlyCollection<string> ids, CancellationToken token)
    {
        await using var context = new CentralContingenciaDbContext(options);
        return await Guard(() => context.ProfileHealthChecks.AsNoTracking().Where(c => ids.Contains(c.AdsPowerProfileId)).ToListAsync(token));
    }

    public async Task AddAsync(ProfileHealthCheck check, CancellationToken token)
    {
        await using var context = new CentralContingenciaDbContext(options);
        context.ProfileHealthChecks.Add(check);
        try { await context.SaveChangesAsync(token); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new HealthCheckException(HealthCheckFailure.Conflict); }
        catch (Exception error) when (error is DbUpdateException or NpgsqlException or TimeoutException)
        { throw new HealthCheckException(HealthCheckFailure.Unavailable); }
    }

    private static async Task<List<ProfileHealthCheck>> Guard(Func<Task<List<ProfileHealthCheck>>> query)
    {
        try { return await query(); }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or InvalidOperationException)
        { throw new HealthCheckException(HealthCheckFailure.Unavailable); }
    }
}
