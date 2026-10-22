using System.Text.Json;
using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Domain.WorkQueue;
using CentralContingencia.Infrastructure;
using CentralContingencia.Infrastructure.Persistence;
using CentralContingencia.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CentralContingencia.IntegrationTests;

/// <summary>Worker real contra PostgreSQL real, com o executor simulado e o relógio do sistema (lease de 5 s, heartbeat de 1 s).</summary>
public class WorkerEndToEndTests(AuthFixture fixture) : IClassFixture<AuthFixture>, IAsyncLifetime
{
    private readonly Guid actor = Guid.NewGuid();
    private readonly List<(ServiceProvider Provider, WorkerService Worker)> workers = [];

    public async Task InitializeAsync()
    {
        await using var context = new CentralContingenciaDbContext(DbOptions);
        await context.Database.ExecuteSqlRawAsync("TRUNCATE work.\"Tasks\", audit.\"WorkTaskEvents\"");
    }

    public async Task DisposeAsync()
    {
        if (client is not null) await client.DisposeAsync();
        foreach (var (provider, worker) in workers)
        {
            try { await worker.StopAsync(CancellationToken.None); } catch (Exception) { }
            await provider.DisposeAsync();
        }
    }

    private DbContextOptions<CentralContingenciaDbContext> DbOptions => new DbContextOptionsBuilder<CentralContingenciaDbContext>().UseNpgsql(fixture.ConnectionString).Options;

