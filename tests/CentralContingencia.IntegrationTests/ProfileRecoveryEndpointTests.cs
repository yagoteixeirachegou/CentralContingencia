using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CentralContingencia.Application.Profiles.Recovery;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Infrastructure.Identity;
using CentralContingencia.Infrastructure.Persistence;
using CentralContingencia.Infrastructure.Proxies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using System.Collections.Concurrent;

namespace CentralContingencia.IntegrationTests;

[Collection("Authentication")]
public sealed class ProfileRecoveryEndpointTests(AuthFixture fixture) : IDisposable
{
    private readonly AuthFactory factory = new(fixture.ConnectionString, permitLimit: 1000, globalPermitLimit: 10000);
    private const string PreviewPath = "/api/v1/profiles/recovery/preview";
    private const string CommitPath = "/api/v1/profiles/recovery/commit";
    private const string Header = "PERFIL;PLATAFORMA;PLATAFORMA;PROXY;PORTA;USUARIO;SENHA;STATUS;DATA DE VENCIMENTO;REGIAO;ADsPower Lote";
    public void Dispose() => factory.Dispose();
    private HttpClient Client() => factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
    private static async Task Csrf(HttpClient client)
    { var value = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", value.GetProperty("token").GetString()); }
    private async Task Login(HttpClient client, string? email = null)
    { await Csrf(client); Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email = email ?? fixture.AdminEmail, password = fixture.Password })).StatusCode); await Csrf(client); }
    private static MultipartFormDataContent File(string csv)
    { var content = new MultipartFormDataContent(); content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "synthetic.csv"); return content; }
    private static string Row(Proxy proxy, string name) => $"{name};DARKPOST;TIKTOK;{proxy.Host.Value};{proxy.Port.Value};recovery-private-user;recovery-private-secret;ATIVO;31/12;;derived";
    private async Task<(Proxy Proxy, string Csv, string Name)> Seed()
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-30); var id = Guid.NewGuid(); var name = "Recovery " + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ProxyEncryption:ActiveKeyId"] = "synthetic-recovery", ["ProxyEncryption:Keys:synthetic-recovery"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }).Build();
        var protector = new AesGcmProxyPasswordProtector(configuration);
        var proxy = Proxy.Create(id, ProxyHost.Create("recovery-" + Guid.NewGuid().ToString("N") + ".synthetic.test"), ProxyPort.Create(8080), "recovery-private-user",
            protector.Encrypt(id, "recovery-private-secret"), "BR", now.AddDays(1), now); proxy.ChangeStatus(ProxyStatus.Active, now);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>(); db.Proxies.Add(proxy); await db.SaveChangesAsync();
        var persisted = await db.Proxies.AsNoTracking().SingleAsync(p => p.Id == proxy.Id);
        return (persisted, Header + "\n" + Row(persisted, name), name);
    }
    private static async Task<RecoveryPreview> Preview(HttpClient client, string csv, string query = "")
    { using var response = await client.PostAsync(PreviewPath + query, File(csv)); Assert.Equal(HttpStatusCode.OK, response.StatusCode); var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("recovery-private-secret", text); Assert.DoesNotContain("recovery-private-user", text); return (await response.Content.ReadFromJsonAsync<RecoveryPreview>())!; }
    private static async Task<HttpResponseMessage> Commit(HttpClient client, string csv, string? proof, Guid key, string query = "")
    { using var request = new HttpRequestMessage(HttpMethod.Post, CommitPath + query) { Content = File(csv) }; request.Headers.Add("X-Import-Confirmation", proof ?? "invalid"); request.Headers.Add("Idempotency-Key", key.ToString()); return await client.SendAsync(request); }
    private static string Fingerprint(Proxy proxy) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(proxy)));
    [PostgresFact]
    public async Task InfoproductRecoveryPreservesEmptySecondSlotAndExistingProxy()
    {
        var (proxy, csv, _) = await Seed();
        csv = csv.Replace("DARKPOST;TIKTOK", "INFOPRODUTO;");
        using var client = Client(); await Login(client);
        var preview = await Preview(client, csv);
        Assert.Equal(1, preview.Summary.Proposed);
        Assert.Equal("INFOPRODUTO", Assert.Single(preview.Items).Platform1);
        Assert.Null(preview.Items[0].Platform2);
        using var response = await Commit(client, csv, preview.ConfirmationToken, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = (await response.Content.ReadFromJsonAsync<RecoveryReport>())!;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        var profile = await db.OperationalProfiles.SingleAsync(p => p.Id == report.Items[0].ProfileId);
        Assert.Equal("INFOPRODUTO", profile.Platform1); Assert.Null(profile.Platform2);
        profile.UpdateDetails(profile.Name, profile.Platform, null, "INFOPRODUTO", DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        Assert.Equal(Fingerprint(proxy), Fingerprint(await db.Proxies.AsNoTracking().SingleAsync(p => p.Id == proxy.Id)));
    }
    [PostgresFact]
    public async Task RecoveryCreatesConfirmedLinkPreservesEncryptedProxyAndReplaysWithoutExtraAudit()
    {
        var (proxy, csv, name) = await Seed(); using var client = Client(); await Login(client); var before = Fingerprint(proxy);
        var preview = await Preview(client, csv); Assert.Equal(1, preview.Summary.Proposed); Assert.Equal(proxy.Id, preview.Items[0].ProxyId); Assert.Equal(2, preview.Summary.AssignmentsRecoverable);
        var key = Guid.NewGuid(); using var response = await Commit(client, csv, preview.ConfirmationToken, key); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = (await response.Content.ReadFromJsonAsync<RecoveryReport>())!; Assert.Equal(1, report.Recovered); var result = Assert.Single(report.Items);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        var profile = await db.OperationalProfiles.SingleAsync(p => p.Id == result.ProfileId); Assert.Equal(name, profile.Name.Value); Assert.Equal("DARKPOST", profile.Platform1); Assert.Equal("TIKTOK", profile.Platform2); Assert.Null(profile.Platform);
        var allocation = await db.ProxyAllocations.SingleAsync(a => a.Id == result.AllocationId); Assert.Equal(AllocationStatus.Confirmed, allocation.Status); Assert.True(allocation.CreatedAt > proxy.CreatedAt); Assert.Equal(3, await db.OperationalAuditEvents.CountAsync(e => e.ProfileId == profile.Id));
        Assert.Equal(before, Fingerprint(await db.Proxies.AsNoTracking().SingleAsync(p => p.Id == proxy.Id)));
        using var replay = await Commit(client, csv, "expired proof", key); Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.True((await replay.Content.ReadFromJsonAsync<RecoveryReport>())!.Replayed);
        Assert.Equal(3, await db.OperationalAuditEvents.CountAsync(e => e.ProfileId == profile.Id));
        var next = await Preview(client, csv); Assert.Equal("AlreadyLinked", next.Items[0].Outcome);
        using var nextResponse = await Commit(client, csv, next.ConfirmationToken, Guid.NewGuid()); Assert.Equal(HttpStatusCode.OK, nextResponse.StatusCode); Assert.Equal(0, (await nextResponse.Content.ReadFromJsonAsync<RecoveryReport>())!.Recovered);
        Assert.Single(await db.ProxyAllocations.Where(a => a.ProxyId == proxy.Id).ToArrayAsync());
        foreach (var text in new[] { await response.Content.ReadAsStringAsync(), JsonSerializer.Serialize(await db.ProfileRecoveryBatches.Include(b => b.Lines).Where(b => b.Id == report.RecoveryId).SingleAsync()), JsonSerializer.Serialize(await db.OperationalAuditEvents.Where(e => e.ProfileId == profile.Id).ToArrayAsync()) })
            foreach (var secret in new[] { "recovery-private-secret", "recovery-private-user", proxy.Host.Value }) Assert.DoesNotContain(secret, text);
    }
    [PostgresFact]
    public async Task ModifiedFileOptionsAndConcurrentProxyChangeRejectStaleProofWithoutProfiles()
    {
        var (proxy, csv, _) = await Seed(); using var client = Client(); await Login(client); var preview = await Preview(client, csv);
        using var modified = await Commit(client, csv.Replace("TIKTOK", "BM"), preview.ConfirmationToken, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Conflict, modified.StatusCode);
        using var options = await Commit(client, csv, preview.ConfirmationToken, Guid.NewGuid(), "?skipRowsWithoutConnection=true"); Assert.Equal(HttpStatusCode.Conflict, options.StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>(); var existing = await db.Proxies.SingleAsync(p => p.Id == proxy.Id); existing.ChangeStatus(ProxyStatus.Suspended, DateTimeOffset.UtcNow); await db.SaveChangesAsync();
        using var stale = await Commit(client, csv, preview.ConfirmationToken, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.Empty(await db.ProxyAllocations.Where(a => a.ProxyId == proxy.Id).ToArrayAsync());
    }
    [PostgresFact]
    public async Task ExpiredReservationsRecoverAtomicallyAndReleasedHistoryRemains()
    {
        var (proxy, csv, _) = await Seed(); var now = proxy.CreatedAt; var old = OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create("Old " + Guid.NewGuid()), null, now); var domain = new AllocationDomainService();
        var reservation = domain.Reserve(Guid.NewGuid(), old, proxy, Guid.NewGuid(), Guid.NewGuid(), now, now.AddMinutes(5), []);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>(); db.OperationalProfiles.Add(old); db.ProxyAllocations.Add(reservation); await db.SaveChangesAsync();
        using var client = Client(); await Login(client); var preview = await Preview(client, csv); Assert.Equal(1, preview.Summary.Proposed);
        using var response = await Commit(client, csv, preview.ConfirmationToken, Guid.NewGuid()); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var all = await db.ProxyAllocations.AsNoTracking().Where(a => a.ProxyId == proxy.Id).ToArrayAsync(); Assert.Equal(2, all.Length); Assert.Equal(AllocationStatus.Expired, all.Single(a => a.Id == reservation.Id).Status);
        Assert.Equal(1, await db.OperationalAuditEvents.CountAsync(e => e.AllocationId == reservation.Id && e.Action == CentralContingencia.Domain.Audit.OperationalAction.AllocationExpired));
    }
    [PostgresFact]
    public async Task ConcurrentRepeatedConfirmationCreatesExactlyOneAllocationAndReceipt()
    {
        var (proxy, csv, _) = await Seed(); using var client = Client(); await Login(client); var preview = await Preview(client, csv); var key = Guid.NewGuid();
        var responses = await Task.WhenAll(Commit(client, csv, preview.ConfirmationToken, key), Commit(client, csv, preview.ConfirmationToken, key));
        foreach (var response in responses) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reports = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<RecoveryReport>())); Assert.Equal(reports[0]!.RecoveryId, reports[1]!.RecoveryId); Assert.Contains(reports, r => r!.Replayed);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>(); Assert.Single(await db.ProxyAllocations.Where(a => a.ProxyId == proxy.Id).ToArrayAsync());
        Assert.Single(await db.ProfileRecoveryBatches.Where(b => b.IdempotencyKey == key).ToArrayAsync()); foreach (var response in responses) response.Dispose();
    }
    [PostgresFact]
    public async Task ConcurrentDifferentConfirmationsConflictInsteadOfOverwriting()
    {
        var (proxy, csv, _) = await Seed(); using var client = Client(); await Login(client); var secondCsv = csv.Replace("DARKPOST", "FLAG");
        var first = await Preview(client, csv); var second = await Preview(client, secondCsv);
        var responses = await Task.WhenAll(Commit(client, csv, first.ConfirmationToken, Guid.NewGuid()), Commit(client, secondCsv, second.ConfirmationToken, Guid.NewGuid()));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>(); Assert.Single(await db.ProxyAllocations.Where(a => a.ProxyId == proxy.Id).ToArrayAsync()); foreach (var response in responses) response.Dispose();
    }
    [PostgresFact]
    public async Task AuditFailureRollsBackProfilesAllocationsReceiptAndReturnsSanitized500()
    {
        var (proxy, csv, name) = await Seed(); using var client = Client(); await Login(client); var preview = await Preview(client, csv);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>(); var before = Fingerprint(proxy);
        await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION audit.synthetic_recovery_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'synthetic audit unavailable'; END $$; CREATE TRIGGER synthetic_recovery_failure BEFORE INSERT ON audit.\"OperationalEvents\" FOR EACH ROW EXECUTE FUNCTION audit.synthetic_recovery_failure();");
        var key = Guid.NewGuid();
        try
        {
            using var response = await Commit(client, csv, preview.ConfirmationToken, key); Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("synthetic audit unavailable", text); Assert.DoesNotContain("recovery-private-secret", text);
            Assert.Empty(await db.OperationalProfiles.FromSqlInterpolated($"SELECT *, xmin FROM profiles.\"OperationalProfiles\" WHERE \"Name\" = {name}").ToArrayAsync());
            Assert.Empty(await db.ProxyAllocations.Where(a => a.ProxyId == proxy.Id).ToArrayAsync()); Assert.Equal(before, Fingerprint(await db.Proxies.AsNoTracking().SingleAsync(p => p.Id == proxy.Id)));
            Assert.Empty(await db.ProfileRecoveryBatches.Where(b => b.IdempotencyKey == key).ToArrayAsync());
        }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER synthetic_recovery_failure ON audit.\"OperationalEvents\"; DROP FUNCTION audit.synthetic_recovery_failure();"); }
    }
    [PostgresFact]
    public async Task AmbiguousEndpointsExistingNamesAndReservationsAreExplicitConflicts()
    {
        var (proxy, csv, name) = await Seed(); using var client = Client(); await Login(client);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        var ambiguous = Proxy.Create(Guid.NewGuid(), proxy.Host, proxy.Port, "another-user", EncryptedProxyPassword.Create("test", RandomNumberGenerator.GetBytes(12), [1], RandomNumberGenerator.GetBytes(16)), "BR", proxy.ExpiresAt, proxy.CreatedAt);
        ambiguous.ChangeStatus(ProxyStatus.Active, proxy.CreatedAt); db.Proxies.Add(ambiguous); await db.SaveChangesAsync();
        var preview = await Preview(client, csv); Assert.Equal("AmbiguousProxy", preview.Items[0].Issues[0].Code); Assert.Null(preview.ConfirmationToken);
        var (other, secondCsv, secondName) = await Seed();
        var profile = OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create(secondName.ToLowerInvariant()), null, other.CreatedAt); db.OperationalProfiles.Add(profile); await db.SaveChangesAsync();
        var existing = await Preview(client, secondCsv); Assert.Equal("ExistingProfile", existing.Items[0].Issues[0].Code);
        var domain = new AllocationDomainService(); var allocation = domain.Reserve(Guid.NewGuid(), profile, other, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5), []); db.ProxyAllocations.Add(allocation); await db.SaveChangesAsync();
        var reserved = await Preview(client, secondCsv); Assert.Equal("ActiveAllocation", reserved.Items[0].Issues[0].Code); Assert.Null(reserved.ConfirmationToken);
        Assert.Equal(AllocationStatus.Reserved, (await db.ProxyAllocations.AsNoTracking().SingleAsync(a => a.Id == allocation.Id)).Status);
    }
    [PostgresFact]
    public async Task CsvCredentialsNeverEnterResponsesOrObservedLogs()
    {
        var (_, csv, _) = await Seed(); using var logs = new Capture();
        await using var logged = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(logs)));
        using var client = logged.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false }); await Login(client);
        var preview = await Preview(client, csv); using var response = await Commit(client, csv, preview.ConfirmationToken, Guid.NewGuid()); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (var message in logs.Messages.Append(await response.Content.ReadAsStringAsync()))
            foreach (var secret in new[] { "recovery-private-secret", "recovery-private-user", preview.ConfirmationToken! }) Assert.DoesNotContain(secret, message);
    }
    private sealed class Capture : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(Messages);
        public void Dispose() { }
        private sealed class Logger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
        }
    }
    [PostgresFact]
    public async Task AuthenticationRolesCsrfLimitsAndOpenApiAreEnforced()
    {
        using var client = Client(); using var anonymous = await client.PostAsync(PreviewPath, File(Header)); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        foreach (var role in new[] { "Manager", "Operator" })
        {
            using var scope = factory.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var email = "recovery-" + Guid.NewGuid().ToString("N") + "@example.test";
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email }; Assert.True((await users.CreateAsync(user, fixture.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            using var restricted = Client(); await Login(restricted, email); foreach (var path in new[] { PreviewPath, CommitPath }) { using var denied = await restricted.PostAsync(path, File(Header)); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); }
        }
        await Login(client); client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        foreach (var path in new[] { PreviewPath, CommitPath }) { using var noCsrf = await client.PostAsync(path, File(Header)); Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode); }
        await Csrf(client); using var large = await client.PostAsync(PreviewPath, File(new string('x', 2097153))); Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
        var openApi = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json"); var operation = openApi.GetProperty("paths").GetProperty(CommitPath).GetProperty("post"); Assert.True(operation.GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data", out _));
    }
}
