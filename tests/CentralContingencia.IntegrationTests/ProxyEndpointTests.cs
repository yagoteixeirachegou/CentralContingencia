using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CentralContingencia.Application.Identity;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Domain.Audit;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Infrastructure.Persistence;
using CentralContingencia.Infrastructure.Proxies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CentralContingencia.IntegrationTests;

[Collection("Authentication")]
public class ProxyEndpointTests(AuthFixture fixture)
{
    private WebApplicationFactory<Program> KeyFactory() => fixture.Factory.WithWebHostBuilder(builder =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string,string?>
        { ["ProxyEncryption:ActiveKeyId"] = "endpoint-test-v1", ["ProxyEncryption:Keys:endpoint-test-v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) })));
    private static object Request(string? region = null) => new { host = "proxy.example.test", port = 8080,
        username = "synthetic-endpoint-user", password = "synthetic-endpoint-secret", region = region ?? "BR",
        status = "Active", expiresAt = DateTimeOffset.UtcNow.AddDays(3) };
    private static async Task Csrf(HttpClient client)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
    }
    private async Task Login(HttpClient client, string? role = null)
    {
        var email = fixture.AdminEmail;
        if (role is not null && role != "Admin")
        {
            using var admin = fixture.Client(); await Login(admin);
            await Csrf(admin); email = $"proxy-{Guid.NewGuid():N}@example.test";
            Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/v1/users", new { email, password = fixture.Password, role })).StatusCode);
        }
        await Csrf(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = fixture.Password })).StatusCode);
        await Csrf(client);
    }
    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static async Task<ProxyResponse> Create(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/proxies", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProxyResponse>())!;
    }
    [PostgresTheory]
    [InlineData("Admin",201)][InlineData("Manager",201)][InlineData("Operator",403)]
    public async Task RoleMatrixProtectsReadsAndMutations(string role, int writeStatus)
    {
        await using var factory = KeyFactory(); using var client = Client(factory); await Login(client, role);
        var response = await client.PostAsJsonAsync("/api/v1/proxies", Request());
        Assert.Equal(writeStatus, (int)response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/proxies")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/proxies/available")).StatusCode);
        if (writeStatus == 201)
        {
            var proxy = (await response.Content.ReadFromJsonAsync<ProxyResponse>())!;
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/proxies/{proxy.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/v1/proxies/{proxy.Id}", new { status = "Suspended" })).StatusCode);
        }
        else Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/v1/proxies/{Guid.NewGuid()}", new { status = "Suspended" })).StatusCode);
    }
    [PostgresFact]
    public async Task OnlyAdminRevealsCredentialsWithAuditAndNoCache()
    {
        await using var factory = KeyFactory();
        using var manager = Client(factory); await Login(manager, "Manager");
        var created = await Create(manager, Request());
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync($"/api/v1/proxies/{created.Id}/credentials/reveal", new { })).StatusCode);
        using var anonymous = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"/api/v1/proxies/{created.Id}/credentials/reveal", new { })).StatusCode);
        using var admin = Client(factory); await Login(admin);
        var response = await admin.PostAsJsonAsync($"/api/v1/proxies/{created.Id}/credentials/reveal", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("synthetic-endpoint-secret", body.GetProperty("password").GetString());
        Assert.Equal("synthetic-endpoint-user", body.GetProperty("username").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/v1/proxies/{Guid.NewGuid()}/credentials/reveal", new { })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        var revealed = await context.ProxyAuditEvents.AsNoTracking().Where(e => e.ProxyId == created.Id && e.Action == ProxyAuditAction.Revealed).ToListAsync();
        var audit = Assert.Single(revealed);
        Assert.DoesNotContain("synthetic-endpoint-secret", audit.CorrelationId);
    }
    [PostgresFact]
    public async Task AnonymousRequestsAreDenied()
    {
        using var client = fixture.Client();
        foreach (var path in new[] { "/api/v1/proxies", "/api/v1/proxies/available", $"/api/v1/proxies/{Guid.NewGuid()}" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/proxies", Request())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PatchAsJsonAsync($"/api/v1/proxies/{Guid.NewGuid()}", new { status = "Active" })).StatusCode);
    }
    [PostgresFact]
    public async Task ResponsesAreMaskedPasswordsEncryptedAndAuditSanitized()
    {
        await using var factory = KeyFactory(); using var client = Client(factory); await Login(client);
        var response = await client.PostAsJsonAsync("/api/v1/proxies", Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ProxyResponse>())!;
        Assert.EndsWith(created.Id.ToString(), response.Headers.Location!.ToString());
        var passwordUpdate = "synthetic-updated-secret";
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/v1/proxies/{created.Id}", new { password = passwordUpdate, port = 3128 })).StatusCode);
        foreach (var path in new[] { $"/api/v1/proxies/{created.Id}", "/api/v1/proxies", "/api/v1/proxies/available" })
        {
            var text = await client.GetStringAsync(path);
            Assert.DoesNotContain("synthetic-endpoint-secret", text); Assert.DoesNotContain(passwordUpdate, text);
            Assert.DoesNotContain("synthetic-endpoint-user", text); Assert.DoesNotContain("ciphertext", text.ToLowerInvariant());
            Assert.DoesNotContain("keyId", text); Assert.Contains("***", text);
        }
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        var proxy = await db.Proxies.AsNoTracking().SingleAsync(p => p.Id == created.Id);
        Assert.Equal(3128, proxy.Port.Value);
        Assert.Equal(passwordUpdate, scope.ServiceProvider.GetRequiredService<IProxyPasswordProtector>().Decrypt(proxy.Id, proxy.Password));
        var audits = await db.ProxyAuditEvents.Where(e => e.ProxyId == proxy.Id).OrderBy(e => e.OccurredAt).ToListAsync();
        Assert.Equal(new[] { ProxyAuditAction.Created, ProxyAuditAction.Updated }, audits.Select(e => e.Action));
        var auditJson = JsonSerializer.Serialize(audits);
        Assert.DoesNotContain("synthetic-endpoint", auditJson); Assert.DoesNotContain(passwordUpdate, auditJson);
        Assert.All(audits, audit => { Assert.NotEqual(Guid.Empty, audit.ActorId); Assert.InRange(audit.CorrelationId.Length, 1, 128); });
    }
    [PostgresFact]
    public async Task MissingKeyFailsClosedButReadsAndAuthenticationWork()
    {
        using var client = fixture.Client(); await Login(client);
        var response = await client.PostAsJsonAsync("/api/v1/proxies", Request());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/proxies")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }
    [PostgresFact]
    public async Task PostAndPatchRequireValidCsrfAndDoNotWriteOnFailure()
    {
        await using var factory = KeyFactory(); using var client = Client(factory); await Login(client);
        var created = await Create(client, Request());
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/proxies", Request())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"/api/v1/proxies/{created.Id}", new { region = "AR" })).StatusCode);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "tampered");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"/api/v1/proxies/{created.Id}", new { region = "AR" })).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        Assert.Equal("BR", (await db.Proxies.SingleAsync(p => p.Id == created.Id)).Region);
        Assert.Single(await db.ProxyAuditEvents.Where(e => e.ProxyId == created.Id).ToListAsync());
    }
    [PostgresTheory]
    [InlineData("{\"port\":0}")][InlineData("{\"host\":\"http://proxy.test\"}")]
    [InlineData("{\"username\":\"\"}")][InlineData("{\"password\":\"\"}")]
    [InlineData("{\"status\":\"Available\"}")][InlineData("{\"expiresAt\":\"2020-01-01T00:00:00Z\"}")]
    [InlineData("{\"password\":null}")][InlineData("{}")][InlineData("{\"id\":\"x\"}")]
    [InlineData("{\"region\":\"AR\",\"Region\":\"BR\"}")][InlineData("{\"expiresAt\":\"2030-01-01T12:00:00\"}")]
    public async Task InvalidPatchReturnsSanitizedProblemWithoutMutation(string body)
    {
        await using var factory = KeyFactory(); using var client = Client(factory); await Login(client);
        var created = await Create(client, Request());
        var response = await client.PatchAsync($"/api/v1/proxies/{created.Id}", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.DoesNotContain("synthetic-endpoint-secret", await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        Assert.Single(await db.ProxyAuditEvents.Where(e => e.ProxyId == created.Id).ToListAsync());
    }
    [PostgresTheory]
    [InlineData("page=0")][InlineData("pageSize=101")][InlineData("sort=password")]
    [InlineData("direction=invalid")][InlineData("status=Available")][InlineData("port=65536")]
    [InlineData("host=http%3A%2F%2Fproxy.test")]
    public async Task InvalidQueriesReturn400(string query)
    {
        using var client = fixture.Client(); await Login(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/proxies?" + query)).StatusCode);
    }
    [PostgresFact]
    public async Task FilteringPaginationOrderingAvailabilityAndMissingIdsWork()
    {
        await using var factory = KeyFactory(); using var client = Client(factory); await Login(client);
        var region = "test-" + Guid.NewGuid().ToString("N");
        var first = await Create(client, Request(region)); var second = await Create(client, Request(region));
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/v1/proxies/{first.Id}", new { status = "Suspended" })).StatusCode);
        var page = (await client.GetFromJsonAsync<ProxyPage>($"/api/v1/proxies?region={region}&pageSize=1&sort=createdAt&direction=asc"))!;
        Assert.Equal(2, page.Total); Assert.Single(page.Items); Assert.Equal(first.Id, page.Items[0].Id);
        var page2 = (await client.GetFromJsonAsync<ProxyPage>($"/api/v1/proxies?region={region}&page=2&pageSize=1&sort=createdAt&direction=asc"))!;
        Assert.Equal(second.Id, Assert.Single(page2.Items).Id);
        var available = (await client.GetFromJsonAsync<ProxyPage>($"/api/v1/proxies/available?region={region}"))!;
        Assert.Equal(second.Id, Assert.Single(available.Items).Id);
        var filtered = (await client.GetFromJsonAsync<ProxyPage>($"/api/v1/proxies?region={region}&status=Suspended&host=PROXY.example.test&port=8080&sort=host"))!;
        Assert.Equal(first.Id, Assert.Single(filtered.Items).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/proxies/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PatchAsJsonAsync($"/api/v1/proxies/{Guid.NewGuid()}", new { region = "AR" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/proxies/00000000-0000-0000-0000-000000000000")).StatusCode);
    }
    [PostgresFact]
    public async Task ExpiredActiveProxyIsExcludedAndExpiryFilterAndAllSortsExecute()
    {
        await using var factory = KeyFactory(); using var client = Client(factory); await Login(client);
        var region = "expiry-" + Guid.NewGuid().ToString("N");
        var active = await Create(client, Request(region));
        Guid expiredId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<IProxyPasswordProtector>();
            expiredId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            var expired = Proxy.Create(expiredId, ProxyHost.Create("expired.example.test"), ProxyPort.Create(3128), "synthetic-user",
                protector.Encrypt(expiredId, "synthetic-secret"), region, now.AddDays(-1), now);
            expired.ChangeStatus(ProxyStatus.Active, now); db.Add(expired); await db.SaveChangesAsync();
        }
        var available = (await client.GetFromJsonAsync<ProxyPage>($"/api/v1/proxies/available?region={region}"))!;
        Assert.Equal(active.Id, Assert.Single(available.Items).Id);
        var expiredPage = (await client.GetFromJsonAsync<ProxyPage>($"/api/v1/proxies?region={region}&expiresBefore={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}"))!;
        Assert.Equal(expiredId, Assert.Single(expiredPage.Items).Id); Assert.False(expiredPage.Items[0].Available);
        foreach (var sort in new[] { "host", "port", "region", "status", "expiresAt", "updatedAt", "createdAt" })
            Assert.Equal(2, (await client.GetFromJsonAsync<ProxyPage>($"/api/v1/proxies?region={region}&sort={sort}&direction=desc"))!.Items.Count);
    }
    [PostgresFact]
    public async Task InvalidCreationEmptyPasswordOversizeAndInvalidMediaTypeAreRejected()
    {
        await using var factory = KeyFactory(); using var client = Client(factory); await Login(client);
        foreach (var body in new object[] {
            new { host = "proxy.test", port = 0, username = "u", password = "synthetic", region = "BR", expiresAt = DateTimeOffset.UtcNow.AddDays(1) },
            new { host = "proxy.test", port = 1, username = "u", password = new string('a',1025), region = "BR", expiresAt = DateTimeOffset.UtcNow.AddDays(1) },
            new { host = "proxy.test", port = 1, username = "u", password = "", region = "BR", expiresAt = DateTimeOffset.UtcNow.AddDays(1) },
            new { host = "proxy.test", port = 1, username = "u", password = "synthetic", region = "BR", expiresAt = DateTimeOffset.UtcNow.AddDays(-1) } })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/proxies", body)).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync("/api/v1/proxies", new StringContent("{}"))).StatusCode);
    }
    [PostgresFact]
    public async Task ConcurrentUpdateRollsBackAuditForLosingWriter()
    {
        await using var factory = KeyFactory(); using var client = Client(factory); await Login(client);
        var created = await Create(client, Request());
        using var firstScope = factory.Services.CreateScope(); using var secondScope = factory.Services.CreateScope();
        var firstStore = firstScope.ServiceProvider.GetRequiredService<IProxyStore>();
        var secondStore = secondScope.ServiceProvider.GetRequiredService<IProxyStore>();
        var first = (await firstStore.FindAsync(created.Id, default))!; var second = (await secondStore.FindAsync(created.Id, default))!;
        first.ChangeStatus(ProxyStatus.Suspended, DateTimeOffset.UtcNow); second.ChangeStatus(ProxyStatus.Disabled, DateTimeOffset.UtcNow);
        Assert.True(await firstStore.SaveAsync(first, new(Guid.NewGuid(), first.Id, ProxyAuditAction.Updated, DateTimeOffset.UtcNow, "winner"), false, default));
        Assert.False(await secondStore.SaveAsync(second, new(Guid.NewGuid(), second.Id, ProxyAuditAction.Updated, DateTimeOffset.UtcNow, "loser"), false, default));
        using var read = factory.Services.CreateScope(); var db = read.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        Assert.Equal(2, await db.ProxyAuditEvents.CountAsync(e => e.ProxyId == created.Id));
        Assert.False(await db.ProxyAuditEvents.AnyAsync(e => e.ProxyId == created.Id && e.CorrelationId == "loser"));
    }
}
