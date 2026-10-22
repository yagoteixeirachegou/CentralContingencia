using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using CentralContingencia.Api.Hosting;
using CentralContingencia.Infrastructure.Hosting;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CentralContingencia.IntegrationTests;

/// <summary>Simula o IP de origem da conexão (o TestServer não tem um), para exercitar a confiança em proxies.</summary>
public sealed class FakeRemoteIpFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, pipeline) =>
        {
            if (context.Request.Headers.TryGetValue("X-Test-Remote-Ip", out var value)) context.Connection.RemoteIpAddress = IPAddress.Parse(value.ToString());
            return pipeline();
        });
        next(app);
    };
}

public sealed class DeploymentFactory(Dictionary<string, string?> settings, string environment = "Development", string? webRoot = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        if (webRoot is not null) builder.UseWebRoot(webRoot);
        // UseSetting vale já nas leituras de configuração do Program.cs; ConfigureAppConfiguration só valeria depois delas.
        foreach (var (key, value) in settings) if (value is not null) builder.UseSetting(key, value);
        builder.ConfigureServices(services => services.AddTransient<IStartupFilter, FakeRemoteIpFilter>());
    }
    public HttpClient Https() => CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    public HttpClient Http() => CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
}

public static class DeploymentSettings
{
    public static (string Base64, string Password) NewCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=p6-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var password = Guid.NewGuid().ToString("N");
        return (Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, password)), password);
    }

    public static Dictionary<string, string?> Proxy(string proxy = "10.0.0.5", int? globalLimit = null)
    {
        var settings = new Dictionary<string, string?> { ["ReverseProxy:Enabled"] = "true", ["ReverseProxy:KnownProxies:0"] = proxy };
        if (globalLimit is not null) settings["Security:GlobalPermitLimit"] = globalLimit.ToString();
        return settings;
    }

    /// <summary>Configuração mínima válida para ambiente de produção (sem segredo real).</summary>
    public static Dictionary<string, string?> Production(string connection = "Host=127.0.0.1;Port=1;Database=p6_unreachable;Username=p6;Timeout=1")
    {
        var (certificate, password) = NewCertificate();
        var settings = Proxy();
        settings["ConnectionStrings:DefaultConnection"] = connection;
        settings["AllowedHosts"] = "localhost";
        settings["DataProtection:Store"] = "Database";
        settings["DataProtection:CertificateBase64"] = certificate;
        settings["DataProtection:CertificatePassword"] = password;
        return settings;
    }

    public static HttpRequestMessage FromProxy(string path, string clientIp, string remote = "10.0.0.5", string? proto = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Test-Remote-Ip", remote);
        request.Headers.Add("X-Forwarded-For", clientIp);
        if (proto is not null) request.Headers.Add("X-Forwarded-Proto", proto);
        return request;
    }
}

public class ReverseProxyTests
{
    private static async Task<HttpStatusCode> Send(HttpClient client, HttpRequestMessage request) { using (request) return (await client.SendAsync(request)).StatusCode; }

    [Fact]
    public async Task TrustedProxyForwardedProtoSatisfiesHttpsRequirement()
    {
        await using var factory = new DeploymentFactory(DeploymentSettings.Proxy());
        using var client = factory.Http();
        Assert.Equal(HttpStatusCode.OK, await Send(client, DeploymentSettings.FromProxy("/api/v1/auth/csrf", "203.0.113.1", proto: "https")));
    }

    [Fact]
    public async Task UntrustedSourceCannotForgeHttps()
    {
        await using var factory = new DeploymentFactory(DeploymentSettings.Proxy());
        using var client = factory.Http();
        Assert.Equal(HttpStatusCode.BadRequest, await Send(client, DeploymentSettings.FromProxy("/api/v1/auth/csrf", "203.0.113.1", remote: "198.51.100.7", proto: "https")));
    }

    [Fact]
    public async Task ForwardedHeadersAreIgnoredWhenReverseProxyIsNotConfigured()
    {
        await using var factory = new DeploymentFactory([]);
        using var client = factory.Http();
        Assert.Equal(HttpStatusCode.BadRequest, await Send(client, DeploymentSettings.FromProxy("/api/v1/auth/csrf", "203.0.113.1", proto: "https")));
    }

