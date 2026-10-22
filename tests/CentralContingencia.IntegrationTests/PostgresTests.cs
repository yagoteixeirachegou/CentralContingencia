using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CentralContingencia.IntegrationTests;

public class PostgresTests
{
    [PostgresFact]
    public async Task ProviderConnectsToLocalPostgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("CENTRAL_TEST_POSTGRES");
        var options = new DbContextOptionsBuilder<CentralContingenciaDbContext>()
            .UseNpgsql(connectionString).Options;
        await using var context = new CentralContingenciaDbContext(options);
        Assert.True(await context.Database.CanConnectAsync());
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CENTRAL_TEST_POSTGRES")))
            Skip = "Configure CENTRAL_TEST_POSTGRES para testar PostgreSQL local real.";
    }
}
