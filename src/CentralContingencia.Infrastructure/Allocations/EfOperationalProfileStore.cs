using CentralContingencia.Application.Allocations;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using CentralContingencia.Domain.Audit;
using System.Linq.Expressions;

namespace CentralContingencia.Infrastructure.Allocations;

public sealed class EfOperationalProfileStore(DbContextOptions<CentralContingenciaDbContext> options, TimeProvider? clock = null) : IOperationalProfileStore
{
    public async Task AddAsync(OperationalProfile profile, CancellationToken token, OperationalAuditContext? audit = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await using var context = new CentralContingenciaDbContext(options);
        try
        {
            context.OperationalProfiles.Add(profile);
            if (audit is not null) context.OperationalAuditEvents.Add(new(audit.ActorId, profile.Id, null, OperationalAction.ProfileCreated, profile.CreatedAt, audit.CorrelationId));
            await context.SaveChangesAsync(token);
        }
        catch (Exception error) when (error is DbUpdateException or NpgsqlException) { throw AllocationPersistenceErrors.Translate(error); }
    }
    public async Task<ProfilePage> QueryAsync(ProfileQuery query, CancellationToken token)
    {
        await using var context = new CentralContingenciaDbContext(options);
        try
        {
            var rows = context.OperationalProfiles.AsNoTracking();
            if (query.Name is not null) { var name = ProfileName.Create(query.Name); rows = rows.Where(p => p.Name == name); }
            if (query.Platform is not null) rows = rows.Where(p => p.Platform == query.Platform);
            if (query.Status is not null) { var status = Enum.Parse<ProfileStatus>(query.Status); rows = rows.Where(p => p.Status == status); }
            var total = await rows.CountAsync(token);
            IOrderedQueryable<OperationalProfile> Order<T>(Expression<Func<OperationalProfile, T>> key) => query.Direction == "asc" ? rows.OrderBy(key) : rows.OrderByDescending(key);
            var sorted = query.Sort switch { "name" => Order(p => p.Name), "platform" => Order(p => p.Platform), "status" => Order(p => p.Status), "updatedAt" => Order(p => p.UpdatedAt), _ => Order(p => p.CreatedAt) };
            var items = await sorted.ThenBy(p => p.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(token);
            return new(items.Select(OperationalManagementService.Response).ToArray(), query.Page, query.PageSize, total);
        }
        catch (NpgsqlException error) { throw AllocationPersistenceErrors.Translate(error); }
    }
    public async Task<OperationalProfile> UpdateAsync(Guid id, string? name, string? platform, CancellationToken token, OperationalAuditContext audit,
        bool platform1Specified = false, string? platform1 = null, bool platform2Specified = false, string? platform2 = null)
    {
        await using var context = new CentralContingenciaDbContext(options);
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", token);
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM profiles.\"OperationalProfiles\" WHERE \"Id\" = {id} FOR UPDATE", token);
            var profile = await context.OperationalProfiles.SingleOrDefaultAsync(p => p.Id == id, token) ?? throw new AllocationPersistenceException(AllocationPersistenceFailure.NotFound);
            var now = (clock ?? TimeProvider.System).GetUtcNow();
            AllocationPersistenceErrors.Domain(() => profile.UpdateDetails(name is null ? profile.Name : ProfileName.Create(name), platform ?? profile.Platform,
                platform1Specified ? platform1 : profile.Platform1, platform2Specified ? platform2 : profile.Platform2, now));
            context.OperationalAuditEvents.Add(new(audit.ActorId, id, null, OperationalAction.ProfileUpdated, now, audit.CorrelationId));
            await context.SaveChangesAsync(token); await transaction.CommitAsync(token); return profile;
        }
        catch (Exception error) when (error is DbUpdateException or NpgsqlException) { throw AllocationPersistenceErrors.Translate(error); }
    }
    public async Task<OperationalProfile?> FindAsync(Guid id, CancellationToken token)
    {
        await using var context = new CentralContingenciaDbContext(options);
        try { return await context.OperationalProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, token); }
        catch (NpgsqlException error) { throw AllocationPersistenceErrors.Translate(error); }
    }
}
