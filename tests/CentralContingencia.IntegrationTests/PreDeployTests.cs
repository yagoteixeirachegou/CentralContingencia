using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CentralContingencia.Infrastructure.Hosting;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CentralContingencia.IntegrationTests;

/// <summary>Banco totalmente vazio (sem nenhuma migration), como o PostgreSQL novo do Railway. Removido ao final.</summary>
public sealed class EmptyDatabase : IAsyncDisposable
{
    private readonly string admin;
    public string Connection { get; }
    public string Password { get; }

    private EmptyDatabase(string admin, string connection, string password) { this.admin = admin; Connection = connection; Password = password; }

    public static async Task<EmptyDatabase> Create()
    {
        var configured = Environment.GetEnvironmentVariable("CENTRAL_TEST_POSTGRES")!;
        var builder = new NpgsqlConnectionStringBuilder(configured);
        if (builder.Host is not ("127.0.0.1" or "localhost")) throw new InvalidOperationException("Testes somente em PostgreSQL local.");
        var name = "p6_empty_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(configured))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }
        builder.Database = name;
        return new(configured, builder.ConnectionString, builder.Password ?? "");
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{new NpgsqlConnectionStringBuilder(Connection).Database}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }

    public CentralContingenciaDbContext Context() => new(new DbContextOptionsBuilder<CentralContingenciaDbContext>().UseNpgsql(Connection).Options);
}

public class MigrationCommandTests
{
    private static async Task<(int Code, string Output)> Run(CentralContingenciaDbContext context, bool plan, string? confirm, string? destructive = null)
    {
        var writer = new StringWriter();
        var code = await MigrationRunner.RunAsync(context, plan, confirm, destructive, writer, default);
        return (code, writer.ToString());
    }

