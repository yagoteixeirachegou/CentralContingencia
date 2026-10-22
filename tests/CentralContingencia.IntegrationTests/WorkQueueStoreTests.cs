using System.Text.Json;
using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Domain.WorkQueue;
using CentralContingencia.Infrastructure.Persistence;
using CentralContingencia.Infrastructure.WorkQueue;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CentralContingencia.IntegrationTests;

/// <summary>Cada classe de teste da fila usa seu próprio banco descartável: tarefas de um teste nunca são reservadas por outro.</summary>
public abstract class WorkQueueHarness(AuthFixture fixture) : IAsyncLifetime
{
    // Cada teste começa com a fila vazia: tarefas de testes anteriores nunca entram nas contagens nem nos claims.
    public async Task InitializeAsync()
    {
        await using var context = new CentralContingenciaDbContext(DbOptions);
        await context.Database.ExecuteSqlRawAsync("TRUNCATE work.\"Tasks\", audit.\"WorkTaskEvents\"");
    }
    public Task DisposeAsync() => Task.CompletedTask;

    protected static readonly WorkTaskType Simulated = WorkTaskType.SimulatedProcessing;
    protected readonly TestClock Clock = new();
    protected readonly WorkQueueOptions Options = new() { LeaseSeconds = 60, RetryBaseDelaySeconds = 10, RetryMaxDelaySeconds = 300, DefaultMaxAttempts = 3 };
    protected readonly Guid Actor = Guid.NewGuid();

    protected DbContextOptions<CentralContingenciaDbContext> DbOptions => new DbContextOptionsBuilder<CentralContingenciaDbContext>().UseNpgsql(fixture.ConnectionString).Options;
    protected IWorkTaskService Tasks => new WorkTaskService(new EfWorkTaskStore(DbOptions), new EfExecutorStore(DbOptions), Options, Clock);
    protected IWorkQueueService Queue => new WorkQueueService(new EfWorkTaskStore(DbOptions), Options, Clock);

    protected static CreateWorkTaskRequest Request(string payload = """{"version":1}""", int? maxAttempts = null) =>
        new("SimulatedProcessing", JsonDocument.Parse(payload).RootElement.Clone(), maxAttempts);

    protected async Task<Guid> Create(string payload = """{"version":1}""", int? maxAttempts = null, Guid? key = null) =>
        (await Tasks.CreateAsync(Actor, key ?? Guid.NewGuid(), Request(payload, maxAttempts), "corr", default)).Task.Id;

    protected async Task<WorkTask> Load(Guid id)
    {
        await using var context = new CentralContingenciaDbContext(DbOptions);
        return await context.WorkTasks.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    protected async Task<List<WorkTaskAction>> Actions(Guid id)
    {
        await using var context = new CentralContingenciaDbContext(DbOptions);
        return (await context.WorkTaskEvents.AsNoTracking().Where(e => e.TaskId == id).OrderBy(e => e.Sequence).ToListAsync()).Select(e => e.Action).ToList();
    }

    protected async Task<ClaimedWorkTask> ClaimOne(string worker)
    {
        var claimed = await Queue.ClaimAsync(worker, [Simulated], 1, default);
        return Assert.Single(claimed);
    }

    protected static async Task<WorkQueueFailure> FailureOf(Func<Task> action) => (await Assert.ThrowsAsync<WorkQueueException>(action)).Failure;
}

public class WorkQueueStoreTests(AuthFixture fixture) : WorkQueueHarness(fixture), IClassFixture<AuthFixture>
{
    [PostgresFact]
    public async Task CreateIsIdempotentForTheSameKeyAndContent()
    {
        var key = Guid.NewGuid();
        var first = await Tasks.CreateAsync(Actor, key, Request(), "corr", default);
        var second = await Tasks.CreateAsync(Actor, key, Request(), "other-corr", default);
        Assert.True(first.Created); Assert.False(second.Created);
        Assert.Equal(first.Task.Id, second.Task.Id);
        Assert.Equal("corr", second.Task.CorrelationId);
        Assert.Equal([WorkTaskAction.Created], await Actions(first.Task.Id));
    }

