using System.Security.Claims;
using CentralContingencia.Application.Identity;
using CentralContingencia.Application.Warmup;

namespace CentralContingencia.Api.Warmup;

public sealed record ProgressQuery(string[] AdsPowerProfileIds);

/// <summary>Aquecimento (nome comercial): verificação diária e somente leitura de impedimentos, em esteira de 7 dias.</summary>
public static class WarmupEndpoints
{
    public static void MapWarmupEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/warmup").WithTags("Aquecimento (verificação)").RequireAuthorization(AccessPolicies.Operations);
        group.MapPost("/progress", (ProgressQuery query, IHealthCheckService service, CancellationToken token) => Execute(async () => Results.Ok(await service.ProgressAsync(query.AdsPowerProfileIds ?? [], token))))
            .Produces<IReadOnlyList<HealthCheckProgress>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(503)
            .WithSummary("Progresso da esteira de 7 dias para até 200 perfis do AdsPower. Consulta somente leitura (POST por causa da lista de ids).");
        group.MapGet("/profiles/{adsPowerProfileId}/checks", (string adsPowerProfileId, IHealthCheckService service, CancellationToken token) => Execute(async () => Results.Ok(await service.HistoryAsync(adsPowerProfileId, token))))
            .Produces<HealthCheckHistory>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(503)
            .WithSummary("Histórico das verificações de um perfil: dia da esteira, data/hora, resultado e quem registrou.");
        group.MapPost("/checks", (RecordHealthCheckRequest request, HttpContext context, IHealthCheckService service, CancellationToken token) => Execute(async () =>
            {
                var actor = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                var result = await service.RecordAsync(actor, request, token);
                return Results.Created($"/api/v1/warmup/profiles/{result.AdsPowerProfileId}/checks", result);
            }))
            .RequireAuthorization(AccessPolicies.Management).RequireRateLimiting("sensitive")
            .Produces<HealthCheckResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(409).ProducesProblem(429).ProducesProblem(503)
            .WithSummary("Registra o resultado de uma verificação (uma por perfil por dia UTC, no máximo 7 por esteira). Admin e Manager; CSRF.");
    }

    private static async Task<IResult> Execute(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (HealthCheckException error)
        {
            var (status, title) = error.Failure switch
            {
                HealthCheckFailure.Validation => (400, "Dados da verificação inválidos."),
                HealthCheckFailure.Conflict => (409, "Já existe verificação deste perfil hoje ou a esteira de 7 dias está completa."),
                _ => (503, "Verificações indisponíveis no momento."),
            };
            return Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = error.Failure.ToString() });
        }
    }
}
