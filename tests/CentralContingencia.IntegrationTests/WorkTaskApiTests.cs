using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Domain.WorkQueue;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CentralContingencia.IntegrationTests;

public class WorkTaskApiTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private const string Valid = """{"type":"SimulatedProcessing","payload":{"version":1,"steps":2}}""";

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string? json = null, string? key = null, bool csrf = true, string contentType = "application/json")
    {
        using var request = new HttpRequestMessage(method, path);
        if (csrf && method != HttpMethod.Get)
            request.Headers.Add("X-CSRF-TOKEN", (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString());
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, new MediaTypeHeaderValue(contentType));
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> Create(HttpClient client, string json = Valid, string? key = null) =>
        Send(client, HttpMethod.Post, "/api/v1/tasks", json, key ?? Guid.NewGuid().ToString());

    private static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<HttpClient> Session(string role)
    {
        using var admin = fixture.Client();
        await Login(admin, fixture.AdminEmail);
        var email = fixture.AdminEmail;
        if (role != "Admin")
        {
            email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
            var created = await Send(admin, HttpMethod.Post, "/api/v1/users", JsonSerializer.Serialize(new { email, password = fixture.Password, role }));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }
        var client = fixture.Client();
        await Login(client, email);
        return client;
    }

    private async Task Login(HttpClient client, string email)
    {
        var response = await Send(client, HttpMethod.Post, "/api/v1/auth/login", JsonSerializer.Serialize(new { email, password = fixture.Password }));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [PostgresFact]
    public async Task AnonymousCallersAreRejected()
    {
        using var client = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/tasks/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Create(client)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, HttpMethod.Post, $"/api/v1/tasks/{Guid.NewGuid()}/cancel")).StatusCode);
    }

    [PostgresFact]
    public async Task AuthorizationFollowsTheOperationalScope()
    {
        using var admin = await Session("Admin");
        using var manager = await Session("Manager");
        using var operatorClient = await Session("Operator");
        var created = await Json(await Create(admin));
        var id = created.GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.Created, (await Create(manager)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Create(operatorClient)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(operatorClient, HttpMethod.Post, $"/api/v1/tasks/{id}/cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await operatorClient.GetAsync($"/api/v1/tasks/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await operatorClient.GetAsync("/api/v1/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Post, $"/api/v1/tasks/{id}/cancel")).StatusCode);
    }

    [PostgresFact]
    public async Task CreateReturnsThePublicViewWithoutInternalFields()
    {
        using var admin = await Session("Admin");
        var response = await Create(admin);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response);
        Assert.Equal($"/api/v1/tasks/{body.GetProperty("id").GetString()}", response.Headers.Location!.OriginalString);
        Assert.Equal("Pending", body.GetProperty("status").GetString());
        Assert.Equal("SimulatedProcessing", body.GetProperty("type").GetString());
        Assert.Equal(0, body.GetProperty("attemptCount").GetInt32());
        Assert.Equal(3, body.GetProperty("maxAttempts").GetInt32());
        Assert.Equal(2, body.GetProperty("payload").GetProperty("steps").GetInt32());
        Assert.False(body.GetProperty("cancelRequested").GetBoolean());
        var text = body.GetRawText();
        foreach (var forbidden in new[] { "workerId", "leaseUntil", "heartbeatAt", "fingerprint", "idempotencyKey", "createdBy", "\"lastError\"" })
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task IdempotencyKeyIsRequiredAndReplaysTheSameTask()
    {
        using var admin = await Session("Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, HttpMethod.Post, "/api/v1/tasks", Valid)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, HttpMethod.Post, "/api/v1/tasks", Valid, "não-é-uuid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, HttpMethod.Post, "/api/v1/tasks", Valid, Guid.Empty.ToString())).StatusCode);
        var key = Guid.NewGuid().ToString();
        var first = await Create(admin, key: key);
        var second = await Create(admin, key: key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((await Json(first)).GetProperty("id").GetString(), (await Json(second)).GetProperty("id").GetString());
        var different = await Create(admin, """{"type":"SimulatedProcessing","payload":{"version":1,"steps":5}}""", key);
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
    }

    [PostgresTheory]
    [InlineData("""{"type":"Unknown","payload":{"version":1}}""")]
    [InlineData("""{"type":"simulatedprocessing","payload":{"version":1}}""")]
    [InlineData("""{"type":0,"payload":{"version":1}}""")]
    [InlineData("""{"type":"0","payload":{"version":1}}""")]
    [InlineData("""{"payload":{"version":1}}""")]
    [InlineData("""{"type":"SimulatedProcessing"}""")]
    [InlineData("""{"type":"SimulatedProcessing","payload":[]}""")]
    [InlineData("""{"type":"SimulatedProcessing","payload":{"version":1,"password":"x"}}""")]
    [InlineData("""{"type":"SimulatedProcessing","payload":{"version":1,"cookies":"sessionid=abc"}}""")]
    [InlineData("""{"type":"SimulatedProcessing","payload":{"version":1,"steps":999}}""")]
    [InlineData("""{"type":"SimulatedProcessing","payload":{"version":7}}""")]
    [InlineData("""{"type":"SimulatedProcessing","payload":{"version":1},"maxAttempts":0}""")]
    [InlineData("""{"type":"SimulatedProcessing","payload":{"version":1},"maxAttempts":11}""")]
    [InlineData("""{"type":"SimulatedProcessing","payload":{"version":1},"status":"Succeeded"}""")]
    [InlineData("""{"type":"SimulatedProcessing","payload":{"version":1},"workerId":"w"}""")]
    [InlineData("""{"type":"SimulatedProcessing","payload":{"version":1},"attemptCount":0}""")]
    [InlineData("""{"type":"SimulatedProcessing","payload":{"version":1},"leaseUntil":"2030-01-01T00:00:00Z"}""")]
    [InlineData("""{""")]
    [InlineData("""null""")]
    public async Task InvalidBodiesAreRejectedWithoutEchoingContent(string json)
    {
        using var admin = await Session("Admin");
        var response = await Create(admin, json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sessionid", text);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task OversizedAndWronglyTypedBodiesAreRefused()
    {
        using var admin = await Session("Admin");
        var big = $$"""{"type":"SimulatedProcessing","payload":{"version":1},"padding":"{{new string('x', 5000)}}"}""";
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await Create(admin, big)).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await Send(admin, HttpMethod.Post, "/api/v1/tasks", Valid, Guid.NewGuid().ToString(), contentType: "text/plain")).StatusCode);
    }

    [PostgresFact]
    public async Task MutationsRequireCsrf()
    {
        using var admin = await Session("Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, HttpMethod.Post, "/api/v1/tasks", Valid, Guid.NewGuid().ToString(), csrf: false)).StatusCode);
        var id = (await Json(await Create(admin))).GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(admin, HttpMethod.Post, $"/api/v1/tasks/{id}/cancel", csrf: false)).StatusCode);
        Assert.Equal("Pending", (await Json(await admin.GetAsync($"/api/v1/tasks/{id}"))).GetProperty("status").GetString());
    }

    [PostgresFact]
    public async Task ClientCannotReachInternalOperations()
    {
        using var admin = await Session("Admin");
        var id = (await Json(await Create(admin))).GetProperty("id").GetString();
        foreach (var method in new[] { HttpMethod.Patch, HttpMethod.Put, HttpMethod.Delete })
        {
            var response = await Send(admin, method, $"/api/v1/tasks/{id}", """{"status":"Succeeded"}""");
            Assert.True(response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound, $"{method}: {response.StatusCode}");
        }
        foreach (var internalPath in new[] { "claim", "heartbeat", "complete", "fail" })
            Assert.Equal(HttpStatusCode.NotFound, (await Send(admin, HttpMethod.Post, $"/api/v1/tasks/{id}/{internalPath}", "{}")).StatusCode);
        Assert.Equal("Pending", (await Json(await admin.GetAsync($"/api/v1/tasks/{id}"))).GetProperty("status").GetString());
    }

    [PostgresFact]
    public async Task GetAndListBehave()
    {
        using var admin = await Session("Admin");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/tasks/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/tasks/not-a-guid")).StatusCode);
        var ids = new List<string>();
        for (var i = 0; i < 3; i++) ids.Add((await Json(await Create(admin))).GetProperty("id").GetString()!);
        await Send(admin, HttpMethod.Post, $"/api/v1/tasks/{ids[0]}/cancel");
        var page1 = await Json(await admin.GetAsync("/api/v1/tasks?pageSize=2"));
        Assert.Equal(2, page1.GetProperty("items").GetArrayLength());
        Assert.Equal(1, page1.GetProperty("page").GetInt32());
        Assert.True(page1.GetProperty("total").GetInt32() >= 3);
        var page2 = await Json(await admin.GetAsync("/api/v1/tasks?pageSize=2&page=2"));
        Assert.NotEqual(page1.GetProperty("items")[0].GetProperty("id").GetString(), page2.GetProperty("items")[0].GetProperty("id").GetString());
        var cancelled = await Json(await admin.GetAsync("/api/v1/tasks?status=Cancelled&type=SimulatedProcessing&pageSize=100"));
        Assert.Contains(cancelled.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetString() == ids[0]);
        Assert.DoesNotContain(cancelled.GetProperty("items").EnumerateArray(), item => item.GetProperty("status").GetString() != "Cancelled");
        foreach (var bad in new[] { "pageSize=0", "pageSize=101", "page=0", "page=10001", "status=Bogus", "type=Bogus" })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/v1/tasks?{bad}")).StatusCode);
    }

    [PostgresFact]
    public async Task CancelWorksForPendingIsIdempotentAndConflictsAfterCompletion()
    {
        using var admin = await Session("Admin");
        var id = (await Json(await Create(admin))).GetProperty("id").GetString();
        var first = await Send(admin, HttpMethod.Post, $"/api/v1/tasks/{id}/cancel");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("Cancelled", (await Json(first)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Send(admin, HttpMethod.Post, $"/api/v1/tasks/{id}/cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(admin, HttpMethod.Post, $"/api/v1/tasks/{Guid.NewGuid()}/cancel")).StatusCode);

        var doneId = Guid.Parse((await Json(await Create(admin))).GetProperty("id").GetString()!);
        using var scope = fixture.Factory.Services.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkQueueService>();
        var claimed = (await queue.ClaimAsync("api-test-worker", [WorkTaskType.SimulatedProcessing], 50, default)).Single(t => t.Id == doneId);
        await queue.CompleteAsync("api-test-worker", doneId, claimed.Attempt, null, default);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(admin, HttpMethod.Post, $"/api/v1/tasks/{doneId}/cancel")).StatusCode);
    }

    [PostgresFact]
    public async Task RunningCancelShowsAsRequestedUntilTheWorkerConfirms()
    {
        using var admin = await Session("Admin");
        var id = Guid.Parse((await Json(await Create(admin))).GetProperty("id").GetString()!);
        using var scope = fixture.Factory.Services.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkQueueService>();
        var claimed = (await queue.ClaimAsync("api-test-worker-2", [WorkTaskType.SimulatedProcessing], 50, default)).Single(t => t.Id == id);
        var cancel = await Json(await Send(admin, HttpMethod.Post, $"/api/v1/tasks/{id}/cancel"));
        Assert.Equal("Running", cancel.GetProperty("status").GetString());
        Assert.True(cancel.GetProperty("cancelRequested").GetBoolean());
        await queue.ConfirmCancelledAsync("api-test-worker-2", id, claimed.Attempt, default);
        Assert.Equal("Cancelled", (await Json(await admin.GetAsync($"/api/v1/tasks/{id}"))).GetProperty("status").GetString());
    }

    [PostgresFact]
    public async Task ErrorsAreExposedOnlyAsSanitizedCodes()
    {
        using var admin = await Session("Admin");
        var id = Guid.Parse((await Json(await Create(admin, """{"type":"SimulatedProcessing","payload":{"version":1}}""", Guid.NewGuid().ToString()))).GetProperty("id").GetString()!);
        using var scope = fixture.Factory.Services.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkQueueService>();
        var claimed = (await queue.ClaimAsync("api-test-worker-3", [WorkTaskType.SimulatedProcessing], 50, default)).Single(t => t.Id == id);
        await queue.FailAsync("api-test-worker-3", id, claimed.Attempt, "db_error", "Host=db.internal;Password=hunter2", false, default);
        var body = await admin.GetStringAsync($"/api/v1/tasks/{id}");
        Assert.Contains("\"lastErrorCode\":\"db_error\"", body);
        Assert.DoesNotContain("hunter2", body);
        Assert.DoesNotContain("db.internal", body);
        await using var context = new CentralContingenciaDbContext(new DbContextOptionsBuilder<CentralContingenciaDbContext>().UseNpgsql(fixture.ConnectionString).Options);
        var stored = await context.WorkTasks.AsNoTracking().SingleAsync(t => t.Id == id);
        Assert.DoesNotContain("hunter2", stored.LastError);
    }

    [PostgresFact]
    public async Task InfrastructureFailureBecomesASanitized503()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddScoped<IWorkTaskService, UnavailableTaskService>()));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        await Login(client, fixture.AdminEmail);
        foreach (var response in new[] { await client.GetAsync("/api/v1/tasks"), await Create(client), await Send(client, HttpMethod.Post, $"/api/v1/tasks/{Guid.NewGuid()}/cancel") })
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Npgsql", text);
            Assert.DoesNotContain("never-used", text);
        }
    }

    [PostgresFact]
    public async Task OpenApiDocumentsTheTaskEndpoints()
    {
        using var client = fixture.Client();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");
        var create = paths.GetProperty("/api/v1/tasks").GetProperty("post");
        Assert.Contains(create.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.Contains(create.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "X-CSRF-TOKEN");
        Assert.True(create.TryGetProperty("security", out _));
        Assert.True(paths.TryGetProperty("/api/v1/tasks/{id}/cancel", out _));
        Assert.True(paths.GetProperty("/api/v1/tasks").TryGetProperty("get", out _));
    }

    private sealed class UnavailableTaskService : IWorkTaskService
    {
        private static WorkQueueException Down() => new(WorkQueueFailure.Unavailable, new InvalidOperationException("Npgsql: Host=db.internal Password=never-used"));
        public Task<CreatedWorkTask> CreateAsync(Guid actorId, Guid idempotencyKey, CreateWorkTaskRequest request, string correlationId, CancellationToken token) => throw Down();
        public Task<WorkTaskResponse> GetAsync(Guid id, CancellationToken token) => throw Down();
        public Task<WorkTaskPage> ListAsync(string? status, string? type, int page, int pageSize, CancellationToken token) => throw Down();
        public Task<WorkTaskResponse> CancelAsync(Guid actorId, Guid id, CancellationToken token) => throw Down();
    }
}
