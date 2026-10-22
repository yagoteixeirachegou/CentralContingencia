using Microsoft.AspNetCore.Mvc.Testing;
namespace CentralContingencia.IntegrationTests;
public class ApiTests
{
    [Theory]
    [InlineData("Development", 200)]
    [InlineData("Production", 404)]
    public async Task OpenApiIsOnlyAvailableInDevelopment(string environment, int expectedStatus)
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("environment", environment);
                // Fora de Development a API exige configuração de produção completa (ver ProductionConfiguration).
                if (environment != "Development") foreach (var (key, value) in DeploymentSettings.Production()) builder.UseSetting(key, value);
            });
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        if (expectedStatus == 200)
        {
            var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/health", out _));
            var login = document.RootElement.GetProperty("paths").GetProperty("/api/v1/auth/login").GetProperty("post");
            Assert.Contains(login.GetProperty("parameters").EnumerateArray(), parameter => parameter.GetProperty("name").GetString() == "X-CSRF-TOKEN");
            Assert.Equal("__Host-Central.Session", document.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("SessionCookie").GetProperty("name").GetString());
            var paths = document.RootElement.GetProperty("paths");
            var grid = paths.GetProperty("/api/v1/proxies/operational-grid").GetProperty("get");
            Assert.True(grid.TryGetProperty("security", out _));
            foreach (var name in new[] { "page", "pageSize", "profile", "region", "platform", "availability", "sort", "direction" })
                Assert.Contains(grid.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == name);
            var patch = paths.GetProperty("/api/v1/proxies/{id}").GetProperty("patch");
            Assert.Contains(patch.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "X-CSRF-TOKEN");
            Assert.True(patch.TryGetProperty("security", out _));
            Assert.True(paths.TryGetProperty("/api/v1/proxies/available", out _));
            var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
            foreach (var field in new[] { "platform1", "platform2" })
                Assert.True(schemas.GetProperty("CreateProfileRequest").GetProperty("properties").TryGetProperty(field, out _));
            Assert.False(schemas.GetProperty("OperationalGridRow").GetProperty("properties").TryGetProperty("password", out _));
            Assert.True(schemas.GetProperty("CreateProxyRequest").GetProperty("properties").GetProperty("password").GetProperty("writeOnly").GetBoolean());
            Assert.False(schemas.GetProperty("ProxyResponse").GetProperty("properties").TryGetProperty("password", out _));
            var preview = paths.GetProperty("/api/v1/proxies/import/preview").GetProperty("post");
            Assert.True(preview.GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data", out _));
            Assert.True(preview.TryGetProperty("security", out _));
            foreach (var name in new[] { "X-CSRF-TOKEN", "page", "pageSize" })
                Assert.Contains(preview.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == name);
            var commit=paths.GetProperty("/api/v1/proxies/import/commit").GetProperty("post");
            Assert.True(commit.GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data",out _));
            Assert.True(commit.TryGetProperty("security",out _));
            foreach(var name in new[]{"X-CSRF-TOKEN","X-Import-Confirmation","Idempotency-Key","defaultRegion"})
                Assert.Contains(commit.GetProperty("parameters").EnumerateArray(),p=>p.GetProperty("name").GetString()==name);
        }
    }
    [Fact]
    public async Task HealthWorksWithoutDatabase()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task ProxyRoutesRequireAuthentication()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync("/api/v1/proxies");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}

