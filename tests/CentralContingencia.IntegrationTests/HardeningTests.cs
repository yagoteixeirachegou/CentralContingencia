using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.WorkQueue;
using CentralContingencia.Infrastructure.Persistence;
using CentralContingencia.Infrastructure.WorkQueue;
using CentralContingencia.Worker;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CentralContingencia.IntegrationTests;

public class AuthFailureThrottleHttpTests
{
    private const string Unreachable = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=never-used;Timeout=1;Command Timeout=1";
    private const string WellFormedUnknown = "ce1.00000000000000000000000000000001.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static WebApplicationFactory<Program> Factory(TestClock clock, Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = Unreachable, ["Security:AuthFailureLimit"] = "3", ["Security:AuthFailureWindowSeconds"] = "60", ["Security:GlobalPermitLimit"] = "1000" };
        if (extra is not null) foreach (var (key, value) in extra) settings[key] = value;
        return new DeploymentFactory(settings).WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(clock)));
    }

    private static async Task<HttpResponseMessage> Call(HttpClient client, string path, string bearer, string remote, string? forwardedFor = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-Remote-Ip", remote);
        if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        if (bearer.Length > 0) request.Headers.Add("Authorization", "Bearer " + bearer);
        return await client.SendAsync(request);
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    [Fact]
    public async Task InvalidExecutorCredentialsAreThrottledPerClientWithoutTouchingTheDatabase()
    {
        var clock = new TestClock();
        await using var factory = Factory(clock);
        using var client = Client(factory);
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Call(client, "/api/v1/agent/config", "ce1.curto.curto", "198.51.100.1")).StatusCode);
        // Bloqueado: mesmo uma credencial bem formada recebe 429 e NÃO chega ao banco (que está fora do ar: seria 503).
        var blocked = await Call(client, "/api/v1/agent/config", WellFormedUnknown, "198.51.100.1");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        var wait = int.Parse(blocked.Headers.GetValues("Retry-After").Single());
        Assert.InRange(wait, 1, 60);
        Assert.DoesNotContain("never-used", await blocked.Content.ReadAsStringAsync());
        // Outro cliente não é afetado: chega ao banco (indisponível → 503, nunca 401 nem 429).
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Call(client, "/api/v1/agent/config", WellFormedUnknown, "198.51.100.2")).StatusCode);
        // Passada a janela, o cliente volta a ser atendido.
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Call(client, "/api/v1/agent/config", WellFormedUnknown, "198.51.100.1")).StatusCode);
    }

    [Fact]
    public async Task RequestsWithoutCredentialsAreNotCountedAndTheOutageIsNotAFailure()
    {
        var clock = new TestClock();
        await using var factory = Factory(clock);
        using var client = Client(factory);
        for (var i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Call(client, "/api/v1/agent/config", "", "198.51.100.1")).StatusCode);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Call(client, "/api/v1/agent/config", WellFormedUnknown, "198.51.100.1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Call(client, "/api/v1/agent/config", "ce1.curto.curto", "198.51.100.1")).StatusCode);
    }

    [Fact]
    public async Task BehindATrustedProxyTheRealClientIsThrottledNotTheProxy()
    {
        var clock = new TestClock();
        await using var factory = Factory(clock, new() { ["ReverseProxy:Enabled"] = "true", ["ReverseProxy:KnownProxies:0"] = "10.0.0.5" });
        using var client = Client(factory);
        for (var i = 0; i < 3; i++) await Call(client, "/api/v1/agent/config", "ce1.curto.curto", "10.0.0.5", "203.0.113.9");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Call(client, "/api/v1/agent/config", WellFormedUnknown, "10.0.0.5", "203.0.113.9")).StatusCode);
        // Outro cliente atrás do MESMO proxy continua passando.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Call(client, "/api/v1/agent/config", WellFormedUnknown, "10.0.0.5", "203.0.113.10")).StatusCode);
    }

    [Fact]
    public async Task AnUntrustedSourceCannotEvadeOrFrameOthersWithForwardedFor()
    {
        var clock = new TestClock();
        await using var factory = Factory(clock, new() { ["ReverseProxy:Enabled"] = "true", ["ReverseProxy:KnownProxies:0"] = "10.0.0.5" });
        using var client = Client(factory);
        // Origem NÃO confiável varia o X-Forwarded-For a cada tentativa: o freio usa o IP real da conexão.
        for (var i = 0; i < 3; i++) await Call(client, "/api/v1/agent/config", "ce1.curto.curto", "198.51.100.7", $"203.0.113.{20 + i}");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Call(client, "/api/v1/agent/config", WellFormedUnknown, "198.51.100.7", "203.0.113.99")).StatusCode);
        // E não consegue incriminar um cliente legítimo: o XFF dele é ignorado, então o IP "203.0.113.50" nunca é contado.
        for (var i = 0; i < 6; i++) await Call(client, "/api/v1/agent/config", "ce1.curto.curto", "198.51.100.8", "203.0.113.50");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Call(client, "/api/v1/agent/config", WellFormedUnknown, "10.0.0.5", "203.0.113.50")).StatusCode);
    }

    [Fact]
    public async Task N8nIntegrationKeyAttemptsAreThrottledSeparatelyFromExecutors()
    {
        var clock = new TestClock();
        var good = "integration-key-" + Guid.NewGuid().ToString("N") + "-synthetic";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(good))).ToLowerInvariant();
        await using var factory = Factory(clock, new() { ["Integration:Keys:0:Id"] = Guid.NewGuid().ToString(), ["Integration:Keys:0:Name"] = "n8n-teste", ["Integration:Keys:0:KeyHash"] = hash, ["Integration:Keys:0:Scopes:0"] = "profile-ingestion" });
        using var client = Client(factory);
        const string path = "/api/v1/integration/profile-ingestions/availability";
        var wrong = "wrong-integration-key-" + new string('x', 20);
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Call(client, path, wrong, "198.51.100.1")).StatusCode);
        var blocked = await Call(client, path, wrong, "198.51.100.1");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.NotNull(blocked.Headers.RetryAfter);
        // Bloqueado inclusive com a chave correta (limitação conhecida de NAT/IP compartilhado): documentado no relatório.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Call(client, path, good, "198.51.100.1")).StatusCode);
        // Outro cliente com a chave correta passa da autenticação (o resto pode falhar por falta do AdsPower local: 502/503, nunca 401/429).
        var other = await Call(client, path, good, "198.51.100.2");
        Assert.DoesNotContain(other.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests });
        // O escopo do executor para o mesmo IP segue livre.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Call(client, "/api/v1/agent/config", WellFormedUnknown, "198.51.100.1")).StatusCode);
    }
}

