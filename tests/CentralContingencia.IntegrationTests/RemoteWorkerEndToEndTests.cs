using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Domain.WorkQueue;
using CentralContingencia.Infrastructure.Persistence;
using CentralContingencia.Worker;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CentralContingencia.IntegrationTests;

public sealed class SwitchableHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public volatile bool Down;
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return Down ? Task.FromException<HttpResponseMessage>(new HttpRequestException("rede indisponível (simulada)")) : base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Worker REAL (WorkerService + HttpWorkQueueClient + WorkerComposition no modo remoto) falando HTTPS com a API real em memória.
/// O worker não recebe string de conexão: todo acesso ao PostgreSQL acontece dentro da API.
/// </summary>
public class RemoteWorkerEndToEndTests(AuthFixture fixture) : IClassFixture<AuthFixture>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> factory = fixture.Factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("WorkQueue:LeaseSeconds", "5");
        builder.UseSetting("WorkQueue:ExecutorHeartbeatSeconds", "1");
        builder.UseSetting("WorkQueue:ExecutorOnlineSeconds", "2");
        builder.UseSetting("WorkQueue:RetryBaseDelaySeconds", "0");
        builder.UseSetting("WorkQueue:RetryMaxDelaySeconds", "0");
        builder.UseSetting("WorkQueue:RecoveryIntervalSeconds", "1");
    });
    private ChannelHarness? harness;
    private ChannelHarness H => harness ??= new(fixture, factory);
    private readonly List<(ServiceProvider Provider, WorkerService Worker)> workers = [];

    public async Task InitializeAsync() => await H.Truncate();

    public async Task DisposeAsync()
    {
        foreach (var (provider, worker) in workers)
        {
            try { await worker.StopAsync(CancellationToken.None); } catch (Exception) { }
            await provider.DisposeAsync();
        }
        if (harness is not null) await harness.DisposeAsync();
        await factory.DisposeAsync();
    }

    private sealed class Env : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "worker-test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private async Task<(WorkerService Worker, SwitchableHandler Network, ServiceProvider Provider)> StartWorker(string credential, int concurrency = 3)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Worker:Api:BaseUrl"] = "https://localhost", ["Worker:Api:Credential"] = credential,
            ["Worker:PollIntervalMs"] = "50", ["Worker:MaxConcurrency"] = concurrency.ToString(), ["Worker:HeartbeatSeconds"] = "1",
            ["Worker:ErrorBackoffMaxSeconds"] = "2", ["Worker:ShutdownGraceSeconds"] = "10",
        }).Build();
        var network = new SwitchableHandler(factory.Server.CreateHandler());
        var services = new ServiceCollection();
        services.AddLogging();
        WorkerComposition.Add(services, configuration, new Env(), http => http.ConfigurePrimaryHttpMessageHandler(() => network));
        var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<WorkerService>();
        workers.Add((provider, worker));
        await worker.StartAsync(CancellationToken.None);
        return (worker, network, provider);
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

    private async Task<string> StatusOf(HttpClient admin, Guid id) => (await ChannelHarness.Get(admin, $"/api/v1/tasks/{id}")).GetProperty("status").GetString()!;
    private async Task WaitStatus(HttpClient admin, Guid id, string status, int seconds = 30) => await WaitFor(async () => await StatusOf(admin, id) == status, seconds);

    [PostgresFact]
    public async Task RemoteWorkerExecutesEveryOutcomeOverHttpWithoutTouchingTheDatabase()
    {
        var admin = await H.Session();
        var (executorId, credential) = await ChannelHarness.Provision(admin);
        var success = await ChannelHarness.CreateTask(admin, """{"version":1,"steps":2,"stepDelayMs":20}""");
        var retry = await ChannelHarness.CreateTask(admin, """{"version":1,"behavior":"FailUntilAttempt","succeedOnAttempt":2}""", 3);
        var dead = await ChannelHarness.CreateTask(admin, """{"version":1,"behavior":"FailTransient"}""", 2);
        var permanent = await ChannelHarness.CreateTask(admin, """{"version":1,"behavior":"FailPermanent"}""");
        var (_, _, provider) = await StartWorker(credential);

        foreach (var (id, status) in new[] { (success, "Succeeded"), (retry, "Succeeded"), (dead, "DeadLetter"), (permanent, "Failed") }) await WaitStatus(admin, id, status);
        var ok = await ChannelHarness.Get(admin, $"/api/v1/tasks/{success}");
        Assert.Equal(2, ok.GetProperty("result").GetProperty("stepsCompleted").GetInt32());
        Assert.Equal(executorId, ok.GetProperty("executorId").GetGuid());
        Assert.StartsWith("exec-", ok.GetProperty("executorName").GetString());
        var retried = await ChannelHarness.Get(admin, $"/api/v1/tasks/{retry}");
        Assert.Equal(2, retried.GetProperty("attemptCount").GetInt32());
        Assert.Equal("simulated_transient", (await ChannelHarness.Get(admin, $"/api/v1/tasks/{dead}")).GetProperty("lastErrorCode").GetString());
        Assert.Equal("simulated_permanent", (await ChannelHarness.Get(admin, $"/api/v1/tasks/{permanent}")).GetProperty("lastErrorCode").GetString());

        // Presença do executor registrada pelo próprio worker.
        var view = await ChannelHarness.Get(admin, $"/api/v1/executors/{executorId}");
        Assert.Equal("Online", view.GetProperty("connection").GetString());
        Assert.NotEqual(JsonValueKind.Null, view.GetProperty("workerVersion").ValueKind);

        // O worker remoto não tem como falar com o PostgreSQL.
        Assert.Null(provider.GetService<DbContextOptions<CentralContingenciaDbContext>>());
        Assert.Null(provider.GetService<IWorkTaskStore>());
        Assert.Null(provider.GetService<IExecutorStore>());
    }

    [PostgresFact]
    public async Task CancellationReachesARunningTaskThroughTheHeartbeat()
    {
        var admin = await H.Session();
        var (_, credential) = await ChannelHarness.Provision(admin);
        var id = await ChannelHarness.CreateTask(admin, """{"version":1,"steps":50,"stepDelayMs":200}""");
        await StartWorker(credential);
        await WaitStatus(admin, id, "Running");
        Assert.Equal(HttpStatusCode.OK, (await ChannelHarness.Send(admin, HttpMethod.Post, $"/api/v1/tasks/{id}/cancel")).StatusCode);
        await WaitStatus(admin, id, "Cancelled", 20);
        Assert.Equal([WorkTaskAction.Created, WorkTaskAction.Claimed, WorkTaskAction.CancelRequested, WorkTaskAction.Cancelled], await H.Actions(id));
    }

    [PostgresFact]
    public async Task GracefulShutdownReturnsTheTaskAndMarksTheExecutorOffline()
    {
        var admin = await H.Session();
        var (executorId, credential) = await ChannelHarness.Provision(admin);
        var id = await ChannelHarness.CreateTask(admin, """{"version":1,"steps":50,"stepDelayMs":200}""");
        var (worker, _, _) = await StartWorker(credential);
        await WaitStatus(admin, id, "Running");
        await worker.StopAsync(CancellationToken.None);
        var task = await H.Load(id);
        Assert.Equal((WorkTaskStatus.Pending, 0), (task.Status, task.AttemptCount));
        Assert.Equal(WorkTaskAction.Released, (await H.Actions(id)).Last());
        var view = await ChannelHarness.Get(admin, $"/api/v1/executors/{executorId}");
        Assert.Equal(("Offline", "Offline"), (view.GetProperty("connection").GetString(), view.GetProperty("state").GetString()));
    }

    [PostgresFact]
    public async Task WorkerRestartGivesBackWhatTheDeadProcessHeldWithoutWaitingForTheLease()
    {
        var admin = await H.Session();
        var (_, credential) = await ChannelHarness.Provision(admin);
        var id = await ChannelHarness.CreateTask(admin, """{"version":1,"steps":1}""");
        // Processo anterior: reservou e morreu (sem heartbeat, sem release).
        var previous = H.Agent(credential);
        Assert.Single(await ChannelHarness.Claim(previous, 1));
        Assert.Equal("Running", await StatusOf(admin, id));

        var started = DateTime.UtcNow;
        await StartWorker(credential);
        await WaitStatus(admin, id, "Succeeded", 20);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(4.5), "Deveria concluir antes de o lease (5 s) vencer.");
        var actions = await H.Actions(id);
        Assert.Contains(WorkTaskAction.Released, actions);
        Assert.DoesNotContain(WorkTaskAction.LeaseExpired, actions);
        Assert.Equal(1, (await H.Load(id)).AttemptCount); // a liberação não consumiu a tentativa
    }

    [PostgresFact]
    public async Task TaskOfADeadExecutorIsRecoveredByTheApiAndFinishedByAnotherExecutor()
    {
        var admin = await H.Session();
        var (_, ghostCredential) = await ChannelHarness.Provision(admin);
        var (liveId, liveCredential) = await ChannelHarness.Provision(admin);
        var id = await ChannelHarness.CreateTask(admin, """{"version":1,"steps":1}""");
        Assert.Single(await ChannelHarness.Claim(H.Agent(ghostCredential), 1)); // fantasma: reserva e some
        await StartWorker(liveCredential);
        await WaitStatus(admin, id, "Succeeded", 40);
        var task = await H.Load(id);
        Assert.Equal(2, task.AttemptCount);
        Assert.Equal(liveId.ToString("D"), task.LastWorkerId);
        Assert.Contains(WorkTaskAction.LeaseExpired, await H.Actions(id));
    }

    [PostgresFact]
    public async Task ShortNetworkLossDoesNotInterruptARunningTask()
    {
        var admin = await H.Session();
        var (_, credential) = await ChannelHarness.Provision(admin);
        var id = await ChannelHarness.CreateTask(admin, """{"version":1,"steps":16,"stepDelayMs":250}""");
        var (_, network, _) = await StartWorker(credential);
        await WaitStatus(admin, id, "Running");
        network.Down = true;
        await Task.Delay(1800); // menos que o lease de 5 s
        network.Down = false;
        await WaitStatus(admin, id, "Succeeded", 30);
        Assert.Equal(1, (await H.Load(id)).AttemptCount);
    }

    [PostgresFact]
    public async Task LongNetworkLossLosesTheLeaseAndTheTaskIsReexecutedExactlyOnceMore()
    {
        var admin = await H.Session();
        var (_, credential) = await ChannelHarness.Provision(admin);
        var id = await ChannelHarness.CreateTask(admin, """{"version":1,"steps":10,"stepDelayMs":300}""");
        var (_, network, _) = await StartWorker(credential);
        await WaitStatus(admin, id, "Running");
        network.Down = true;
        await Task.Delay(8000); // mais que o lease (5 s): a API recupera a tarefa
        Assert.Equal("Pending", await StatusOf(admin, id));
        network.Down = false;
        await WaitStatus(admin, id, "Succeeded", 30);
        var task = await H.Load(id);
        Assert.Equal(2, task.AttemptCount);
        Assert.Single((await H.Actions(id)).Where(a => a == WorkTaskAction.Succeeded)); // o resultado atrasado da 1ª tentativa foi descartado
    }

    [PostgresFact]
    public async Task RevokingTheCredentialStopsTheWorkerFromClaimingMoreWork()
    {
        var admin = await H.Session();
        var (executorId, credential) = await ChannelHarness.Provision(admin);
        var (_, network, _) = await StartWorker(credential);
        await WaitFor(async () => (await ChannelHarness.Get(admin, $"/api/v1/executors/{executorId}")).GetProperty("connection").GetString() == "Online");
        Assert.Equal(HttpStatusCode.OK, (await ChannelHarness.Send(admin, HttpMethod.Post, $"/api/v1/executors/{executorId}/revoke")).StatusCode);
        await Task.Delay(500);
        var id = await ChannelHarness.CreateTask(admin);
        await Task.Delay(2500);
        Assert.Equal("Pending", await StatusOf(admin, id));
        Assert.True(network.Calls > 0);
        // O worker segue vivo (com backoff), sem derrubar o processo.
        Assert.Equal("Revoked", (await ChannelHarness.Get(admin, $"/api/v1/executors/{executorId}")).GetProperty("connection").GetString());
    }
}

