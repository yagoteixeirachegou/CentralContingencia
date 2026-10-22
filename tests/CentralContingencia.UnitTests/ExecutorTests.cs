using System.Net;
using System.Text;
using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Domain.WorkQueue;
using CentralContingencia.Infrastructure.Persistence;
using CentralContingencia.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CentralContingencia.UnitTests;

public class ExecutorCredentialTests
{
    [Fact]
    public void GeneratedCredentialHasTheDocumentedShapeAndOnlyItsHashIsDerivable()
    {
        var id = Guid.NewGuid();
        var (credential, hash) = ExecutorCredentials.Generate(id);
        Assert.StartsWith($"ce1.{id:N}.", credential);
        Assert.True(ExecutorCredentials.TryParse(credential, out var parsedId, out var secret));
        Assert.Equal(id, parsedId);
        Assert.Equal(43, secret.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.DoesNotContain(secret, hash);
        Assert.True(ExecutorCredentials.Verify(secret, hash));
    }

    [Fact]
    public void CredentialsAreUniqueAndVerifyOnlyWithTheirOwnSecret()
    {
        var (first, firstHash) = ExecutorCredentials.Generate(Guid.NewGuid());
        var (second, _) = ExecutorCredentials.Generate(Guid.NewGuid());
        Assert.NotEqual(first, second);
        ExecutorCredentials.TryParse(second, out _, out var otherSecret);
        Assert.False(ExecutorCredentials.Verify(otherSecret, firstHash));
        Assert.False(ExecutorCredentials.Verify("", firstHash));
        Assert.False(ExecutorCredentials.Verify(otherSecret, "curto"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer x")]
    [InlineData("ce1.zzzz.abc")]
    [InlineData("ce2.00000000000000000000000000000001.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("ce1.00000000000000000000000000000000.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("ce1.00000000000000000000000000000001.curto")]
    [InlineData("ce1.00000000000000000000000000000001.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void MalformedCredentialsAreRejected(string? credential) => Assert.False(ExecutorCredentials.TryParse(credential, out _, out _));
}

public class WorkExecutorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Hash = new('a', 64);
    private static WorkExecutor New(string name = "win-adspower-01") => WorkExecutor.Provision(Guid.NewGuid(), name, [WorkTaskType.SimulatedProcessing], Hash, Guid.NewGuid(), T0);

    [Fact]
    public void ProvisionStartsActiveAndOffline()
    {
        var executor = New();
        Assert.Equal((ExecutorStatus.Active, ExecutorState.Offline), (executor.Status, executor.State));
        Assert.Equal("WIN-ADSPOWER-01", executor.NormalizedName);
        Assert.Equal([WorkTaskType.SimulatedProcessing], executor.AllowedTypeList);
        Assert.False(executor.IsOnline(T0, TimeSpan.FromMinutes(1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData(" nome")]
    [InlineData("nome ")]
    [InlineData("nome;drop")]
    [InlineData("nome\ncom quebra")]
    public void InvalidNamesAreRejected(string name) => Assert.Throws<ArgumentException>(() => New(name));

    [Fact]
    public void ProvisionRejectsBadInputs()
    {
        Assert.Throws<ArgumentException>(() => WorkExecutor.Provision(Guid.Empty, "nome ok", [WorkTaskType.SimulatedProcessing], Hash, Guid.NewGuid(), T0));
        Assert.Throws<ArgumentException>(() => WorkExecutor.Provision(Guid.NewGuid(), "nome ok", [], Hash, Guid.NewGuid(), T0));
        Assert.Throws<ArgumentException>(() => WorkExecutor.Provision(Guid.NewGuid(), "nome ok", [(WorkTaskType)99], Hash, Guid.NewGuid(), T0));
        Assert.Throws<ArgumentException>(() => WorkExecutor.Provision(Guid.NewGuid(), "nome ok", [WorkTaskType.SimulatedProcessing], "não-é-hash", Guid.NewGuid(), T0));
    }

    [Fact]
    public void SeenMakesTheExecutorOnlineOnlyWithinTheWindow()
    {
        var executor = New();
        executor.RecordSeen(ExecutorState.Busy, "1.2.3", T0);
        Assert.Equal(("1.2.3", ExecutorState.Busy), (executor.WorkerVersion, executor.State));
        Assert.True(executor.IsOnline(T0.AddSeconds(59), TimeSpan.FromSeconds(60)));
        Assert.False(executor.IsOnline(T0.AddSeconds(61), TimeSpan.FromSeconds(60)));
        executor.RecordSeen(ExecutorState.Offline, null, T0.AddSeconds(5));
        Assert.False(executor.IsOnline(T0.AddSeconds(6), TimeSpan.FromSeconds(60)));
        Assert.Equal("1.2.3", executor.WorkerVersion);
    }

    [Fact]
    public void InvalidVersionIsRejected() => Assert.Throws<ArgumentException>(() => New().RecordSeen(ExecutorState.Idle, "1.0; DROP", T0));

    [Fact]
    public void RotateReplacesTheHashAndRevokedExecutorsCannotRotateOrBeSeen()
    {
        var executor = New();
        executor.RotateCredential(new string('b', 64), T0.AddMinutes(1));
        Assert.Equal(new string('b', 64), executor.CredentialHash);
        Assert.Equal(T0.AddMinutes(1), executor.CredentialRotatedAt);
        executor.Revoke(Guid.NewGuid(), T0.AddMinutes(2));
        Assert.Equal(ExecutorError.Revoked, Assert.Throws<ExecutorStateException>(() => executor.RotateCredential(Hash, T0.AddMinutes(3))).Error);
        Assert.Equal(ExecutorError.Revoked, Assert.Throws<ExecutorStateException>(() => executor.RecordSeen(ExecutorState.Idle, null, T0.AddMinutes(3))).Error);
        Assert.False(executor.IsOnline(T0.AddMinutes(2), TimeSpan.FromHours(1)));
    }

    [Fact]
    public void RevokeIsFinalAndIdempotent()
    {
        var executor = New();
        executor.RecordSeen(ExecutorState.Busy, null, T0);
        var actor = Guid.NewGuid();
        executor.Revoke(actor, T0.AddMinutes(1));
        executor.Revoke(Guid.NewGuid(), T0.AddMinutes(9));
        Assert.Equal((ExecutorStatus.Revoked, actor, T0.AddMinutes(1), ExecutorState.Offline), (executor.Status, executor.RevokedBy, executor.RevokedAt, executor.State));
    }

    [Fact]
    public void AuditEventCarriesNoSecrets()
    {
        var executor = New();
        var evt = ExecutorEvent.Create(executor, ExecutorAction.Provisioned, Guid.NewGuid(), T0);
        Assert.Equal(executor.Id, evt.ExecutorId);
        Assert.DoesNotContain(executor.CredentialHash, evt.ToString());
    }
}

public class WorkTaskReplayTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static WorkTask Running(string worker = "ex-1", int max = 3)
    {
        var task = WorkTask.Create(Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), WorkTaskType.SimulatedProcessing, 1, "{\"version\":1}", max, "c", T0);
        task.Claim(worker, T0, TimeSpan.FromSeconds(60));
        return task;
    }

    [Fact]
    public void ClaimRemembersTheLastExecutorAfterTheLeaseEnds()
    {
        var task = Running();
        task.Succeed("ex-1", 1, T0.AddSeconds(1), null);
        Assert.Null(task.WorkerId);
        Assert.Equal("ex-1", task.LastWorkerId);
    }

    [Fact]
    public void DuplicateSuccessIsRecognizedOnlyForTheSameExecutorAndAttempt()
    {
        var task = Running();
        task.Succeed("ex-1", 1, T0.AddSeconds(1), null);
        Assert.True(task.IsSucceededBy("ex-1", 1));
        Assert.False(task.IsSucceededBy("ex-2", 1));
        Assert.False(task.IsSucceededBy("ex-1", 2));
    }

    [Fact]
    public void DuplicateFailureIsRecognizedForRetryDeadLetterAndPermanent()
    {
        var retry = Running();
        retry.Fail("ex-1", 1, T0.AddSeconds(1), "net", null, true, T0.AddSeconds(10));
        Assert.True(retry.IsFailureReportedBy("ex-1", 1, "net"));
        Assert.False(retry.IsFailureReportedBy("ex-1", 1, "other"));
        Assert.False(retry.IsFailureReportedBy("ex-2", 1, "net"));
        retry.Claim("ex-2", T0.AddSeconds(10), TimeSpan.FromSeconds(60));
        Assert.False(retry.IsFailureReportedBy("ex-1", 1, "net")); // outra tentativa em andamento: não é repetição
        var permanent = Running();
        permanent.Fail("ex-1", 1, T0.AddSeconds(1), "bad", null, false, T0);
        Assert.True(permanent.IsFailureReportedBy("ex-1", 1, "bad"));
        Assert.False(Running().IsFailureReportedBy("ex-1", 1, "x")); // ainda Running
    }

    [Fact]
    public void DuplicateCancellationConfirmationIsRecognized()
    {
        var task = Running();
        task.RequestCancel(Guid.NewGuid(), T0.AddSeconds(1));
        task.ConfirmCancelled("ex-1", 1, T0.AddSeconds(2));
        Assert.True(task.IsCancellationConfirmedBy("ex-1", 1));
        Assert.False(task.IsCancellationConfirmedBy("ex-2", 1));
    }
}

public sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Path, string? Body)> Requests { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request.Method, request.RequestUri!.AbsolutePath + request.RequestUri.Query, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
        return respond(request);
    }
}

public class HttpWorkQueueClientTests
{
    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static HttpWorkQueueClient Client(StubHandler handler) => new(new HttpClient(handler) { BaseAddress = new Uri("https://api.invalid/") });

    private static async Task<WorkQueueException> Fails(StubHandler handler) =>
        await Assert.ThrowsAsync<WorkQueueException>(() => Client(handler).CompleteAsync("ignored", Guid.NewGuid(), 1, null, default));

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, WorkQueueFailure.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, WorkQueueFailure.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound, WorkQueueFailure.NotFound)]
    [InlineData(HttpStatusCode.BadRequest, WorkQueueFailure.Validation)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, WorkQueueFailure.Validation)]
    [InlineData(HttpStatusCode.TooManyRequests, WorkQueueFailure.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, WorkQueueFailure.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, WorkQueueFailure.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, WorkQueueFailure.Unavailable)]
    public async Task HttpStatusIsMappedToTheQueueFailure(HttpStatusCode status, WorkQueueFailure expected) =>
        Assert.Equal(expected, (await Fails(new(_ => Json(status, """{"title":"x"}""")))).Failure);

    [Theory]
    [InlineData("""{"code":"LeaseLost"}""", WorkQueueFailure.LeaseLost)]
    [InlineData("""{"code":"Conflict"}""", WorkQueueFailure.Conflict)]
    [InlineData("não é json", WorkQueueFailure.Conflict)]
    public async Task ConflictDistinguishesLeaseLoss(string body, WorkQueueFailure expected) =>
        Assert.Equal(expected, (await Fails(new(_ => Json(HttpStatusCode.Conflict, body)))).Failure);

    [Fact]
    public async Task RetryAfterIsPreservedAndBounded()
    {
        var seconds = await Fails(new(_ => { var r = Json(HttpStatusCode.TooManyRequests, "{}"); r.Headers.RetryAfter = new(TimeSpan.FromSeconds(7)); return r; }));
        Assert.Equal(TimeSpan.FromSeconds(7), seconds.RetryAfter);
        var huge = await Fails(new(_ => { var r = Json(HttpStatusCode.ServiceUnavailable, "{}"); r.Headers.RetryAfter = new(TimeSpan.FromHours(5)); return r; }));
        Assert.Equal(TimeSpan.FromMinutes(5), huge.RetryAfter);
        var none = await Fails(new(_ => Json(HttpStatusCode.ServiceUnavailable, "{}")));
        Assert.Null(none.RetryAfter);
    }

    [Fact]
    public async Task NetworkFailuresBecomeUnavailableWithoutLeakingTheHost()
    {
        var error = await Fails(new(_ => throw new HttpRequestException("No such host is known: secret-host.internal")));
        Assert.Equal(WorkQueueFailure.Unavailable, error.Failure);
        Assert.DoesNotContain("secret-host", error.Message);
        var timeout = await Fails(new(_ => throw new TaskCanceledException("timeout")));
        Assert.Equal(WorkQueueFailure.Unavailable, timeout.Failure);
    }

    [Fact]
    public async Task CallerCancellationIsNotReportedAsAnOutage()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new StubHandler(_ => throw new TaskCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).CompleteAsync("x", Guid.NewGuid(), 1, null, cts.Token));
    }