    [PostgresFact]
    public async Task PlanOnlyListsEverythingAndWritesNothing()
    {
        await using var database = await EmptyDatabase.Create();
        await using var context = database.Context();
        var (code, output) = await Run(context, plan: true, confirm: null);
        Assert.Equal(0, code);
        Assert.Contains("Pendentes: 16", output);
        Assert.Contains("_AddClaimKey", output);
        Assert.Contains("Somente plano", output);
        Assert.DoesNotContain("destrutivas", output); // a sequência completa é aditiva
        Assert.Empty(await context.Database.GetAppliedMigrationsAsync());
        await using var connection = new NpgsqlConnection(database.Connection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM information_schema.schemata WHERE schema_name IN ('identity','work','audit','security')", connection);
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [PostgresFact]
    public async Task ApplyRequiresTheExactTargetConfirmation()
    {
        await using var database = await EmptyDatabase.Create();
        await using var context = database.Context();
        foreach (var wrong in new string?[] { null, "", "yes", "20261021205430_AddExecutors", "APPLY" })
        {
            var (code, output) = await Run(context, plan: false, confirm: wrong);
            Assert.Equal(1, code);
            Assert.Contains("Recusado", output);
        }
        Assert.Empty(await context.Database.GetAppliedMigrationsAsync()); // nada foi aplicado
    }

    [PostgresFact]
    public async Task ConfirmedApplyMigratesEverythingAndASecondRunIsANoOp()
    {
        await using var database = await EmptyDatabase.Create();
        await using var context = database.Context();
        var target = context.Database.GetMigrations().Last();
        var (code, output) = await Run(context, plan: false, confirm: target);
        Assert.Equal(0, code);
        Assert.Contains($"até {target}", output);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        var (again, noop) = await Run(context, plan: false, confirm: null);
        Assert.Equal(0, again);
        Assert.Contains("Nada a aplicar", noop);
        // A saída traz só ids de migration: nada de conexão, host ou senha.
        Assert.DoesNotContain(database.Password, output + noop);
        Assert.DoesNotContain("Host=", output + noop, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", output + noop);
    }

    [PostgresFact]
    public async Task UpgradeFromAnOlderVersionAppliesOnlyWhatIsMissing()
    {
        await using var database = await EmptyDatabase.Create();
        await using var context = database.Context();
        await context.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261016175350_AddProfileHealthChecks");
        var (code, output) = await Run(context, plan: true, confirm: null);
        Assert.Equal(0, code);
        Assert.Contains("Migrations aplicadas: 12. Pendentes: 4", output);
        Assert.DoesNotContain("destrutivas", output);
        Assert.Equal(0, (await Run(context, plan: false, confirm: context.Database.GetMigrations().Last())).Code);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    [PostgresFact]
    public async Task UnreachableDatabaseFailsWithoutLeakingTheConnection()
    {
        var options = new DbContextOptionsBuilder<CentralContingenciaDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=never-used;Timeout=1").Options;
        await using var context = new CentralContingenciaDbContext(options);
        var (code, output) = await Run(context, plan: false, confirm: "x");
        Assert.Equal(1, code);
        Assert.Contains("Falha", output);
        Assert.DoesNotContain("never-used", output);
        Assert.DoesNotContain("127.0.0.1", output);
    }
}

public class PublicDomainSimulationTests(AuthFixture fixture) : IClassFixture<AuthFixture>
{
    private const string PublicHost = "staging.example.test";

    private (DeploymentFactory Factory, Dictionary<string, string?> Settings) Build(Dictionary<string, string?>? extra = null, Dictionary<string, string?>? reuse = null)
    {
        var settings = reuse ?? DeploymentSettings.Production(fixture.ConnectionString);
        settings["AllowedHosts"] = PublicHost + ";healthcheck.railway.app";
        settings["WorkQueue:RecoveryIntervalSeconds"] = "0";
        if (extra is not null) foreach (var (key, value) in extra) settings[key] = value;
        return (new DeploymentFactory(settings, "Production"), settings);
    }

    private static HttpRequestMessage Via(HttpMethod method, string path, string host = PublicHost, string remote = "10.0.0.5", string forwardedFor = "203.0.113.9", string? proto = "https")
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Host = host;
        request.Headers.Add("X-Test-Remote-Ip", remote);
        if (forwardedFor.Length > 0) request.Headers.Add("X-Forwarded-For", forwardedFor);
        if (proto is not null) request.Headers.Add("X-Forwarded-Proto", proto);
        return request;
    }

    private static string CookieOf(HttpResponseMessage response, string name) =>
        Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith(name + "=")).ToString();

    private static void AssertSecureHostCookie(string cookie)
    {
        Assert.StartsWith("__Host-", cookie);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task LoginThroughAProxyOnThePublicDomainKeepsSecureCookiesAndCsrf()
    {
        var (factory, _) = Build();
        await using var _ = factory;
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost"), HandleCookies = false, AllowAutoRedirect = false });
        // O pedido chega ao contêiner em HTTP (borda termina o TLS) e traz X-Forwarded-Proto: https.
        var csrfResponse = await client.SendAsync(Via(HttpMethod.Get, "/api/v1/auth/csrf"));
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);
        var csrfCookie = CookieOf(csrfResponse, "__Host-Central.Csrf"); AssertSecureHostCookie(csrfCookie);
        var token = (await csrfResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var csrfPair = csrfCookie.Split(';')[0];

        // Sem o cabeçalho CSRF o login é recusado, mesmo com tudo certo no resto.
        using var noToken = Via(HttpMethod.Post, "/api/v1/auth/login");
        noToken.Headers.Add("Cookie", csrfPair);
        noToken.Content = JsonContent.Create(new { email = fixture.AdminEmail, password = fixture.Password });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(noToken)).StatusCode);

        using var login = Via(HttpMethod.Post, "/api/v1/auth/login");
        login.Headers.Add("Cookie", csrfPair); login.Headers.Add("X-CSRF-TOKEN", token);
        login.Content = JsonContent.Create(new { email = fixture.AdminEmail, password = fixture.Password });
        var loggedIn = await client.SendAsync(login);
        Assert.Equal(HttpStatusCode.NoContent, loggedIn.StatusCode);
        var session = CookieOf(loggedIn, "__Host-Central.Session"); AssertSecureHostCookie(session);
        Assert.True(loggedIn.Headers.Contains("Strict-Transport-Security"));
        Assert.Contains("no-store", loggedIn.Headers.CacheControl?.ToString());

        using var me = Via(HttpMethod.Get, "/api/v1/auth/me");
        me.Headers.Add("Cookie", session.Split(';')[0]);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(me)).StatusCode);
    }

    [PostgresFact]
    public async Task SessionSurvivesReplacingTheContainer()
    {
        var (first, settings) = Build();
        string cookie;
        await using (first)
        {
            using var client = first.CreateClient(new() { BaseAddress = new Uri("http://localhost"), HandleCookies = false, AllowAutoRedirect = false });
            var csrfResponse = await client.SendAsync(Via(HttpMethod.Get, "/api/v1/auth/csrf"));
            var csrfPair = CookieOf(csrfResponse, "__Host-Central.Csrf").Split(';')[0];
            var token = (await csrfResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
            using var login = Via(HttpMethod.Post, "/api/v1/auth/login");
            login.Headers.Add("Cookie", csrfPair); login.Headers.Add("X-CSRF-TOKEN", token);
            login.Content = JsonContent.Create(new { email = fixture.AdminEmail, password = fixture.Password });
            cookie = CookieOf(await client.SendAsync(login), "__Host-Central.Session").Split(';')[0];
        }
        // Contêiner novo: mesmo banco e mesmo certificado do Data Protection (variáveis do serviço), nenhuma memória em comum.
        var (second, _) = Build(reuse: settings);
        await using (second)
        {
            using var client = second.CreateClient(new() { BaseAddress = new Uri("http://localhost"), HandleCookies = false, AllowAutoRedirect = false });
            using var me = Via(HttpMethod.Get, "/api/v1/auth/me");
            me.Headers.Add("Cookie", cookie);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(me)).StatusCode);
        }
    }

    [PostgresFact]
    public async Task PlatformHealthCheckHostIsAllowedAndNeverRedirected()
    {
        var (factory, _) = Build();
        await using var _ = factory;
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        // O health check da Railway chega por HTTP, com Host healthcheck.railway.app e sem garantia de X-Forwarded-Proto vindo de peer confiável.
        using var liveness = Via(HttpMethod.Get, "/health", host: "healthcheck.railway.app", remote: "198.51.100.77", forwardedFor: "", proto: null);
        var live = await client.SendAsync(liveness);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using var readiness = Via(HttpMethod.Get, "/health/ready", host: "healthcheck.railway.app", remote: "198.51.100.77", forwardedFor: "", proto: null);
        var ready = await client.SendAsync(readiness);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Ready", await ready.Content.ReadAsStringAsync());
        // Host desconhecido continua recusado.
        using var stranger = Via(HttpMethod.Get, "/health", host: "evil.example", forwardedFor: "");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(stranger)).StatusCode);
        // E a API continua exigindo HTTPS para o que importa (peer não confiável não consegue declarar https).
        using var forged = Via(HttpMethod.Get, "/api/v1/auth/csrf", remote: "198.51.100.77");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(forged)).StatusCode);
    }

    private const string ProbeToken = "probe-token-sintetico-0123456789";

    private static HttpRequestMessage Probe(string? token, string remote = "10.0.0.5", string forwardedFor = "203.0.113.9", string? proto = "https")
    {
        var request = Via(HttpMethod.Get, "/diagnostics/network", remote: remote, forwardedFor: forwardedFor, proto: proto);
        if (token is not null) request.Headers.Add("X-Probe-Token", token);
        return request;
    }

    [PostgresFact]
    public async Task NetworkProbeDoesNotExistWithoutAToken()
    {
        var (off, _) = Build();
        await using var _ = off;
        using var client = off.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Probe(ProbeToken))).StatusCode);
    }

    [PostgresFact]
    public async Task NetworkProbeRefusesShortTokensAtStartup()
    {
        var (factory, _) = Build(new() { ["Diagnostics:NetworkProbeToken"] = "curto" });
        await using var _ = factory;
        Assert.ThrowsAny<Exception>(() => factory.Services);
    }

    [PostgresFact]
    public async Task NetworkProbeShowsWhatTheApiTrustsAndLeaksNothing()
    {
        var (factory, _) = Build(new() { ["Diagnostics:NetworkProbeToken"] = ProbeToken });
        await using var _ = factory;
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost"), HandleCookies = false, AllowAutoRedirect = false });
        // Sem token, token errado ou de outro tamanho: 404 (não revela que a sonda existe).
        foreach (var bad in new string?[] { null, "", "errado", ProbeToken + "x", ProbeToken[..^1] })
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Probe(bad))).StatusCode);

        // Cadeia com um X-Forwarded-For forjado pelo cliente à esquerda e o IP real (acrescentado pelo proxy) à direita.
        using var request = Probe(ProbeToken, forwardedFor: "6.6.6.6, 203.0.113.9");
        request.Headers.Add("X-Real-IP", "198.51.100.1"); request.Headers.Add("Cookie", "__Host-Central.Session=valor-secreto"); request.Headers.Add("Authorization", "Bearer segredo-que-nao-pode-aparecer");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal("203.0.113.9", body.GetProperty("connectionRemoteIp").GetString()); // vale o último salto (ForwardLimit=1); o "6.6.6.6" forjado é ignorado
        Assert.True(body.GetProperty("isHttps").GetBoolean());
        Assert.True(body.GetProperty("forwardedHeadersMiddleware").GetBoolean());
        Assert.Equal(PublicHost, body.GetProperty("host").GetString());
        Assert.Equal("198.51.100.1", body.GetProperty("headers").GetProperty("X-Real-IP").GetString());
        Assert.DoesNotContain("segredo-que-nao-pode-aparecer", text);
        Assert.DoesNotContain("valor-secreto", text);
        Assert.DoesNotContain(ProbeToken, text);

        // Origem NÃO confiável: nada do X-Forwarded-* vale. Vê-se o IP real da conexão e que o esquema continua http, sem precisar de login.
        using var untrusted = Probe(ProbeToken, remote: "198.51.100.77", forwardedFor: "203.0.113.9");
        var seen = JsonDocument.Parse(await (await client.SendAsync(untrusted)).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("198.51.100.77", seen.GetProperty("connectionRemoteIp").GetString());
        Assert.False(seen.GetProperty("isHttps").GetBoolean());
    }
}
