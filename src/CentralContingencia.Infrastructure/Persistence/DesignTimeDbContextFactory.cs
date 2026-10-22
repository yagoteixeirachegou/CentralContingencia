using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace CentralContingencia.Infrastructure.Persistence;
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CentralContingenciaDbContext>
{
    public CentralContingenciaDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ??
            "Host=127.0.0.1;Port=55432;Database=central_contingencia_dev;Username=central_dev";
        return new(new DbContextOptionsBuilder<CentralContingenciaDbContext>().UseNpgsql(connection).Options);
    }
}
