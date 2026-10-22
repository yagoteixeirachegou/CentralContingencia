using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Domain.WorkQueue;
using CentralContingencia.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CentralContingencia.UnitTests;

public sealed class FakeQueue : IWorkQueueService
{
    public List<string> Calls { get; } = [];
    public string? CompletedResult { get; private set; }
    public string? FailedCode { get; private set; }
    public string? FailedMessage { get; private set; }
    public bool? FailedTransient { get; private set; }
    public Func<bool> HeartbeatResult { get; set; } = () => false;
    public Exception? HeartbeatError { get; set; }
    public Func<int, Exception?> CompleteError { get; set; } = _ => null;
    private int completeCalls;
    private readonly object gate = new();

    public Task<IReadOnlyList<ClaimedWorkTask>> ClaimAsync(string workerId, IReadOnlyCollection<WorkTaskType> types, int max, CancellationToken token) => Task.FromResult<IReadOnlyList<ClaimedWorkTask>>([]);
    public Task<bool> HeartbeatAsync(string workerId, Guid id, int attempt, CancellationToken token)
    {
        Record("heartbeat");
        if (HeartbeatError is not null) throw HeartbeatError;
        return Task.FromResult(HeartbeatResult());
    }
    public Task CompleteAsync(string workerId, Guid id, int attempt, string? resultJson, CancellationToken token)
    {
        Record("complete"); CompletedResult = resultJson;
        var error = CompleteError(Interlocked.Increment(ref completeCalls));
        return error is null ? Task.CompletedTask : Task.FromException(error);
    }
    public Task<WorkTaskStatus> FailAsync(string workerId, Guid id, int attempt, string code, string? message, bool transient, CancellationToken token)
    {
        Record("fail"); FailedCode = code; FailedMessage = message; FailedTransient = transient;
        return Task.FromResult(WorkTaskStatus.Pending);
    }
    public Task ConfirmCancelledAsync(string workerId, Guid id, int attempt, CancellationToken token) { Record("cancelled"); return Task.CompletedTask; }
    public Task ReleaseAsync(string workerId, Guid id, int attempt, CancellationToken token) { Record("release"); return Task.CompletedTask; }
    public Task<int> RecoverExpiredAsync(CancellationToken token) => Task.FromResult(0);
    public Task<IReadOnlyList<ClaimedWorkTask>> HeldAsync(string workerId, CancellationToken token) => Task.FromResult<IReadOnlyList<ClaimedWorkTask>>([]);
    public Task<bool> IsCancelRequestedAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.FromResult(false);
    private void Record(string call) { lock (gate) Calls.Add(call); }
    public bool Called(string call) { lock (gate) return Calls.Contains(call); }
    public bool OnlyHeartbeats() { lock (gate) return Calls.All(c => c == "heartbeat"); }
}

public sealed class CapturingLogger : ILogger
{
    public List<string> Lines { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Lines) Lines.Add(formatter(state, exception) + (exception is null ? "" : " | " + exception));
    }
}

public sealed class DelegateExecutor(Func<TaskExecutionContext, CancellationToken, Task<TaskOutcome>> run) : ITaskExecutor
{
    public WorkTaskType Type => WorkTaskType.SimulatedProcessing;
    public Task<TaskOutcome> ExecuteAsync(TaskExecutionContext context, CancellationToken token) => run(context, token);
}

public class TaskRunnerTests
{
    private static readonly ClaimedWorkTask Task1 = new(Guid.NewGuid(), WorkTaskType.SimulatedProcessing, 1, """{"version":1}""", 1, 3, "corr-1", DateTimeOffset.UtcNow.AddMinutes(1));

    private static WorkerOptions Options() => new() { HeartbeatSeconds = 1, ShutdownGraceSeconds = 5, MaxConcurrency = 1 };

    private static TaskRunner Runner(FakeQueue queue, ITaskExecutor executor, CapturingLogger? logger = null) =>
        new(queue, new Dictionary<WorkTaskType, ITaskExecutor> { [WorkTaskType.SimulatedProcessing] = executor }, "w-test", Options(), TimeProvider.System, logger ?? new CapturingLogger());

    [Fact]
    public async Task SuccessCompletesWithSerializedTypedResult()
    {
        var queue = new FakeQueue();
        await Runner(queue, new DelegateExecutor((_, _) => Task.FromResult<TaskOutcome>(new TaskOutcome.Success(new SimulatedResult(2, 1, "Succeed"))))).RunAsync(Task1, CancellationToken.None);
        Assert.Equal(["complete"], queue.Calls);
        Assert.Equal("""{"stepsCompleted":2,"attempt":1,"behavior":"Succeed"}""", queue.CompletedResult);
    }

