using CentralContingencia.Application.Health;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CentralContingencia.Infrastructure.Hosting;

public sealed class EfReadinessProbe(CentralContingenciaDbContext context) : IReadinessProbe
{
    public async Task<bool> IsReadyAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            if (!await context.Database.CanConnectAsync(timeout.Token)) return false;
            return !(await context.Database.GetPendingMigrationsAsync(timeout.Token)).Any();
        }
        catch (Exception) { return false; } // Sem detalhes: a resposta pública não pode revelar infraestrutura.
    }
}