    [Fact]
    public async Task RateLimitUsesRealClientIpBehindTrustedProxy()
    {
        await using var factory = new DeploymentFactory(DeploymentSettings.Proxy(globalLimit: 2));
        using var client = factory.Https();
        Assert.Equal(HttpStatusCode.OK, await Send(client, DeploymentSettings.FromProxy("/health", "203.0.113.1")));
        Assert.Equal(HttpStatusCode.OK, await Send(client, DeploymentSettings.FromProxy("/health", "203.0.113.1")));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Send(client, DeploymentSettings.FromProxy("/health", "203.0.113.1")));
        // Outro cliente atrás do mesmo proxy tem a própria cota.
        Assert.Equal(HttpStatusCode.OK, await Send(client, DeploymentSettings.FromProxy("/health", "203.0.113.2")));
    }

    [Fact]
    public async Task UntrustedSourceCannotEvadeRateLimitByForgingForwardedFor()
    {
        await using var factory = new DeploymentFactory(DeploymentSettings.Proxy(globalLimit: 2));
        using var client = factory.Https();
        Assert.Equal(HttpStatusCode.OK, await Send(client, DeploymentSettings.FromProxy("/health", "203.0.113.1", remote: "198.51.100.7")));
        Assert.Equal(HttpStatusCode.OK, await Send(client, DeploymentSettings.FromProxy("/health", "203.0.113.2", remote: "198.51.100.7")));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Send(client, DeploymentSettings.FromProxy("/health", "203.0.113.3", remote: "198.51.100.7")));
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("not-a-network")]
    public void WildcardOrInvalidNetworksAreRejected(string network)
    {
        var settings = new ReverseProxySettings { Enabled = true, KnownNetworks = [network] };
        Assert.Throws<InvalidOperationException>(() => settings.Parse());
    }

    [Fact]
    public void EnabledWithoutTrustedSourcesIsRejected() =>
        Assert.Throws<InvalidOperationException>(() => new ReverseProxySettings { Enabled = true }.Parse());

    [Fact]
    public void AnyAddressIsNotAcceptedAsProxy() =>
        Assert.Throws<InvalidOperationException>(() => new ReverseProxySettings { Enabled = true, KnownProxies = ["0.0.0.0"] }.Parse());
}

