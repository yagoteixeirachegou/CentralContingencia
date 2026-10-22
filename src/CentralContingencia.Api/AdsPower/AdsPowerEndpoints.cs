using CentralContingencia.Application.AdsPower;
using CentralContingencia.Application.Identity;

namespace CentralContingencia.Api.AdsPower;

public static class AdsPowerEndpoints
{
    public static void MapAdsPowerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/adspower").WithTags("AdsPower");
        group.MapGet("/dashboard", (IAdsPowerDashboardService service, CancellationToken token) => Execute(async () => Results.Ok(await service.GetAsync(false, token))))
            .RequireAuthorization(AccessPolicies.Operations).Produces<AdsPowerDashboard>(200).ProducesProblem(401).ProducesProblem(403).ProducesProblem(502).ProducesProblem(503)
            .WithSummary("Perfis do AdsPower por grupo. Lê o AdsPower local em modo somente leitura; resultado em memória (padrão 5 minutos). Nenhum dado de credencial é lido ou devolvido.");
        group.MapPost("/dashboard/refresh", (IAdsPowerDashboardService service, CancellationToken token) => Execute(async () => Results.Ok(await service.GetAsync(true, token))))
            .RequireAuthorization(AccessPolicies.Management).RequireRateLimiting("sensitive").Produces<AdsPowerDashboard>(200).ProducesProblem(401).ProducesProblem(403).ProducesProblem(429).ProducesProblem(502).ProducesProblem(503)
            .WithSummary("Força nova leitura do AdsPower, ignorando o resultado em memória. Admin e Manager; sem corpo.");
    }

    private static async Task<IResult> Execute(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (AdsPowerException error)
        {
            var (status, title) = error.Failure switch
            {
                AdsPowerFailure.NotConfigured => (503, "O endereço do AdsPower não está configurado para este computador."),
                AdsPowerFailure.Unreachable => (503, "Não foi possível falar com o AdsPower. Abra o aplicativo e tente novamente."),
                AdsPowerFailure.Unauthorized => (502, "O AdsPower recusou a chave de API. Confira a chave configurada."),
                _ => (502, "O AdsPower devolveu uma resposta inesperada."),
            };
            return Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = error.Failure.ToString() });
        }
    }
}