public class ClaimIdempotencyTests(AuthFixture fixture) : WorkQueueHarness(fixture), IClassFixture<AuthFixture>
{
    private async Task<IReadOnlyList<ClaimedWorkTask>> Claim(string worker, Guid key, int max = 5) => await Queue.ClaimAsync(worker, [Simulated], max, key, default);

    [PostgresFact]
    public async Task ReplayingTheSameKeyReturnsTheSameTasksWithoutNewAttemptsOrEvents()
    {
        var first = await Create(); var second = await Create();
        var key = Guid.NewGuid();
        var original = await Claim("ex-1", key, 2);
        Assert.Equal(2, original.Count);
        var replay = await Claim("ex-1", key, 2);
        Assert.Equal(original.Select(t => t.Id).Order(), replay.Select(t => t.Id).Order()); // a ordem de entrega não é garantida
        Assert.All(replay, t => Assert.Equal(1, t.Attempt));
        foreach (var (before, after) in original.OrderBy(t => t.Id).Zip(replay.OrderBy(t => t.Id))) Assert.Equal(before.LeaseUntil, after.LeaseUntil, TimeSpan.FromMilliseconds(1)); // não renova nem altera o lease (PostgreSQL guarda microssegundos)
        foreach (var id in new[] { first, second }) Assert.Equal([WorkTaskAction.Created, WorkTaskAction.Claimed], await Actions(id));
    }

    [PostgresFact]
    public async Task ReplayNeverReservesOtherTasksWhileTheOriginalOnesAreHeld()
    {
        await Create(); await Create(); var third = await Create();
        var key = Guid.NewGuid();
        var original = await Claim("ex-1", key, 2);
        var replay = await Claim("ex-1", key, 5);
        Assert.Equal(2, replay.Count); // devolve só o que já segura; a terceira continua na fila
        Assert.Equal(WorkTaskStatus.Pending, (await Load(third)).Status);
        Assert.Equal(original.Select(t => t.Id).Order(), replay.Select(t => t.Id).Order());
    }