public class AgentChannelResilienceTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    [PostgresFact]
    public async Task ExecutorRateLimitIsPerExecutorAndAnswersWithRetryAfter()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("Security:ExecutorPermitLimit", "5"));
        await using var h = new ChannelHarness(fixture, factory);
        var admin = await h.Session();
        var a = h.Agent((await ChannelHarness.Provision(admin)).Credential);
        var b = h.Agent((await ChannelHarness.Provision(admin)).Credential);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/v1/agent/config")).StatusCode);
        var limited = await a.GetAsync("/api/v1/agent/config");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter is not null);
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync("/api/v1/agent/config")).StatusCode);
    }

    [PostgresFact]
    public async Task AgentChannelIsNotStarvedByTheSharedPerIpLimit()
    {
        await using var normal = new ChannelHarness(fixture);
        var credential = (await ChannelHarness.Provision(await normal.Session())).Credential; // emitido antes de apertar o limite global
        await using var strict = new DeploymentFactory(new() { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString, ["Security:GlobalPermitLimit"] = "3" });
        await using var h = new ChannelHarness(fixture, strict);
        var agent = h.Agent(credential);
        // Pessoas e demais rotas continuam sob o limite por IP...
        using var anonymous = strict.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health")).StatusCode);
        var limited = await anonymous.GetAsync("/health");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.InRange(int.Parse(limited.Headers.GetValues("Retry-After").Single()), 1, 60);
        // ...mas o worker, mesmo IP, segue atendido (cota própria por executor).
        for (var i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync("/api/v1/agent/config")).StatusCode);
        // Sem cabeçalho Bearer a rota do agente responde 401 sem custo (nenhuma consulta ao banco); a autorização vem antes do limitador.
        using var bare = strict.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await bare.GetAsync("/api/v1/agent/config")).StatusCode);
    }

    [PostgresFact]
    public async Task DatabaseOutageIsA503WithRetryAfterNeverA401()
    {
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=never-used;Timeout=1;Command Timeout=1" };
        await using var factory = new DeploymentFactory(settings);
        using var client = factory.Https();
        client.DefaultRequestHeaders.Authorization = new("Bearer", $"ce1.{Guid.NewGuid():N}.{new string('A', 43)}");
        var response = await client.GetAsync("/api/v1/agent/config");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("never-used", text);
        Assert.DoesNotContain("Npgsql", text);
    }
}
