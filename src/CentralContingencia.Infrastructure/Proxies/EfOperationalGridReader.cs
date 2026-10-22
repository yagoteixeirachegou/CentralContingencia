using System.Data;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Infrastructure.Allocations;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CentralContingencia.Infrastructure.Proxies;

public sealed class EfOperationalGridReader(DbContextOptions<CentralContingenciaDbContext> options, TimeProvider clock) : IOperationalGridReader
{
    // An unmapped read model keeps VO converters and credential-bearing aggregates
    // out of filtering/materialization. Only the fixed SQL below and parameters are used.
    private const string ProjectionSql = """
        SELECT p."Id" AS "ProxyId", f."Id" AS "ProfileId", f."Name" AS "ProfileName",
               f."Platform1", f."Platform2", p."Host", p."Port", p."Region", p."ExpiresAt", p."Status",
               CASE WHEN a."Status"='Confirmed' THEN 'InUse'
                    WHEN a."Status"='Reserved' THEN 'Reserved'
                    WHEN p."Status"='Active' AND p."CreatedAt"<=@evaluated_at AND p."ExpiresAt">@evaluated_at THEN 'Available'
                    ELSE 'Unavailable' END AS "Availability",
               a."Id" AS "AllocationId",
               CASE WHEN a."Status"='Reserved' THEN a."ReservationExpiresAt" ELSE NULL END AS "ReservationExpiresAt"
        FROM proxies."Proxies" p
        LEFT JOIN allocations."ProxyAllocations" a ON a."ProxyId"=p."Id"
             AND (a."Status"='Confirmed' OR (a."Status"='Reserved' AND a."ReservationExpiresAt">@evaluated_at))
        LEFT JOIN profiles."OperationalProfiles" f ON f."Id"=a."ProfileId"
        """;
    private static string ContainsPattern(string value) => "%" + value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    public async Task<OperationalGridPage> QueryAsync(OperationalGridQuery query, CancellationToken token)
    {
        await using var db = new CentralContingenciaDbContext(options);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
            await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", token);
            var now = clock.GetUtcNow();
            var rows = db.Database.SqlQueryRaw<OperationalGridRow>(ProjectionSql, new NpgsqlParameter("evaluated_at", now));
            if (query.Profile is not null)
            {
                var pattern = ContainsPattern(query.Profile);
                rows = rows.Where(r => r.ProfileName != null && EF.Functions.ILike(r.ProfileName, pattern, "\\"));
            }
            if (query.Region is not null)
            {
                var pattern = ContainsPattern(query.Region);
                rows = rows.Where(r => EF.Functions.ILike(r.Region, pattern, "\\"));
            }
            if (query.Platform is not null) rows = rows.Where(r => r.Platform1 == query.Platform || r.Platform2 == query.Platform);
            if (query.Availability is not null) rows = rows.Where(r => r.Availability == query.Availability);
            var total = await rows.CountAsync(token);
            var sorted = query.Direction == "asc" ? rows.OrderBy(r => r.ExpiresAt) : rows.OrderByDescending(r => r.ExpiresAt);
            var items = await sorted.ThenBy(r => r.ProxyId).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(token);
            await transaction.CommitAsync(token);
            return new(items, query.Page, query.PageSize, total, now);
        }
        catch (NpgsqlException error) { throw AllocationPersistenceErrors.Translate(error); }
    }
}
