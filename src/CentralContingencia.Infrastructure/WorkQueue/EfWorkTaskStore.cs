using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Domain.WorkQueue;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CentralContingencia.Infrastructure.WorkQueue;

/// <summary>
/// Cada operação usa um contexto próprio e uma transação: trava a linha (FOR UPDATE; SKIP LOCKED no claim e na recuperação),
/// aplica a transição do domínio e grava a auditoria no mesmo commit. Concorrência entre workers é resolvida pelo PostgreSQL.
/// </summary>
public sealed class EfWorkTaskStore(DbContextOptions<CentralContingenciaDbContext> options) : IWorkTaskStore
{
    public Task<StoredCreate> CreateAsync(WorkTask task, DateTimeOffset now, CancellationToken token) => Run(async () =>
    {
        for (var attempt = 0; ; attempt++)
        {
            await using var context = new CentralContingenciaDbContext(options);
            var existing = await context.WorkTasks.AsNoTracking().SingleOrDefaultAsync(t => t.CreatedBy == task.CreatedBy && t.IdempotencyKey == task.IdempotencyKey, token);
            if (existing is not null) return Replay(existing, task);
            context.WorkTasks.Add(task);
            context.WorkTaskEvents.Add(WorkTaskEvent.Create(task, WorkTaskAction.Created, task.CreatedBy, null, now));
            try { await context.SaveChangesAsync(token); return new StoredCreate(task, true); }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } && attempt == 0)
            {
                // Outra requisição com a mesma chave venceu a corrida: devolve o resultado dela (ou conflito).
            }
        }
    });

    private static StoredCreate Replay(WorkTask existing, WorkTask requested) =>
        existing.RequestFingerprint == requested.RequestFingerprint ? new(existing, false) : throw new WorkQueueException(WorkQueueFailure.Conflict);

    public Task<WorkTask?> GetAsync(Guid id, CancellationToken token) => Run(async () =>
    {
        await using var context = new CentralContingenciaDbContext(options);
        return await context.WorkTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, token);
    });

    public Task<(IReadOnlyList<WorkTask> Items, int Total)> ListAsync(WorkTaskFilter filter, CancellationToken token) => Run(async () =>
    {
        await using var context = new CentralContingenciaDbContext(options);
        var query = context.WorkTasks.AsNoTracking().AsQueryable();
        if (filter.Status is { } status) query = query.Where(t => t.Status == status);
        if (filter.Type is { } type) query = query.Where(t => t.Type == type);
        var total = await query.CountAsync(token);
        var items = await query.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id).Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToListAsync(token);
        return ((IReadOnlyList<WorkTask>)items, total);
    });

    public Task<WorkTask> RequestCancelAsync(Guid id, Guid actorId, DateTimeOffset now, CancellationToken token) => Mutate(id, token, task =>
    {
        var before = (task.Status, task.CancelRequested);
        task.RequestCancel(actorId, now);
        var changed = before != (task.Status, task.CancelRequested);
        var action = task.Status == WorkTaskStatus.Cancelled ? WorkTaskAction.Cancelled : WorkTaskAction.CancelRequested;
        return (task, changed ? WorkTaskEvent.Create(task, action, actorId, null, now) : null);
    });

    public Task<IReadOnlyList<WorkTask>> ClaimAsync(string workerId, IReadOnlyCollection<WorkTaskType> types, int max, DateTimeOffset now, TimeSpan lease, Guid? claimKey, CancellationToken token) => Run(async () =>
    {
        var utc = now.ToUniversalTime();
        var typeNames = types.Select(t => t.ToString()).ToArray();
        await using var context = new CentralContingenciaDbContext(options);
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        if (claimKey is { } key)
        {
            // Reentrega: o executor repete a chave porque a resposta se perdeu. Só devolve o que ELE já segura com lease vigente (nunca lease alheio).
            var held = await context.WorkTasks.AsNoTracking().Where(t => t.Status == WorkTaskStatus.Running && t.WorkerId == workerId && t.ClaimKey == key && t.LeaseUntil > utc).OrderBy(t => t.StartedAt).ThenBy(t => t.Id).ToListAsync(token);
            if (held.Count > 0) return (IReadOnlyList<WorkTask>)held;
        }
        // SKIP LOCKED: workers concorrentes nunca disputam nem esperam a mesma linha; cada uma é entregue a um só.
        var ids = await context.Database.SqlQuery<Guid>($"""
            SELECT "Id" AS "Value" FROM work."Tasks"
            WHERE "Status" = 'Pending' AND "AvailableAt" <= {utc} AND "Type" = ANY({typeNames})
            ORDER BY "AvailableAt", "CreatedAt" LIMIT {max} FOR UPDATE SKIP LOCKED
            """).ToListAsync(token);
        if (ids.Count == 0) return (IReadOnlyList<WorkTask>)[];
        var tasks = await context.WorkTasks.Where(t => ids.Contains(t.Id)).ToListAsync(token);
        foreach (var task in tasks.OrderBy(t => t.AvailableAt).ThenBy(t => t.CreatedAt))
        {
            task.Claim(workerId, now, lease, claimKey);
            context.WorkTaskEvents.Add(WorkTaskEvent.Create(task, WorkTaskAction.Claimed, null, workerId, now));
        }
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return tasks;
    });

    public Task<bool> HeartbeatAsync(Guid id, string workerId, int attempt, DateTimeOffset now, TimeSpan lease, CancellationToken token) =>
        Mutate(id, token, task => (task.Heartbeat(workerId, attempt, now, lease), (WorkTaskEvent?)null));

    public Task CompleteAsync(Guid id, string workerId, int attempt, string? resultJson, DateTimeOffset now, CancellationToken token) => Mutate(id, token, task =>
    {
        if (task.IsSucceededBy(workerId, attempt)) return (true, null); // repetição da mesma conclusão: aceita sem novo efeito
        task.Succeed(workerId, attempt, now, resultJson);
        return (true, WorkTaskEvent.Create(task, WorkTaskAction.Succeeded, null, workerId, now));
    });

    public Task<WorkTaskStatus> FailAsync(Guid id, string workerId, int attempt, string code, string? message, bool transient, DateTimeOffset now, Func<int, TimeSpan> retryDelay, CancellationToken token) => Mutate(id, token, task =>
    {
        if (task.IsFailureReportedBy(workerId, attempt, code)) return (task.Status, null);
        var status = task.Fail(workerId, attempt, now, code, message, transient, now + retryDelay(task.AttemptCount));
        var action = status switch
        {
            WorkTaskStatus.Pending => WorkTaskAction.RetryScheduled, WorkTaskStatus.DeadLetter => WorkTaskAction.DeadLettered,
            WorkTaskStatus.Cancelled => WorkTaskAction.Cancelled, _ => WorkTaskAction.Failed,
        };
        return (status, WorkTaskEvent.Create(task, action, null, workerId, now, task.LastErrorCode));
    });

    public Task ConfirmCancelledAsync(Guid id, string workerId, int attempt, DateTimeOffset now, CancellationToken token) => Mutate(id, token, task =>
    {
        if (task.IsCancellationConfirmedBy(workerId, attempt)) return (true, null);
        task.ConfirmCancelled(workerId, attempt, now);
        return (true, WorkTaskEvent.Create(task, WorkTaskAction.Cancelled, null, workerId, now));
    });

    public Task ReleaseAsync(Guid id, string workerId, int attempt, DateTimeOffset now, CancellationToken token) => Mutate(id, token, task =>
    {
        var status = task.Release(workerId, attempt, now);
        return (true, WorkTaskEvent.Create(task, status == WorkTaskStatus.Cancelled ? WorkTaskAction.Cancelled : WorkTaskAction.Released, null, workerId, now));
    });

    public Task<int> RecoverExpiredAsync(DateTimeOffset now, Func<int, TimeSpan> retryDelay, int limit, CancellationToken token) => Run(async () =>
    {
        var utc = now.ToUniversalTime();
        await using var context = new CentralContingenciaDbContext(options);
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        var ids = await context.Database.SqlQuery<Guid>($"""
            SELECT "Id" AS "Value" FROM work."Tasks"
            WHERE "Status" = 'Running' AND "LeaseUntil" <= {utc}
            ORDER BY "LeaseUntil" LIMIT {limit} FOR UPDATE SKIP LOCKED
            """).ToListAsync(token);
        if (ids.Count == 0) return 0;
        var tasks = await context.WorkTasks.Where(t => ids.Contains(t.Id)).ToListAsync(token);
        foreach (var task in tasks)
        {
            var worker = task.WorkerId;
            var status = task.RecoverExpiredLease(now, now + retryDelay(task.AttemptCount));
            var action = status switch { WorkTaskStatus.Pending => WorkTaskAction.LeaseExpired, WorkTaskStatus.Cancelled => WorkTaskAction.Cancelled, _ => WorkTaskAction.DeadLettered };
            context.WorkTaskEvents.Add(WorkTaskEvent.Create(task, action, null, worker, now, task.LastErrorCode));
        }
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return tasks.Count;
    });

    public Task<IReadOnlyList<WorkTask>> ListHeldAsync(string workerId, DateTimeOffset now, CancellationToken token) => Run(async () =>
    {
        await using var context = new CentralContingenciaDbContext(options);
        var utc = now.ToUniversalTime();
        return (IReadOnlyList<WorkTask>)await context.WorkTasks.AsNoTracking().Where(t => t.Status == WorkTaskStatus.Running && t.WorkerId == workerId && t.LeaseUntil > utc).OrderBy(t => t.StartedAt).ToListAsync(token);
    });

    public Task<bool> IsCancelRequestedAsync(Guid id, string workerId, int attempt, DateTimeOffset now, CancellationToken token) => Run(async () =>
    {
        await using var context = new CentralContingenciaDbContext(options);
        var task = await context.WorkTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, token) ?? throw new WorkQueueException(WorkQueueFailure.NotFound);
        return task.OwnsLease(workerId, attempt, now) ? task.CancelRequested : throw new WorkQueueException(WorkQueueFailure.LeaseLost);
    });

    private Task<T> Mutate<T>(Guid id, CancellationToken token, Func<WorkTask, (T Result, WorkTaskEvent? Event)> change) => Run(async () =>
    {
        await using var context = new CentralContingenciaDbContext(options);
        await using var transaction = await context.Database.BeginTransactionAsync(token);
        await context.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM work."Tasks" WHERE "Id" = {id} FOR UPDATE""", token);
        var task = await context.WorkTasks.SingleOrDefaultAsync(t => t.Id == id, token) ?? throw new WorkQueueException(WorkQueueFailure.NotFound);
        var (result, auditEvent) = change(task);
        if (auditEvent is not null) context.WorkTaskEvents.Add(auditEvent);
        await context.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return result;
    });

    private static async Task<T> Run<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (WorkTaskStateException error) { throw new WorkQueueException(error.Error == WorkTaskError.LeaseLost ? WorkQueueFailure.LeaseLost : WorkQueueFailure.Conflict); }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or DbUpdateException or InvalidOperationException)
        { throw new WorkQueueException(WorkQueueFailure.Unavailable, error); }
    }
}