    [PostgresFact]
    public async Task AnotherExecutorCannotStealLeasesByReusingTheKey()
    {
        var id = await Create();
        var key = Guid.NewGuid();
        Assert.Single(await Claim("ex-1", key));
        Assert.Empty(await Claim("ex-2", key)); // nada para o outro: a tarefa não está disponível e não é entregue por chave
        var task = await Load(id);
        Assert.Equal(("ex-1", WorkTaskStatus.Running), (task.WorkerId, task.Status));
        Assert.Equal(1, task.AttemptCount);
        // O dono segue podendo concluir; o outro não.
        Assert.Equal(WorkQueueFailure.LeaseLost, await FailureOf(() => Queue.CompleteAsync("ex-2", id, 1, null, default)));
        await Queue.CompleteAsync("ex-1", id, 1, null, default);
    }

    [PostgresFact]
    public async Task ReplayAfterTheLeaseExpiredOrTheTaskFinishedDeliversNothingOld()
    {
        var done = await Create(); var expiring = await Create(); var later = await Create();
        var key = Guid.NewGuid();
        var claimed = await Claim("ex-1", key, 2);
        await Queue.CompleteAsync("ex-1", claimed.First(t => t.Id == done).Id, 1, null, default);
        Clock.Advance(TimeSpan.FromSeconds(61)); // o lease da segunda venceu
        var replay = await Claim("ex-1", key, 5);
        Assert.DoesNotContain(replay, t => t.Id == done || t.Id == expiring);
        Assert.Equal(WorkTaskStatus.Succeeded, (await Load(done)).Status);
        Assert.Equal(WorkTaskStatus.Running, (await Load(expiring)).Status); // aguarda a recuperação normal
        Assert.Contains(replay, t => t.Id == later); // e a chave reutilizada serve de chave de um claim novo
        Assert.Equal(1, replay.Count);
    }

    [PostgresFact]
    public async Task ConcurrentCallsWithTheSameKeyNeverDuplicateATask()
    {
        for (var i = 0; i < 12; i++) await Create();
        var key = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => Claim("ex-1", key, 3))));
        var all = results.SelectMany(r => r).ToList();
        // Reentregas podem repetir o que já era do executor, mas nenhuma tarefa é reservada duas vezes: cada uma tem exatamente 1 tentativa e 1 evento Claimed.
        foreach (var id in all.Select(t => t.Id).Distinct())
        {
            Assert.Equal(1, (await Load(id)).AttemptCount);
            Assert.Single((await Actions(id)).Where(a => a == WorkTaskAction.Claimed));
        }
    }

    [PostgresFact]
    public async Task EmptyKeyIsRejectedAndKeylessClaimsStillWork()
    {
        await Create();
        Assert.Equal(WorkQueueFailure.Validation, await FailureOf(() => Queue.ClaimAsync("ex-1", [Simulated], 1, Guid.Empty, default)));
        var plain = Assert.Single(await Queue.ClaimAsync("ex-1", [Simulated], 1, default));
        Assert.Null((await Load(plain.Id)).ClaimKey);
    }
}

public class ClaimIdempotencyHttpTests(AuthFixture fixture) : IClassFixture<AuthFixture>, IAsyncLifetime
{
    private ChannelHarness? harness;
    private ChannelHarness H => harness ??= new(fixture);
    public async Task InitializeAsync() => await H.Truncate();
    public async Task DisposeAsync() { if (harness is not null) await harness.DisposeAsync(); }

    private static async Task<List<JsonElement>> Claim(HttpClient agent, string? key, int max = 5)
    {
        var response = await ChannelHarness.Send(agent, HttpMethod.Post, "/api/v1/agent/tasks/claim", new { max }, key);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tasks").EnumerateArray().ToList();
    }

    [PostgresFact]
    public async Task LostClaimResponseIsRecoveredWithTheSameIdempotencyKey()
    {
        var admin = await H.Session();
        var (_, credentialA) = await ChannelHarness.Provision(admin);
        var (_, credentialB) = await ChannelHarness.Provision(admin);
        var a = H.Agent(credentialA); var b = H.Agent(credentialB);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) ids.Add(await ChannelHarness.CreateTask(admin));
        var key = Guid.NewGuid().ToString();

