using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Domain.WorkQueue;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CentralContingencia.IntegrationTests;

/// <summary>Cliente HTTP de teste: sessão de administrador (cookie + CSRF) e clientes de executor (Bearer, sem cookie).</summary>
public sealed class ChannelHarness(AuthFixture fixture, WebApplicationFactory<Program>? factory = null) : IAsyncDisposable
{
    public WebApplicationFactory<Program> Factory { get; } = factory ?? fixture.Factory;
    private readonly List<HttpClient> clients = [];

    public static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object? body = null, string? key = null, bool csrf = true, string? raw = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (csrf && method != HttpMethod.Get && client.DefaultRequestHeaders.Authorization is null)
            request.Headers.Add("X-CSRF-TOKEN", (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString());
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        if (raw is not null) request.Content = new StringContent(raw, Encoding.UTF8, "application/json");
        else if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    public async Task<HttpClient> Session(string role = "Admin")
    {
        using var admin = fixture.Factory == Factory ? fixture.Client() : Factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        await Login(admin, fixture.AdminEmail);
        var email = fixture.AdminEmail;
        if (role != "Admin")
        {
            email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
            Assert.Equal(HttpStatusCode.Created, (await Send(admin, HttpMethod.Post, "/api/v1/users", new { email, password = fixture.Password, role })).StatusCode);
        }
        var client = Factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await Login(client, email);
        clients.Add(client);
        return client;
    }

    private async Task Login(HttpClient client, string email) =>
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, HttpMethod.Post, "/api/v1/auth/login", new { email, password = fixture.Password })).StatusCode);

    public HttpClient Agent(string? credential)
    {
        var client = Factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false, AllowAutoRedirect = false });
        if (credential is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        clients.Add(client);
        return client;
    }

    public static async Task<(Guid Id, string Credential)> Provision(HttpClient admin, string? name = null, string[]? types = null)
    {
        var response = await Send(admin, HttpMethod.Post, "/api/v1/executors", new { name = name ?? $"exec-{Guid.NewGuid():N}"[..20], allowedTypes = types });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("executor").GetProperty("id").GetGuid(), body.GetProperty("credential").GetString()!);
    }

    public static async Task<Guid> CreateTask(HttpClient admin, string payload = """{"version":1}""", int? maxAttempts = null)
    {
        var response = await Send(admin, HttpMethod.Post, "/api/v1/tasks", key: Guid.NewGuid().ToString(), raw: $$"""{"type":"SimulatedProcessing","payload":{{payload}}{{(maxAttempts is null ? "" : $",\"maxAttempts\":{maxAttempts}")}}}""");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public static async Task<List<JsonElement>> Claim(HttpClient agent, int max = 5)
    {
        var response = await Send(agent, HttpMethod.Post, "/api/v1/agent/tasks/claim", new { max });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tasks").EnumerateArray().ToList();
    }

    public static async Task<JsonElement> Get(HttpClient client, string path) => await client.GetFromJsonAsync<JsonElement>(path);

    public DbContextOptions<CentralContingenciaDbContext> Db => new DbContextOptionsBuilder<CentralContingenciaDbContext>().UseNpgsql(fixture.ConnectionString).Options;

    public async Task Truncate()
    {
        await using var context = new CentralContingenciaDbContext(Db);
        await context.Database.ExecuteSqlRawAsync("TRUNCATE work.\"Tasks\", audit.\"WorkTaskEvents\"");
    }

    public async Task<WorkTask> Load(Guid id)
    {
        await using var context = new CentralContingenciaDbContext(Db);
        return await context.WorkTasks.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    public async Task<List<WorkTaskAction>> Actions(Guid id)
    {
        await using var context = new CentralContingenciaDbContext(Db);
        return (await context.WorkTaskEvents.AsNoTracking().Where(e => e.TaskId == id).OrderBy(e => e.Sequence).ToListAsync()).Select(e => e.Action).ToList();
    }

    public async ValueTask DisposeAsync() { foreach (var client in clients) client.Dispose(); await ValueTask.CompletedTask; }
}

public class ExecutorProvisioningTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private ChannelHarness Harness => new(fixture);

    [PostgresFact]
    public async Task OnlyAdminsProvisionAndTheCredentialAppearsExactlyOnce()
    {
        await using var h = Harness;
        var admin = await h.Session(); var manager = await h.Session("Manager"); var operatorClient = await h.Session("Operator");
        var name = $"win-{Guid.NewGuid():N}"[..16];
        Assert.Equal(HttpStatusCode.Forbidden, (await ChannelHarness.Send(manager, HttpMethod.Post, "/api/v1/executors", new { name })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ChannelHarness.Send(operatorClient, HttpMethod.Post, "/api/v1/executors", new { name })).StatusCode);
        using var anonymous = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/executors")).StatusCode);

        var response = await ChannelHarness.Send(admin, HttpMethod.Post, "/api/v1/executors", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("executor").GetProperty("id").GetGuid();
        var credential = body.GetProperty("credential").GetString()!;
        Assert.StartsWith($"ce1.{id:N}.", credential);
        Assert.Equal(("Active", "Offline", "Offline"), (body.GetProperty("executor").GetProperty("status").GetString(), body.GetProperty("executor").GetProperty("connection").GetString(), body.GetProperty("executor").GetProperty("state").GetString()));
        Assert.Equal("SimulatedProcessing", body.GetProperty("executor").GetProperty("allowedTypes")[0].GetString());

        // Nenhuma leitura devolve a credencial, nem para o Admin; Manager e Operator leem a visão pública.
        foreach (var reader in new[] { admin, manager, operatorClient })
        {
            var list = await reader.GetStringAsync("/api/v1/executors");
            var detail = await reader.GetStringAsync($"/api/v1/executors/{id}");
            foreach (var text in new[] { list, detail })
            {
                Assert.DoesNotContain(credential, text);
                Assert.DoesNotContain("\"credential\":", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("hash", text, StringComparison.OrdinalIgnoreCase);
            }
            Assert.Contains(name, detail);
        }
    }

    [PostgresFact]
    public async Task OnlyTheHashIsPersistedAndProvisioningIsAudited()
    {
        await using var h = Harness;
        var admin = await h.Session();
        var (id, credential) = await ChannelHarness.Provision(admin);
        ExecutorCredentials.TryParse(credential, out _, out var secret);
        await using var context = new CentralContingenciaDbContext(h.Db);
        var row = await context.WorkExecutors.AsNoTracking().SingleAsync(e => e.Id == id);
        Assert.Equal(ExecutorCredentials.Hash(secret), row.CredentialHash);
        Assert.DoesNotContain(secret, row.CredentialHash);
        var columns = await context.Database.SqlQueryRaw<string>("""SELECT column_name AS "Value" FROM information_schema.columns WHERE table_schema='work' AND table_name='Executors'""").ToListAsync();
        Assert.DoesNotContain(columns, c => c.Contains("Secret", StringComparison.OrdinalIgnoreCase) || c == "Credential");
        var events = await context.ExecutorEvents.AsNoTracking().Where(e => e.ExecutorId == id).ToListAsync();
        Assert.Equal([ExecutorAction.Provisioned], events.Select(e => e.Action));
        Assert.DoesNotContain(secret, System.Text.Json.JsonSerializer.Serialize(events));
    }

    [PostgresTheory]
    [InlineData("""{"name":"ab"}""")]
    [InlineData("""{"name":""}""")]
    [InlineData("""{}""")]
    [InlineData("""{"name":"nome; drop table"}""")]
    [InlineData("""{"name":"nome valido","allowedTypes":[]}""")]
    [InlineData("""{"name":"nome valido","allowedTypes":["Unknown"]}""")]
    [InlineData("""{"name":"nome valido","allowedTypes":["0"]}""")]
    [InlineData("""{"name":"nome valido","status":"Revoked"}""")]
    [InlineData("""{"name":"nome valido","credentialHash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""")]
    [InlineData("""{""")]
    public async Task InvalidProvisioningBodiesAreRejected(string json)
    {
        await using var h = Harness;
        var admin = await h.Session();
        Assert.Equal(HttpStatusCode.BadRequest, (await ChannelHarness.Send(admin, HttpMethod.Post, "/api/v1/executors", raw: json)).StatusCode);
    }

    [PostgresFact]
    public async Task NamesAreUniqueIgnoringCaseAndProvisioningRequiresCsrf()
    {
        await using var h = Harness;
        var admin = await h.Session();
        var name = $"Nome-{Guid.NewGuid():N}"[..20];
        await ChannelHarness.Provision(admin, name);
        Assert.Equal(HttpStatusCode.Conflict, (await ChannelHarness.Send(admin, HttpMethod.Post, "/api/v1/executors", new { name = name.ToUpperInvariant() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ChannelHarness.Send(admin, HttpMethod.Post, "/api/v1/executors", new { name = "outro nome ok" }, csrf: false)).StatusCode);
    }

    [PostgresFact]
    public async Task RotationInvalidatesTheOldCredentialAtOnceAndRevocationIsFinal()
    {
        await using var h = Harness;
        var admin = await h.Session();
        var (id, oldCredential) = await ChannelHarness.Provision(admin);
        var agent = h.Agent(oldCredential);
        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync("/api/v1/agent/config")).StatusCode);

        var rotated = await ChannelHarness.Send(admin, HttpMethod.Post, $"/api/v1/executors/{id}/rotate");
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var newCredential = (await rotated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("credential").GetString()!;
        Assert.NotEqual(oldCredential, newCredential);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync("/api/v1/agent/config")).StatusCode);
        var fresh = h.Agent(newCredential);
        Assert.Equal(HttpStatusCode.OK, (await fresh.GetAsync("/api/v1/agent/config")).StatusCode);

        var revoked = await ChannelHarness.Send(admin, HttpMethod.Post, $"/api/v1/executors/{id}/revoke");
        Assert.Equal("Revoked", (await revoked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("connection").GetString());
        Assert.Equal(HttpStatusCode.OK, (await ChannelHarness.Send(admin, HttpMethod.Post, $"/api/v1/executors/{id}/revoke")).StatusCode); // idempotente
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.GetAsync("/api/v1/agent/config")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ChannelHarness.Send(admin, HttpMethod.Post, $"/api/v1/executors/{id}/rotate")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ChannelHarness.Send(admin, HttpMethod.Post, $"/api/v1/executors/{Guid.NewGuid()}/revoke")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ChannelHarness.Send(admin, HttpMethod.Post, $"/api/v1/executors/{Guid.NewGuid()}/rotate")).StatusCode);

        await using var context = new CentralContingenciaDbContext(h.Db);
        Assert.Equal([ExecutorAction.Provisioned, ExecutorAction.CredentialRotated, ExecutorAction.Revoked],
            (await context.ExecutorEvents.AsNoTracking().Where(e => e.ExecutorId == id).OrderBy(e => e.Sequence).ToListAsync()).Select(e => e.Action));
    }

    [PostgresFact]
    public async Task ManagersAndOperatorsCannotRotateOrRevoke()
    {
        await using var h = Harness;
        var admin = await h.Session(); var manager = await h.Session("Manager");
        var (id, credential) = await ChannelHarness.Provision(admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await ChannelHarness.Send(manager, HttpMethod.Post, $"/api/v1/executors/{id}/rotate")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ChannelHarness.Send(manager, HttpMethod.Post, $"/api/v1/executors/{id}/revoke")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Agent(credential).GetAsync("/api/v1/agent/config")).StatusCode);
    }
}

