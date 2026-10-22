using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Domain.WorkQueue;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CentralContingencia.Infrastructure.WorkQueue;

public sealed class EfExecutorStore(DbContextOptions<CentralContingenciaDbContext> options) : IExecutorStore
{
    public Task AddAsync(WorkExecutor executor, ExecutorEvent auditEvent, CancellationToken token) => Run(async () =>
    {
        await using var context = new CentralContingenciaDbContext(options);
        context.WorkExecutors.Add(executor);
        context.ExecutorEvents.Add(auditEvent);
        try { await context.SaveChangesAsync(token); }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ExecutorException(ExecutorFailure.Conflict); }
        return true;
    });

    public Task<WorkExecutor?> GetAsync(Guid id, CancellationToken token) => Run(async () =>
    {
        await using var context = new CentralContingenciaDbContext(options);
        return await context.WorkExecutors.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, token);
    });

    public Task<IReadOnlyList<WorkExecutor>> ListAsync(CancellationToken token) => Run(async () =>
    {
        await using var context = new CentralContingenciaDbContext(options);
        return (IReadOnlyList<WorkExecutor>)await context.WorkExecutors.AsNoTracking().ToListAsync(token);
    });

    public Task<IReadOnlyDictionary<string, int>> RunningCountsAsync(CancellationToken token) => Run(async () =>
    {
        await using var context = new CentralContingenciaDbContext(options);
        var rows = await context.WorkTasks.AsNoTracking().Where(t => t.Status == WorkTaskStatus.Running && t.WorkerId != null)
            .GroupBy(t => t.WorkerId!).Select(g => new { Worker = g.Key, Count = g.Count() }).ToListAsync(token);
        return (IReadOnlyDictionary<string, int>)rows.ToDictionary(r => r.Worker, r => r.Count);
    });

    public Task<IReadOnlyDictionary<string, string>> NamesAsync(IReadOnlyCollection<string> ids, CancellationToken token) => Run(async () =>
    {
        var guids = ids.Select(i => Guid.TryParseExact(i, "D", out var g) ? g : (Guid?)null).Where(g => g is not null).Select(g => g!.Value).ToList();
        await using var context = new CentralContingenciaDbContext(options);
        var rows = await context.WorkExecutors.AsNoTracking().Where(e => guids.Contains(e.Id)).Select(e => new { e.Id, e.Name }).ToListAsync(token);
        return (IReadOnlyDictionary<string, string>)rows.ToDictionary(r => r.Id.ToString("D"), r => r.Name);
    });

    public Task<WorkExecutor> MutateAsync(Guid id, Func<WorkExecutor, ExecutorEvent?> change, CancellationToken token) => Run(async () =>
    {
        await using var context = new CentralContingenciaDbContext(options);
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await context.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM work."Executors" WHERE "Id" = {id} FOR UPDATE""", token);
        var executor = await context.WorkExecutors.SingleOrDefaultAsync(e => e.Id == id, token) ?? throw new ExecutorException(ExecutorFailure.NotFound);
        if (change(executor) is { } auditEvent) context.ExecutorEvents.Add(auditEvent);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return executor;
    });

    private static async Task<T> Run<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or DbUpdateException or (InvalidOperationException and not ExecutorStateException))
        { throw new ExecutorException(ExecutorFailure.Unavailable, error); }
    }
}