    [PostgresFact]
    public async Task SameKeyWithDifferentContentIsAConflictAndOtherActorsDoNotCollide()
    {
        var key = Guid.NewGuid();
        await Tasks.CreateAsync(Actor, key, Request(), "c", default);
        Assert.Equal(WorkQueueFailure.Conflict, await FailureOf(() => Tasks.CreateAsync(Actor, key, Request("""{"version":1,"steps":2}"""), "c", default)));
        Assert.Equal(WorkQueueFailure.Conflict, await FailureOf(() => Tasks.CreateAsync(Actor, key, Request(maxAttempts: 5), "c", default)));
        var other = await Tasks.CreateAsync(Guid.NewGuid(), key, Request(), "c", default);
        Assert.True(other.Created);
    }

    [PostgresFact]
    public async Task ConcurrentCreatesWithOneKeyProduceOneTask()
    {
        var key = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => Tasks.CreateAsync(Actor, key, Request(), "c", default))));
        Assert.Single(results.Select(r => r.Task.Id).Distinct());
        Assert.Single(results, r => r.Created);
        await using var context = new CentralContingenciaDbContext(DbOptions);
        Assert.Equal(1, await context.WorkTasks.CountAsync(t => t.IdempotencyKey == key));
        Assert.Equal(1, await context.WorkTaskEvents.CountAsync(e => e.TaskId == results[0].Task.Id));
    }

    [PostgresFact]
    public async Task ClaimIsAtomicAcrossConcurrentWorkers()
    {
        var created = new HashSet<Guid>();
        for (var i = 0; i < 40; i++) created.Add(await Create());
        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(w => Task.Run(async () =>
        {
            var mine = new List<ClaimedWorkTask>();
            while (true)
            {
                var batch = await Queue.ClaimAsync($"worker-{w}", [Simulated], 3, default);
                if (batch.Count == 0) return (Worker: $"worker-{w}", Tasks: mine);
                mine.AddRange(batch);
            }
        })));
        var all = claims.SelectMany(c => c.Tasks).ToList();
        Assert.Equal(40, all.Count);
        Assert.Equal(created, all.Select(t => t.Id).ToHashSet());
        Assert.All(all, t => Assert.Equal(1, t.Attempt));
        foreach (var (worker, mine) in claims)
            foreach (var claimed in mine) Assert.Equal(worker, (await Load(claimed.Id)).WorkerId);
    }

    [PostgresFact]
    public async Task ClaimRespectsOrderTypesAndBackoff()
    {
        var first = await Create(); Clock.Advance(TimeSpan.FromSeconds(1)); var second = await Create();
        Assert.Equal(WorkQueueFailure.Validation, await FailureOf(() => Queue.ClaimAsync("w", [], 1, default)));
        Assert.Equal(WorkQueueFailure.Validation, await FailureOf(() => Queue.ClaimAsync("w", [Simulated], 0, default)));
        var claimed = await ClaimOne("w");
        Assert.Equal(first, claimed.Id);
        await Queue.FailAsync("w", first, claimed.Attempt, "net", "x", true, default);
        // A primeira está em backoff (10 s): a segunda é entregue antes.
        Assert.Equal(second, (await ClaimOne("w2")).Id);
        Assert.Empty(await Queue.ClaimAsync("w3", [Simulated], 5, default));
        Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(first, (await ClaimOne("w3")).Id);
    }

    [PostgresFact]
    public async Task HeartbeatRenewsTheLeaseSoRecoveryLeavesTheTaskAlone()
    {
        var id = await Create();
        var claimed = await ClaimOne("w");
        Clock.Advance(TimeSpan.FromSeconds(50));
        Assert.False(await Queue.HeartbeatAsync("w", id, claimed.Attempt, default));
        Clock.Advance(TimeSpan.FromSeconds(50)); // 100 s desde o claim, 50 s desde o heartbeat
        Assert.Equal(0, await Queue.RecoverExpiredAsync(default));
        await Queue.CompleteAsync("w", id, claimed.Attempt, """{"ok":true}""", default);
        var done = await Load(id);
        Assert.Equal(WorkTaskStatus.Succeeded, done.Status);
        Assert.Equal(1, done.AttemptCount);
        Assert.Equal([WorkTaskAction.Created, WorkTaskAction.Claimed, WorkTaskAction.Succeeded], await Actions(id));
    }

    [PostgresFact]
    public async Task AbandonedWorkerLosesTheLeaseAndTheTaskIsRecoveredToAnotherWorker()
    {
        var id = await Create();
        var first = await ClaimOne("dead-worker");
        Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(1, await Queue.RecoverExpiredAsync(default));
        var recovered = await Load(id);
        Assert.Equal(WorkTaskStatus.Pending, recovered.Status);
        Assert.Equal("lease_expired", recovered.LastErrorCode);
        Assert.Null(recovered.WorkerId);
        Clock.Advance(TimeSpan.FromSeconds(10));
        var second = await ClaimOne("live-worker");
        Assert.Equal(2, second.Attempt);
        // O worker original "ressuscita": nada do que ele faz vale mais.
        Assert.Equal(WorkQueueFailure.LeaseLost, await FailureOf(() => Queue.HeartbeatAsync("dead-worker", id, first.Attempt, default)));
        Assert.Equal(WorkQueueFailure.LeaseLost, await FailureOf(() => Queue.CompleteAsync("dead-worker", id, first.Attempt, null, default)));
        Assert.Equal(WorkQueueFailure.LeaseLost, await FailureOf(() => Queue.FailAsync("dead-worker", id, first.Attempt, "x", null, false, default)));
        Assert.Equal(WorkQueueFailure.LeaseLost, await FailureOf(() => Queue.ReleaseAsync("dead-worker", id, first.Attempt, default)));
        // Mesmo id de worker, tentativa antiga: também recusado.
        Assert.Equal(WorkQueueFailure.LeaseLost, await FailureOf(() => Queue.CompleteAsync("live-worker", id, first.Attempt, null, default)));
        await Queue.CompleteAsync("live-worker", id, second.Attempt, null, default);
        Assert.Equal(WorkTaskStatus.Succeeded, (await Load(id)).Status);
        Assert.Equal([WorkTaskAction.Created, WorkTaskAction.Claimed, WorkTaskAction.LeaseExpired, WorkTaskAction.Claimed, WorkTaskAction.Succeeded], await Actions(id));
    }

    [PostgresFact]
    public async Task ExpiredLeaseCannotBeUsedEvenBeforeRecoveryRuns()
    {
        var id = await Create();
        var claimed = await ClaimOne("w");
        Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(WorkQueueFailure.LeaseLost, await FailureOf(() => Queue.CompleteAsync("w", id, claimed.Attempt, null, default)));
        Assert.Equal(WorkQueueFailure.LeaseLost, await FailureOf(() => Queue.HeartbeatAsync("w", id, claimed.Attempt, default)));
        Assert.Equal(WorkTaskStatus.Running, (await Load(id)).Status);
    }

    [PostgresFact]
    public async Task RecoveryExhaustsAttemptsIntoDeadLetter()
    {
        var id = await Create(maxAttempts: 2);
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await ClaimOne($"w{attempt}");
            Clock.Advance(TimeSpan.FromSeconds(61));
            await Queue.RecoverExpiredAsync(default);
            Clock.Advance(TimeSpan.FromSeconds(300));
        }
        var task = await Load(id);
        Assert.Equal(WorkTaskStatus.DeadLetter, task.Status);
        Assert.Equal(2, task.AttemptCount);
        Assert.NotNull(task.CompletedAt);
        Assert.Empty(await Queue.ClaimAsync("w3", [Simulated], 1, default));
        Assert.Equal(WorkTaskAction.DeadLettered, (await Actions(id)).Last());
    }

    [PostgresFact]
    public async Task RetryUsesExponentialBackoffUntilDeadLetter()
    {
        var id = await Create(maxAttempts: 3);
        var one = await ClaimOne("w");
        Assert.Equal(WorkTaskStatus.Pending, await Queue.FailAsync("w", id, one.Attempt, "net", "falha", true, default));
        var afterFirst = await Load(id);
        Assert.Equal(Clock.GetUtcNow() + TimeSpan.FromSeconds(10), afterFirst.AvailableAt, TimeSpan.FromMilliseconds(1));
        Assert.Empty(await Queue.ClaimAsync("w", [Simulated], 1, default));
        Clock.Advance(TimeSpan.FromSeconds(10));
        var two = await ClaimOne("w");
        Assert.Equal(WorkTaskStatus.Pending, await Queue.FailAsync("w", id, two.Attempt, "net", "falha", true, default));
        Assert.Equal(Clock.GetUtcNow() + TimeSpan.FromSeconds(20), (await Load(id)).AvailableAt, TimeSpan.FromMilliseconds(1));
        Clock.Advance(TimeSpan.FromSeconds(20));
        var three = await ClaimOne("w");
        Assert.Equal(WorkTaskStatus.DeadLetter, await Queue.FailAsync("w", id, three.Attempt, "net", "falha", true, default));
        Assert.Equal([WorkTaskAction.Created, WorkTaskAction.Claimed, WorkTaskAction.RetryScheduled, WorkTaskAction.Claimed, WorkTaskAction.RetryScheduled,
            WorkTaskAction.Claimed, WorkTaskAction.DeadLettered], await Actions(id));
    }

    [PostgresFact]
    public async Task PermanentFailureStopsImmediatelyAndStoresOnlySanitizedError()
    {
        var id = await Create();
        var claimed = await ClaimOne("w");
        Assert.Equal(WorkTaskStatus.Failed, await Queue.FailAsync("w", id, claimed.Attempt, "bad_input", "password=hunter2 host=db.internal", false, default));
        var task = await Load(id);
        Assert.Equal("bad_input", task.LastErrorCode);
        Assert.DoesNotContain("hunter2", task.LastError);
        Assert.DoesNotContain("db.internal", task.LastError);
        var response = await Tasks.GetAsync(id, default);
        Assert.DoesNotContain("hunter2", JsonSerializer.Serialize(response));
    }

    [PostgresFact]
    public async Task ReleaseGivesTheAttemptBack()
    {
        var id = await Create(maxAttempts: 1);
        var claimed = await ClaimOne("w");
        await Queue.ReleaseAsync("w", id, claimed.Attempt, default);
        var task = await Load(id);
        Assert.Equal((WorkTaskStatus.Pending, 0), (task.Status, task.AttemptCount));
        Assert.Equal(1, (await ClaimOne("w2")).Attempt);
        Assert.Equal(WorkTaskAction.Released, (await Actions(id))[2]);
    }

    [PostgresFact]
    public async Task CancelPendingIsImmediateAndTheTaskIsNeverClaimed()
    {
        var id = await Create();
        var cancelled = await Tasks.CancelAsync(Actor, id, default);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Empty(await Queue.ClaimAsync("w", [Simulated], 1, default));
        Assert.Equal("Cancelled", (await Tasks.CancelAsync(Actor, id, default)).Status); // repetir é seguro
        Assert.Equal([WorkTaskAction.Created, WorkTaskAction.Cancelled], await Actions(id));
    }

    [PostgresFact]
    public async Task CancelRunningIsCooperative()
    {
        var id = await Create();
        var claimed = await ClaimOne("w");
        var requested = await Tasks.CancelAsync(Actor, id, default);
        Assert.Equal(("Running", true), (requested.Status, requested.CancelRequested));
        await Tasks.CancelAsync(Actor, id, default);
        Assert.True(await Queue.HeartbeatAsync("w", id, claimed.Attempt, default));
        await Queue.ConfirmCancelledAsync("w", id, claimed.Attempt, default);
        Assert.Equal(WorkTaskStatus.Cancelled, (await Load(id)).Status);
        Assert.Equal([WorkTaskAction.Created, WorkTaskAction.Claimed, WorkTaskAction.CancelRequested, WorkTaskAction.Cancelled], await Actions(id));
    }

    [PostgresFact]
    public async Task CancelRequestedOnAnAbandonedTaskEndsCancelledAfterRecovery()
    {
        var id = await Create();
        await ClaimOne("w");
        await Tasks.CancelAsync(Actor, id, default);
        Clock.Advance(TimeSpan.FromSeconds(61));
        await Queue.RecoverExpiredAsync(default);
        Assert.Equal(WorkTaskStatus.Cancelled, (await Load(id)).Status);
    }

    [PostgresFact]
    public async Task CancelOfAFinishedTaskIsAConflictAndUnknownTasksAreNotFound()
    {
        var id = await Create();
        var claimed = await ClaimOne("w");
        await Queue.CompleteAsync("w", id, claimed.Attempt, null, default);
        Assert.Equal(WorkQueueFailure.Conflict, await FailureOf(() => Tasks.CancelAsync(Actor, id, default)));
        Assert.Equal(WorkQueueFailure.NotFound, await FailureOf(() => Tasks.CancelAsync(Actor, Guid.NewGuid(), default)));
        Assert.Equal(WorkQueueFailure.NotFound, await FailureOf(() => Tasks.GetAsync(Guid.NewGuid(), default)));
        Assert.Equal(WorkQueueFailure.NotFound, await FailureOf(() => Queue.HeartbeatAsync("w", Guid.NewGuid(), 1, default)));
    }

    [PostgresFact]
    public async Task ConcurrentCompleteAndCancelNeverCorruptTheState()
    {
        for (var round = 0; round < 10; round++)
        {
            var id = await Create();
            var claimed = await ClaimOne("w");
            var complete = Task.Run(async () => { try { await Queue.CompleteAsync("w", id, claimed.Attempt, null, default); } catch (WorkQueueException) { } });
            var cancel = Task.Run(async () => { try { await Tasks.CancelAsync(Actor, id, default); } catch (WorkQueueException) { } });
            await Task.WhenAll(complete, cancel);
            var task = await Load(id);
            // O complete vence sempre que chega com lease vigente; o cancelamento posterior vira conflito (ou pedido ignorado).
            Assert.Equal(WorkTaskStatus.Succeeded, task.Status);
            Assert.Null(task.WorkerId);
        }
    }

    [PostgresFact]
    public async Task ListPaginatesFiltersAndOrdersNewestFirst()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++) { ids.Add(await Create()); Clock.Advance(TimeSpan.FromSeconds(1)); }
        await Tasks.CancelAsync(Actor, ids[0], default);
        var page = await Tasks.ListAsync(null, null, 1, 2, default);
        Assert.True(page.Total >= 5);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(ids[4], page.Items[0].Id);
        var cancelled = await Tasks.ListAsync("Cancelled", "SimulatedProcessing", 1, 100, default);
        Assert.Contains(cancelled.Items, i => i.Id == ids[0]);
        Assert.All(cancelled.Items, i => Assert.Equal("Cancelled", i.Status));
        Assert.Equal(WorkQueueFailure.Validation, await FailureOf(() => Tasks.ListAsync("Bogus", null, 1, 20, default)));
        Assert.Equal(WorkQueueFailure.Validation, await FailureOf(() => Tasks.ListAsync(null, "Bogus", 1, 20, default)));
        Assert.Equal(WorkQueueFailure.Validation, await FailureOf(() => Tasks.ListAsync(null, null, 0, 20, default)));
        Assert.Equal(WorkQueueFailure.Validation, await FailureOf(() => Tasks.ListAsync(null, null, 1, 101, default)));
    }

    [PostgresFact]
    public async Task ResponseNeverExposesInternalFields()
    {
        var id = await Create();
        await ClaimOne("secret-worker-name");
        var json = JsonSerializer.Serialize(await Tasks.GetAsync(id, default), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("secret-worker-name", json);
        foreach (var forbidden in new[] { "workerId", "leaseUntil", "heartbeatAt", "requestFingerprint", "idempotencyKey", "createdBy", "lastError\"" })
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task DatabaseConstraintsRejectInconsistentRows()
    {
        var id = await Create();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        async Task<string?> Violation(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("id", id);
            try { await command.ExecuteNonQueryAsync(); return null; }
            catch (PostgresException error) { return error.ConstraintName; }
        }
        Assert.Equal("CK_Tasks_Lease", await Violation("""UPDATE work."Tasks" SET "Status"='Running' WHERE "Id"=@id"""));
        Assert.Equal("CK_Tasks_Completed", await Violation("""UPDATE work."Tasks" SET "Status"='Succeeded' WHERE "Id"=@id"""));
        Assert.Equal("CK_Tasks_Attempts", await Violation("""UPDATE work."Tasks" SET "AttemptCount"=9 WHERE "Id"=@id"""));
        Assert.Equal("CK_Tasks_Status", await Violation("""UPDATE work."Tasks" SET "Status"='Weird' WHERE "Id"=@id"""));
        Assert.Equal("CK_Tasks_Type", await Violation("""UPDATE work."Tasks" SET "Type"='AdsPower' WHERE "Id"=@id"""));
    }

    [PostgresFact]
    public async Task PayloadAndAuditNeverCarrySecrets()
    {
        var id = await Create("""{"version":1,"steps":2}""");
        var claimed = await ClaimOne("w");
        await Queue.FailAsync("w", id, claimed.Attempt, "x", "Authorization: Bearer abcdefghijklmnopqrstuvwxyz123456", true, default);
        await using var context = new CentralContingenciaDbContext(DbOptions);
        var dump = JsonSerializer.Serialize(await context.WorkTasks.AsNoTracking().Where(t => t.Id == id).ToListAsync())
            + JsonSerializer.Serialize(await context.WorkTaskEvents.AsNoTracking().Where(e => e.TaskId == id).ToListAsync());
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", dump);
        Assert.DoesNotContain("Bearer", dump);
    }
}

public class WorkQueueDatabaseFailureTests
{
    private static (IWorkTaskService Tasks, IWorkQueueService Queue) Unreachable()
    {
        var options = new DbContextOptionsBuilder<CentralContingenciaDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=never-used;Timeout=1;Command Timeout=1").Options;
        var queueOptions = new WorkQueueOptions();
        var store = new EfWorkTaskStore(options);
        return (new WorkTaskService(store, new EfExecutorStore(options), queueOptions, TimeProvider.System), new WorkQueueService(store, queueOptions, TimeProvider.System));
    }

    [Fact]
    public async Task DatabaseOutageSurfacesAsSanitizedUnavailable()
    {
        var (tasks, queue) = Unreachable();
        var request = new CreateWorkTaskRequest("SimulatedProcessing", JsonDocument.Parse("""{"version":1}""").RootElement.Clone(), null);
        var errors = new[]
        {
            await Assert.ThrowsAsync<WorkQueueException>(() => tasks.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), request, "c", default)),
            await Assert.ThrowsAsync<WorkQueueException>(() => tasks.GetAsync(Guid.NewGuid(), default)),
            await Assert.ThrowsAsync<WorkQueueException>(() => tasks.ListAsync(null, null, 1, 20, default)),
            await Assert.ThrowsAsync<WorkQueueException>(() => tasks.CancelAsync(Guid.NewGuid(), Guid.NewGuid(), default)),
            await Assert.ThrowsAsync<WorkQueueException>(() => queue.ClaimAsync("w", [WorkTaskType.SimulatedProcessing], 1, default)),
            await Assert.ThrowsAsync<WorkQueueException>(() => queue.HeartbeatAsync("w", Guid.NewGuid(), 1, default)),
            await Assert.ThrowsAsync<WorkQueueException>(() => queue.RecoverExpiredAsync(default)),
        };
        Assert.All(errors, e => { Assert.Equal(WorkQueueFailure.Unavailable, e.Failure); Assert.DoesNotContain("never-used", e.ToString().Split("--->")[0]); });
    }
}