public class AgentAuthenticationTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private ChannelHarness Harness => new(fixture);

    [PostgresFact]
    public async Task InvalidCredentialsAreRejectedWithoutSayingWhy()
    {
        await using var h = Harness;
        var admin = await h.Session();
        var (id, credential) = await ChannelHarness.Provision(admin);
        ExecutorCredentials.TryParse(credential, out _, out var secret);
        var wrongSecret = $"ce1.{id:N}.{new string(secret.Reverse().ToArray())}";
        var unknownId = $"ce1.{Guid.NewGuid():N}.{secret}";
        var bodies = new List<(int Status, string? Title)>();
        foreach (var attempt in new string?[] { null, "x", "Basic abc", wrongSecret, unknownId, "ce1.curto.curto", credential + "x", credential.ToUpperInvariant() })
        {
            using var client = attempt is null ? h.Agent(null) : h.Agent("placeholder");
            if (attempt is not null) client.DefaultRequestHeaders.Authorization = attempt.StartsWith("Basic") ? new("Basic", "abc") : new("Bearer", attempt);
            var response = await client.GetAsync("/api/v1/agent/config");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(secret, text);
            bodies.Add(((int)response.StatusCode, JsonDocument.Parse(text).RootElement.GetProperty("title").GetString()));
        }
        Assert.Single(bodies.Distinct()); // a resposta é idêntica: não revela se o id existe nem se o segredo errou
    }

    [PostgresFact]
    public async Task CredentialsAndSessionsAreNotInterchangeable()
    {
        await using var h = Harness;
        var admin = await h.Session();
        var (_, credential) = await ChannelHarness.Provision(admin);
        // Sessão humana (cookie) não entra no canal do executor, nem com CSRF.
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/v1/agent/config")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ChannelHarness.Send(admin, HttpMethod.Post, "/api/v1/agent/tasks/claim", new { max = 1 })).StatusCode);
        // Credencial de executor não abre nenhuma rota de usuário.
        var agent = h.Agent(credential);
        foreach (var path in new[] { "/api/v1/tasks", "/api/v1/executors", "/api/v1/proxies", "/api/v1/auth/me", "/api/v1/profiles" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await agent.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ChannelHarness.Send(agent, HttpMethod.Post, "/api/v1/tasks", key: Guid.NewGuid().ToString(), raw: """{"type":"SimulatedProcessing","payload":{"version":1}}""")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ChannelHarness.Send(agent, HttpMethod.Post, "/api/v1/executors", new { name = "tentativa de escalada" })).StatusCode);
    }

    [PostgresFact]
    public async Task ConfigReflectsTheExecutorAndMarksPresence()
    {
        await using var h = Harness;
        var admin = await h.Session();
        var (id, credential) = await ChannelHarness.Provision(admin);
        var agent = h.Agent(credential);
        var config = await ChannelHarness.Get(agent, "/api/v1/agent/config");
        Assert.Equal(id, config.GetProperty("executorId").GetGuid());
        Assert.Equal("SimulatedProcessing", config.GetProperty("allowedTypes")[0].GetString());
        Assert.Equal(20, config.GetProperty("heartbeatSeconds").GetInt32());
        Assert.Equal(60, config.GetProperty("leaseSeconds").GetInt32());
        Assert.DoesNotContain("credential", config.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var beat = await ChannelHarness.Send(agent, HttpMethod.Post, "/api/v1/agent/heartbeat", new { state = "Busy", workerVersion = "1.2.3" });
        Assert.Equal(HttpStatusCode.OK, beat.StatusCode);
        var view = await ChannelHarness.Get(admin, $"/api/v1/executors/{id}");
        Assert.Equal(("Online", "Busy", "1.2.3"), (view.GetProperty("connection").GetString(), view.GetProperty("state").GetString(), view.GetProperty("workerVersion").GetString()));
        Assert.NotEqual(JsonValueKind.Null, view.GetProperty("lastSeenAt").ValueKind);
    }

    [PostgresTheory]
    [InlineData("""{"state":"Offline"}""")]
    [InlineData("""{"state":"Bogus"}""")]
    [InlineData("""{}""")]
    [InlineData("""{"state":"Idle","workerVersion":"1.0; DROP TABLE"}""")]
    [InlineData("""{"state":"Idle","executorId":"00000000-0000-0000-0000-000000000001"}""")]
    public async Task InvalidHeartbeatsAreRejected(string json)
    {
        await using var h = Harness;
        var (_, credential) = await ChannelHarness.Provision(await h.Session());
        Assert.Equal(HttpStatusCode.BadRequest, (await ChannelHarness.Send(h.Agent(credential), HttpMethod.Post, "/api/v1/agent/heartbeat", raw: json)).StatusCode);
    }

    [PostgresFact]
    public async Task OpenApiDescribesTheAgentChannelWithBearerAndWithoutCsrf()
    {
        await using var h = Harness;
        using var client = fixture.Client();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var claim = document.GetProperty("paths").GetProperty("/api/v1/agent/tasks/claim").GetProperty("post");
        Assert.DoesNotContain(claim.TryGetProperty("parameters", out var parameters) ? parameters.EnumerateArray() : [], p => p.GetProperty("name").GetString() == "X-CSRF-TOKEN");
        Assert.Equal("ExecutorKey", claim.GetProperty("security")[0].EnumerateObject().Single().Name);
        Assert.Equal("bearer", document.GetProperty("components").GetProperty("securitySchemes").GetProperty("ExecutorKey").GetProperty("scheme").GetString());
        Assert.True(document.GetProperty("paths").TryGetProperty("/api/v1/executors/{id}/rotate", out _));
    }
}

