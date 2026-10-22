using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CentralContingencia.Application.AdsPower;
using CentralContingencia.Application.Ingestion;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CentralContingencia.IntegrationTests;

/// <summary>Ingestão de perfis pelo n8n. Cada teste usa um PostgreSQL descartável próprio, porque a disponibilidade de proxies é global.</summary>
public sealed class IngestionEndpointTests : IAsyncLifetime
{
    private const string Key = "synthetic-integration-key-0123456789abcdef";
    private const string OtherKey = "synthetic-other-integration-key-abcdef0123";
    private const string ScopelessKey = "synthetic-scopeless-integration-key-9876543210";
    private static readonly Guid KeyId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherKeyId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private string database = "", adminConnection = "", connection = "";
    private WebApplicationFactory<Program>? factory;
    private readonly FakeClock clock = new(DateTimeOffset.UtcNow);
    private readonly FakeGroups groups = new();

    /// <summary>O AdsPower de mentira: devolve o grupo ativo e o maior "Perfil N" dele (o piso da numeração).</summary>
    private sealed class FakeGroups : IAdsPowerGroupReader
    {
        public string GroupId { get; set; } = "10906211"; public string GroupName { get; set; } = "FB Teste | YAGO"; public int Highest { get; set; } = 1597;
        public AdsPowerFailure? Fail { get; set; }
        public List<string> Asked { get; } = [];
        public Task<AdsPowerGroupSnapshot> ReadGroupAsync(string groupName, CancellationToken token)
        {
            lock (Asked) Asked.Add(groupName);
            if (Fail is { } failure) throw new AdsPowerException(failure);
            return Task.FromResult(new AdsPowerGroupSnapshot(GroupId, GroupName, 0, Highest));
        }
    }

    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = start;
        public void Advance(TimeSpan span) => Now += span;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class FakeProtector : IProxyPasswordProtector
    {
        public static string PasswordOf(Guid proxyId) => $"pw-{proxyId:N}";
        public EncryptedProxyPassword Encrypt(Guid proxyId, string password) => EncryptedProxyPassword.Create("test", RandomNumberGenerator.GetBytes(12), [1, 2, 3], RandomNumberGenerator.GetBytes(16));
        public string Decrypt(Guid proxyId, EncryptedProxyPassword password) => PasswordOf(proxyId);
    }
    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    private sealed class IngestionFactory(string connection, TimeProvider time, IAdsPowerGroupReader reader) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connection, ["ProxyEncryption:ActiveKeyId"] = null,
                ["Security:EndpointPermitLimit"] = "100000", ["Security:GlobalPermitLimit"] = "100000", ["Security:IntegrationPermitLimit"] = "100000",
                ["Integration:Keys:0:Id"] = KeyId.ToString(), ["Integration:Keys:0:Name"] = "n8n-test", ["Integration:Keys:0:KeyHash"] = Hash(Key), ["Integration:Keys:0:Scopes:0"] = "profile-ingestion",
                ["Integration:Keys:1:Id"] = OtherKeyId.ToString(), ["Integration:Keys:1:Name"] = "other", ["Integration:Keys:1:KeyHash"] = Hash(OtherKey), ["Integration:Keys:1:Scopes:0"] = "profile-ingestion",
                ["Integration:Keys:2:Id"] = Guid.NewGuid().ToString(), ["Integration:Keys:2:Name"] = "scopeless", ["Integration:Keys:2:KeyHash"] = Hash(ScopelessKey), ["Integration:Keys:2:Scopes:0"] = "something-else",
            }));
            builder.ConfigureServices(services => { services.AddSingleton<TimeProvider>(time); services.AddSingleton<IProxyPasswordProtector, FakeProtector>(); services.AddScoped(_ => reader); });
        }
    }

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("CENTRAL_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var options = new NpgsqlConnectionStringBuilder(configured);
        if (options.Host is not ("127.0.0.1" or "localhost")) throw new InvalidOperationException("Testes somente em PostgreSQL local.");
        adminConnection = configured; database = "p6_auth_test_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(adminConnection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin); await create.ExecuteNonQueryAsync();
        }
        options.Database = database; connection = options.ConnectionString;
        factory = new IngestionFactory(connection, clock, groups);
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>().Database.MigrateAsync();
    }
    public async Task DisposeAsync()
    {
        factory?.Dispose();
        if (database.Length == 0) return;
        if (!database.StartsWith("p6_auth_test_") || !Guid.TryParseExact(database["p6_auth_test_".Length..], "N", out _)) throw new InvalidOperationException("Nome do banco descartável inválido.");
        await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
    }

    private CentralContingenciaDbContext Db() => new(new DbContextOptionsBuilder<CentralContingenciaDbContext>().UseNpgsql(connection).Options);
    private HttpClient Client(string? key = Key)
    {
        var client = factory!.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (key is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return client;
    }
    private static StringContent Body(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
    private async Task<(HttpResponseMessage Response, JsonElement Json)> Reserve(object body, Guid? key = null, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/integration/profile-ingestions/reserve") { Content = Body(body) };
        if (key != Guid.Empty) request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        var response = await (client ?? Client()).SendAsync(request); var text = await response.Content.ReadAsStringAsync();
        return (response, text.Length > 0 ? JsonDocument.Parse(text).RootElement.Clone() : default);
    }
    private async Task<HttpResponseMessage> Post(string path, object? body = null, HttpClient? client = null) => await (client ?? Client()).PostAsync("/api/v1/integration/profile-ingestions/" + path, Body(body ?? new { }));
    private async Task<JsonElement> Availability() => JsonDocument.Parse(await Client().GetStringAsync("/api/v1/integration/profile-ingestions/availability")).RootElement.Clone();

    private async Task<List<Proxy>> SeedProxies(int count, bool withSheetLines = false, int firstLine = 2)
    {
        var created = clock.Now.AddMinutes(-30); var proxies = new List<Proxy>();
        for (var i = 0; i < count; i++)
        {
            var proxy = Proxy.Create(Guid.NewGuid(), ProxyHost.Create($"ing-{Guid.NewGuid():N}.synthetic.test"), ProxyPort.Create(8000 + i), $"ing-user-{i}",
                EncryptedProxyPassword.Create("test", RandomNumberGenerator.GetBytes(12), [1, 2, 3], RandomNumberGenerator.GetBytes(16)), "Brasil", clock.Now.AddDays(30), created);
            proxy.ChangeStatus(ProxyStatus.Active, created); proxies.Add(proxy);
        }
        await using var db = Db(); db.Proxies.AddRange(proxies);
        if (withSheetLines) db.ProxyImportBatches.Add(ProxyImportBatch.Create(Guid.NewGuid(), Guid.NewGuid(), "synthetic", created, "seed",
            proxies.Select((p, i) => new ProxyImportLine(firstLine + i, p.Id, ProxyImportOutcome.Imported)).ToArray()));
        await db.SaveChangesAsync();
        return proxies;
    }
    private async Task SeedProfile(string name)
    {
        await using var db = Db(); db.OperationalProfiles.Add(OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create(name), null, clock.Now.AddMinutes(-30))); await db.SaveChangesAsync();
    }
    private static string[] Names(JsonElement json) => json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("profileName").GetString()!).ToArray();
    private static Guid[] AllocationIds(JsonElement json) => json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("allocationId").GetGuid()).ToArray();

    [PostgresFact]
    public async Task Requires_a_valid_integration_key_with_the_right_scope_and_does_not_open_human_endpoints()
    {
        foreach (var key in new string?[] { null, "wrong-key-wrong-key-wrong-key-wrong-key", "short" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client(key).GetAsync("/api/v1/integration/profile-ingestions/availability")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(ScopelessKey).GetAsync("/api/v1/integration/profile-ingestions/availability")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Client().GetAsync("/api/v1/integration/profile-ingestions/availability")).StatusCode);
        // Menor privilégio: a chave de serviço não abre a API das pessoas.
        foreach (var path in new[] { "/api/v1/proxies", "/api/v1/profiles", "/api/v1/adspower/dashboard", "/api/v1/proxies/operational-grid" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync(path)).StatusCode);
    }

    [PostgresFact]
    public async Task Availability_uses_the_adspower_group_for_numbering_and_ignores_other_central_profiles()
    {
        await SeedProxies(3); groups.Highest = 1243;
        await SeedProfile("Perfil 1597"); await SeedProfile("Perfil 9000"); await SeedProfile("Perfil 12"); await SeedProfile("Teste manual"); // perfis da Central fora da ingestão não contam
        var json = await Availability();
        Assert.Equal(3, json.GetProperty("availableProxies").GetInt32()); Assert.Equal(1244, json.GetProperty("nextProfileNumber").GetInt32()); Assert.Equal("Perfil 1244", json.GetProperty("nextProfileName").GetString());
        var group = json.GetProperty("group"); Assert.Equal("10906211", group.GetProperty("id").GetString()); Assert.Equal("FB Teste | YAGO", group.GetProperty("name").GetString()); Assert.Equal("Test", group.GetProperty("mode").GetString());
        Assert.Equal(["FB Teste | YAGO"], groups.Asked.Distinct()); // em modo Test só o grupo de testes é consultado
        Assert.DoesNotContain("password", json.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task Reserves_a_full_batch_returning_credentials_only_for_the_reserved_proxies()
    {
        var proxies = await SeedProxies(6); await SeedProfile("Perfil 1597");
        var (response, json) = await Reserve(new { count = 5, platform1 = "ESTEIRA DE PERFIL" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("Complete", json.GetProperty("status").GetString()); Assert.Equal(5, json.GetProperty("reserved").GetInt32()); Assert.Empty(json.GetProperty("pending").EnumerateArray());
        Assert.Equal(["Perfil 1598", "Perfil 1599", "Perfil 1600", "Perfil 1601", "Perfil 1602"], Names(json));
        Assert.Equal("10906211", json.GetProperty("group").GetProperty("id").GetString()); Assert.Equal("Test", json.GetProperty("group").GetProperty("mode").GetString());
        var used = json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("proxy")).ToArray();
        Assert.Equal(5, used.Select(p => p.GetProperty("host").GetString()).Distinct().Count());
        foreach (var proxy in used)
        {
            var source = proxies.Single(p => p.Host.Value == proxy.GetProperty("host").GetString());
            Assert.Equal(source.Port.Value, proxy.GetProperty("port").GetInt32()); Assert.Equal(source.Username, proxy.GetProperty("username").GetString());
            Assert.Equal(FakeProtector.PasswordOf(source.Id), proxy.GetProperty("password").GetString());
        }
        // A sexta proxy não faz parte do lote: nenhum dado dela pode aparecer.
        var spare = proxies.Single(p => !used.Any(u => u.GetProperty("host").GetString() == p.Host.Value));
        Assert.DoesNotContain(spare.Host.Value, json.GetRawText()); Assert.DoesNotContain(FakeProtector.PasswordOf(spare.Id), json.GetRawText());
        await using var db = Db();
        Assert.Equal(5, await db.ProxyAllocations.CountAsync(a => a.Status == AllocationStatus.Reserved));
        var profiles = await db.OperationalProfiles.Where(p => p.IngestionBatchId != null).ToListAsync();
        Assert.Equal(5, profiles.Count); Assert.All(profiles, p => { Assert.Equal(ProfileStatus.Active, p.Status); Assert.Equal("ESTEIRA DE PERFIL", p.Platform1); Assert.Null(p.AdsPowerId); });
        var audit = await db.OperationalAuditEvents.ToListAsync();
        Assert.Equal(5, audit.Count(e => e.Action.ToString() == "ProfileCreated" && e.ActorId == KeyId)); Assert.Equal(5, audit.Count(e => e.Action.ToString() == "AllocationReserved" && e.ActorId == KeyId));
        Assert.All(json.GetProperty("items").EnumerateArray(), item => Assert.True(item.GetProperty("reservationExpiresAt").GetDateTimeOffset() <= clock.Now.AddMinutes(15).AddSeconds(1)));
    }

    [PostgresFact]
    public async Task Delivers_what_it_can_and_lists_the_pending_positions_with_the_reason()
    {
        await SeedProxies(3);
        var (_, partial) = await Reserve(new { count = 5 });
        Assert.Equal("Partial", partial.GetProperty("status").GetString()); Assert.Equal(5, partial.GetProperty("requested").GetInt32()); Assert.Equal(3, partial.GetProperty("reserved").GetInt32());
        Assert.Equal(["Perfil 1598", "Perfil 1599", "Perfil 1600"], Names(partial)); // pendentes não consomem número
        var pending = partial.GetProperty("pending").EnumerateArray().ToArray();
        Assert.Equal([4, 5], pending.Select(p => p.GetProperty("position").GetInt32())); Assert.All(pending, p => { Assert.Equal("NoProxyAvailable", p.GetProperty("reason").GetString()); Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("message").GetString())); });
        var (_, empty) = await Reserve(new { count = 2 });
        Assert.Equal("Empty", empty.GetProperty("status").GetString()); Assert.Equal(0, empty.GetProperty("reserved").GetInt32()); Assert.Equal(2, empty.GetProperty("pending").GetArrayLength()); Assert.Empty(empty.GetProperty("items").EnumerateArray());
        await using var db = Db(); Assert.Equal(3, await db.OperationalProfiles.CountAsync(p => p.IngestionBatchId != null));
    }

    [PostgresFact]
    public async Task Picks_proxies_in_the_original_sheet_order()
    {
        var proxies = await SeedProxies(4, withSheetLines: true); // linhas 2..5 na ordem de cadastro
        await using (var db = Db())
        {   // inverte a ordem da planilha: a última criada é a primeira linha
            await db.Database.ExecuteSqlRawAsync("UPDATE audit.\"ProxyImportLines\" SET \"Line\" = 100 - \"Line\"");
        }
        var extra = await SeedProxies(1); // sem linha de planilha: vem depois
        var (_, json) = await Reserve(new { count = 5 });
        var hosts = json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("proxy").GetProperty("host").GetString()).ToArray();
        Assert.Equal(proxies.AsEnumerable().Reverse().Select(p => p.Host.Value).Append(extra[0].Host.Value), hosts);
    }

    [PostgresFact]
    public async Task Reserved_proxies_are_exclusive_across_batches()
    {
        await SeedProxies(3);
        var (_, first) = await Reserve(new { count = 2 }); var (_, second) = await Reserve(new { count = 2 });
        Assert.Equal(2, first.GetProperty("reserved").GetInt32()); Assert.Equal("Partial", second.GetProperty("status").GetString()); Assert.Equal(1, second.GetProperty("reserved").GetInt32());
        var hosts = first.GetProperty("items").EnumerateArray().Concat(second.GetProperty("items").EnumerateArray()).Select(i => i.GetProperty("proxy").GetProperty("host").GetString()).ToArray();
        Assert.Equal(3, hosts.Distinct().Count()); Assert.Equal(0, (await Availability()).GetProperty("availableProxies").GetInt32());
    }

    [PostgresFact]
    public async Task Repeating_the_same_request_returns_the_original_result_without_new_rows()
    {
        await SeedProxies(2); var key = Guid.NewGuid();
        var (_, first) = await Reserve(new { count = 3 }, key);
        await SeedProxies(1); // uma proxy nova não muda o resultado já decidido
        var (response, replay) = await Reserve(new { count = 3 }, key);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(replay.GetProperty("replayed").GetBoolean()); Assert.Equal("Partial", replay.GetProperty("status").GetString());
        Assert.Equal(AllocationIds(first), AllocationIds(replay)); Assert.Equal(1, replay.GetProperty("pending").GetArrayLength());
        Assert.Equal(first.GetProperty("items")[0].GetProperty("proxy").GetProperty("password").GetString(), replay.GetProperty("items")[0].GetProperty("proxy").GetProperty("password").GetString());
        var (conflict, _) = await Reserve(new { count = 2 }, key); Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var (platform, _) = await Reserve(new { count = 3, platform1 = "BM" }, key); Assert.Equal(HttpStatusCode.Conflict, platform.StatusCode);
        await using var db = Db(); Assert.Equal(2, await db.ProxyAllocations.CountAsync()); Assert.Equal(2, await db.OperationalProfiles.CountAsync()); Assert.Equal(1, await db.ProfileIngestionBatches.CountAsync());
    }

    [PostgresFact]
    public async Task Confirming_marks_the_proxy_in_use_and_stores_the_adspower_id_and_serial()
    {
        await SeedProxies(2); var (_, json) = await Reserve(new { count = 2 }); var ids = AllocationIds(json);
        var ok = await Post($"{ids[0]}/confirm", new { adsPowerId = "k1abc12", serialNumber = "1234" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        await using (var db = Db())
        {
            var allocation = await db.ProxyAllocations.SingleAsync(a => a.Id == ids[0]); Assert.Equal(AllocationStatus.Confirmed, allocation.Status);
            var profile = await db.OperationalProfiles.SingleAsync(p => p.Id == allocation.ProfileId); Assert.Equal("k1abc12", profile.AdsPowerId); Assert.Equal("1234", profile.AdsPowerSerialNumber);
            Assert.Equal(1, await db.OperationalAuditEvents.CountAsync(e => e.AllocationId == ids[0] && e.Action == CentralContingencia.Domain.Audit.OperationalAction.AllocationConfirmed && e.ActorId == KeyId));
            Assert.Equal(1, await db.OperationalAuditEvents.CountAsync(e => e.ProfileId == profile.Id && e.Action == CentralContingencia.Domain.Audit.OperationalAction.ProfileUpdated));
        }
        // Repetir os mesmos valores é aceito e não duplica a auditoria; trocar o id do AdsPower é conflito.
        Assert.Equal(HttpStatusCode.NoContent, (await Post($"{ids[0]}/confirm", new { adsPowerId = "k1abc12", serialNumber = "1234" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post($"{ids[0]}/confirm", new { adsPowerId = "other-id-9" })).StatusCode);
        await using (var db = Db()) Assert.Equal(1, await db.OperationalAuditEvents.CountAsync(e => e.AllocationId == ids[0] && e.Action == CentralContingencia.Domain.Audit.OperationalAction.AllocationConfirmed));
        // O mesmo id do AdsPower não pode vincular dois perfis.
        Assert.Equal(HttpStatusCode.Conflict, (await Post($"{ids[1]}/confirm", new { adsPowerId = "k1abc12" })).StatusCode);
        await using (var db = Db()) Assert.Equal(AllocationStatus.Reserved, (await db.ProxyAllocations.SingleAsync(a => a.Id == ids[1])).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await Post($"{ids[1]}/confirm", new { adsPowerId = "k2xyz34" })).StatusCode);
        // Depois de confirmada, a conexão não é mais entregue em repetições da reserva.
        // (A chave de idempotência original não está mais em mãos; o conteúdo é validado pelo teste de reserva vencida.)
    }

    [PostgresFact]
    public async Task Confirm_validates_input_and_hides_other_credentials_batches()
    {
        await SeedProxies(1); var (_, json) = await Reserve(new { count = 1 }); var id = AllocationIds(json)[0];
        Assert.Equal(HttpStatusCode.NotFound, (await Post($"{Guid.NewGuid()}/confirm", new { adsPowerId = "k1abc12" })).StatusCode);
        foreach (var body in new object[] { new { adsPowerId = "" }, new { adsPowerId = "x" }, new { adsPowerId = "bad id!" }, new { adsPowerId = "k1abc12", serialNumber = "12ab" }, new { adsPowerId = "k1abc12", extra = 1 }, new { } })
            Assert.Equal(HttpStatusCode.BadRequest, (await Post($"{id}/confirm", body)).StatusCode);
        // Outra credencial de serviço não enxerga nem altera a reserva.
        Assert.Equal(HttpStatusCode.NotFound, (await Post($"{id}/confirm", new { adsPowerId = "k1abc12" }, Client(OtherKey))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post($"{id}/release", null, Client(OtherKey))).StatusCode);
        await using var db = Db(); Assert.Equal(AllocationStatus.Reserved, (await db.ProxyAllocations.SingleAsync(a => a.Id == id)).Status);
    }

    [PostgresFact]
    public async Task Releasing_frees_the_proxy_and_the_profile_number_can_be_reused()
    {
        var proxies = await SeedProxies(2); await SeedProfile("Perfil 1597");
        var (_, json) = await Reserve(new { count = 2 }); var ids = AllocationIds(json);
        Assert.Equal(HttpStatusCode.NoContent, (await Post($"{ids[1]}/release")).StatusCode);
        var availability = await Availability(); Assert.Equal(1, availability.GetProperty("availableProxies").GetInt32()); Assert.Equal("Perfil 1599", availability.GetProperty("nextProfileName").GetString());
        await using (var db = Db())
        {
            var allocation = await db.ProxyAllocations.SingleAsync(a => a.Id == ids[1]); Assert.Equal(AllocationStatus.Released, allocation.Status); Assert.Equal(AllocationEndReason.ProfileClosed, allocation.EndReason);
            Assert.Equal(ProfileStatus.Closed, (await db.OperationalProfiles.SingleAsync(p => p.Id == allocation.ProfileId)).Status);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await Post($"{ids[1]}/release")).StatusCode); // repetir é aceito
        var (_, again) = await Reserve(new { count = 1 });
        Assert.Equal(["Perfil 1599"], Names(again)); // número reaproveitado, como na planilha
        // Confirmada, a reserva não pode mais ser liberada por esta via.
        Assert.Equal(HttpStatusCode.NoContent, (await Post($"{ids[0]}/confirm", new { adsPowerId = "k1abc12", serialNumber = "77" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Post($"{ids[0]}/release")).StatusCode);
        await using var final = Db(); Assert.Equal(ProfileStatus.Active, (await final.OperationalProfiles.SingleAsync(p => p.AdsPowerId == "k1abc12")).Status);
    }

    [PostgresFact]
    public async Task A_lapsed_reservation_cannot_be_confirmed_and_is_recovered_with_number_and_proxy_released()
    {
        await SeedProxies(2); var key = Guid.NewGuid();
        var (_, json) = await Reserve(new { count = 2 }, key); var ids = AllocationIds(json);
        clock.Advance(TimeSpan.FromMinutes(16));
        var (_, replay) = await Reserve(new { count = 2 }, key); // repetição depois do prazo não devolve mais a conexão
        Assert.All(replay.GetProperty("items").EnumerateArray(), item => { Assert.Equal("Expired", item.GetProperty("status").GetString()); Assert.Equal(JsonValueKind.Null, item.GetProperty("proxy").ValueKind); });
        Assert.Equal(HttpStatusCode.Conflict, (await Post($"{ids[0]}/confirm", new { adsPowerId = "k1abc12" })).StatusCode);
        await using (var db = Db())
        {
            Assert.Equal(2, await db.ProxyAllocations.CountAsync(a => a.Status == AllocationStatus.Expired)); Assert.Equal(2, await db.OperationalProfiles.CountAsync(p => p.Status == ProfileStatus.Closed));
            Assert.Equal(2, await db.OperationalAuditEvents.CountAsync(e => e.Action == CentralContingencia.Domain.Audit.OperationalAction.AllocationExpired));
            Assert.Null((await db.OperationalProfiles.FirstAsync(p => p.IngestionBatchId != null)).AdsPowerId);
        }
        var availability = await Availability(); Assert.Equal(2, availability.GetProperty("availableProxies").GetInt32()); Assert.Equal("Perfil 1598", availability.GetProperty("nextProfileName").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await Post($"{ids[1]}/release")).StatusCode); // liberar o que venceu é inofensivo
    }

    [PostgresFact]
    public async Task Confirmed_reservations_never_expose_the_connection_again()
    {
        await SeedProxies(1); var key = Guid.NewGuid(); var (_, json) = await Reserve(new { count = 1 }, key);
        Assert.Equal(HttpStatusCode.NoContent, (await Post($"{AllocationIds(json)[0]}/confirm", new { adsPowerId = "k1abc12" })).StatusCode);
        var (_, replay) = await Reserve(new { count = 1 }, key); var item = replay.GetProperty("items")[0];
        Assert.Equal("Confirmed", item.GetProperty("status").GetString()); Assert.Equal(JsonValueKind.Null, item.GetProperty("proxy").ValueKind);
        Assert.DoesNotContain("pw-", replay.GetRawText()); Assert.DoesNotContain("pw-", (await Availability()).GetRawText());
    }

    [PostgresFact]
    public async Task Concurrent_batches_never_share_a_proxy()
    {
        await SeedProxies(6);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Reserve(new { count = 2 })));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.Response.StatusCode));
        Assert.Equal(6, results.Sum(r => r.Json.GetProperty("reserved").GetInt32()));
        var hosts = results.SelectMany(r => r.Json.GetProperty("items").EnumerateArray()).Select(i => i.GetProperty("proxy").GetProperty("host").GetString()).ToArray();
        Assert.Equal(6, hosts.Distinct().Count());
        var names = results.SelectMany(r => Names(r.Json)).ToArray(); Assert.Equal(6, names.Distinct().Count());
        await using var db = Db(); Assert.Equal(6, await db.ProxyAllocations.CountAsync(a => a.Status == AllocationStatus.Reserved));
    }

    [PostgresFact]
    public async Task Downgrading_the_migration_is_refused_while_adspower_links_or_batches_exist()
    {
        await SeedProxies(1); var (_, json) = await Reserve(new { count = 1 });
        await using var db = Db();
        var guard = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync("20261012174401_AddInfoproductProfileAssignment"));
        Assert.Equal(PostgresErrorCodes.RaiseException, guard.SqlState);
        Assert.Equal(1, await db.OperationalProfiles.CountAsync(p => p.IngestionBatchId != null)); // nada foi descartado
        Assert.Equal(1, await db.ProfileIngestionBatches.CountAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await Post($"{AllocationIds(json)[0]}/confirm", new { adsPowerId = "k1abc12" })).StatusCode);
    }

    [PostgresFact]
    public async Task Rejects_invalid_requests_without_side_effects()
    {
        await SeedProxies(2);
        foreach (var body in new object[] { new { count = 0 }, new { count = 51 }, new { count = -1 }, new { }, new { count = 1, platform1 = "INEXISTENTE" }, new { count = 1, unknown = true } })
            Assert.Equal(HttpStatusCode.BadRequest, (await Reserve(body)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reserve(new { count = 1 }, Guid.Empty)).Response.StatusCode); // sem Idempotency-Key
        using var badKey = new HttpRequestMessage(HttpMethod.Post, "/api/v1/integration/profile-ingestions/reserve") { Content = Body(new { count = 1 }) }; badKey.Headers.Add("Idempotency-Key", "not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, (await Client().SendAsync(badKey)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client().PostAsync("/api/v1/integration/profile-ingestions/reserve", new StringContent("{ nope", Encoding.UTF8, "application/json"))).StatusCode);
        await using var db = Db(); Assert.Equal(0, await db.OperationalProfiles.CountAsync()); Assert.Equal(0, await db.ProxyAllocations.CountAsync()); Assert.Equal(0, await db.ProfileIngestionBatches.CountAsync());
    }

    [PostgresFact]
    public async Task Ignores_inactive_expired_and_suspended_proxies()
    {
        var proxies = await SeedProxies(4);
        await using (var db = Db())
        {
            var loaded = await db.Proxies.ToListAsync(); var set = proxies.Select(p => loaded.Single(l => l.Id == p.Id)).ToList(); // mesma ordem da lista semeada
            set[0].ChangeStatus(ProxyStatus.Suspended, clock.Now.AddMinutes(-10)); set[1].ChangeStatus(ProxyStatus.Disabled, clock.Now.AddMinutes(-10));
            set[2].UpdateDetails(set[2].Host, set[2].Port, set[2].Username, set[2].Region, clock.Now.AddMinutes(-1), ProxyStatus.Active, clock.Now.AddMinutes(-10)); // vencida
            await db.SaveChangesAsync();
        }
        var (_, json) = await Reserve(new { count = 4 });
        Assert.Equal(1, json.GetProperty("reserved").GetInt32()); Assert.Equal(proxies[3].Host.Value, json.GetProperty("items")[0].GetProperty("proxy").GetProperty("host").GetString()); Assert.Equal(3, json.GetProperty("pending").GetArrayLength());
    }

    [PostgresFact]
    public async Task Numbering_never_crosses_groups_and_the_central_only_adds_what_the_adspower_group_does_not_show_yet()
    {
        await SeedProxies(8);
        groups.Highest = 1250; // grupo de teste (YAGO)
        var (_, test) = await Reserve(new { count = 2 });
        Assert.Equal(["Perfil 1251", "Perfil 1252"], Names(test));
        // Reservas da Central que o AdsPower ainda não mostra entram na conta: o próximo lote do mesmo grupo segue em 1253.
        var (_, next) = await Reserve(new { count = 1 }); Assert.Equal(["Perfil 1253"], Names(next));
        // Outro grupo (produção): o piso vem do AdsPower e os lotes de teste não interferem.
        groups.GroupId = "10477650"; groups.GroupName = "03 ESTEIRA DE PERFIL"; groups.Highest = 1243;
        var availability = await Availability(); Assert.Equal("Perfil 1244", availability.GetProperty("nextProfileName").GetString());
        var (_, production) = await Reserve(new { count = 2 });
        Assert.Equal(["Perfil 1244", "Perfil 1245"], Names(production)); Assert.Equal("10477650", production.GetProperty("group").GetProperty("id").GetString());
        await using var db = Db();
        Assert.Equal(["10477650", "10906211"], (await db.ProfileIngestionBatches.Select(b => b.AdsPowerGroupId!).Distinct().ToListAsync()).Order().ToArray());
        // A mesma chave de idempotência usada em outro grupo não é a mesma operação.
        var key = Guid.NewGuid(); var (ok, _) = await Reserve(new { count = 1 }, key); Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        groups.GroupId = "10906211"; groups.GroupName = "FB Teste | YAGO";
        Assert.Equal(HttpStatusCode.Conflict, (await Reserve(new { count = 1 }, key)).Response.StatusCode);
    }

    [PostgresFact]
    public async Task The_adspower_floor_wins_when_it_is_higher_than_what_the_central_reserved()
    {
        await SeedProxies(4); groups.Highest = 1243;
        var (_, first) = await Reserve(new { count = 1 }); Assert.Equal(["Perfil 1244"], Names(first));
        groups.Highest = 1260; // alguém criou perfis direto no AdsPower
        var (_, second) = await Reserve(new { count = 1 }); Assert.Equal(["Perfil 1261"], Names(second));
    }

    [PostgresTheory]
    [InlineData(AdsPowerFailure.GroupNotFound, 503)]
    [InlineData(AdsPowerFailure.Unreachable, 503)]
    [InlineData(AdsPowerFailure.NotConfigured, 503)]
    [InlineData(AdsPowerFailure.Unauthorized, 502)]
    [InlineData(AdsPowerFailure.Failed, 502)]
    public async Task Without_the_adspower_group_nothing_is_reserved_and_no_number_is_invented(AdsPowerFailure failure, int status)
    {
        await SeedProxies(2); groups.Fail = failure;
        var (response, json) = await Reserve(new { count = 2 }); Assert.Equal(status, (int)response.StatusCode); Assert.Equal(failure.ToString(), json.GetProperty("code").GetString());
        var availability = await Client().GetAsync("/api/v1/integration/profile-ingestions/availability"); Assert.Equal(status, (int)availability.StatusCode);
        Assert.DoesNotContain("Exception", await availability.Content.ReadAsStringAsync());
        await using var db = Db(); Assert.Equal(0, await db.OperationalProfiles.CountAsync()); Assert.Equal(0, await db.ProxyAllocations.CountAsync()); Assert.Equal(0, await db.ProfileIngestionBatches.CountAsync());
        groups.Fail = null; Assert.Equal(2, (await Reserve(new { count = 2 })).Json.GetProperty("reserved").GetInt32()); // volta ao normal
    }

    private static (int Released, int Confirmed, int Ended) Counts(JsonElement json) => (json.GetProperty("released").GetInt32(), json.GetProperty("confirmed").GetInt32(), json.GetProperty("alreadyEnded").GetInt32());
    private async Task<JsonElement> ReleaseBatch(Guid batch, HttpClient? client = null)
    {
        var response = await Post($"batches/{batch}/release", null, client); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [PostgresFact]
    public async Task Releasing_a_whole_batch_frees_what_is_pending_and_keeps_what_was_confirmed()
    {
        await SeedProxies(4); var (_, json) = await Reserve(new { count = 3 }); var batch = json.GetProperty("batchId").GetGuid(); var ids = AllocationIds(json);
        Assert.Equal(HttpStatusCode.NoContent, (await Post($"{ids[0]}/confirm", new { adsPowerId = "k1abc12", serialNumber = "55" })).StatusCode);
        Assert.Equal((2, 1, 0), Counts(await ReleaseBatch(batch)));
        await using (var db = Db())
        {
            Assert.Equal(AllocationStatus.Confirmed, (await db.ProxyAllocations.SingleAsync(a => a.Id == ids[0])).Status);
            foreach (var id in ids.Skip(1)) { var allocation = await db.ProxyAllocations.SingleAsync(a => a.Id == id); Assert.Equal((AllocationStatus.Released, AllocationEndReason.ProfileClosed), (allocation.Status, allocation.EndReason)); Assert.Equal(ProfileStatus.Closed, (await db.OperationalProfiles.SingleAsync(p => p.Id == allocation.ProfileId)).Status); }
            Assert.Equal(ProfileStatus.Active, (await db.OperationalProfiles.SingleAsync(p => p.AdsPowerId == "k1abc12")).Status);
            Assert.Equal(2, await db.OperationalAuditEvents.CountAsync(e => e.Action == CentralContingencia.Domain.Audit.OperationalAction.ProfileClosed && e.ActorId == KeyId));
            Assert.Equal(2, await db.OperationalAuditEvents.CountAsync(e => e.Action == CentralContingencia.Domain.Audit.OperationalAction.AllocationReleased && e.ActorId == KeyId));
        }
        var availability = await Availability(); Assert.Equal(3, availability.GetProperty("availableProxies").GetInt32()); Assert.Equal("Perfil 1599", availability.GetProperty("nextProfileName").GetString()); // 1599 e 1600 voltam a ser usados
        // Repetir é seguro e não cria novos eventos.
        Assert.Equal((0, 1, 2), Counts(await ReleaseBatch(batch)));
        await using var again = Db(); Assert.Equal(2, await again.OperationalAuditEvents.CountAsync(e => e.Action == CentralContingencia.Domain.Audit.OperationalAction.AllocationReleased));
    }

    [PostgresFact]
    public async Task Batch_release_is_private_to_the_credential_and_treats_lapsed_reservations_as_already_ended()
    {
        await SeedProxies(3); var (_, json) = await Reserve(new { count = 2 }); var batch = json.GetProperty("batchId").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await Post($"batches/{batch}/release", null, Client(OtherKey))).StatusCode); // outra credencial
        Assert.Equal(HttpStatusCode.NotFound, (await Post($"batches/{Guid.NewGuid()}/release")).StatusCode); // lote inexistente
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post($"batches/{batch}/release", null, Client(null))).StatusCode);
        await using (var db = Db()) Assert.Equal(2, await db.ProxyAllocations.CountAsync(a => a.Status == AllocationStatus.Reserved)); // nada foi desfeito pelas tentativas recusadas
        clock.Advance(TimeSpan.FromMinutes(16)); // reservas vencidas já não estão "vivas"
        Assert.Equal((0, 0, 2), Counts(await ReleaseBatch(batch)));
        Assert.Equal(3, (await Availability()).GetProperty("availableProxies").GetInt32());
    }
}
