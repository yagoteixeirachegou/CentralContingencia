using System.Linq.Expressions;
using CentralContingencia.Application.Proxies.Import;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CentralContingencia.Infrastructure.Proxies;

public sealed class EfProxyImportConflictReader(CentralContingenciaDbContext context) : IProxyImportConflictReader
{
    public async Task<IReadOnlySet<ProxyEndpointKey>> FindExistingAsync(IReadOnlyCollection<ProxyEndpointKey> endpoints, CancellationToken token)
    {
        var found = new HashSet<ProxyEndpointKey>();
        foreach (var batch in endpoints.Chunk(100))
        {
            var parameter = Expression.Parameter(typeof(Proxy), "proxy");
            Expression filter = Expression.Constant(false);
            foreach (var endpoint in batch)
            {
                var host = Expression.Equal(Expression.Property(parameter, nameof(Proxy.Host)), Expression.Constant(ProxyHost.Create(endpoint.Host)));
                var port = Expression.Equal(Expression.Property(parameter, nameof(Proxy.Port)), Expression.Constant(ProxyPort.Create(endpoint.Port)));
                filter = Expression.OrElse(filter, Expression.AndAlso(host, port));
            }
            var predicate = Expression.Lambda<Func<Proxy, bool>>(filter, parameter);
            var matches = await context.Proxies.AsNoTracking().Where(predicate)
                .Select(p => new ProxyEndpointKey(p.Host.Value, p.Port.Value)).Distinct().ToListAsync(token);
            found.UnionWith(matches);
        }
        return found;
    }
}
