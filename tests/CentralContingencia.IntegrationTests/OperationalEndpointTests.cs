using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CentralContingencia.Application.Allocations;
using CentralContingencia.Domain.Audit;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CentralContingencia.IntegrationTests;

[Collection("Authentication")]
public class OperationalEndpointTests(AuthFixture fixture) : IDisposable
{
    // Isolated functional host: do not consume rate-limit counters of authentication tests.
    private readonly AuthFactory operationsFactory = new(fixture.ConnectionString, permitLimit: 1000, globalPermitLimit: 10000);
    private HttpClient Client() => operationsFactory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    public void Dispose() => operationsFactory.Dispose();
    private static async Task Csrf(HttpClient client)
    { var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); }
    private async Task Login(HttpClient client, string role = "Admin")
    {
        var email = fixture.AdminEmail;
        if (role != "Admin")
        {
            using var admin = Client(); await Login(admin); email = $"ops-{Guid.NewGuid():N}@example.test";
            Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/v1/users", new { email, password = fixture.Password, role })).StatusCode);
        }
        await Csrf(client); Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = fixture.Password })).StatusCode); await Csrf(client);
    }
    private static object Profile(string? name = null) => new { name = name ?? $"Perfil sintético {Guid.NewGuid():N}", platform = "Sintética" };
    private static async Task<ProfileResponse> Create(HttpClient client, string? name = null)
    { using var response = await client.PostAsJsonAsync("/api/v1/profiles", Profile(name)); Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<ProfileResponse>())!; }
    private async Task<Guid> Proxy(ProxyStatus status = ProxyStatus.Active, bool expired = false)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-1); var id = Guid.NewGuid();
        var proxy = CentralContingencia.Domain.Proxies.Proxy.Create(id, ProxyHost.Create("operational.synthetic.test"), ProxyPort.Create(8080), "operational-synthetic-user",
            EncryptedProxyPassword.Create("ops-synthetic", RandomNumberGenerator.GetBytes(12), [1, 2], RandomNumberGenerator.GetBytes(16)), "BR", expired ? now.AddMinutes(-1) : now.AddDays(1), now);
        proxy.ChangeStatus(status, now);
        using var scope = fixture.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>(); db.Proxies.Add(proxy); await db.SaveChangesAsync(); return id;
    }
    private static async Task<HttpResponseMessage> Reserve(HttpClient client, Guid profile, Guid proxy, Guid? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/allocations/reserve")
        { Content = JsonContent.Create(new { profileId = profile, proxyId = proxy, reservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) }) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString()); return await client.SendAsync(request);
    }
    [PostgresTheory]
    [InlineData("Admin", 201)][InlineData("Manager", 201)][InlineData("Operator", 403)]
    public async Task RolesProtectEveryWriteAndAllowReads(string role, int status)
    {
        using var client = Client(); await Login(client, role);
        Assert.Equal(status, (int)(await client.PostAsJsonAsync("/api/v1/profiles", Profile())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/profiles")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/allocations")).StatusCode);
        if (role == "Operator")
        {
            var id = Guid.NewGuid();
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/v1/profiles/{id}", new { name = "Sintético" })).StatusCode);
            foreach (var path in new[] { $"/api/v1/profiles/{id}/close", $"/api/v1/allocations/{id}/confirm", $"/api/v1/allocations/{id}/release" })
                Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path, new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Reserve(client, id, id)).StatusCode);
        }
        else
        {
            var profile = await Create(client); var proxy = await Proxy();
            Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/v1/profiles/{profile.Id}", new { name = "Atualizado" })).StatusCode);
            using var reservation = await Reserve(client, profile.Id, proxy); Assert.Equal(HttpStatusCode.Created, reservation.StatusCode);
            var allocation = (await reservation.Content.ReadFromJsonAsync<AllocationResponse>())!;
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/v1/allocations/{allocation.Id}/confirm", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/v1/allocations/{allocation.Id}/release", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/v1/profiles/{profile.Id}/close", new { })).StatusCode);
        }
    }
    [PostgresFact]
    public async Task ProfileCrudPaginationAndHistoryUseRealContract()
    {
        using var client = Client(); await Login(client); var name = $"Nome sintético {Guid.NewGuid():N}";
        var profile = await Create(client, name);
        var loaded = (await client.GetFromJsonAsync<ProfileResponse>($"/api/v1/profiles/{profile.Id}"))!;
        Assert.Equal(profile.Id, loaded.Id); Assert.Equal(profile.Name, loaded.Name); Assert.Equal(profile.Platform, loaded.Platform);
        Assert.Equal(profile.Status, loaded.Status); Assert.InRange((profile.CreatedAt - loaded.CreatedAt).Duration(), TimeSpan.Zero, TimeSpan.FromTicks(10));
        var page = await client.GetFromJsonAsync<ProfilePage>("/api/v1/profiles?name=" + Uri.EscapeDataString(name) + "&pageSize=1&sort=name&direction=asc");
        Assert.Equal(1, page!.Total); Assert.Single(page.Items);
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/v1/profiles/{profile.Id}", new { platform = "Atualizada" })).StatusCode);
        var updated = await client.GetFromJsonAsync<ProfileResponse>($"/api/v1/profiles/{profile.Id}"); Assert.Equal(name, updated!.Name); Assert.Equal("Atualizada", updated.Platform);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
            Assert.Equal(1, await db.OperationalAuditEvents.CountAsync(e => e.ProfileId == profile.Id && e.Action == OperationalAction.ProfileUpdated));
        }
        Assert.Equal(0, (await client.GetFromJsonAsync<AllocationPage>($"/api/v1/profiles/{profile.Id}/allocations"))!.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/profiles/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/profiles/{Guid.NewGuid()}/allocations")).StatusCode);
    }
    [PostgresFact]
    public async Task AllocationClosureReuseAvailabilityAndSanitizedAuditAreConsistent()
    {
        using var client = Client(); await Login(client); var profile = await Create(client); var next = await Create(client); var proxy = await Proxy();
        using var reservation = await Reserve(client, profile.Id, proxy); Assert.Equal(HttpStatusCode.Created, reservation.StatusCode);
        var allocation = (await reservation.Content.ReadFromJsonAsync<AllocationResponse>())!;
        Assert.Equal("Reserved", allocation.Status);
        var proxyJson = await client.GetFromJsonAsync<JsonElement>($"/api/v1/proxies/{proxy}"); Assert.False(proxyJson.GetProperty("available").GetBoolean());
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/proxies?host=operational.synthetic.test&pageSize=100");
        Assert.False(list.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == proxy).GetProperty("available").GetBoolean());
        using var patch = await client.PatchAsJsonAsync($"/api/v1/proxies/{proxy}", new { region = "Sintética" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode); Assert.False((await patch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("available").GetBoolean());
        var available = await client.GetFromJsonAsync<JsonElement>("/api/v1/proxies/available?host=operational.synthetic.test&pageSize=100");
        Assert.DoesNotContain(available.GetProperty("items").EnumerateArray(), p => p.GetProperty("id").GetGuid() == proxy);
        Assert.Equal(HttpStatusCode.Conflict, (await Reserve(client, next.Id, proxy)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Reserve(client, profile.Id, await Proxy())).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/v1/allocations/{allocation.Id}/confirm", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/v1/profiles/{profile.Id}/close", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PatchAsJsonAsync($"/api/v1/profiles/{profile.Id}", new { name = "Alterado" })).StatusCode);
        Assert.True((await client.GetFromJsonAsync<JsonElement>($"/api/v1/proxies/{proxy}")).GetProperty("available").GetBoolean());
        using var reused = await Reserve(client, next.Id, proxy); Assert.Equal(HttpStatusCode.Created, reused.StatusCode);
        var history = await client.GetFromJsonAsync<AllocationPage>($"/api/v1/profiles/{profile.Id}/allocations?status=Released&sort=updatedAt&direction=asc");
        Assert.Equal("ProfileClosed", Assert.Single(history!.Items).EndReason);
        using var scope = fixture.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        var audits = await db.OperationalAuditEvents.Where(e => e.ProfileId == profile.Id).ToArrayAsync();
        Assert.Contains(audits, e => e.Action == OperationalAction.ProfileCreated); Assert.Contains(audits, e => e.Action == OperationalAction.AllocationReserved);
        Assert.Contains(audits, e => e.Action == OperationalAction.AllocationConfirmed); Assert.Contains(audits, e => e.Action == OperationalAction.AllocationReleased); Assert.Contains(audits, e => e.Action == OperationalAction.ProfileClosed);
        foreach (var text in new[] { JsonSerializer.Serialize(audits), JsonSerializer.Serialize(history), await reservation.Content.ReadAsStringAsync(), await reused.Content.ReadAsStringAsync() })
        { Assert.DoesNotContain("operational-synthetic-user", text); Assert.DoesNotContain("ciphertext", text.ToLowerInvariant()); Assert.DoesNotContain(profile.Name, text); }
    }
    [PostgresTheory]
    [InlineData(true)][InlineData(false)]
    public async Task ConcurrentRequestsHaveOneWinnerAndOneAuditedAllocation(bool sameProxy)
    {
        using var client = Client(); await Login(client); var first = await Create(client); var second = await Create(client); var p1 = await Proxy(); var p2 = await Proxy();
        var responses = await Task.WhenAll(Reserve(client, first.Id, p1), Reserve(client, sameProxy ? second.Id : first.Id, sameProxy ? p1 : p2));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var winner = (await responses.Single(r => r.StatusCode == HttpStatusCode.Created).Content.ReadFromJsonAsync<AllocationResponse>())!;
        using var scope = fixture.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        Assert.Equal(1, await db.OperationalAuditEvents.CountAsync(e => e.AllocationId == winner.Id && e.Action == OperationalAction.AllocationReserved));
        foreach (var response in responses) response.Dispose();
    }
    [PostgresTheory]
    [InlineData("Suspended")][InlineData("Disabled")][InlineData("Expired")]
    public async Task InvalidProxiesAndMissingProfilesAreRejected(string state)
    {
        using var client = Client(); await Login(client); var profile = await Create(client);
        var proxy = await Proxy(state == "Expired" ? ProxyStatus.Active : Enum.Parse<ProxyStatus>(state), state == "Expired");
        Assert.Equal(HttpStatusCode.Conflict, (await Reserve(client, profile.Id, proxy)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Reserve(client, Guid.NewGuid(), proxy)).StatusCode);
    }
    [PostgresFact]
    public async Task CsrfAndAnonymousRequestsCannotWrite()
    {
        using var anonymous = Client();
        foreach (var path in new[] { "/api/v1/profiles", "/api/v1/allocations", $"/api/v1/profiles/{Guid.NewGuid()}/allocations" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/profiles", Profile())).StatusCode);
        using var client = Client(); await Login(client); var profile = await Create(client); var proxy = await Proxy();
        using var reservation = await Reserve(client, profile.Id, proxy); var allocation = (await reservation.Content.ReadFromJsonAsync<AllocationResponse>())!;
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/profiles", Profile())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"/api/v1/profiles/{profile.Id}", new { name = "Falha" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reserve(client, profile.Id, proxy)).StatusCode);
        foreach (var path in new[] { $"/api/v1/profiles/{profile.Id}/close", $"/api/v1/allocations/{allocation.Id}/confirm", $"/api/v1/allocations/{allocation.Id}/release" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, new { })).StatusCode);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "synthetic-invalid-token"); Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/profiles", Profile())).StatusCode);
    }
    [PostgresFact]
    public async Task InvalidBodiesQueriesAndKeysAreSanitized()
    {
        using var client = Client(); await Login(client); var profile = await Create(client);
        foreach (var body in new[] { "{}", "{\"name\":null}", "{\"name\":\"a\",\"name\":\"b\"}", "{\"password\":\"synthetic-sensitive\"}", "{\"name\":5}" })
        {
            using var response = await client.PatchAsync($"/api/v1/profiles/{profile.Id}", new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.DoesNotContain("synthetic-sensitive", await response.Content.ReadAsStringAsync());
        }
        foreach (var query in new[] { "page=0", "pageSize=101", "sort=password", "direction=other", "status=Other" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/profiles?" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/allocations/reserve", new { profileId = profile.Id, proxyId = await Proxy(), reservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) })).StatusCode);
    }
    [PostgresFact]
    public async Task AuditFailureRollsBackProfileCreation()
    {
        using var client = Client(); await Login(client); var name = $"Rollback {Guid.NewGuid():N}";
        using var scope = fixture.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.\"OperationalEvents\" ADD CONSTRAINT test_operational_audit_failure CHECK (\"Action\" <> 'ProfileCreated') NOT VALID");
        try
        {
            using var response = await client.PostAsJsonAsync("/api/v1/profiles", Profile(name));
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(name, body); Assert.DoesNotContain("test_operational_audit_failure", body);
            Assert.False(await db.OperationalProfiles.AnyAsync(p => p.Name == CentralContingencia.Domain.Profiles.ProfileName.Create(name)));
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.\"OperationalEvents\" DROP CONSTRAINT test_operational_audit_failure"); }
    }
    [PostgresFact]
    public async Task OpenApiDocumentsOperationalSecurityAndSafeResponses()
    {
        using var client = Client(); var doc = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        foreach (var path in new[] { "/api/v1/profiles", "/api/v1/allocations", "/api/v1/allocations/reserve" })
            Assert.True(doc.GetProperty("paths").GetProperty(path).GetProperty(path.EndsWith("reserve") ? "post" : "get").TryGetProperty("security", out _));
        var operation = doc.GetProperty("paths").GetProperty("/api/v1/allocations/reserve").GetProperty("post");
        Assert.True(operation.GetProperty("responses").TryGetProperty("500", out _));
        Assert.Contains("15 minutos", operation.GetProperty("summary").GetString());
        Assert.Contains("204", doc.GetProperty("paths").GetProperty("/api/v1/allocations/{id}/confirm").GetProperty("post").GetProperty("summary").GetString());
        Assert.Contains(operation.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "X-CSRF-TOKEN");
        Assert.Contains(operation.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.False(doc.GetProperty("components").GetProperty("schemas").GetProperty("AllocationResponse").GetProperty("properties").TryGetProperty("password", out _));
        var profileList = doc.GetProperty("paths").GetProperty("/api/v1/profiles").GetProperty("get");
        var size = profileList.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString()!.Equals("pageSize", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(100, size.GetProperty("schema").GetProperty("maximum").GetInt32());
    }
}