public class AgentTaskFlowTests(AuthFixture fixture) : IClassFixture<AuthFixture>, IAsyncLifetime
{
    private readonly TestClock clock = new();
    private ChannelHarness? harness;
    private WebApplicationFactory<Program>? factory;
    private ChannelHarness H => harness ??= new(fixture, factory ??= fixture.Factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<TimeProvider>(clock))));

    public async Task InitializeAsync() => await H.Truncate();
    public async Task DisposeAsync() { if (harness is not null) await harness.DisposeAsync(); }

    private async Task<(HttpClient Admin, HttpClient A, Guid IdA, HttpClient B, Guid IdB)> Setup()
    {
        var admin = await H.Session();
        var (idA, credentialA) = await ChannelHarness.Provision(admin);
        var (idB, credentialB) = await ChannelHarness.Provision(admin);
        return (admin, H.Agent(credentialA), idA, H.Agent(credentialB), idB);
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string path, object? body = null) => ChannelHarness.Send(client, HttpMethod.Post, "/api/v1/agent/" + path, body);

    [PostgresFact]
    public async Task ClaimDeliversTheTaskOnceAndShowsTheExecutorToOperators()
    {
        var (admin, a, idA, b, _) = await Setup();
        var taskId = await ChannelHarness.CreateTask(admin, """{"version":1,"steps":2}""");
        var claimed = Assert.Single(await ChannelHarness.Claim(a));
        Assert.Equal(taskId, claimed.GetProperty("id").GetGuid());
        Assert.Equal(1, claimed.GetProperty("attempt").GetInt32());
        Assert.Equal(2, claimed.GetProperty("payload").GetProperty("steps").GetInt32());
        Assert.Empty(await ChannelHarness.Claim(b)); // já está com A

        var view = await ChannelHarness.Get(admin, $"/api/v1/tasks/{taskId}");
        Assert.Equal(("Running", idA), (view.GetProperty("status").GetString(), view.GetProperty("executorId").GetGuid()));
        Assert.StartsWith("exec-", view.GetProperty("executorName").GetString());
        Assert.NotEqual(JsonValueKind.Null, view.GetProperty("lastActivityAt").ValueKind);
        Assert.DoesNotContain("leaseUntil", view.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var executor = await ChannelHarness.Get(admin, $"/api/v1/executors/{idA}");
        Assert.Equal(1, executor.GetProperty("runningTasks").GetInt32());
        Assert.Equal("Online", executor.GetProperty("connection").GetString());
    }

    [PostgresFact]
    public async Task ClaimIsRestrictedByAllowedTypesAndMax()
    {
        var (admin, a, _, _, _) = await Setup();
        await ChannelHarness.CreateTask(admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(a, "tasks/claim", new { types = new[] { "Bogus" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(a, "tasks/claim", new { max = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(a, "tasks/claim", new { max = 6 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(a, "tasks/claim", new { max = 1, executorId = Guid.NewGuid() })).StatusCode);
        var none = await Post(a, "tasks/claim", new { types = Array.Empty<string>() });
        Assert.Empty((await none.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tasks").EnumerateArray());
        Assert.Single(await ChannelHarness.Claim(a, 1));
    }

    [PostgresFact]
    public async Task ConcurrentExecutorsNeverShareATask()
    {
        var admin = await H.Session();
        var ids = new HashSet<Guid>();
        for (var i = 0; i < 24; i++) ids.Add(await ChannelHarness.CreateTask(admin));
        var agents = new List<HttpClient>();
        for (var i = 0; i < 6; i++) agents.Add(H.Agent((await ChannelHarness.Provision(admin)).Credential));
        var results = await Task.WhenAll(agents.Select(agent => Task.Run(async () =>
        {
            var mine = new List<Guid>();
            while (true)
            {
                var batch = await ChannelHarness.Claim(agent, 3);
                if (batch.Count == 0) return mine;
                mine.AddRange(batch.Select(t => t.GetProperty("id").GetGuid()));
            }
        })));
        var all = results.SelectMany(r => r).ToList();
        Assert.Equal(24, all.Count);
        Assert.Equal(ids, all.ToHashSet());
    }

    [PostgresFact]
    public async Task ExecutorsAreIsolatedFromEachOther()
    {
        var (admin, a, _, b, _) = await Setup();
        var taskId = await ChannelHarness.CreateTask(admin);
        await ChannelHarness.Claim(a);
        var attempt = new { attempt = 1 };
        // B não é dono: nada do que ele faz altera a tarefa de A.
        foreach (var (path, body) in new (string, object)[] { ($"tasks/{taskId}/heartbeat", attempt), ($"tasks/{taskId}/complete", new { attempt = 1, result = new { x = 1 } }),
            ($"tasks/{taskId}/fail", new { attempt = 1, code = "x", transient = false }), ($"tasks/{taskId}/release", attempt), ($"tasks/{taskId}/cancelled", attempt) })
        {
            var response = await Post(b, path, body);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("LeaseLost", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        Assert.Equal(HttpStatusCode.Conflict, (await b.GetAsync($"/api/v1/agent/tasks/{taskId}/cancellation?attempt=1")).StatusCode);
        Assert.Empty((await ChannelHarness.Get(b, "/api/v1/agent/tasks/held")).GetProperty("tasks").EnumerateArray());
        Assert.Single((await ChannelHarness.Get(a, "/api/v1/agent/tasks/held")).GetProperty("tasks").EnumerateArray());
        Assert.Equal(WorkTaskStatus.Running, (await H.Load(taskId)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(b, $"tasks/{Guid.NewGuid()}/heartbeat", attempt)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(a, $"tasks/{taskId}/complete", new { attempt = 1, result = new { ok = true } })).StatusCode);
    }

    [PostgresFact]
    public async Task HeartbeatRenewsTheLeaseAndAnExpiredLeaseLosesTheTask()
    {
        var (admin, a, _, b, _) = await Setup();
        var kept = await ChannelHarness.CreateTask(admin);
        await ChannelHarness.Claim(a, 1);
        clock.Advance(TimeSpan.FromSeconds(50));
        var beat = await Post(a, $"tasks/{kept}/heartbeat", new { attempt = 1 });
        Assert.Equal(HttpStatusCode.OK, beat.StatusCode);
        Assert.False((await beat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cancelRequested").GetBoolean());
        clock.Advance(TimeSpan.FromSeconds(50));
        using (var scope = H.Factory.Services.CreateScope()) Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IWorkQueueService>().RecoverExpiredAsync(default));
        Assert.Equal(HttpStatusCode.NoContent, (await Post(a, $"tasks/{kept}/complete", new { attempt = 1 })).StatusCode);

        var lost = await ChannelHarness.CreateTask(admin);
        await ChannelHarness.Claim(a, 1);
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(HttpStatusCode.Conflict, (await Post(a, $"tasks/{lost}/heartbeat", new { attempt = 1 })).StatusCode); // vencido, mesmo antes da recuperação
        Assert.Equal(HttpStatusCode.Conflict, (await Post(a, $"tasks/{lost}/complete", new { attempt = 1 })).StatusCode);
        using (var scope = H.Factory.Services.CreateScope()) Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IWorkQueueService>().RecoverExpiredAsync(default));
        clock.Advance(TimeSpan.FromSeconds(10));
        var retaken = Assert.Single(await ChannelHarness.Claim(b, 1));
        Assert.Equal(2, retaken.GetProperty("attempt").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(a, $"tasks/{lost}/complete", new { attempt = 1 })).StatusCode); // o antigo dono não consegue mais nada
        Assert.Equal(HttpStatusCode.NoContent, (await Post(b, $"tasks/{lost}/complete", new { attempt = 2 })).StatusCode);
        Assert.Equal([WorkTaskAction.Created, WorkTaskAction.Claimed, WorkTaskAction.LeaseExpired, WorkTaskAction.Claimed, WorkTaskAction.Succeeded], await H.Actions(lost));
    }

    [PostgresFact]
    public async Task DuplicateResultsAreIdempotentAndConflictingOnesAreRefused()
    {
        var (admin, a, _, b, _) = await Setup();
        var ok = await ChannelHarness.CreateTask(admin);
        await ChannelHarness.Claim(a, 1);
        var complete = new { attempt = 1, result = new { stepsCompleted = 2 } };
        Assert.Equal(HttpStatusCode.NoContent, (await Post(a, $"tasks/{ok}/complete", complete)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(a, $"tasks/{ok}/complete", complete)).StatusCode); // resposta perdida: repetição aceita
        Assert.Equal(HttpStatusCode.Conflict, (await Post(b, $"tasks/{ok}/complete", complete)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(a, $"tasks/{ok}/fail", new { attempt = 1, code = "x", transient = false })).StatusCode); // sucesso não vira falha
        Assert.Equal([WorkTaskAction.Created, WorkTaskAction.Claimed, WorkTaskAction.Succeeded], await H.Actions(ok));
        Assert.Equal(2, (await ChannelHarness.Get(admin, $"/api/v1/tasks/{ok}")).GetProperty("result").GetProperty("stepsCompleted").GetInt32());

        var failing = await ChannelHarness.CreateTask(admin);
        await ChannelHarness.Claim(a, 1);
        var fail = new { attempt = 1, code = "net", message = "falha", transient = true };
        var first = await Post(a, $"tasks/{failing}/fail", fail);
        var second = await Post(a, $"tasks/{failing}/fail", fail);
        Assert.Equal("Pending", (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal("Pending", (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(a, $"tasks/{failing}/fail", new { attempt = 1, code = "outro", transient = true })).StatusCode);
        Assert.Equal([WorkTaskAction.Created, WorkTaskAction.Claimed, WorkTaskAction.RetryScheduled], await H.Actions(failing));
    }

    [PostgresFact]
    public async Task RetriesBackOffAndExhaustIntoDeadLetter()
    {
        var (admin, a, _, _, _) = await Setup();
        var id = await ChannelHarness.CreateTask(admin, maxAttempts: 2);
        await ChannelHarness.Claim(a, 1);
        await Post(a, $"tasks/{id}/fail", new { attempt = 1, code = "net", transient = true });
        Assert.Empty(await ChannelHarness.Claim(a, 1)); // em backoff
        clock.Advance(TimeSpan.FromSeconds(6));
        await ChannelHarness.Claim(a, 1);
        var response = await Post(a, $"tasks/{id}/fail", new { attempt = 2, code = "net", transient = true });
        Assert.Equal("DeadLetter", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal("DeadLetter", (await ChannelHarness.Get(admin, $"/api/v1/tasks/{id}")).GetProperty("status").GetString());
    }

    [PostgresFact]
    public async Task CancellationReachesTheExecutorThroughHeartbeatAndTheReadOnlyQuery()
    {
        var (admin, a, _, _, _) = await Setup();
        var running = await ChannelHarness.CreateTask(admin);
        await ChannelHarness.Claim(a, 1);
        Assert.False((await ChannelHarness.Get(a, $"/api/v1/agent/tasks/{running}/cancellation?attempt=1")).GetProperty("cancelRequested").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await ChannelHarness.Send(admin, HttpMethod.Post, $"/api/v1/tasks/{running}/cancel")).StatusCode);
        Assert.True((await ChannelHarness.Get(a, $"/api/v1/agent/tasks/{running}/cancellation?attempt=1")).GetProperty("cancelRequested").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await a.GetAsync($"/api/v1/agent/tasks/{running}/cancellation")).StatusCode);
        var beat = await Post(a, $"tasks/{running}/heartbeat", new { attempt = 1 });
        Assert.True((await beat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cancelRequested").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await Post(a, $"tasks/{running}/cancelled", new { attempt = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(a, $"tasks/{running}/cancelled", new { attempt = 1 })).StatusCode); // repetição
        Assert.Equal("Cancelled", (await ChannelHarness.Get(admin, $"/api/v1/tasks/{running}")).GetProperty("status").GetString());

        var pending = await ChannelHarness.CreateTask(admin);
        await ChannelHarness.Send(admin, HttpMethod.Post, $"/api/v1/tasks/{pending}/cancel");
        Assert.Empty(await ChannelHarness.Claim(a));
    }

    [PostgresFact]
    public async Task ShutdownReturnsEverythingTheExecutorHoldsAndMarksItOffline()
    {
        var (admin, a, idA, b, _) = await Setup();
        var first = await ChannelHarness.CreateTask(admin); var second = await ChannelHarness.CreateTask(admin);
        Assert.Equal(2, (await ChannelHarness.Claim(a, 2)).Count);
        Assert.Equal(2, (await ChannelHarness.Get(a, "/api/v1/agent/tasks/held")).GetProperty("tasks").GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await Post(a, "shutdown")).StatusCode);
        foreach (var id in new[] { first, second })
        {
            var task = await H.Load(id);
            Assert.Equal((WorkTaskStatus.Pending, 0), (task.Status, task.AttemptCount));
        }
        var view = await ChannelHarness.Get(admin, $"/api/v1/executors/{idA}");
        Assert.Equal(("Offline", "Offline", 0), (view.GetProperty("connection").GetString(), view.GetProperty("state").GetString(), view.GetProperty("runningTasks").GetInt32()));
        Assert.Equal(2, (await ChannelHarness.Claim(b, 2)).Count);
    }

    [PostgresFact]
    public async Task PresenceExpiresWhenTheExecutorGoesSilent()
    {
        var (admin, a, idA, _, _) = await Setup();
        await Post(a, "heartbeat", new { state = "Idle", workerVersion = "1.0.0" });
        Assert.Equal("Online", (await ChannelHarness.Get(admin, $"/api/v1/executors/{idA}")).GetProperty("connection").GetString());
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal("Offline", (await ChannelHarness.Get(admin, $"/api/v1/executors/{idA}")).GetProperty("connection").GetString());
        var list = await ChannelHarness.Get(admin, "/api/v1/executors");
        Assert.Contains(list.EnumerateArray(), e => e.GetProperty("id").GetGuid() == idA && e.GetProperty("connection").GetString() == "Offline");
    }

    [PostgresFact]
    public async Task RevokedExecutorsCannotClaimOrChangeTheirTasks()
    {
        var (admin, a, idA, b, _) = await Setup();
        var taskId = await ChannelHarness.CreateTask(admin);
        await ChannelHarness.Claim(a, 1);
        Assert.Equal(HttpStatusCode.OK, (await ChannelHarness.Send(admin, HttpMethod.Post, $"/api/v1/executors/{idA}/revoke")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(a, "tasks/claim", new { max = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(a, $"tasks/{taskId}/heartbeat", new { attempt = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(a, $"tasks/{taskId}/complete", new { attempt = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post(a, "shutdown")).StatusCode);
        Assert.Equal(WorkTaskStatus.Running, (await H.Load(taskId)).Status);
        // O lease do revogado vence, a recuperação devolve a tarefa e outro executor a conclui.
        clock.Advance(TimeSpan.FromSeconds(61));
        using (var scope = H.Factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IWorkQueueService>().RecoverExpiredAsync(default);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Single(await ChannelHarness.Claim(b, 1));
    }

    [PostgresFact]
    public async Task BodiesAreBoundedAndStrict()
    {
        var (admin, a, _, _, _) = await Setup();
        var id = await ChannelHarness.CreateTask(admin);
        await ChannelHarness.Claim(a, 1);
        var big = new string('x', 5000);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(a, $"tasks/{id}/complete", new { attempt = 1, result = new { big } })).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await ChannelHarness.Send(a, HttpMethod.Post, $"/api/v1/agent/tasks/{id}/complete", raw: "{\"attempt\":1,\"result\":{\"x\":\"" + new string('y', 9000) + "\"}}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(a, $"tasks/{id}/complete", new { attempt = 1, status = "Failed" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(a, $"tasks/{id}/heartbeat", new { attempt = 1, leaseUntil = "2099-01-01T00:00:00Z" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(a, $"tasks/{id}/fail", new { attempt = 1, code = "x", transient = true, workerId = "outro" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await a.PostAsync($"/api/v1/agent/tasks/{id}/heartbeat", new StringContent("attempt=1", Encoding.UTF8, "text/plain"))).StatusCode);
        Assert.Equal(WorkTaskStatus.Running, (await H.Load(id)).Status);
    }

    [PostgresFact]
    public async Task FailureTextIsSanitizedBeforeItIsStored()
    {
        var (admin, a, _, _, _) = await Setup();
        var id = await ChannelHarness.CreateTask(admin);
        await ChannelHarness.Claim(a, 1);
        await Post(a, $"tasks/{id}/fail", new { attempt = 1, code = "Bad Code!", message = "password=hunter2 Host=db.internal token abcdefghijklmnopqrstuvwxyz0123456789", transient = false });
        var task = await H.Load(id);
        Assert.Equal("error", task.LastErrorCode);
        Assert.DoesNotContain("hunter2", task.LastError);
        Assert.DoesNotContain("db.internal", task.LastError);
        var view = (await admin.GetStringAsync($"/api/v1/tasks/{id}"));
        Assert.DoesNotContain("hunter2", view);
    }
}
