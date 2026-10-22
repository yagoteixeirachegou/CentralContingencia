using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using CentralContingencia.Application.Allocations;
using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Audit;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Infrastructure.Allocations;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CentralContingencia.IntegrationTests;

[Collection("Authentication")]
public class ReservationReliabilityTests(AuthFixture fixture)
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private DbContextOptions<CentralContingenciaDbContext> Options => new DbContextOptionsBuilder<CentralContingenciaDbContext>().UseNpgsql(fixture.ConnectionString).Options;
    private CentralContingenciaDbContext Db() => new(Options);
    private async Task<(Guid Profile, Guid Proxy)> Seed(Clock clock)
    {
        var now = clock.Now.AddMinutes(-1);
        var profile = OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create("Sintético confiabilidade"), "Sintética", now);
        var proxy = Proxy.Create(Guid.NewGuid(), ProxyHost.Create("reliability.synthetic.test"), ProxyPort.Create(8080), "sensitive-synthetic-user",
            EncryptedProxyPassword.Create("synthetic-key", RandomNumberGenerator.GetBytes(12), [1, 2], RandomNumberGenerator.GetBytes(16)), "BR", now.AddDays(1), now);
        proxy.ChangeStatus(ProxyStatus.Active, now);
        await using var db = Db(); db.OperationalProfiles.Add(profile); db.Proxies.Add(proxy); await db.SaveChangesAsync();
        return (profile.Id, proxy.Id);
    }
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Host(Clock clock, int minutes = 15) =>
        new AuthFactory(fixture.ConnectionString, permitLimit: 1000, globalPermitLimit: 10000).WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)));
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ProxyAllocations:ReservationMinutes"] = minutes.ToString() }));
        });
    private async Task<HttpClient> Login(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        async Task Csrf()
        {
            var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        }
        await Csrf(); Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = fixture.AdminEmail, password = fixture.Password })).StatusCode);
        await Csrf(); return client;
    }
    private static async Task<HttpResponseMessage> Reserve(HttpClient client, Guid profile, Guid proxy, DateTimeOffset? deadline = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/allocations/reserve")
        { Content = JsonContent.Create(deadline is null ? (object)new { profileId = profile, proxyId = proxy } : new { profileId = profile, proxyId = proxy, reservationExpiresAt = deadline }) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); return await client.SendAsync(request);
    }
    private static async Task<AllocationResponse> Created(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sensitive-synthetic-user", body); Assert.DoesNotContain("synthetic-key", body); Assert.DoesNotContain("ciphertext", body);
        return (await response.Content.ReadFromJsonAsync<AllocationResponse>())!;
    }
    [PostgresTheory]
    [InlineData(15)][InlineData(3)]
    public async Task ConfiguredDefaultDeadlineAndExplicitMaximumAreEnforced(int minutes)
    {
        var clock = new Clock(); var seed = await Seed(clock); using var host = Host(clock, minutes); using var client = await Login(host);
        using var rejected = await Reserve(client, seed.Profile, seed.Proxy, clock.Now.AddMinutes(minutes + 1));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var response = await Reserve(client, seed.Profile, seed.Proxy); var allocation = await Created(response);
        Assert.InRange((allocation.ReservationExpiresAt - clock.Now.AddMinutes(minutes)).Duration(), TimeSpan.Zero, TimeSpan.FromTicks(10));
    }
    [PostgresFact]
    public async Task ExpiredAvailabilityReadsDoNotWriteAndConcurrentReuseExpiresExactlyOnce()
    {
        var clock = new Clock(); var seed = await Seed(clock); using var host = Host(clock); using var client = await Login(host);
        using var reserved = await Reserve(client, seed.Profile, seed.Proxy); var old = await Created(reserved);
        clock.Now = old.ReservationExpiresAt;
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/proxies/{seed.Proxy}"); Assert.True(detail.GetProperty("available").GetBoolean());
        var available = await client.GetFromJsonAsync<JsonElement>("/api/v1/proxies/available?host=reliability.synthetic.test&pageSize=100");
        Assert.Contains(available.GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetGuid() == seed.Proxy);
        await using (var db = Db())
        {
            Assert.Equal(AllocationStatus.Reserved, (await db.ProxyAllocations.SingleAsync(a => a.Id == old.Id)).Status);
            Assert.False(await db.OperationalAuditEvents.AnyAsync(e => e.AllocationId == old.Id && e.Action == OperationalAction.AllocationExpired));
        }
        var other = await Seed(clock);
        var responses = await Task.WhenAll(Reserve(client, seed.Profile, seed.Proxy), Reserve(client, other.Profile, seed.Proxy));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            await using var db = Db();
            var expired = await db.ProxyAllocations.SingleAsync(a => a.Id == old.Id);
            Assert.Equal(AllocationStatus.Expired, expired.Status); Assert.Equal(AllocationEndReason.ReservationExpired, expired.EndReason);
            Assert.InRange((clock.Now - expired.EndedAt!.Value).Duration(), TimeSpan.Zero, TimeSpan.FromTicks(10));
            var audit = Assert.Single(await db.OperationalAuditEvents.Where(e => e.AllocationId == old.Id && e.Action == OperationalAction.AllocationExpired).ToArrayAsync());
            Assert.Equal("reservation-expiry", audit.CorrelationId); Assert.InRange((clock.Now - audit.OccurredAt).Duration(), TimeSpan.Zero, TimeSpan.FromTicks(10));
            Assert.Equal(1, await db.ProxyAllocations.CountAsync(a => a.ProxyId == seed.Proxy && (a.Status == AllocationStatus.Reserved || a.Status == AllocationStatus.Confirmed)));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
    [PostgresFact]
    public async Task ConcurrentConfirmationReplaysAreIdempotentAndReleasedConfirmationConflicts()
    {
        var clock = new Clock(); var seed = await Seed(clock); using var host = Host(clock); using var client = await Login(host);
        using var reserved = await Reserve(client, seed.Profile, seed.Proxy); var allocation = await Created(reserved);
        var path = $"/api/v1/allocations/{allocation.Id}/confirm";
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync(path, new { })));
        foreach (var response in responses) { Assert.Equal(HttpStatusCode.NoContent, response.StatusCode); response.Dispose(); }
        var confirmed = await client.GetFromJsonAsync<AllocationResponse>($"/api/v1/allocations/{allocation.Id}");
        clock.Now = allocation.ReservationExpiresAt.AddMinutes(1);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(path, new { })).StatusCode);
        var replay = await client.GetFromJsonAsync<AllocationResponse>($"/api/v1/allocations/{allocation.Id}"); Assert.Equal(confirmed, replay);
        await using (var db = Db())
        { Assert.Equal(1, await db.OperationalAuditEvents.CountAsync(e => e.AllocationId == allocation.Id && e.Action == OperationalAction.AllocationConfirmed)); }
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/v1/allocations/{allocation.Id}/release", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path, new { })).StatusCode);
    }
    [PostgresFact]
    public async Task ExpiredConfirmationCommitsAuditedRecoveryButReturnsConflict()
    {
        var clock = new Clock(); var seed = await Seed(clock); using var host = Host(clock); using var client = await Login(host);
        using var reserved = await Reserve(client, seed.Profile, seed.Proxy); var allocation = await Created(reserved);
        clock.Now = allocation.ReservationExpiresAt;
        for (var attempt = 0; attempt < 2; attempt++)
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/allocations/{allocation.Id}/confirm", new { })).StatusCode);
        await using var db = Db();
        Assert.Equal(AllocationStatus.Expired, (await db.ProxyAllocations.SingleAsync(a => a.Id == allocation.Id)).Status);
        Assert.Equal(1, await db.OperationalAuditEvents.CountAsync(e => e.AllocationId == allocation.Id && e.Action == OperationalAction.AllocationExpired));
        Assert.False(await db.OperationalAuditEvents.AnyAsync(e => e.AllocationId == allocation.Id && e.Action == OperationalAction.AllocationConfirmed));
    }
    [PostgresTheory]
    [InlineData("AllocationExpired")][InlineData("AllocationConfirmed")][InlineData("AllocationReserved")]
    public async Task AuditFailureReturnsSanitized500AndRollsBackAllChanges(string action)
    {
        var clock = new Clock(); var seed = await Seed(clock); using var host = Host(clock); using var client = await Login(host);
        using var reserved = await Reserve(client, seed.Profile, seed.Proxy); var allocation = await Created(reserved);
        await using var db = Db();
        var before = await db.ProxyAllocations.AsNoTracking().SingleAsync(a => a.Id == allocation.Id);
        var envelope = (await db.Proxies.AsNoTracking().SingleAsync(p => p.Id == seed.Proxy)).Password;
        if (action != "AllocationConfirmed") clock.Now = allocation.ReservationExpiresAt;
        // Restrict the injected fault to this synthetic resource and restore it in finally.
        // Only generated UUIDs and the fixed InlineData action whitelist enter this test DDL.
        var faultSql = $"ALTER TABLE audit.\"OperationalEvents\" ADD CONSTRAINT test_reliability_audit_failure CHECK (\"ProfileId\" <> '{seed.Profile}' OR \"Action\" <> '{action}') NOT VALID";
        await db.Database.ExecuteSqlRawAsync(faultSql);
        try
        {
            using var response = action == "AllocationConfirmed"
                ? await client.PostAsJsonAsync($"/api/v1/allocations/{allocation.Id}/confirm", new { })
                : await Reserve(client, seed.Profile, seed.Proxy);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            foreach (var sensitive in new[] { "test_reliability_audit_failure", "sensitive-synthetic-user", "synthetic-key", "Postgres", "INSERT", "ciphertext" }) Assert.DoesNotContain(sensitive, body);
            var after = await db.ProxyAllocations.AsNoTracking().SingleAsync(a => a.Id == allocation.Id);
            Assert.Equal(before.Status, after.Status); Assert.Equal(before.UpdatedAt, after.UpdatedAt); Assert.Null(after.EndedAt); Assert.Null(after.ConfirmedAt);
            Assert.Equal(1, await db.ProxyAllocations.CountAsync(a => a.ProxyId == seed.Proxy));
            Assert.False(await db.OperationalAuditEvents.AnyAsync(e => e.AllocationId == allocation.Id && (e.Action == OperationalAction.AllocationExpired || e.Action == OperationalAction.AllocationConfirmed)));
            Assert.Equal(envelope, (await db.Proxies.AsNoTracking().SingleAsync(p => p.Id == seed.Proxy)).Password);
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.\"OperationalEvents\" DROP CONSTRAINT test_reliability_audit_failure"); }
        using var retry = action == "AllocationConfirmed" ? await client.PostAsJsonAsync($"/api/v1/allocations/{allocation.Id}/confirm", new { }) : await Reserve(client, seed.Profile, seed.Proxy);
        Assert.Equal(action == "AllocationConfirmed" ? HttpStatusCode.NoContent : HttpStatusCode.Created, retry.StatusCode);
    }
    [PostgresTheory]
    [InlineData(false)][InlineData(true)]
    public async Task ExplicitConcurrentExpirationAndProfileClosurePreserveExpirationReason(bool expireFirst)
    {
        var clock = new Clock(); var seed = await Seed(clock); var store = new EfAllocationStore(Options, clock);
        var reservation = await store.ReserveAsync(new(Guid.NewGuid(), seed.Profile, seed.Proxy, Guid.NewGuid(), Guid.NewGuid(), null), default);
        clock.Now = reservation.ReservationExpiresAt;
        if (expireFirst) await Task.WhenAll(store.ExpireAsync(reservation.Id, default), store.ExpireAsync(reservation.Id, default));
        await store.CloseProfileAsync(seed.Profile, default, new(Guid.NewGuid(), "synthetic-close"));
        var history = Assert.Single(await store.HistoryAsync(seed.Profile, default));
        Assert.Equal(AllocationStatus.Expired, history.Status); Assert.Equal(AllocationEndReason.ReservationExpired, history.EndReason);
        await using var db = Db();
        Assert.Equal(1, await db.OperationalAuditEvents.CountAsync(e => e.AllocationId == reservation.Id && e.Action == OperationalAction.AllocationExpired));
        Assert.False(await db.OperationalAuditEvents.AnyAsync(e => e.AllocationId == reservation.Id && e.Action == OperationalAction.AllocationReleased));
        Assert.Equal(ProfileStatus.Closed, (await db.OperationalProfiles.SingleAsync(p => p.Id == seed.Profile)).Status);
    }
    [PostgresFact]
    public async Task ExpiredConfirmationRacingWithReuseCannotConfirmOrDuplicateRecovery()
    {
        var clock = new Clock(); var seed = await Seed(clock); using var host = Host(clock); using var client = await Login(host);
        using var reserved = await Reserve(client, seed.Profile, seed.Proxy); var allocation = await Created(reserved);
        clock.Now = allocation.ReservationExpiresAt;
        var confirmTask = client.PostAsJsonAsync($"/api/v1/allocations/{allocation.Id}/confirm", new { });
        var reuseTask = Reserve(client, seed.Profile, seed.Proxy);
        using var confirm = await confirmTask; using var reuse = await reuseTask;
        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode); Assert.Equal(HttpStatusCode.Created, reuse.StatusCode);
        await using var db = Db();
        Assert.Equal(AllocationStatus.Expired, (await db.ProxyAllocations.SingleAsync(a => a.Id == allocation.Id)).Status);
        Assert.Equal(1, await db.OperationalAuditEvents.CountAsync(e => e.AllocationId == allocation.Id && e.Action == OperationalAction.AllocationExpired));
        Assert.Equal(2, await db.ProxyAllocations.CountAsync(a => a.ProfileId == seed.Profile));
    }
    [PostgresTheory]
    [InlineData("ProfileUpdated")][InlineData("ProfileClosed")][InlineData("AllocationReleased")]
    public async Task AuditFailureRollsBackProfileUpdatesClosureAndRelease(string action)
    {
        var clock = new Clock(); var seed = await Seed(clock); using var host = Host(clock); using var client = await Login(host);
        using var reserved = await Reserve(client, seed.Profile, seed.Proxy); var allocation = await Created(reserved);
        await using var db = Db();
        var before = await db.OperationalProfiles.AsNoTracking().SingleAsync(p => p.Id == seed.Profile);
        var faultSql = $"ALTER TABLE audit.\"OperationalEvents\" ADD CONSTRAINT test_reliability_audit_failure CHECK (\"ProfileId\" <> '{seed.Profile}' OR \"Action\" <> '{action}') NOT VALID";
        await db.Database.ExecuteSqlRawAsync(faultSql);
        try
        {
            using var response = action switch
            {
                "ProfileUpdated" => await client.PatchAsJsonAsync($"/api/v1/profiles/{seed.Profile}", new { name = "Atualização sintética" }),
                "ProfileClosed" => await client.PostAsJsonAsync($"/api/v1/profiles/{seed.Profile}/close", new { }),
                _ => await client.PostAsJsonAsync($"/api/v1/allocations/{allocation.Id}/release", new { })
            };
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.DoesNotContain("test_reliability_audit_failure", await response.Content.ReadAsStringAsync());
            var after = await db.OperationalProfiles.AsNoTracking().SingleAsync(p => p.Id == seed.Profile);
            Assert.Equal(before.Status, after.Status); Assert.Equal(before.Name, after.Name); Assert.Equal(before.UpdatedAt, after.UpdatedAt);
            Assert.Equal(AllocationStatus.Reserved, (await db.ProxyAllocations.AsNoTracking().SingleAsync(a => a.Id == allocation.Id)).Status);
            var expectedAction = Enum.Parse<OperationalAction>(action);
            Assert.False(await db.OperationalAuditEvents.AnyAsync(e => e.ProfileId == seed.Profile && e.Action == expectedAction));
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.\"OperationalEvents\" DROP CONSTRAINT test_reliability_audit_failure"); }
    }
}