    [Fact]
    public async Task TransientAndPermanentFailuresAreReportedWithTheirKind()
    {
        var transient = new FakeQueue();
        await Runner(transient, new DelegateExecutor((_, _) => Task.FromResult<TaskOutcome>(new TaskOutcome.Failure("net", "x", true)))).RunAsync(Task1, CancellationToken.None);
        Assert.Equal(("net", true), (transient.FailedCode, transient.FailedTransient));
        var permanent = new FakeQueue();
        await Runner(permanent, new DelegateExecutor((_, _) => Task.FromResult<TaskOutcome>(new TaskOutcome.Failure("bad", "x", false)))).RunAsync(Task1, CancellationToken.None);
        Assert.Equal(("bad", false), (permanent.FailedCode, permanent.FailedTransient));
    }

    [Fact]
    public async Task ExecutorExceptionBecomesTransientFailureWithoutLeakingItsMessage()
    {
        var queue = new FakeQueue(); var logger = new CapturingLogger();
        await Runner(queue, new DelegateExecutor((_, _) => throw new InvalidOperationException("Host=db.internal;Password=hunter2")), logger).RunAsync(Task1, CancellationToken.None);
        Assert.Equal(("executor_error", true), (queue.FailedCode, queue.FailedTransient));
        Assert.DoesNotContain("hunter2", queue.FailedMessage);
        Assert.DoesNotContain(logger.Lines, line => line.Contains("hunter2") || line.Contains("db.internal"));
    }

    [Fact]
    public async Task OversizedResultIsRejectedAsPermanentFailure()
    {
        var queue = new FakeQueue();
        await Runner(queue, new DelegateExecutor((_, _) => Task.FromResult<TaskOutcome>(new TaskOutcome.Success(new string('x', TaskRunner.MaxResultBytes + 10))))).RunAsync(Task1, CancellationToken.None);
        Assert.Equal(("result_too_large", false), (queue.FailedCode, queue.FailedTransient));
        Assert.False(queue.Called("complete"));
    }

    [Fact]
    public async Task CancelRequestSeenByHeartbeatInterruptsExecutionAndConfirmsCancellation()
    {
        var queue = new FakeQueue { HeartbeatResult = () => true };
        var observed = false;
        await Runner(queue, new DelegateExecutor(async (_, token) =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(30), token); }
            catch (OperationCanceledException) { observed = true; throw; }
            return new TaskOutcome.Success(null);
        })).RunAsync(Task1, CancellationToken.None);
        Assert.True(observed);
        Assert.Contains("cancelled", queue.Calls);
        Assert.False(queue.Called("complete") || queue.Called("fail"));
    }

    [Fact]
    public async Task LeaseLostStopsExecutionAndDiscardsTheResult()
    {
        var queue = new FakeQueue { HeartbeatError = new WorkQueueException(WorkQueueFailure.LeaseLost) };
        var observed = false;
        await Runner(queue, new DelegateExecutor(async (_, token) =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(30), token); }
            catch (OperationCanceledException) { observed = true; throw; }
            return new TaskOutcome.Success(null);
        })).RunAsync(Task1, CancellationToken.None);
        Assert.True(observed);
        Assert.True(queue.OnlyHeartbeats());
    }

    [Fact]
    public async Task ShutdownReleasesTheTaskInsteadOfFailingIt()
    {
        var queue = new FakeQueue();
        using var stopping = new CancellationTokenSource();
        var started = new TaskCompletionSource();
        var run = Runner(queue, new DelegateExecutor(async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new TaskOutcome.Success(null);
        })).RunAsync(Task1, stopping.Token);
        await started.Task;
        await stopping.CancelAsync();
        await run;
        Assert.Equal("release", queue.Calls.Last());
        Assert.False(queue.Called("fail") || queue.Called("complete"));
    }

    [Fact]
    public async Task UnavailableReportIsRetriedAndLeaseLostIsTolerated()
    {
        var flaky = new FakeQueue { CompleteError = call => call < 3 ? new WorkQueueException(WorkQueueFailure.Unavailable) : null };
        await Runner(flaky, new DelegateExecutor((_, _) => Task.FromResult<TaskOutcome>(new TaskOutcome.Success(null)))).RunAsync(Task1, CancellationToken.None);
        Assert.Equal(3, flaky.Calls.Count(c => c == "complete"));
        var lost = new FakeQueue { CompleteError = _ => new WorkQueueException(WorkQueueFailure.LeaseLost) };
        await Runner(lost, new DelegateExecutor((_, _) => Task.FromResult<TaskOutcome>(new TaskOutcome.Success(null)))).RunAsync(Task1, CancellationToken.None);
        Assert.Single(lost.Calls, "complete");
    }

    [Fact]
    public async Task HeartbeatsKeepFlowingWhileTheTaskRuns()
    {
        var queue = new FakeQueue();
        await Runner(queue, new DelegateExecutor(async (_, token) => { await Task.Delay(TimeSpan.FromMilliseconds(2500), token); return new TaskOutcome.Success(null); })).RunAsync(Task1, CancellationToken.None);
        Assert.True(queue.Calls.Count(c => c == "heartbeat") >= 2);
    }

    [Fact]
    public async Task HeartbeatInfrastructureFailureDoesNotAbortTheTask()
    {
        var queue = new FakeQueue { HeartbeatError = new WorkQueueException(WorkQueueFailure.Unavailable) };
        await Runner(queue, new DelegateExecutor(async (_, token) => { await Task.Delay(TimeSpan.FromMilliseconds(1500), token); return new TaskOutcome.Success(null); })).RunAsync(Task1, CancellationToken.None);
        Assert.True(queue.Called("complete"));
    }
}

