using System.Text.Json;
using CentralContingencia.Application.Identity;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Infrastructure;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Identity;
using CentralContingencia.Infrastructure.Identity;
using Npgsql;

// Harness local: não faz parte do produto nem da solution. Falha fechada fora do banco descartável.
try
{
    var baseConnection = Environment.GetEnvironmentVariable("CENTRAL_UI_BASE_CONNECTION") ?? throw new InvalidOperationException();
    var options = new NpgsqlConnectionStringBuilder(baseConnection);
    var name = Environment.GetEnvironmentVariable("CENTRAL_UI_DATABASE") ?? "";
    if (options.Host != "127.0.0.1" || options.Port != 55432 || options.Database != "central_contingencia_dev" ||
        !name.StartsWith("p6_ui_test_") || !Guid.TryParseExact(name["p6_ui_test_".Length..], "N", out _)) throw new InvalidOperationException();
    var operation = args.Single();
    var databaseSecret = Environment.GetEnvironmentVariable("CENTRAL_UI_DATABASE_PASSWORD") ?? throw new InvalidOperationException();
    if (!System.Text.RegularExpressions.Regex.IsMatch(databaseSecret, "^[a-f0-9]{64}$")) throw new InvalidOperationException();
    if (operation is "create" or "drop")
    {
        await using var connection = new NpgsqlConnection(baseConnection);
        await connection.OpenAsync();
        if (operation == "create")
        {
            // Segredo hex gerado em memória; role sem privilégios no banco homologado.
            await using var role = new NpgsqlCommand($"CREATE ROLE \"{name}\" LOGIN PASSWORD '{databaseSecret}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT", connection);
            await role.ExecuteNonQueryAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\" OWNER \"{name}\"", connection);
            await create.ExecuteNonQueryAsync();
            await using var guard = new NpgsqlCommand("SELECT has_table_privilege(@role, 'audit.\"AuthenticationEvents\"', 'INSERT')", connection);
            guard.Parameters.AddWithValue("role", name);
            if ((bool)(await guard.ExecuteScalarAsync())!) throw new InvalidOperationException();
        }
        else
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
            await using var role = new NpgsqlCommand($"DROP ROLE IF EXISTS \"{name}\"", connection);
            await role.ExecuteNonQueryAsync();
            Console.WriteLine("Banco e role descartáveis removidos."); return;
        }
    }
    options.Database = name;
    options.Username = name; options.Password = databaseSecret;
    await using (var isolated = new NpgsqlConnection(options.ConnectionString))
    {
        await isolated.OpenAsync();
        await using var destination = new NpgsqlCommand("SELECT current_database() = @name AND current_user = @name", isolated);
        destination.Parameters.AddWithValue("name", name);
        if (!(bool)(await destination.ExecuteScalarAsync())!) throw new InvalidOperationException();
    }
    var configuration = new ConfigurationBuilder().AddEnvironmentVariables().AddInMemoryCollection(new Dictionary<string, string?> {
        ["ConnectionStrings:DefaultConnection"] = options.ConnectionString }).Build();
    var registrations = new ServiceCollection();
    registrations.AddLogging(logging => logging.ClearProviders());
    registrations.AddDataProtection(); registrations.AddInfrastructure(configuration);
    using var provider = registrations.BuildServiceProvider();
    using var scope = provider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CentralContingenciaDbContext>();
    if (operation == "create")
    {
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<IAdminProvisioner>().ProvisionAsync("admin-ui@example.test",
            Environment.GetEnvironmentVariable("CENTRAL_UI_PASSWORD") ?? throw new InvalidOperationException());
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.FindByEmailAsync("admin-ui@example.test");
        if (admin is null || !await users.CheckPasswordAsync(admin, Environment.GetEnvironmentVariable("CENTRAL_UI_PASSWORD")!)) throw new InvalidOperationException();
        Console.WriteLine("Fixture sintética pronta; migrations aplicadas somente no banco descartável.");
    }
    else if (operation == "verify")
    {
        var rows = await db.Proxies.AsNoTracking().ToListAsync();
        var audit = await db.ProxyAuditEvents.AsNoTracking().ToListAsync();
        var secret = "SyntheticProxy!123";
        var protector = scope.ServiceProvider.GetRequiredService<IProxyPasswordProtector>();
        if (rows.Count != 1 || audit.Count != 2 || protector.Decrypt(rows[0].Id, rows[0].Password) != secret ||
            JsonSerializer.Serialize(audit).Contains(secret)) throw new InvalidOperationException();
        Console.WriteLine("Persistência, credencial cifrada e auditoria sanitizada verificadas no banco descartável.");
    }
    else throw new InvalidOperationException();
}
catch
{
    // Não imprimir exceções de conexão/configuração ou material sensível do harness.
    Console.Error.WriteLine("Fixture recusada/falhou. Verifique configuração local, nome descartável e operação.");
    Environment.ExitCode = 1;
}
