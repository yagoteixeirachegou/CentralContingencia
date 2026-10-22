using System.Security.Claims;
using CentralContingencia.Application.Allocations;
using CentralContingencia.Application.Identity;
using CentralContingencia.Application.Proxies;

namespace CentralContingencia.Api.Proxies;

public static class OperationalGridEndpoint
{
    public const string Path = "/api/v1/proxies/operational-grid";
    public static void MapOperationalGrid(this WebApplication app)
    {
        app.MapGet(Path, async (HttpContext context, IOperationalGridService service, [AsParameters] GridQueryParameters query, CancellationToken token) =>
        {
            var allowed = new[] { "page", "pageSize", "profile", "region", "platform", "availability", "sort", "direction" };
            if (context.Request.Query.Any(pair => pair.Value.Count != 1 || !allowed.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)))
                return Results.Problem(statusCode: 400, title: "Parâmetros do grid inválidos.");
            var actor = new ProxyActor(Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!), context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray());
            try { return Results.Ok(await service.ListAsync(actor, query.ToQuery(), token)); }
            catch (AllocationPersistenceException error)
            {
                return Results.Problem(statusCode: error.Failure switch
                { AllocationPersistenceFailure.Validation => 400, AllocationPersistenceFailure.Forbidden => 403, _ => 503 },
                    title: "Consulta operacional indisponível ou parâmetros inválidos.");
            }
        }).RequireAuthorization(AccessPolicies.Operations).WithTags("Proxies")
            .WithSummary("Grid operacional: uma linha por proxy, filtros combinados e disponibilidade calculada; leitura sem efeitos persistentes.")
            .Produces<OperationalGridPage>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(503);
    }
}
public sealed class GridQueryParameters
{
    public string? Page { get; init; }
    public string? PageSize { get; init; }
    public string? Profile { get; init; }
    public string? Region { get; init; }
    public string? Platform { get; init; }
    public string? Availability { get; init; }
    public string? Sort { get; init; }
    public string? Direction { get; init; }
    private static int Number(string? value, int fallback)
    {
        if (value is null) return fallback;
        if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var result))
            throw new AllocationPersistenceException(AllocationPersistenceFailure.Validation);
        return result;
    }
    public OperationalGridQuery ToQuery() => new(Number(Page, 1), Number(PageSize, 20), Profile, Region, Platform, Availability, Sort ?? "expiresAt", Direction ?? "asc");
}
