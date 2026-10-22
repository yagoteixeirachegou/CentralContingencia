using System.Security.Claims;
using CentralContingencia.Api.Integration;
using CentralContingencia.Application.Identity;
using CentralContingencia.Application.WorkQueue;

namespace CentralContingencia.Api.WorkQueue;

/// <summary>
/// Gestão dos executores (cookie + CSRF). Provisionar, rotacionar e revogar são exclusivos de Admin; a credencial é devolvida uma única vez
/// (Cache-Control: no-store) e nunca mais consultável. A leitura (sem credencial) é aberta a quem acompanha as tarefas.
/// </summary>
public static class ExecutorAdminEndpoints
{
    public static void MapExecutorAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/executors").WithTags("Executores");

        Problems(group.MapPost("", (HttpContext context, IExecutorAdminService service, CancellationToken token) => Execute(async () =>
        {
            var (body, rejected) = await IngestionEndpoints.ReadBodyAsync<ProvisionExecutorRequest>(context);
            if (rejected is not null) return rejected;
            var provisioned = await service.ProvisionAsync(Actor(context), body!, token);
            return Results.Created($"/api/v1/executors/{provisioned.Executor.Id}", provisioned);
        })).RequireAuthorization(AccessPolicies.Administration).RequireRateLimiting("sensitive").Produces<ProvisionedExecutor>(201)
            .WithSummary("Provisiona um executor (Admin; CSRF). Corpo { name, allowedTypes? }. A resposta traz a credencial UMA vez; guarde-a no cofre do worker. Nome repetido → 409."));

        Problems(group.MapGet("", (IExecutorAdminService service, CancellationToken token) => Execute(async () => Results.Ok(await service.ListAsync(token))))
            .RequireAuthorization(AccessPolicies.Operations).Produces<IReadOnlyList<ExecutorView>>()
            .WithSummary("Lista executores com conexão (Online/Offline/Revoked), estado informado, última comunicação, versão e tarefas em execução. Sem credenciais."));

        Problems(group.MapGet("/{id:guid}", (Guid id, IExecutorAdminService service, CancellationToken token) => Execute(async () => Results.Ok(await service.GetAsync(id, token))))
            .RequireAuthorization(AccessPolicies.Operations).Produces<ExecutorView>().WithSummary("Detalhe de um executor."));

        Problems(group.MapPost("/{id:guid}/rotate", (Guid id, HttpContext context, IExecutorAdminService service, CancellationToken token) => Execute(async () =>
                Results.Ok(await service.RotateAsync(Actor(context), id, token))))
            .RequireAuthorization(AccessPolicies.Administration).RequireRateLimiting("sensitive").Produces<ProvisionedExecutor>()
            .WithSummary("Rotaciona a credencial (Admin; CSRF; sem corpo). A anterior deixa de valer na hora; a nova é exibida uma vez. Executor revogado → 409."));

        Problems(group.MapPost("/{id:guid}/revoke", (Guid id, HttpContext context, IExecutorAdminService service, CancellationToken token) => Execute(async () =>
                Results.Ok(await service.RevokeAsync(Actor(context), id, token))))
            .RequireAuthorization(AccessPolicies.Administration).RequireRateLimiting("sensitive").Produces<ExecutorView>()
            .WithSummary("Revoga o executor (Admin; CSRF; sem corpo; definitivo e idempotente). Ele não consegue mais reservar nem alterar tarefas; leases em curso vencem e são recuperados."));
    }

    private static Guid Actor(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static RouteHandlerBuilder Problems(RouteHandlerBuilder route) =>
        route.ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(413).ProducesProblem(415).ProducesProblem(429).ProducesProblem(503);

    private static async Task<IResult> Execute(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (ExecutorException error)
        {
            var (status, title) = error.Failure switch
            {
                ExecutorFailure.Validation => (400, "Dados do executor inválidos."),
                ExecutorFailure.NotFound => (404, "Executor não encontrado."),
                ExecutorFailure.Conflict => (409, "Conflito: já existe executor com este nome ou ele está revogado."),
                _ => (503, "Serviço indisponível. Tente novamente."),
            };
            return Results.Problem(statusCode: status, title: title);
        }
    }
}