public class SimulatedExecutorTests
{
    private static TaskExecutionContext Context(string payload, int attempt = 1) => new(Guid.NewGuid(), WorkTaskType.SimulatedProcessing, 1, payload, attempt, 5, "c");
    private readonly SimulatedProcessingExecutor executor = new(TimeProvider.System);

    [Fact]
    public async Task SucceedsWithTypedResult()
    {
        var outcome = await executor.ExecuteAsync(Context("""{"version":1,"steps":3,"stepDelayMs":1}"""), CancellationToken.None);
        Assert.Equal(new TaskOutcome.Success(new SimulatedResult(3, 1, "Succeed")), outcome);
    }

    [Theory]
    [InlineData("FailTransient", true)]
    [InlineData("FailPermanent", false)]
    public async Task SimulatesFailures(string behavior, bool transient)
    {
        var outcome = Assert.IsType<TaskOutcome.Failure>(await executor.ExecuteAsync(Context($$"""{"version":1,"behavior":"{{behavior}}"}"""), CancellationToken.None));
        Assert.Equal(transient, outcome.Transient);
    }

    [Fact]
    public async Task FailUntilAttemptSucceedsOnTheConfiguredAttempt()
    {
        const string payload = """{"version":1,"behavior":"FailUntilAttempt","succeedOnAttempt":3}""";
        Assert.IsType<TaskOutcome.Failure>(await executor.ExecuteAsync(Context(payload, 1), CancellationToken.None));
        Assert.IsType<TaskOutcome.Failure>(await executor.ExecuteAsync(Context(payload, 2), CancellationToken.None));
        Assert.IsType<TaskOutcome.Success>(await executor.ExecuteAsync(Context(payload, 3), CancellationToken.None));
    }

    [Fact]
    public async Task InvalidStoredPayloadIsAPermanentFailure()
    {
        var outcome = Assert.IsType<TaskOutcome.Failure>(await executor.ExecuteAsync(Context("""{"version":9}"""), CancellationToken.None));
        Assert.False(outcome.Transient);
        Assert.Equal("invalid_payload", outcome.Code);
    }

    [Fact]
    public async Task HonorsCancellationBetweenSteps()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(Context("""{"version":1,"steps":50,"stepDelayMs":1000}"""), cts.Token));
    }
}

public class WorkerOptionsTests
{
    [Fact]
    public void DefaultsAreValid() => new WorkerOptions().Validate(new WorkQueueOptions());

    [Theory]
    [InlineData(0, 1, 20)]
    [InlineData(17, 1, 20)]
    [InlineData(2, 0, 20)]
    [InlineData(2, 1, 0)]
    public void OutOfRangeValuesAreRejected(int concurrency, int heartbeat, int recovery) =>
        Assert.Throws<InvalidOperationException>(() => new WorkerOptions { MaxConcurrency = concurrency, HeartbeatSeconds = heartbeat, RecoveryIntervalSeconds = recovery }.Validate(new WorkQueueOptions()));

    [Fact]
    public void HeartbeatMustFitThreeTimesInsideTheLease() =>
        Assert.Throws<InvalidOperationException>(() => new WorkerOptions { HeartbeatSeconds = 30 }.Validate(new WorkQueueOptions { LeaseSeconds = 60 }));

    [Fact]
    public void GeneratedWorkerIdsAreUniqueAndBounded()
    {
        var options = new WorkerOptions();
        var first = options.ResolveWorkerId(); var second = options.ResolveWorkerId();
        Assert.NotEqual(first, second);
        Assert.True(first.Length <= 100);
        Assert.Equal("fixo", new WorkerOptions { WorkerId = "fixo" }.ResolveWorkerId());
    }
}

