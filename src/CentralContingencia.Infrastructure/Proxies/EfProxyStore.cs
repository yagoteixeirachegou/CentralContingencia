using System.Linq.Expressions;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Domain.Audit;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CentralContingencia.Infrastructure.Proxies;

public sealed class EfProxyStore(CentralContingenciaDbContext context) : IProxyStore
{
    public async Task<ProxyPage> QueryAsync(ValidatedProxyQuery query, DateTimeOffset now, CancellationToken token)
    {
        var rows = context.Proxies.AsNoTracking();
        if (query.Host is not null) rows = rows.Where(p => p.Host == query.Host);
        if (query.Port is not null) { var port = ProxyPort.Create(query.Port.Value); rows = rows.Where(p => p.Port == port); }
        if (query.Region is not null) rows = rows.Where(p => p.Region == query.Region);
        if (query.Status is not null) rows = rows.Where(p => p.Status == query.Status);
        if (query.ExpiresBefore is not null) rows = rows.Where(p => p.ExpiresAt <= query.ExpiresBefore);
        if (query.AvailableOnly) rows = rows.Where(p => p.Status == ProxyStatus.Active && p.CreatedAt <= now && p.ExpiresAt > now &&
            !context.ProxyAllocations.Any(a => a.ProxyId == p.Id && (a.Status == CentralContingencia.Domain.Allocations.AllocationStatus.Confirmed || a.Status == CentralContingencia.Domain.Allocations.AllocationStatus.Reserved && a.ReservationExpiresAt > now)));
        var total = await rows.CountAsync(token);
        var sorted = query.Sort switch
        {
            "host" => Order(rows, p => p.Host, query.Descending),
            "port" => Order(rows, p => p.Port, query.Descending),
            "status" => Order(rows, p => p.Status, query.Descending),
            "region" => Order(rows, p => p.Region, query.Descending),
            "expiresAt" => Order(rows, p => p.ExpiresAt, query.Descending),
            "updatedAt" => Order(rows, p => p.UpdatedAt, query.Descending),
            _ => Order(rows, p => p.CreatedAt, query.Descending)
        };
        // Projeção no SQL: leituras não selecionam usuário, ciphertext, nonce, tag ou KeyId.
        var items = await sorted.ThenBy(p => p.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(p => new ProxyResponse(p.Id, p.Host.Value, p.Port.Value, "***", p.Status.ToString(), p.Region,
                p.ExpiresAt, p.CreatedAt, p.UpdatedAt, p.Status == ProxyStatus.Active && p.CreatedAt <= now && p.ExpiresAt > now &&
                !context.ProxyAllocations.Any(a => a.ProxyId == p.Id && (a.Status == CentralContingencia.Domain.Allocations.AllocationStatus.Confirmed || a.Status == CentralContingencia.Domain.Allocations.AllocationStatus.Reserved && a.ReservationExpiresAt > now))))
            .ToListAsync(token);
        return new(items, query.Page, query.PageSize, total);
    }
    private static IOrderedQueryable<Proxy> Order<T>(IQueryable<Proxy> query, Expression<Func<Proxy,T>> key, bool descending) =>
        descending ? query.OrderByDescending(key) : query.OrderBy(key);
    public Task<Proxy?> FindAsync(Guid id, CancellationToken token) => context.Proxies.SingleOrDefaultAsync(p => p.Id == id, token);
    public Task<bool> HasActiveAllocationAsync(Guid id, DateTimeOffset now, CancellationToken token) => context.ProxyAllocations.AnyAsync(a => a.ProxyId == id &&
        (a.Status == CentralContingencia.Domain.Allocations.AllocationStatus.Confirmed || a.Status == CentralContingencia.Domain.Allocations.AllocationStatus.Reserved && a.ReservationExpiresAt > now), token);
    public async Task AddAuditAsync(ProxyAuditEvent audit, CancellationToken token)
    {
        context.ProxyAuditEvents.Add(audit);
        await context.SaveChangesAsync(token);
    }
    public async Task<bool> SaveAsync(Proxy proxy, ProxyAuditEvent audit, bool isNew, CancellationToken token)
    {
        if (isNew) context.Proxies.Add(proxy);
        context.ProxyAuditEvents.Add(audit);
        try { await context.SaveChangesAsync(token); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
        catch (DbUpdateException error) when (error.InnerException is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        { return false; }
    }
}