        var first = await Claim(a, key, 2);                 // "resposta perdida": o cliente não viu este resultado
        var replay = await Claim(a, key, 2);
        Assert.Equal(first.Select(t => t.GetProperty("id").GetGuid()).Order(), replay.Select(t => t.GetProperty("id").GetGuid()).Order());
        Assert.All(replay, t => Assert.Equal(1, t.GetProperty("attempt").GetInt32()));
        // Outro executor com a mesma chave não leva nada do que é de A, só o que está livre.
        var other = await Claim(b, key, 5);
        Assert.DoesNotContain(other, t => first.Any(f => f.GetProperty("id").GetGuid() == t.GetProperty("id").GetGuid()));
        Assert.Equal(1, other.Count);
        foreach (var id in first.Select(t => t.GetProperty("id").GetGuid()))
        {
            Assert.Single((await H.Actions(id)).Where(x => x == WorkTaskAction.Claimed));
            Assert.Equal(1, (await H.Load(id)).AttemptCount);
        }
    }

    [PostgresTheory]
    [InlineData("não-é-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task InvalidIdempotencyKeysAreRejected(string key)
    {
        var admin = await H.Session();
        var agent = H.Agent((await ChannelHarness.Provision(admin)).Credential);
        Assert.Equal(HttpStatusCode.BadRequest, (await ChannelHarness.Send(agent, HttpMethod.Post, "/api/v1/agent/tasks/claim", new { max = 1 }, key)).StatusCode);
    }

    [PostgresFact]
    public async Task ClaimsWithDifferentKeysGetDifferentTasks()
    {
        var admin = await H.Session();
        var agent = H.Agent((await ChannelHarness.Provision(admin)).Credential);
        for (var i = 0; i < 2; i++) await ChannelHarness.CreateTask(admin);
        var one = await Claim(agent, Guid.NewGuid().ToString(), 1); var two = await Claim(agent, Guid.NewGuid().ToString(), 1);
        Assert.NotEqual(one[0].GetProperty("id").GetGuid(), two[0].GetProperty("id").GetGuid());
    }
}

/// <summary>Descarta a resposta do claim DEPOIS de o servidor processá-lo (a falha clássica de rede), uma vez.</summary>
public sealed class DropClaimResponseHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public int Dropped;
    public int ClaimKeysSeen;
    public List<string> Keys { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/tasks/claim"))
        {
            var key = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : "";
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            lock (Keys) Keys.Add(key);
            if (Dropped == 0 && body.Contains("\"id\"")) { Dropped++; response.Dispose(); throw new HttpRequestException("resposta perdida (simulada)"); }
        }
        return response;
    }
}

public class LostClaimResponseEndToEndTests(AuthFixture fixture) : IClassFixture<AuthFixture>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> factory = fixture.Factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("WorkQueue:LeaseSeconds", "30");
        builder.UseSetting("WorkQueue:ExecutorHeartbeatSeconds", "1");
        builder.UseSetting("WorkQueue:ExecutorOnlineSeconds", "2");
        builder.UseSetting("WorkQueue:RecoveryIntervalSeconds", "1");
    });
    private ChannelHarness? harness;
    private ChannelHarness H => harness ??= new(fixture, factory);
    public async Task InitializeAsync() => await H.Truncate();
    public async Task DisposeAsync() { if (harness is not null) await harness.DisposeAsync(); await factory.DisposeAsync(); }

    private sealed class Env : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "worker-test";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    [PostgresFact]
    public async Task WorkerGetsItsReservedTaskBackAfterALostResponseWithoutWaitingForTheLease()
    {
        var admin = await H.Session();
        var (_, credential) = await ChannelHarness.Provision(admin);
        var id = await ChannelHarness.CreateTask(admin, """{"version":1,"steps":1}""");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Worker:Api:BaseUrl"] = "https://localhost", ["Worker:Api:Credential"] = credential, ["Worker:PollIntervalMs"] = "50",
            ["Worker:HeartbeatSeconds"] = "1", ["Worker:ErrorBackoffMaxSeconds"] = "1", ["Worker:ShutdownGraceSeconds"] = "5",
        }).Build();
        var handler = new DropClaimResponseHandler(factory.Server.CreateHandler());
        var services = new ServiceCollection();
        services.AddLogging();
        WorkerComposition.Add(services, configuration, new Env(), http => http.ConfigurePrimaryHttpMessageHandler(() => handler));
        await using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<WorkerService>();
        var started = DateTime.UtcNow;
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline && (await H.Load(id)).Status != WorkTaskStatus.Succeeded) await Task.Delay(100);
        }
        finally { await worker.StopAsync(CancellationToken.None); }

        var task = await H.Load(id);
        Assert.Equal((WorkTaskStatus.Succeeded, 1), (task.Status, task.AttemptCount)); // uma só execução
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(15), "Não deveria depender do vencimento do lease (30 s).");
        Assert.Equal(1, handler.Dropped);
        var actions = await H.Actions(id);
        Assert.Single(actions.Where(a => a == WorkTaskAction.Claimed));
        Assert.DoesNotContain(WorkTaskAction.LeaseExpired, actions);
        lock (handler.Keys) { Assert.True(handler.Keys.Count >= 2); Assert.Equal(handler.Keys[0], handler.Keys[1]); Assert.NotEqual("", handler.Keys[0]); }
    }
}