    private ServiceProvider Build(string workerId, int concurrency = 3)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString,
            ["WorkQueue:LeaseSeconds"] = "5", ["WorkQueue:RetryBaseDelaySeconds"] = "0", ["WorkQueue:RetryMaxDelaySeconds"] = "0", ["WorkQueue:ExecutorHeartbeatSeconds"] = "1", ["WorkQueue:ExecutorOnlineSeconds"] = "2",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkerInfrastructure(configuration);
        services.AddSingleton<ITaskExecutor, SimulatedProcessingExecutor>();
        services.AddSingleton(new WorkerOptions { Mode = WorkerMode.LocalDatabase, WorkerId = workerId, PollIntervalMs = 50, MaxConcurrency = concurrency, HeartbeatSeconds = 1, RecoveryIntervalSeconds = 1, ShutdownGraceSeconds = 10 });
        services.AddSingleton<WorkerService>();
        return services.BuildServiceProvider();
    }

    private async Task<WorkerService> Start(string workerId, int concurrency = 3)
    {
        var provider = Build(workerId, concurrency);
        var worker = provider.GetRequiredService<WorkerService>();
        workers.Add((provider, worker));
        await worker.StartAsync(CancellationToken.None);
        return worker;
    }

    private ServiceProvider? client;
    private IWorkTaskService Tasks => (client ??= Build("client")).CreateScope().ServiceProvider.GetRequiredService<IWorkTaskService>();

    private async Task<Guid> Create(string payload, int? maxAttempts = null)
    {
        var request = new CreateWorkTaskRequest("SimulatedProcessing", JsonDocument.Parse(payload).RootElement.Clone(), maxAttempts);
        return (await Tasks.CreateAsync(actor, Guid.NewGuid(), request, "e2e", default)).Task.Id;
    }

    private async Task<WorkTask> Load(Guid id)
    {
        await using var context = new CentralContingenciaDbContext(DbOptions);
        return await context.WorkTasks.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    private async Task<List<WorkTaskEvent>> Events(Guid id)
    {
        await using var context = new CentralContingenciaDbContext(DbOptions);
        return await context.WorkTaskEvents.AsNoTracking().Where(e => e.TaskId == id).OrderBy(e => e.Sequence).ToListAsync();
    }

    private async Task WaitFor(Func<Task<bool>> condition, int seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }
        Assert.Fail("Condição não atingida a tempo.");
    }

    private Task<bool> IsTerminal(params Guid[] ids) => Task.Run(async () =>
    {
        foreach (var id in ids) if (!(await Load(id)).IsTerminal) return false;
        return true;
    });

    [PostgresFact]
    public async Task WorkerDrivesEveryOutcomeEndToEnd()
    {
        var success = await Create("""{"version":1,"steps":2,"stepDelayMs":20}""");
        var retry = await Create("""{"version":1,"behavior":"FailUntilAttempt","succeedOnAttempt":2}""", 3);
        var dead = await Create("""{"version":1,"behavior":"FailTransient"}""", 2);
        var permanent = await Create("""{"version":1,"behavior":"FailPermanent"}""");
        await Start("worker-e2e");
        await WaitFor(() => IsTerminal(success, retry, dead, permanent));

        var ok = await Load(success);
        Assert.Equal(WorkTaskStatus.Succeeded, ok.Status);
        using (var result = JsonDocument.Parse(ok.Result!)) Assert.Equal(2, result.RootElement.GetProperty("stepsCompleted").GetInt32());
        var retried = await Load(retry);
        Assert.Equal((WorkTaskStatus.Succeeded, 2), (retried.Status, retried.AttemptCount));
        Assert.Contains(WorkTaskAction.RetryScheduled, (await Events(retry)).Select(e => e.Action));
        var exhausted = await Load(dead);
        Assert.Equal((WorkTaskStatus.DeadLetter, 2, "simulated_transient"), (exhausted.Status, exhausted.AttemptCount, exhausted.LastErrorCode));
        var failed = await Load(permanent);
        Assert.Equal((WorkTaskStatus.Failed, 1, "simulated_permanent"), (failed.Status, failed.AttemptCount, failed.LastErrorCode));
        Assert.All(new[] { ok, retried, exhausted, failed }, t => { Assert.Null(t.WorkerId); Assert.Null(t.LeaseUntil); });
    }

    [PostgresFact]
    public async Task TwoWorkersNeverExecuteTheSameTaskTwice()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 30; i++) ids.Add(await Create("""{"version":1,"steps":1,"stepDelayMs":10}"""));
        await Start("worker-one", 2);
        await Start("worker-two", 2);
        await WaitFor(() => IsTerminal([.. ids]));
        foreach (var id in ids)
        {
            var task = await Load(id);
            Assert.Equal((WorkTaskStatus.Succeeded, 1), (task.Status, task.AttemptCount));
            Assert.Single((await Events(id)).Where(e => e.Action == WorkTaskAction.Claimed));
        }
    }

    [PostgresFact]
    public async Task RunningTaskIsCancelledThroughTheHeartbeat()
    {
        var id = await Create("""{"version":1,"steps":50,"stepDelayMs":200}""");
        await Start("worker-cancel");
        await WaitFor(async () => (await Load(id)).Status == WorkTaskStatus.Running);
        await Tasks.CancelAsync(actor, id, default);
        await WaitFor(async () => (await Load(id)).Status == WorkTaskStatus.Cancelled, 20);
        var actions = (await Events(id)).Select(e => e.Action).ToList();
        Assert.Equal([WorkTaskAction.Created, WorkTaskAction.Claimed, WorkTaskAction.CancelRequested, WorkTaskAction.Cancelled], actions);
        Assert.Equal(1, (await Load(id)).AttemptCount);
    }

    [PostgresFact]
    public async Task GracefulShutdownReturnsTheInFlightTaskToTheQueue()
    {
        var id = await Create("""{"version":1,"steps":50,"stepDelayMs":200}""");
        var worker = await Start("worker-stop");
        await WaitFor(async () => (await Load(id)).Status == WorkTaskStatus.Running);
        await worker.StopAsync(CancellationToken.None);
        var task = await Load(id);
        Assert.Equal((WorkTaskStatus.Pending, 0), (task.Status, task.AttemptCount));
        Assert.Null(task.WorkerId);
        Assert.Equal(WorkTaskAction.Released, (await Events(id)).Last().Action);
    }

    [PostgresFact]
    public async Task TaskAbandonedByADeadWorkerIsRecoveredAndFinishedByAnother()
    {
        var id = await Create("""{"version":1,"steps":1}""");
        using (var scope = Build("ghost").GetRequiredService<IServiceScopeFactory>().CreateScope())
        {
            // O "fantasma" reserva a tarefa e nunca mais dá sinal (lease real de 5 s).
            var claimed = await scope.ServiceProvider.GetRequiredService<IWorkQueueService>().ClaimAsync("ghost", [WorkTaskType.SimulatedProcessing], 1, default);
            Assert.Equal(id, Assert.Single(claimed).Id);
        }
        await Start("worker-rescue");
        await WaitFor(async () => (await Load(id)).Status == WorkTaskStatus.Succeeded, 40);
        var task = await Load(id);
        Assert.Equal(2, task.AttemptCount);
        var events = (await Events(id)).Select(e => (e.Action, e.WorkerId)).ToList();
        Assert.Contains((WorkTaskAction.LeaseExpired, "ghost"), events);
        Assert.Contains((WorkTaskAction.Succeeded, "worker-rescue"), events);
    }
}