public sealed class ScriptedQueue(Queue<ClaimedWorkTask> pending, int failuresBeforeWorking) : IWorkQueueService
{
    private int claimCalls;
    public int ClaimCalls => claimCalls;
    public List<string> Calls { get; } = [];
    public Task<IReadOnlyList<ClaimedWorkTask>> ClaimAsync(string workerId, IReadOnlyCollection<WorkTaskType> types, int max, CancellationToken token)
    {
        if (Interlocked.Increment(ref claimCalls) <= failuresBeforeWorking) throw new InvalidOperationException("Host=db.internal;Password=hunter2");
        var batch = new List<ClaimedWorkTask>();
        lock (pending) while (batch.Count < max && pending.TryDequeue(out var next)) batch.Add(next);
        return Task.FromResult<IReadOnlyList<ClaimedWorkTask>>(batch);
    }
    public Task<bool> HeartbeatAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.FromResult(false);
    public Task CompleteAsync(string workerId, Guid id, int attempt, string? resultJson, CancellationToken token) { lock (Calls) Calls.Add("complete"); return Task.CompletedTask; }
    public Task<WorkTaskStatus> FailAsync(string workerId, Guid id, int attempt, string code, string? message, bool transient, CancellationToken token) => Task.FromResult(WorkTaskStatus.Failed);
    public Task ConfirmCancelledAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.CompletedTask;
    public Task ReleaseAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.CompletedTask;
    public Task<int> RecoverExpiredAsync(CancellationToken token) => Task.FromResult(0);
    public Task<IReadOnlyList<ClaimedWorkTask>> HeldAsync(string workerId, CancellationToken token) => Task.FromResult<IReadOnlyList<ClaimedWorkTask>>([]);
    public Task<bool> IsCancelRequestedAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.FromResult(false);
}

public class WorkerServiceTests
{
    private static ClaimedWorkTask Item() => new(Guid.NewGuid(), WorkTaskType.SimulatedProcessing, 1, """{"version":1}""", 1, 3, "c", DateTimeOffset.UtcNow.AddMinutes(1));

    private static (WorkerService Worker, CapturingLogger Logger) Build(ScriptedQueue queue, ITaskExecutor executor, WorkerOptions options)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddScoped<IWorkQueueService>(_ => queue);
        var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var logger = new CapturingLogger();
        var factory = new LoggerAdapter(logger);
        return (new WorkerService(provider.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), [executor], options, TimeProvider.System, factory), logger);
    }

    private sealed class LoggerAdapter(CapturingLogger inner) : ILogger<WorkerService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => inner.Log(logLevel, eventId, state, exception, formatter);
    }

    [Fact]
    public async Task WorkerSurvivesDatabaseOutagesAndKeepsWorkingWithoutLeakingErrorDetails()
    {
        var queue = new ScriptedQueue(new([Item(), Item()]), failuresBeforeWorking: 2);
        var (worker, logger) = Build(queue, new DelegateExecutor((_, _) => Task.FromResult<TaskOutcome>(new TaskOutcome.Success(null))),
            new WorkerOptions { PollIntervalMs = 20, ErrorBackoffMaxSeconds = 1, HeartbeatSeconds = 1, RecoveryIntervalSeconds = 60, ShutdownGraceSeconds = 5 });
        await worker.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && queue.Calls.Count < 2) await Task.Delay(50);
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(2, queue.Calls.Count);
        Assert.True(queue.ClaimCalls > 2);
        Assert.Contains(logger.Lines, l => l.Contains("Falha ao consultar a fila"));
        Assert.DoesNotContain(logger.Lines, l => l.Contains("hunter2") || l.Contains("db.internal"));
    }

    [Fact]
    public async Task ConcurrencyNeverExceedsTheConfiguredLimit()
    {
        var queue = new ScriptedQueue(new(Enumerable.Range(0, 8).Select(_ => Item())), failuresBeforeWorking: 0);
        int current = 0, peak = 0;
        var executor = new DelegateExecutor(async (_, token) =>
        {
            var now = Interlocked.Increment(ref current);
            int seen; while ((seen = Volatile.Read(ref peak)) < now && Interlocked.CompareExchange(ref peak, now, seen) != seen) { }
            await Task.Delay(150, token);
            Interlocked.Decrement(ref current);
            return new TaskOutcome.Success(null);
        });
        var (worker, _) = Build(queue, executor, new WorkerOptions { PollIntervalMs = 20, MaxConcurrency = 2, HeartbeatSeconds = 1, RecoveryIntervalSeconds = 60, ShutdownGraceSeconds = 5 });
        await worker.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline && queue.Calls.Count < 8) await Task.Delay(50);
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(8, queue.Calls.Count);
        Assert.Equal(2, peak);
    }
}
