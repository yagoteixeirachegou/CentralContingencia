using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CentralContingencia.Application.AdsPower;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CentralContingencia.IntegrationTests;

[Collection("Authentication")]
public class AdsPowerEndpointTests(AuthFixture fixture) : IDisposable
{
    private readonly AuthFactory baseFactory = new(fixture.ConnectionString, permitLimit: 1000, globalPermitLimit: 10000);
    private readonly List<WebApplicationFactory<Program>> factories = [];
    public void Dispose() { foreach (var factory in factories) factory.Dispose(); baseFactory.Dispose(); }

    private sealed class FakeService(Func<bool, AdsPowerDashboard> respond) : IAdsPowerDashboardService
    {
        public List<bool> Calls { get; } = [];
        public Task<AdsPowerDashboard> GetAsync(bool refresh, CancellationToken token) { Calls.Add(refresh); return Task.FromResult(respond(refresh)); }
    }
    private static AdsPowerDashboard Sample(bool cached) => new(DateTimeOffset.UtcNow, 3, [new("1", "ESTEIRA DE PERFIL", 3, null, null)], [new("ESTEIRA DE PERFIL", 3)],
        [new("p1", "1", "Perfil 1", "1", "ESTEIRA DE PERFIL", ["ESTEIRA DE PERFIL"], DateTimeOffset.UtcNow)], cached);

    private HttpClient Client(IAdsPowerDashboardService service)
    {
        var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton(service))); factories.Add(factory);
        return factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    }
    private static async Task Csrf(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", response.GetProperty("token").GetString());
    }
    private async Task Login(HttpClient client, IAdsPowerDashboardService service, string role)
    {
        var email = fixture.AdminEmail;
        if (role != "Admin")
        {
            using var admin = Client(service); await Login(admin, service, "Admin");
            email = $"adspower-{Guid.NewGuid():N}@example.test";
            Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/v1/users", new { email, password = fixture.Password, role })).StatusCode);
        }
        await Csrf(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = fixture.Password })).StatusCode);
        await Csrf(client);
    }

    [PostgresFact]
    public async Task Anonymous_is_rejected()
    {
        var service = new FakeService(Sample); using var client = Client(service);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/adspower/dashboard")).StatusCode);
        Assert.Empty(service.Calls);
    }

    [PostgresTheory]
    [InlineData("Admin", HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData("Manager", HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData("Operator", HttpStatusCode.OK, HttpStatusCode.Forbidden)]
    public async Task Roles_read_the_dashboard_and_only_management_can_refresh(string role, HttpStatusCode read, HttpStatusCode refresh)
    {
        var service = new FakeService(Sample); using var client = Client(service); await Login(client, service, role);
        var response = await client.GetAsync("/api/v1/adspower/dashboard"); Assert.Equal(read, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, body.GetProperty("total").GetInt32()); Assert.Equal("ESTEIRA DE PERFIL", body.GetProperty("groups")[0].GetProperty("groupName").GetString());
        Assert.Equal(refresh, (await client.PostAsync("/api/v1/adspower/dashboard/refresh", null)).StatusCode);
        Assert.Equal(refresh == HttpStatusCode.OK ? [false, true] : [false], service.Calls);
    }

    [PostgresTheory]
    [InlineData(AdsPowerFailure.Unreachable, 503)]
    [InlineData(AdsPowerFailure.NotConfigured, 503)]
    [InlineData(AdsPowerFailure.Unauthorized, 502)]
    [InlineData(AdsPowerFailure.Failed, 502)]
    public async Task AdsPower_failures_become_sanitized_problems(AdsPowerFailure failure, int status)
    {
        var service = new FakeService(_ => throw new AdsPowerException(failure)); using var client = Client(service); await Login(client, service, "Operator");
        var response = await client.GetAsync("/api/v1/adspower/dashboard"); Assert.Equal(status, (int)response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(); using var problem = JsonDocument.Parse(text);
        Assert.Equal(failure.ToString(), problem.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("Exception", text); Assert.DoesNotContain("50325", text); Assert.DoesNotContain("Bearer", text);
    }
}