public class RecoveryHostTests(AuthFixture fixture) : IClassFixture<AuthFixture>, IAsyncLifetime
{
    private sealed class Offset(TimeSpan shift) : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + shift; }
    private DbContextOptions<CentralContingenciaDbContext> Db => new DbContextOptionsBuilder<CentralContingenciaDbContext>().UseNpgsql(fixture.ConnectionString).Options;

    public async Task InitializeAsync()
    {
        await using var context = new CentralContingenciaDbContext(Db);
        await context.Database.ExecuteSqlRawAsync("TRUNCATE work.\"Tasks\", audit.\"WorkTaskEvents\"");
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [PostgresFact]
    public async Task ApiRecoversExpiredLeasesOnItsOwnWithoutAnyHttpTraffic()
    {
        var past = new Offset(TimeSpan.FromSeconds(-120));
        var options = new WorkQueueOptions { LeaseSeconds = 60, RetryBaseDelaySeconds = 0, RetryMaxDelaySeconds = 0 };
        var store = new EfWorkTaskStore(Db);
        var tasks = new WorkTaskService(store, new EfExecutorStore(Db), options, past);
        var queue = new WorkQueueService(store, options, past);
        var request = new CreateWorkTaskRequest("SimulatedProcessing", JsonDocument.Parse("""{"version":1}""").RootElement.Clone(), null);
        var id = (await tasks.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), request, "c", default)).Task.Id;
        Assert.Single(await queue.ClaimAsync("dead-executor", [WorkTaskType.SimulatedProcessing], 1, default)); // lease de 60 s concedido 120 s atrás: vencido há 60 s
        Assert.Equal(WorkTaskStatus.Running, await StatusOf(id));

        await using var factory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("WorkQueue:RecoveryIntervalSeconds", "1");
            builder.UseSetting("WorkQueue:RetryBaseDelaySeconds", "0");
            builder.UseSetting("WorkQueue:RetryMaxDelaySeconds", "0");
        });
        _ = factory.Services; // só sobe o host: NENHUMA requisição HTTP é feita
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline && await StatusOf(id) == WorkTaskStatus.Running) await Task.Delay(100);
        Assert.Equal(WorkTaskStatus.Pending, await StatusOf(id));
        await using var context = new CentralContingenciaDbContext(Db);
        Assert.Contains(WorkTaskAction.LeaseExpired, (await context.WorkTaskEvents.AsNoTracking().Where(e => e.TaskId == id).ToListAsync()).Select(e => e.Action));
    }

    private async Task<WorkTaskStatus> StatusOf(Guid id)
    {
        await using var context = new CentralContingenciaDbContext(Db);
        return (await context.WorkTasks.AsNoTracking().SingleAsync(t => t.Id == id)).Status;
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("3601")]
    [InlineData("abc")]
    public void InvalidRecoveryIntervalStopsTheApiAtStartup(string value)
    {
        using var factory = new DeploymentFactory(new() { ["WorkQueue:RecoveryIntervalSeconds"] = value });
        Assert.ThrowsAny<Exception>(() => factory.Services);
    }
}

public class MigrationUpgradeTests
{
    private const string PreviousRelease = "20261016175350_AddProfileHealthChecks"; // último estado antes da Etapa 2A

    [PostgresFact]
    public async Task FullSequenceAppliesStepByStepAndMatchesTheModel()
    {
        await using var isolated = new AuthFixture { ProvisionAdmin = false, InitialMigration = "20261011015548_InitialIdentityAndAuthenticationAudit" };
        await isolated.InitializeAsync();
        try
        {
            using var scope = isolated.Factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
            var migrator = context.GetService<IMigrator>();
            var all = context.Database.GetMigrations().ToList();
            Assert.Equal(16, all.Count);
            Assert.Equal(all, all.Order().ToList()); // ordem cronológica = ordem de aplicação
            foreach (var migration in all.Skip(1)) await migrator.MigrateAsync(migration); // uma por vez, sem pular nenhuma
            Assert.Equal(all, (await context.Database.GetAppliedMigrationsAsync()).ToList());
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.False(context.Database.HasPendingModelChanges(), "O modelo EF diverge das migrations (falta gerar migration).");
            Assert.EndsWith("_AddClaimKey", all[^1]);
            Assert.True(all.IndexOf("20261021191536_AddWorkQueue") < all.IndexOf("20261021205430_AddExecutors"));
        }
        finally { }
    }