public class ProductionConfigurationTests
{
    [Fact]
    public void EmptyConfigurationListsEveryProblemWithoutValues()
    {
        var problems = ProductionConfiguration.Problems(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "*" }).Build());
        Assert.Equal(5, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("ConnectionStrings:DefaultConnection"));
        Assert.Contains(problems, p => p.StartsWith("DataProtection:Store"));
        Assert.Contains(problems, p => p.StartsWith("DataProtection:CertificateBase64"));
        Assert.Contains(problems, p => p.StartsWith("ReverseProxy:Enabled"));
        Assert.Contains(problems, p => p.StartsWith("AllowedHosts"));
    }

    [Fact]
    public void CompleteConfigurationHasNoProblems() =>
        Assert.Empty(ProductionConfiguration.Problems(new ConfigurationBuilder().AddInMemoryCollection(DeploymentSettings.Production()).Build()));

    [Fact]
    public async Task ProductionHostRefusesToStartWithInsecureConfigurationAndDoesNotEchoValues()
    {
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "Host=secret-host-sentinel" };
        await using var factory = new DeploymentFactory(settings, "Production");
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var text = error.ToString();
        Assert.Contains("DataProtection:Store", text);
        Assert.DoesNotContain("secret-host-sentinel", text);
    }

    [Fact]
    public void InvalidCertificateIsRejectedWithoutEchoingItsContent()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:Store"] = "Database", ["DataProtection:CertificateBase64"] = "CERT-SENTINEL-VALUE", ["DataProtection:CertificatePassword"] = "PASSWORD-SENTINEL"
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddCentralDataProtection(configuration));
        Assert.Contains("DataProtection:CertificateBase64", error.Message);
        Assert.DoesNotContain("SENTINEL", error.ToString());
    }

    [Fact]
    public void UnknownStoreIsRejected()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:Store"] = "Memory" }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddCentralDataProtection(configuration));
    }

    [Fact]
    public async Task ValidProductionHostStartsAndDisablesLocalIntegrations()
    {
        await using var factory = new DeploymentFactory(DeploymentSettings.Production(), "Production");
        using var client = factory.Https();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        foreach (var path in new[] { "/api/v1/adspower/dashboard", "/api/v1/integration/profile-ingestions/availability", "/api/v1/profile-ingestions/status" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("LocalIntegrationUnavailable", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        // A verificação (aquecimento) usa só o banco: continua mapeada e exige autenticação.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/warmup/profiles/x/checks")).StatusCode);
    }

    [Fact]
    public async Task ReadinessIsUnavailableWhenDatabaseIsUnreachable()
    {
        await using var factory = new DeploymentFactory(DeploymentSettings.Production(), "Production");
        using var client = factory.Https();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("NotReady", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LocalIntegrationsStayEnabledInDevelopmentByDefaultAndCanBeDisabled()
    {
        await using var enabled = new DeploymentFactory([]);
        using (var client = enabled.Https()) Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/adspower/dashboard")).StatusCode);
        await using var disabled = new DeploymentFactory(new() { ["LocalIntegrations:Enabled"] = "false" });
        using (var client = disabled.Https()) Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/v1/adspower/dashboard")).StatusCode);
    }
}

public sealed class SpaHostingTests : IDisposable
{
    private readonly string webRoot = Path.Combine(Path.GetTempPath(), "p6-spa-" + Guid.NewGuid().ToString("N"));
    public SpaHostingTests()
    {
        Directory.CreateDirectory(Path.Combine(webRoot, "assets"));
        File.WriteAllText(Path.Combine(webRoot, "index.html"), "<!doctype html><title>spa-index-sentinel</title>");
        File.WriteAllText(Path.Combine(webRoot, "assets", "app-abc123.js"), "console.log('asset-sentinel')");
    }
    public void Dispose() => Directory.Delete(webRoot, true);

    [Theory]
    [InlineData("/")]
    [InlineData("/proxies")]
    [InlineData("/proxies/import")]
    [InlineData("/profiles/6f1d3c1e-0000-0000-0000-000000000000")]
    public async Task ClientRoutesFallBackToIndexWithoutCaching(string path)
    {
        await using var factory = new DeploymentFactory([], webRoot: webRoot);
        using var client = factory.Https();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("spa-index-sentinel", await response.Content.ReadAsStringAsync());
        Assert.Contains("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task HashedAssetsAreServedWithLongCache()
    {
        await using var factory = new DeploymentFactory([], webRoot: webRoot);
        using var client = factory.Https();
        var response = await client.GetAsync("/assets/app-abc123.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("asset-sentinel", await response.Content.ReadAsStringAsync());
        Assert.Contains("immutable", response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("/assets/missing.js")]
    [InlineData("/api/v1/does-not-exist")]
    [InlineData("/api/unknown")]
    [InlineData("/health/unknown")]
    public async Task MissingAssetsAndUnknownApiRoutesAreNotFoundInsteadOfIndex(string path)
    {
        await using var factory = new DeploymentFactory([], webRoot: webRoot);
        using var client = factory.Https();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("spa-index-sentinel", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ApiAndHealthAreNotIntercepted()
    {
        await using var factory = new DeploymentFactory([], webRoot: webRoot);
        using var client = factory.Https();
        Assert.Equal("Healthy", await client.GetStringAsync("/health"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/csrf")).StatusCode);
    }

    [Fact]
    public async Task WithoutBuildNothingIsServedAsSpa()
    {
        await using var factory = new DeploymentFactory([]);
        using var client = factory.Https();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/proxies")).StatusCode);
    }
}

[Collection("Authentication")]
public class DatabaseDeploymentTests(AuthFixture fixture)
{
    private static Dictionary<string, string?> Persistent(string connection, string certificate, string password) => new()
    {
        ["ConnectionStrings:DefaultConnection"] = connection, ["DataProtection:Store"] = "Database",
        ["DataProtection:CertificateBase64"] = certificate, ["DataProtection:CertificatePassword"] = password,
    };

    [PostgresFact]
    public async Task SessionSurvivesRestartAndKeysAreStoredEncrypted()
    {
        var (certificate, password) = DeploymentSettings.NewCertificate();
        var settings = Persistent(fixture.ConnectionString, certificate, password);
        string cookie;
        await using (var first = new DeploymentFactory(settings))
        {
            using var client = first.Https();
            var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
            using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email = fixture.AdminEmail, password = fixture.Password }) };
            login.Headers.Add("X-CSRF-TOKEN", csrf);
            var response = await client.SendAsync(login);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-Central.Session=")).Split(';')[0];
        }
        // "Reinício": nova instância da aplicação, mesmo banco e mesmo certificado.
        await using (var second = new DeploymentFactory(settings))
        {
            using var client = second.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
            request.Headers.Add("Cookie", cookie);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        }
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand("SELECT \"Xml\" FROM security.\"DataProtectionKeys\"", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = 0;
        while (await reader.ReadAsync())
        {
            rows++;
            var xml = reader.GetString(0);
            Assert.Contains("EncryptedData", xml);
            Assert.DoesNotContain("masterKey", xml);
        }
        Assert.True(rows >= 1);
    }

    [PostgresFact]
    public async Task ReadinessReflectsAppliedMigrations()
    {
        await using var ready = new DeploymentFactory(new() { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString });
        using (var client = ready.Https()) Assert.Equal("Ready", await client.GetStringAsync("/health/ready"));

        var outdated = new AuthFixture { ProvisionAdmin = false, InitialMigration = "20261016175350_AddProfileHealthChecks" };
        await outdated.InitializeAsync();
        try
        {
            await using var pending = new DeploymentFactory(new() { ["ConnectionStrings:DefaultConnection"] = outdated.ConnectionString });
            using var client = pending.Https();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        }
        finally { await outdated.DisposeAsync(); }
    }
}
