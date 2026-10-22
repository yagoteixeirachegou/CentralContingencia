using CentralContingencia.Application.Identity;
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

[CollectionDefinition("Authentication")]
public class AuthCollection : ICollectionFixture<AuthFixture>;

public sealed class AuthFactory(string connection, int permitLimit = 100, TimeProvider? clock = null, int globalPermitLimit = 1000) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // A recuperação em segundo plano usa o relógio real e atrapalharia testes que controlam o relógio; os testes que a exigem a ligam.
        builder.UseSetting("WorkQueue:RecoveryIntervalSeconds", "0");
        if (clock is not null)
            builder.ConfigureServices(services =>
            {
                services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
                    Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme, options => options.TimeProvider = clock);
                services.Configure<Microsoft.AspNetCore.Identity.SecurityStampValidatorOptions>(options => options.TimeProvider = clock);
            });
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connection,
                // Nunca depender da chave de desenvolvimento herdada pelo processo de testes.
                ["ProxyEncryption:ActiveKeyId"] = null,
                ["Security:EndpointPermitLimit"] = permitLimit.ToString()
                ,["Security:GlobalPermitLimit"] = globalPermitLimit.ToString()
            }));
    }
}
public sealed class AuthFixture : IAsyncLifetime
{
    public AuthFactory Factory { get; private set; } = null!;
    public string ConnectionString { get; private set; } = "";
    public string AdminEmail { get; } = $"admin-{Guid.NewGuid():N}@example.test";
    public string Password { get; } = $"Test!a9{Guid.NewGuid():N}";
    public bool ProvisionAdmin { get; set; } = true;
    public string? InitialMigration { get; set; }
    private string database = "";
    private string adminConnection = "";
    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("CENTRAL_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var options = new NpgsqlConnectionStringBuilder(configured);
        if (options.Host is not ("127.0.0.1" or "localhost")) throw new InvalidOperationException("Testes somente em PostgreSQL local.");
        adminConnection = configured;
        database = "p6_auth_test_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(adminConnection))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
            await command.ExecuteNonQueryAsync();
        }
        options.Database = database;
        ConnectionString = options.ConnectionString;
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using var sentinel = new NpgsqlCommand("CREATE TABLE public.foundation_sentinel (value text NOT NULL); INSERT INTO public.foundation_sentinel VALUES ('synthetic-preserved')", connection);
            await sentinel.ExecuteNonQueryAsync();
        }
        Factory = new AuthFactory(ConnectionString);
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
        if (InitialMigration is null) await context.Database.MigrateAsync();
        else await context.GetService<IMigrator>().MigrateAsync(InitialMigration);
        if (ProvisionAdmin)
            await scope.ServiceProvider.GetRequiredService<IAdminProvisioner>().ProvisionAsync(AdminEmail, Password);
    }
    public HttpClient Client() => Factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
    public async Task DisposeAsync()
    {
        if (database.Length == 0) return;
        await Factory.DisposeAsync();
        if (!database.StartsWith("p6_auth_test_") || !Guid.TryParseExact(database["p6_auth_test_".Length..], "N", out _))
            throw new InvalidOperationException("Nome do banco descartável inválido.");
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
