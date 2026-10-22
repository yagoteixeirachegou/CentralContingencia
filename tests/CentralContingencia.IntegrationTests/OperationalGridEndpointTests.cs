using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using CentralContingencia.Application.Allocations;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Infrastructure.Persistence;
using CentralContingencia.Infrastructure.Proxies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace CentralContingencia.IntegrationTests;

[Collection("Authentication")]
public class OperationalGridEndpointTests(AuthFixture fixture, ITestOutputHelper output) : IDisposable
{
    private readonly AuthFactory factory = new(fixture.ConnectionString, permitLimit: 1000, globalPermitLimit: 10000);
    public void Dispose() => factory.Dispose();
    private HttpClient Client() => factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    private static async Task Csrf(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", response.GetProperty("token").GetString());
    }
    private async Task Login(HttpClient client, string role = "Admin")
    {
        var email = fixture.AdminEmail;
        if (role != "Admin")
        {
            using var admin = Client(); await Login(admin);
            email = $"grid-{Guid.NewGuid():N}@example.test";
            Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/v1/users", new { email, password = fixture.Password, role })).StatusCode);
        }
        await Csrf(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = fixture.Password })).StatusCode);
        await Csrf(client);
    }
    private static Proxy Proxy(string region, DateTimeOffset now, DateTimeOffset expiry, ProxyStatus status = ProxyStatus.Active)
    {
        var p = CentralContingencia.Domain.Proxies.Proxy.Create(Guid.NewGuid(), ProxyHost.Create("grid.synthetic.test"), ProxyPort.Create(8080), "grid-private-user",
            EncryptedProxyPassword.Create("grid-test", RandomNumberGenerator.GetBytes(12), [1, 2, 3], RandomNumberGenerator.GetBytes(16)), region, expiry, now);
        p.ChangeStatus(status, now); return p;
    }
    [PostgresFact]
    public async Task GridUsesLogicalStateAndDoesNotDuplicateHistoryOrExposeCredentials()
    {
        var region = "grid-" + Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow.AddMinutes(-30); var domain = new AllocationDomainService();
        var proxies = Enumerable.Range(0, 8).Select(i => Proxy(region, now, now.AddDays(1).AddMinutes(i))).ToArray();
        proxies[1].ChangeStatus(ProxyStatus.Suspended, now);
        proxies[2] = Proxy(region, now, now.AddMinutes(10));
        proxies[7] = Proxy(region, now, now.AddMinutes(10));
        var profiles = Enumerable.Range(0, 6).Select(i => OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create($"Operação Alpha {i}"), null, now, "DARKPOST", "TIKTOK")).ToArray();
        var actor = Guid.NewGuid();
        ProxyAllocation Reserve(int p, int f, DateTimeOffset deadline, IReadOnlyCollection<ProxyAllocation>? history = null) =>
            domain.Reserve(Guid.NewGuid(), profiles[f], proxies[p], actor, Guid.NewGuid(), history is null ? now : now.AddMinutes(3), deadline, history ?? []);
        var reserved = Reserve(3, 0, DateTimeOffset.UtcNow.AddMinutes(5));
        var confirmed = Reserve(4, 1, now.AddMinutes(5)); domain.Confirm(confirmed, profiles[1], proxies[4], now.AddMinutes(1));
        proxies[4].ChangeStatus(ProxyStatus.Disabled, now.AddMinutes(2));
        var stale = Reserve(5, 2, now.AddMinutes(5));
        var previous = Reserve(6, 3, now.AddMinutes(5)); domain.Confirm(previous, profiles[3], proxies[6], now.AddMinutes(1)); domain.Release(previous, now.AddMinutes(2));
        var reused = Reserve(6, 4, DateTimeOffset.UtcNow.AddMinutes(5), [previous]);
        var expiredInUse = Reserve(7, 5, now.AddMinutes(5)); domain.Confirm(expiredInUse, profiles[5], proxies[7], now.AddMinutes(1));
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
            db.Proxies.AddRange(proxies); db.OperationalProfiles.AddRange(profiles); db.ProxyAllocations.AddRange(reserved, confirmed, stale, previous, reused, expiredInUse); await db.SaveChangesAsync();
        }
        using var client = Client(); await Login(client);
        var root = "/api/v1/proxies/operational-grid?region=" + region;
        var response = await client.GetAsync(root); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var secret in new[] { "grid-private-user", "password", "ciphertext", "nonce", "keyId", "tag\"" }) Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
        var page = JsonSerializer.Deserialize<OperationalGridPage>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(8, page.Total); Assert.Equal(8, page.Items.Select(p => p.ProxyId).Distinct().Count());
        var expected = new[] { "Available", "Unavailable", "Unavailable", "Reserved", "InUse", "Available", "Reserved", "InUse" };
        for (var i = 0; i < 8; i++) Assert.Equal(expected[i], page.Items.Single(r => r.ProxyId == proxies[i].Id).Availability);
        var empty = page.Items.Single(p => p.ProxyId == proxies[5].Id);
        Assert.Null(empty.ProfileId); Assert.Null(empty.ProfileName); Assert.Null(empty.Platform1); Assert.Null(empty.Platform2); Assert.Null(empty.AllocationId);
        Assert.Equal(profiles[4].Id, page.Items.Single(p => p.ProxyId == proxies[6].Id).ProfileId);
        var combined = await client.GetFromJsonAsync<OperationalGridPage>(root.ToUpperInvariant().Replace("/API/V1/PROXIES/OPERATIONAL-GRID?REGION=", "/api/v1/proxies/operational-grid?region=") + "&profile=alpha&platform=TIKTOK&availability=Reserved&pageSize=1&direction=desc");
        Assert.Equal(2, combined!.Total); Assert.Single(combined.Items);
        var second = await client.GetFromJsonAsync<OperationalGridPage>(root + "&profile=ALPHA&platform=DARKPOST&availability=Reserved&pageSize=1&page=2&direction=desc");
        Assert.NotEqual(combined.Items[0].ProxyId, second!.Items[0].ProxyId);
        Assert.True(combined.Items[0].ExpiresAt >= second.Items[0].ExpiresAt);
        foreach (var state in new[] { "Available", "Reserved", "InUse", "Unavailable" })
            Assert.Equal(expected.Count(s => s == state), (await client.GetFromJsonAsync<OperationalGridPage>(root + "&availability=" + state))!.Total);
        using var finalScope = fixture.Factory.Services.CreateScope(); var finalDb = finalScope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        Assert.Equal(AllocationStatus.Reserved, (await finalDb.ProxyAllocations.AsNoTracking().SingleAsync(a => a.Id == stale.Id)).Status);
        Assert.Equal(6, await finalDb.ProxyAllocations.CountAsync(a => proxies.Select(p => p.Id).Contains(a.ProxyId)));
        Assert.Equal(0, await finalDb.OperationalAuditEvents.CountAsync(e => profiles.Select(p => p.Id).Contains(e.ProfileId)));
    }
    [PostgresTheory]
    [InlineData("Admin")][InlineData("Manager")][InlineData("Operator")]
    public async Task GridReadRolesAndQueryValidation(string role)
    {
        using var anonymous = Client(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/proxies/operational-grid")).StatusCode);
        using var client = Client(); await Login(client, role);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/proxies/operational-grid")).StatusCode);
        foreach (var query in new[] { "page=0", "pageSize=101", "availability=Active", "platform=UNKNOWN", "sort=password", "direction=DROP", "page=1&page=2", "region=", "page=abc", "page=", "password=x" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/proxies/operational-grid?" + query)).StatusCode);
    }
    [PostgresFact]
    public async Task IntegrationRoleCannotQueryOperationalGrid()
    {
        await using var isolated = new AuthFixture(); await isolated.InitializeAsync();
        var email = $"integration-grid-{Guid.NewGuid():N}@example.test";
        using (var scope = isolated.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<CentralContingencia.Infrastructure.Identity.ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole<Guid>>>();
            if (!await roles.RoleExistsAsync("Integration")) Assert.True((await roles.CreateAsync(new Microsoft.AspNetCore.Identity.IdentityRole<Guid>("Integration"))).Succeeded);
            var user = new CentralContingencia.Infrastructure.Identity.ApplicationUser { Id = Guid.NewGuid(), Email = email, UserName = email };
            Assert.True((await users.CreateAsync(user, isolated.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Integration")).Succeeded);
        }
        using var client = isolated.Client(); await Csrf(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = isolated.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/proxies/operational-grid")).StatusCode);
    }
    [PostgresFact]
    public async Task OptionalSlotsPatchNullPreservesOmittedSlotsAndLegacyContract()
    {
        using var client = Client(); await Login(client);
        var response = await client.PostAsJsonAsync("/api/v1/profiles", new { name = "Synthetic optional", platform1 = "DARKPOST", platform2 = "TIKTOK" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); var profile = (await response.Content.ReadFromJsonAsync<ProfileResponse>())!;
        Assert.Equal("", profile.Platform); Assert.Equal("DARKPOST", profile.Platform1); Assert.Equal("TIKTOK", profile.Platform2);
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/v1/profiles/{profile.Id}", new { platform1 = (string?)null })).StatusCode);
        var current = (await client.GetFromJsonAsync<ProfileResponse>($"/api/v1/profiles/{profile.Id}"))!; Assert.Null(current.Platform1); Assert.Equal("TIKTOK", current.Platform2);
        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/v1/profiles/{profile.Id}", new { platform = "Legacy original text" })).StatusCode);
        current = (await client.GetFromJsonAsync<ProfileResponse>($"/api/v1/profiles/{profile.Id}"))!; Assert.Equal("Legacy original text", current.Platform); Assert.Null(current.Platform1); Assert.Equal("TIKTOK", current.Platform2);
        foreach (var body in new[] { "{\"platform1\":\"UNKNOWN\"}", "{\"platform2\":\"\"}", "{\"platform1\":null,\"Platform1\":\"FLAG\"}", "{\"name\":null}", "{\"platform\":null}" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsync($"/api/v1/profiles/{profile.Id}", new StringContent(body, System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"/api/v1/profiles/{profile.Id}", new { platform2 = (string?)null })).StatusCode);
        using var reader = Client(); await Login(reader, "Operator");
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PatchAsJsonAsync($"/api/v1/profiles/{profile.Id}", new { platform2 = (string?)null })).StatusCode);
        await Csrf(client);
        foreach (var request in new[] { new { name = "No assignments", platform1 = (string?)null, platform2 = (string?)null }, new { name = "Only second", platform1 = (string?)null, platform2 = (string?)"FLAG" } })
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/profiles", request)).StatusCode);
    }
    private sealed class Commands : DbCommandInterceptor
    {
        public List<string> Reads { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Reads.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
    [PostgresFact]
    public async Task PlatformMatchesEitherIndependentSlotAndProfileSearchEscapesWildcards()
    {
        var region = "slots-" + Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        var profiles = new[] {
            OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create("Literal %_ Name"), null, now, "FLAG"),
            OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create("Literal Other Name"), null, now, null, "FLAG") };
        var proxies = Enumerable.Range(0, 2).Select(_ => Proxy(region, now, now.AddDays(1))).ToArray();
        var domain = new AllocationDomainService();
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
            db.Proxies.AddRange(proxies); db.OperationalProfiles.AddRange(profiles);
            for (var i = 0; i < 2; i++) db.ProxyAllocations.Add(domain.Reserve(Guid.NewGuid(), profiles[i], proxies[i], Guid.NewGuid(), Guid.NewGuid(), now, now.AddMinutes(10), []));
            await db.SaveChangesAsync();
        }
        using var client = Client(); await Login(client);
        var root = "/api/v1/proxies/operational-grid?region=" + region + "&platform=FLAG";
        Assert.Equal(2, (await client.GetFromJsonAsync<OperationalGridPage>(root))!.Total);
        Assert.Equal(1, (await client.GetFromJsonAsync<OperationalGridPage>(root + "&profile=" + Uri.EscapeDataString("%_")))!.Total);
        var all = (await client.GetFromJsonAsync<OperationalGridPage>(root + "&profile=NAME&pageSize=1&direction=asc"))!;
        Assert.Equal(2, all.Total); Assert.Single(all.Items);
    }
    [PostgresFact]
    public async Task UpgradePreservesExistingFreeTextProfilesAndProxyEnvelopes()
    {
        await using var isolated = new AuthFixture { InitialMigration = "20261011230449_AddOperationalAudit", ProvisionAdmin = false };
        await isolated.InitializeAsync();
        var options = new DbContextOptionsBuilder<CentralContingenciaDbContext>().UseNpgsql(isolated.ConnectionString).Options;
        await using var db = new CentralContingenciaDbContext(options);
        var now = DateTimeOffset.UtcNow.AddMinutes(-1); var id = Guid.NewGuid();
        var p = Proxy("Synthetic migration", now, now.AddDays(1)); db.Proxies.Add(p); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO profiles.\"OperationalProfiles\" (\"Id\",\"Name\",\"Platform\",\"Status\",\"CreatedAt\",\"UpdatedAt\") VALUES ({id},{"Synthetic preserved"},{"Legacy outside catalog"},{"Active"},{now},{now})");
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        var profile = await db.OperationalProfiles.SingleAsync(f => f.Id == id);
        Assert.Equal("Legacy outside catalog", profile.Platform); Assert.Null(profile.Platform1); Assert.Null(profile.Platform2);
        Assert.Equal(p.Password, (await db.Proxies.SingleAsync(x => x.Id == p.Id)).Password);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        profile.UpdateDetails(profile.Name, profile.Platform, "FLAG", "TIKTOK", now.AddSeconds(1)); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE profiles.\"OperationalProfiles\" SET \"Platform1\"={"UNKNOWN"} WHERE \"Id\"={id}");
        });
        db.OperationalProfiles.Add(OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create("Synthetic without legacy"), null, now));
        await db.SaveChangesAsync();
        var guard = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.GetService<IMigrator>().MigrateAsync("20261011230449_AddOperationalAudit"));
        Assert.Equal(Npgsql.PostgresErrorCodes.RaiseException, guard.SqlState);
        // Migrations mais novas sem dados sensíveis são revertidas antes do bloqueio; volta à versão atual antes de conferir os dados.
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        Assert.Equal("FLAG", (await db.OperationalProfiles.SingleAsync(f => f.Id == id)).Platform1);
        Assert.Contains("20261012102737_AddProfileAssignments", await db.Database.GetAppliedMigrationsAsync());
    }
    [PostgresFact]
    public async Task TwoThousandRowsUseFixedQueryCountAndLiteralContains()
    {
        var region = "load-" + Guid.NewGuid().ToString("N") + "%_"; var now = DateTimeOffset.UtcNow.AddMinutes(-1);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
            db.Proxies.AddRange(Enumerable.Range(0, 2000).Select(i => Proxy(region, now, now.AddDays(1).AddSeconds(i)))); await db.SaveChangesAsync();
        }
        var commands = new Commands(); var options = new DbContextOptionsBuilder<CentralContingenciaDbContext>().UseNpgsql(fixture.ConnectionString).AddInterceptors(commands).Options;
        var reader = new EfOperationalGridReader(options, TimeProvider.System); var watch = Stopwatch.StartNew();
        var page = await reader.QueryAsync(new(Region: region.ToUpperInvariant(), Page: 10, PageSize: 100, Direction: "desc"), default); watch.Stop();
        Assert.Equal(2000, page.Total); Assert.Equal(100, page.Items.Count); Assert.Equal(2, commands.Reads.Count);
        Assert.All(commands.Reads, sql => { Assert.DoesNotContain("Password", sql); Assert.DoesNotContain("Username", sql); });
        Assert.Equal(page.Items.OrderByDescending(r => r.ExpiresAt).ThenBy(r => r.ProxyId).Select(r => r.ProxyId), page.Items.Select(r => r.ProxyId));
        Assert.Equal(0, (await reader.QueryAsync(new(Region: region + "not-present"), default)).Total);
        output.WriteLine($"Synthetic 2000 rows: grid page query {watch.ElapsedMilliseconds}ms, two SELECTs, no N+1.");
    }
}
