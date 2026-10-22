using System.Security.Claims;
using System.Text.RegularExpressions;
using CentralContingencia.Api.Integration;
using CentralContingencia.Application.Identity;
using CentralContingencia.Application.WorkQueue;

namespace CentralContingencia.Api.WorkQueue;

/// <summary>
/// Fila de tarefas assíncronas. O cliente só cria, consulta, lista e solicita cancelamento: estado, lease, tentativas e worker
/// pertencem ao servidor e nunca aparecem como entrada.
/// </summary>
public static partial class WorkTaskEndpoints
{
    [GeneratedRegex(@"^[A-Za-z0-9._\-]{1,64}$")]
    private static partial Regex CorrelationPattern();

    public static void MapWorkTaskEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/tasks").WithTags("Tarefas");

        Problems(group.MapPost("", (HttpContext context, IWorkTaskService service, CancellationToken token) => Execute(async () =>
        {
            var (body, rejected) = await IngestionEndpoints.ReadBodyAsync<CreateWorkTaskRequest>(context);
            if (rejected is not null) return rejected;
            if (!Guid.TryParse(context.Request.Headers["Idempotency-Key"].ToString(), out var key) || key == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Informe um Idempotency-Key UUID.");
            var created = await service.CreateAsync(Actor(context), key, body!, Correlation(context), token);
            return created.Created ? Results.Created($"/api/v1/tasks/{created.Task.Id}", created.Task) : Results.Ok(created.Task);
        })).RequireAuthorization(AccessPolicies.Management).RequireRateLimiting("sensitive").Produces<WorkTaskResponse>(201).Produces<WorkTaskResponse>(200)
            .WithSummary("Cria uma tarefa (Admin/Manager; CSRF). Idempotency-Key obrigatório: repetir o mesmo pedido devolve a mesma tarefa (200); chave com conteúdo diferente retorna 409. " +
                "Corpo: { type, payload, maxAttempts? } e nada mais. Payload validado e versionado por tipo; não aceita credenciais, cookies ou tokens."));

        Problems(group.MapGet("/{id:guid}", (Guid id, IWorkTaskService service, CancellationToken token) => Execute(async () => Results.Ok(await service.GetAsync(id, token))))
            .RequireAuthorization(AccessPolicies.Operations).Produces<WorkTaskResponse>()
            .WithSummary("Consulta uma tarefa. Não expõe worker, lease nem texto de erro (apenas lastErrorCode)."));

        Problems(group.MapGet("", (string? status, string? type, int? page, int? pageSize, IWorkTaskService service, CancellationToken token) => Execute(async () =>
                Results.Ok(await service.ListAsync(status, type, page ?? 1, pageSize ?? 20, token))))
            .RequireAuthorization(AccessPolicies.Operations).Produces<WorkTaskPage>()
            .WithSummary("Lista tarefas (mais recentes primeiro). Filtros exatos status/type; page 1..10000, pageSize 1..100 (padrão 20)."));

        Problems(group.MapPost("/{id:guid}/cancel", (Guid id, HttpContext context, IWorkTaskService service, CancellationToken token) => Execute(async () =>
                Results.Ok(await service.CancelAsync(Actor(context), id, token))))
            .RequireAuthorization(AccessPolicies.Management).RequireRateLimiting("sensitive").Produces<WorkTaskResponse>()
            .WithSummary("Solicita cancelamento (Admin/Manager; CSRF; sem corpo). Pending cancela na hora; Running fica com cancelRequested=true até o worker confirmar; tarefa já concluída retorna 409. Repetir é seguro."));
    }

    private static Guid Actor(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static string Correlation(HttpContext context) =>
        context.Request.Headers["X-Correlation-Id"].ToString() is { Length: > 0 } header && CorrelationPattern().IsMatch(header) ? header : context.TraceIdentifier;

    private static RouteHandlerBuilder Problems(RouteHandlerBuilder route) =>
        route.ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(413).ProducesProblem(415).ProducesProblem(429).ProducesProblem(503);

    private static async Task<IResult> Execute(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (WorkQueueException error)
        {
            var (status, title) = error.Failure switch
            {
                WorkQueueFailure.Validation => (400, "Dados da tarefa inválidos."),
                WorkQueueFailure.NotFound => (404, "Tarefa não encontrada."),
                WorkQueueFailure.Conflict or WorkQueueFailure.LeaseLost => (409, "Conflito: a chave de idempotência já foi usada com outro conteúdo ou a tarefa não está em estado que permita a operação."),
                _ => (503, "Serviço indisponível. Tente novamente."),
            };
            return Results.Problem(statusCode: status, title: title);
        }
    }
}