    [Fact]
    public async Task ClaimSendsOnlyTypesAndMaxAndMapsTheTasks()
    {
        var id = Guid.NewGuid();
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, $$"""{"tasks":[{"id":"{{id}}","type":"SimulatedProcessing","payloadVersion":1,"payload":{"version":1,"steps":2},"attempt":2,"maxAttempts":3,"correlationId":"c1","leaseUntil":"2030-01-01T00:00:00Z"}]}"""));
        var claimed = Assert.Single(await Client(handler).ClaimAsync("ignorado", [WorkTaskType.SimulatedProcessing], 3, default));
        Assert.Equal((id, WorkTaskType.SimulatedProcessing, 2, 3), (claimed.Id, claimed.Type, claimed.Attempt, claimed.MaxAttempts));
        Assert.Equal("""{"version":1,"steps":2}""", claimed.Payload);
        var request = Assert.Single(handler.Requests);
        Assert.Equal((HttpMethod.Post, "/api/v1/agent/tasks/claim"), (request.Method, request.Path));
        Assert.Equal("""{"types":["SimulatedProcessing"],"max":3}""", request.Body);
        Assert.DoesNotContain("ignorado", request.Body);
    }

    [Fact]
    public async Task ResultFailAndLifecycleCallsUseTheAgentRoutes()
    {
        var id = Guid.NewGuid();
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/fail") ? Json(HttpStatusCode.OK, """{"status":"Pending"}""")
            : request.RequestUri.AbsolutePath.EndsWith("/heartbeat") ? Json(HttpStatusCode.OK, """{"cancelRequested":true}""") : new(HttpStatusCode.NoContent));
        var client = Client(handler);
        await client.CompleteAsync("x", id, 1, """{"stepsCompleted":2}""", default);
        Assert.Equal(WorkTaskStatus.Pending, await client.FailAsync("x", id, 1, "net", "falha", true, default));
        Assert.True(await client.HeartbeatAsync("x", id, 1, default));
        await client.ConfirmCancelledAsync("x", id, 1, default);
        await client.ReleaseAsync("x", id, 1, default);
        await client.ShutdownAsync(default);
        Assert.Equal(["/api/v1/agent/tasks/" + id + "/complete", "/api/v1/agent/tasks/" + id + "/fail", "/api/v1/agent/tasks/" + id + "/heartbeat",
            "/api/v1/agent/tasks/" + id + "/cancelled", "/api/v1/agent/tasks/" + id + "/release", "/api/v1/agent/shutdown"], handler.Requests.Select(r => r.Path));
        Assert.Equal("""{"attempt":1,"result":{"stepsCompleted":2}}""", handler.Requests[0].Body);
        Assert.Equal("""{"attempt":1,"code":"net","message":"falha","transient":true}""", handler.Requests[1].Body);
    }

    [Fact]
    public async Task RecoveryIsNotTheWorkersJobInRemoteMode() => Assert.Equal(0, await Client(new(_ => throw new InvalidOperationException())).RecoverExpiredAsync(default));
}

public class WorkerCompositionTests
{
    private const string Credential = "ce1.0123456789abcdef0123456789abcdef.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "worker";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    private static Dictionary<string, string?> Remote(string url = "https://central.example") => new() { ["Worker:Api:BaseUrl"] = url, ["Worker:Api:Credential"] = Credential };

    [Fact]
    public void RemoteModeNeverRegistersDatabaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        WorkerComposition.Add(services, Config(Remote()), new Env("Production"));
        using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<DbContextOptions<CentralContingenciaDbContext>>());
        Assert.Null(provider.GetService<CentralContingenciaDbContext>());
        Assert.Null(provider.GetService<IWorkTaskStore>());
        Assert.Null(provider.GetService<IExecutorStore>());
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(CentralContingenciaDbContext) || d.ServiceType == typeof(IWorkTaskStore));
        using var scope = provider.CreateScope();
        Assert.IsType<HttpWorkQueueClient>(scope.ServiceProvider.GetRequiredService<IWorkQueueService>());
        Assert.IsType<HttpWorkQueueClient>(scope.ServiceProvider.GetRequiredService<IExecutorSession>());
    }

    [Fact]
    public void RemoteModeRefusesAnyDatabaseConnectionString()
    {
        var values = Remote(); values["ConnectionStrings:DefaultConnection"] = "Host=db.internal;Password=hunter2";
        var error = Assert.Throws<InvalidOperationException>(() => WorkerComposition.Add(new ServiceCollection(), Config(values), new Env("Production")));
        Assert.DoesNotContain("hunter2", error.Message);
        Assert.DoesNotContain("db.internal", error.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void LocalDatabaseModeIsRefusedOutsideDevelopment(string environment)
    {
        var values = new Dictionary<string, string?> { ["Worker:Mode"] = "LocalDatabase", ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1" };
        Assert.Throws<InvalidOperationException>(() => WorkerComposition.Add(new ServiceCollection(), Config(values), new Env(environment)));
    }

    [Fact]
    public void LocalDatabaseModeWorksInDevelopmentAndRegistersTheStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        WorkerComposition.Add(services, Config(new() { ["Worker:Mode"] = "LocalDatabase", ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=x;Username=x" }), new Env("Development"));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IWorkTaskStore>());
        Assert.IsType<NullExecutorSession>(scope.ServiceProvider.GetRequiredService<IExecutorSession>());
    }

    [Theory]
    [InlineData("http://central.example")]
    [InlineData("ftp://central.example")]
    [InlineData("central.example")]
    [InlineData("https://user:senha@central.example")]
    [InlineData("https://central.example/?token=x")]
    [InlineData("")]
    public void BaseUrlMustBeHttpsWithoutSecrets(string url) =>
        Assert.Throws<InvalidOperationException>(() => WorkerComposition.Add(new ServiceCollection(), Config(Remote(url)), new Env("Production")));

    [Fact]
    public void PlainHttpIsAllowedOnlyForLoopbackInDevelopment()
    {
        WorkerComposition.Add(new ServiceCollection(), Config(Remote("http://localhost:5041")), new Env("Development"));
        Assert.Throws<InvalidOperationException>(() => WorkerComposition.Add(new ServiceCollection(), Config(Remote("http://localhost:5041")), new Env("Production")));
        Assert.Throws<InvalidOperationException>(() => WorkerComposition.Add(new ServiceCollection(), Config(Remote("http://central.example")), new Env("Development")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("senha-qualquer-super-secreta")]
    [InlineData("ce1.curto.curto")]
    public void MissingOrMalformedCredentialIsRefusedWithoutEchoingIt(string? credential)
    {
        var values = Remote(); values["Worker:Api:Credential"] = credential;
        var error = Assert.Throws<InvalidOperationException>(() => WorkerComposition.Add(new ServiceCollection(), Config(values), new Env("Production")));
        if (!string.IsNullOrEmpty(credential)) Assert.DoesNotContain(credential, error.Message);
    }

    [Fact]
    public void CredentialCanComeFromAFileOutsideTheConfiguration()
    {
        var path = Path.Combine(Path.GetTempPath(), "p6-cred-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, Credential + Environment.NewLine);
        try
        {
            var values = Remote(); values.Remove("Worker:Api:Credential"); values["Worker:Api:CredentialFile"] = path;
            WorkerComposition.Add(new ServiceCollection(), Config(values), new Env("Production"));
            values["Worker:Api:CredentialFile"] = path + ".missing";
            Assert.Throws<InvalidOperationException>(() => WorkerComposition.Add(new ServiceCollection(), Config(values), new Env("Production")));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DefaultModeIsRemoteAndOptionsNeverPrintTheCredential()
    {
        Assert.Equal(WorkerMode.Remote, new WorkerOptions().Mode);
        Assert.DoesNotContain("ce1", new WorkerApiOptions { Credential = Credential }.ToString());
    }
}

public sealed class FakeSession : IExecutorSession
{
    public Func<ExecutorConfig?> Config { get; set; } = () => new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "ex", ["SimulatedProcessing"], 1, 6, 5, DateTimeOffset.UtcNow);
    public Func<int, Exception?> ConfigError { get; set; } = _ => null;
    public List<string> Calls { get; } = [];
    private int configCalls;
    public Task<ExecutorConfig?> GetConfigAsync(CancellationToken token)
    {
        lock (Calls) Calls.Add("config");
        var error = ConfigError(Interlocked.Increment(ref configCalls));
        return error is null ? Task.FromResult(Config()) : Task.FromException<ExecutorConfig?>(error);
    }
    public Task HeartbeatAsync(ExecutorState state, string workerVersion, CancellationToken token) { lock (Calls) Calls.Add("presence:" + state); return Task.CompletedTask; }
    public Task ShutdownAsync(CancellationToken token) { lock (Calls) Calls.Add("shutdown"); return Task.CompletedTask; }
    public bool Has(string call) { lock (Calls) return Calls.Contains(call); }
}

public class RemoteWorkerServiceTests
{
    private sealed class QuietLogger : ILogger<WorkerService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private static WorkerService Build(IWorkQueueService queue, FakeSession session, WorkerOptions options)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => queue);
        services.AddScoped<IExecutorSession>(_ => session);
        var provider = services.BuildServiceProvider();
        return new WorkerService(provider.GetRequiredService<IServiceScopeFactory>(),
            [new DelegateExecutor((_, _) => Task.FromResult<TaskOutcome>(new TaskOutcome.Success(null)))], options, TimeProvider.System, new QuietLogger());
    }

    private static WorkerOptions Options() => new() { PollIntervalMs = 20, HeartbeatSeconds = 1, ErrorBackoffMaxSeconds = 2, ShutdownGraceSeconds = 5, MaxConcurrency = 2 };

    private static async Task Until(Func<bool> condition, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline && !condition()) await Task.Delay(25);
    }

    [Fact]
    public async Task NothingIsClaimedUntilTheHandshakeSucceedsAndRevokedCredentialsDoNotCrashTheWorker()
    {
        var queue = new ScriptedQueue(new(), 0);
        var session = new FakeSession { ConfigError = call => call <= 2 ? new WorkQueueException(WorkQueueFailure.Unauthorized) : null };
        var worker = Build(queue, session, Options());
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        Assert.Equal(0, queue.ClaimCalls); // ainda sem handshake aceito
        await Until(() => queue.ClaimCalls > 0, 20);
        await worker.StopAsync(CancellationToken.None);
        Assert.True(queue.ClaimCalls > 0);
        Assert.True(session.Calls.Count(c => c == "config") >= 3);
    }

    [Fact]
    public async Task HeartbeatIsLimitedByTheLeaseGrantedByTheServer()
    {
        var options = Options(); options.HeartbeatSeconds = 20;
        var worker = Build(new ScriptedQueue(new(), 0), new FakeSession(), options);
        await worker.StartAsync(CancellationToken.None);
        await Until(() => options.HeartbeatSeconds < 20);
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(2, options.HeartbeatSeconds); // lease 6 s / 3
    }

    [Fact]
    public async Task StartupReleasesWhatAPreviousRunLeftAndPresenceAndShutdownAreReported()
    {
        var queue = new HeldQueue();
        var session = new FakeSession();
        var worker = Build(queue, session, Options());
        await worker.StartAsync(CancellationToken.None);
        await Until(() => queue.Released.Count == 2 && session.Has("presence:Idle"));
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(2, queue.Released.Count);
        Assert.Equal("11111111-1111-1111-1111-111111111111", queue.HeldFor);
        Assert.True(session.Has("presence:Idle"));
        Assert.True(session.Has("shutdown"));
    }

    [Fact]
    public async Task RetryAfterFromTheServerIsRespectedWithJitter()
    {
        var stamps = new List<DateTime>();
        var queue = new TimingQueue(stamps);
        var options = Options(); options.ErrorBackoffMaxSeconds = 10;
        var worker = Build(queue, new FakeSession(), options);
        await worker.StartAsync(CancellationToken.None);
        await Until(() => { lock (stamps) return stamps.Count >= 2; }, 20);
        await worker.StopAsync(CancellationToken.None);
        var gap = stamps[1] - stamps[0];
        Assert.InRange(gap.TotalSeconds, 2.3, 4.0); // Retry-After 3 s ± 20% de jitter
    }

    private sealed class HeldQueue : IWorkQueueService
    {
        public List<Guid> Released { get; } = [];
        public string? HeldFor { get; private set; }
        public Task<IReadOnlyList<ClaimedWorkTask>> ClaimAsync(string workerId, IReadOnlyCollection<WorkTaskType> types, int max, CancellationToken token) => Task.FromResult<IReadOnlyList<ClaimedWorkTask>>([]);
        public Task<IReadOnlyList<ClaimedWorkTask>> HeldAsync(string workerId, CancellationToken token)
        {
            HeldFor = workerId;
            return Task.FromResult<IReadOnlyList<ClaimedWorkTask>>([Held(), Held()]);
        }
        private static ClaimedWorkTask Held() => new(Guid.NewGuid(), WorkTaskType.SimulatedProcessing, 1, "{}", 1, 3, "c", DateTimeOffset.UtcNow.AddMinutes(1));
        public Task ReleaseAsync(string workerId, Guid id, int attempt, CancellationToken token) { lock (Released) Released.Add(id); return Task.CompletedTask; }
        public Task<bool> HeartbeatAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.FromResult(false);
        public Task<bool> IsCancelRequestedAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.FromResult(false);
        public Task CompleteAsync(string workerId, Guid id, int attempt, string? resultJson, CancellationToken token) => Task.CompletedTask;
        public Task<WorkTaskStatus> FailAsync(string workerId, Guid id, int attempt, string code, string? message, bool transient, CancellationToken token) => Task.FromResult(WorkTaskStatus.Failed);
        public Task ConfirmCancelledAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.CompletedTask;
        public Task<int> RecoverExpiredAsync(CancellationToken token) => Task.FromResult(0);
    }

    private sealed class TimingQueue(List<DateTime> stamps) : IWorkQueueService
    {
        public Task<IReadOnlyList<ClaimedWorkTask>> ClaimAsync(string workerId, IReadOnlyCollection<WorkTaskType> types, int max, CancellationToken token)
        {
            lock (stamps) stamps.Add(DateTime.UtcNow);
            throw new WorkQueueException(WorkQueueFailure.Unavailable, null, TimeSpan.FromSeconds(3));
        }
        public Task<IReadOnlyList<ClaimedWorkTask>> HeldAsync(string workerId, CancellationToken token) => Task.FromResult<IReadOnlyList<ClaimedWorkTask>>([]);
        public Task<bool> HeartbeatAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.FromResult(false);
        public Task<bool> IsCancelRequestedAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.FromResult(false);
        public Task CompleteAsync(string workerId, Guid id, int attempt, string? resultJson, CancellationToken token) => Task.CompletedTask;
        public Task<WorkTaskStatus> FailAsync(string workerId, Guid id, int attempt, string code, string? message, bool transient, CancellationToken token) => Task.FromResult(WorkTaskStatus.Failed);
        public Task ConfirmCancelledAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.CompletedTask;
        public Task ReleaseAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.CompletedTask;
        public Task<int> RecoverExpiredAsync(CancellationToken token) => Task.FromResult(0);
    }
}
