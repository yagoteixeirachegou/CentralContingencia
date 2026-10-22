using CentralContingencia.Application.Proxies.Import;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Domain.Audit;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CentralContingencia.Infrastructure.Proxies;

public sealed class EfProxyImportStore(CentralContingenciaDbContext context) : IProxyImportStore
{
    public async Task<IProxyImportTransaction> BeginAsync(CancellationToken token)
    {
        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await context.Database.BeginTransactionAsync(token);
            // Bloqueia INSERT/UPDATE de imports e CRUD durante a checagem final.
            // READ COMMITTED vê conflitos novos depois da aquisição do lock.
            await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", token);
            await context.Database.ExecuteSqlRawAsync("LOCK TABLE proxies.\"Proxies\" IN SHARE ROW EXCLUSIVE MODE", token);
            return new Transaction(context,transaction);
        }
        catch (Exception error) when (error is NpgsqlException or DbUpdateException)
        {
            if (transaction is not null) await transaction.DisposeAsync();
            throw new CsvImportException(CsvImportFailure.Persistence);
        }
        catch { if (transaction is not null) await transaction.DisposeAsync(); throw; }
    }
    private sealed class Transaction(CentralContingenciaDbContext context, IDbContextTransaction transaction) : IProxyImportTransaction
    {
        public async Task<ProxyImportBatch?> FindAsync(Guid actorId, Guid key, CancellationToken token)
        {
            try { return await context.ProxyImportBatches.AsNoTracking().Include(b=>b.Lines).SingleOrDefaultAsync(b=>b.ActorId==actorId && b.IdempotencyKey==key,token); }
            catch (NpgsqlException) { throw new CsvImportException(CsvImportFailure.Persistence); }
        }
        public async Task SaveAsync(ProxyImportBatch batch, IReadOnlyList<Proxy> proxies, IReadOnlyList<ProxyAuditEvent> events, CancellationToken token)
        {
            try
            {
                context.Proxies.AddRange(proxies); context.ProxyAuditEvents.AddRange(events);context.ProxyImportBatches.Add(batch);
                await context.SaveChangesAsync(token); await transaction.CommitAsync(token);
            }
            catch (Exception error) when (error is DbUpdateException or PostgresException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                // Não propaga detalhes do PostgreSQL, que podem conter valores sensíveis.
                throw new CsvImportException(CsvImportFailure.Persistence);
            }
        }
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