    [PostgresFact]
    public async Task UpgradeFromThePreviousReleasePreservesDataAndAddsTheNewObjects()
    {
        await using var isolated = new AuthFixture { InitialMigration = PreviousRelease }; // admin provisionado no esquema ANTIGO
        await isolated.InitializeAsync();
        try
        {
            using var scope = isolated.Factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
            Assert.Equal(PreviousRelease, (await context.Database.GetAppliedMigrationsAsync()).Last());
            var check = ProfileHealthCheck.Create(Guid.NewGuid(), "adspower-preserved", "Perfil sintético", "g1", 1, HealthCheckOutcome.Ok, DateTimeOffset.UtcNow);
            context.ProfileHealthChecks.Add(check);
            await context.SaveChangesAsync();
            var usersBefore = await context.Users.CountAsync();
            Assert.Equal(1, usersBefore);
            Assert.Empty(await SchemasPresent(isolated.ConnectionString, "work", "security")); // a versão anterior não tem esses esquemas

            await context.Database.MigrateAsync(); // o "deploy": aplica tudo o que falta
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.Equal(usersBefore, await context.Users.CountAsync());
            var preserved = await context.ProfileHealthChecks.AsNoTracking().SingleAsync(c => c.Id == check.Id);
            Assert.Equal(("adspower-preserved", "Perfil sintético"), (preserved.AdsPowerProfileId, preserved.ProfileName));
            Assert.Equal(["security", "work"], (await SchemasPresent(isolated.ConnectionString, "work", "security")).Order());

            // Os objetos novos funcionam sobre o banco que já tinha dados.
            var options = new WorkQueueOptions();
            var tasks = new WorkTaskService(new EfWorkTaskStore(context.GetService<DbContextOptions<CentralContingenciaDbContext>>()), new EfExecutorStore(context.GetService<DbContextOptions<CentralContingenciaDbContext>>()), options, TimeProvider.System);
            var created = await tasks.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), new("SimulatedProcessing", JsonDocument.Parse("""{"version":1}""").RootElement.Clone(), null), "c", default);
            Assert.Equal("Pending", created.Task.Status);
            var executors = new ExecutorAdminService(new EfExecutorStore(context.GetService<DbContextOptions<CentralContingenciaDbContext>>()), options, TimeProvider.System);
            Assert.NotEmpty((await executors.ProvisionAsync(Guid.NewGuid(), new("upgrade-test", null), default)).Credential);
        }
        finally { }
    }

    private static async Task<List<string>> SchemasPresent(string connection, params string[] names)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT nspname FROM pg_namespace WHERE nspname = ANY(@names)", db);
        command.Parameters.AddWithValue("names", names);
        var found = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) found.Add(reader.GetString(0));
        return found;
    }

    [PostgresFact]
    public async Task UpgradeScriptIsAdditiveAndTheIdempotentScriptCanRunTwice()
    {
        await using var isolated = new AuthFixture { ProvisionAdmin = false, InitialMigration = PreviousRelease };
        await isolated.InitializeAsync();
        try
        {
            using var scope = isolated.Factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
            var migrator = context.GetService<IMigrator>();
            var upgrade = migrator.GenerateScript(PreviousRelease, null);
            // Nenhuma migration desde a versão anterior remove ou reescreve dados existentes.
            Assert.DoesNotContain("DROP TABLE", upgrade, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DROP COLUMN", upgrade, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ALTER COLUMN", upgrade, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TRUNCATE", upgrade, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE FROM", upgrade.Replace("DELETE FROM \"__EFMigrationsHistory\"", ""), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE ", upgrade, StringComparison.OrdinalIgnoreCase);

            var idempotent = migrator.GenerateScript(null, null, MigrationsSqlGenerationOptions.Idempotent);
            await using var db = new NpgsqlConnection(isolated.ConnectionString);
            await db.OpenAsync();
            for (var run = 0; run < 2; run++)
            {
                await using var command = new NpgsqlCommand(idempotent, db);
                await command.ExecuteNonQueryAsync(); // 1ª: aplica o que falta; 2ª: nada a fazer, sem erro
            }
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        }
        finally { }
    }
}
