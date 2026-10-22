using System.Security.Claims;
using System.Text.Json;
using CentralContingencia.Api.Integration;
using CentralContingencia.Application.Identity;
using CentralContingencia.Application.WorkQueue;

namespace CentralContingencia.Api.WorkQueue;

/// <summary>
/// Canal HTTPS do worker (/api/v1/agent). Autenticação exclusiva por credencial de executor (Bearer), sem cookie nem CSRF.
/// O executor é sempre o da credencial: nenhum endpoint aceita executor, worker, status ou lease informados pelo cliente.
/// As regras (estado, lease, tentativas, tipos permitidos) ficam em WorkTask / casos de uso; aqui só há tradução HTTP.
/// </summary>
public static class AgentEndpoints
{
    private const int MaxResultBytes = 4096;
    private const int MaxBodyBytes = 8192;

    public static void MapAgentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/agent").WithTags("Agente (worker)")
            .RequireAuthorization(ExecutorAuth.Policy).RequireRateLimiting(ExecutorAuth.RateLimit).WithMetadata(new IntegrationEndpointMetadata());

        Problems(group.MapGet("/config", (HttpContext context, IExecutorGateway gateway) => Execute(() => Task.FromResult(Results.Ok(gateway.GetConfig(Identity(context))))))
            .Produces<ExecutorConfig>().WithSummary("Capacidades e configuração do executor autenticado: tipos permitidos, heartbeat recomendado, lease e limite de claim."));

        Problems(group.MapPost("/heartbeat", (HttpContext context, IExecutorGateway gateway, CancellationToken token) => Execute(async () =>
        {
            var (body, rejected) = await IngestionEndpoints.ReadBodyAsync<ExecutorHeartbeatRequest>(context);
            if (rejected is not null) return rejected;
            await gateway.HeartbeatAsync(Identity(context), body!, token);
            return Results.Ok(new ExecutorHeartbeatResponse(DateTimeOffset.UtcNow));
        })).Produces<ExecutorHeartbeatResponse>().WithSummary("Presença do executor: state Idle|Busy|Draining e versão do worker. Atualiza a última comunicação."));

        Problems(group.MapPost("/tasks/claim", (HttpContext context, IExecutorGateway gateway, CancellationToken token) => Execute(async () =>
        {
            var (body, rejected) = await IngestionEndpoints.ReadBodyAsync<AgentClaimRequest>(context);
            if (rejected is not null) return rejected;
            var rawKey = context.Request.Headers["Idempotency-Key"].ToString();
            Guid? claimKey = null;
            if (rawKey.Length > 0)
            {
                if (!Guid.TryParse(rawKey, out var parsed) || parsed == Guid.Empty) return Results.Problem(statusCode: 400, title: "Idempotency-Key deve ser um UUID.");
                claimKey = parsed;
            }
            var claimed = await gateway.ClaimAsync(Identity(context), body!, claimKey, token);
            return Results.Ok(new AgentTaskList(claimed.Select(ToAgentTask).ToList()));
        })).Produces<AgentTaskList>().WithSummary("Reserva atômica de até max tarefas (cabeçalho opcional Idempotency-Key UUID: repetir a MESMA chave, após resposta perdida, devolve as tarefas que este executor já segura, sem reservar outras) (limitado pela configuração) restritas aos tipos permitidos ao executor. Lista vazia se não houver trabalho."));

        Problems(group.MapGet("/tasks/held", (HttpContext context, IWorkQueueService queue, CancellationToken token) => Execute(async () =>
                Results.Ok(new AgentTaskList((await queue.HeldAsync(WorkerId(context), token)).Select(ToAgentTask).ToList()))))
            .Produces<AgentTaskList>().WithSummary("Tarefas com lease vigente neste executor. Após reiniciar, o worker as devolve (release) em vez de esperar o lease vencer."));

        Problems(group.MapPost("/tasks/{id:guid}/heartbeat", (Guid id, HttpContext context, IWorkQueueService queue, CancellationToken token) => Execute(async () =>
            Attempt(await IngestionEndpoints.ReadBodyAsync<AttemptRequest>(context), out var request, out var rejected)
                ? Results.Ok(new CancellationStatus(await queue.HeartbeatAsync(WorkerId(context), id, request!.Attempt, token))) : rejected!))
            .Produces<CancellationStatus>().WithSummary("Renova o lease da tentativa e informa se o cancelamento foi solicitado. 409 LeaseLost se o executor não é mais o dono."));

        Problems(group.MapGet("/tasks/{id:guid}/cancellation", (Guid id, int? attempt, HttpContext context, IWorkQueueService queue, CancellationToken token) => Execute(async () =>
                attempt is null or < 1 ? Results.Problem(statusCode: 400, title: "Informe attempt.")
                    : Results.Ok(new CancellationStatus(await queue.IsCancelRequestedAsync(WorkerId(context), id, attempt.Value, token)))))
            .Produces<CancellationStatus>().WithSummary("Consulta somente leitura do pedido de cancelamento (não renova o lease)."));

        Problems(group.MapPost("/tasks/{id:guid}/complete", (Guid id, HttpContext context, IWorkQueueService queue, CancellationToken token) => Execute(async () =>
        {
            var (body, rejected) = await IngestionEndpoints.ReadBodyAsync<AgentCompleteRequest>(context, MaxBodyBytes);
            if (rejected is not null) return rejected;
            string? result = null;
            if (body!.Result is { ValueKind: not (JsonValueKind.Undefined or JsonValueKind.Null) } element)
            {
                result = element.GetRawText();
                if (System.Text.Encoding.UTF8.GetByteCount(result) > MaxResultBytes) return Results.Problem(statusCode: 400, title: "Resultado grande demais.");
            }
            await queue.CompleteAsync(WorkerId(context), id, body.Attempt, result, token);
            return Results.NoContent();
        })).Produces(204).WithSummary("Conclui com sucesso (resultado JSON ≤ 4 KB, sem segredos). Repetir o mesmo envio é aceito sem novo efeito."));

        Problems(group.MapPost("/tasks/{id:guid}/fail", (Guid id, HttpContext context, IWorkQueueService queue, CancellationToken token) => Execute(async () =>
        {
            var (body, rejected) = await IngestionEndpoints.ReadBodyAsync<AgentFailRequest>(context);
            if (rejected is not null) return rejected;
            var status = await queue.FailAsync(WorkerId(context), id, body!.Attempt, body.Code ?? "", body.Message is { Length: > 1000 } long_ ? long_[..1000] : body.Message, body.Transient, token);
            return Results.Ok(new AgentFailResponse(status.ToString()));
        })).Produces<AgentFailResponse>().WithSummary("Falha da tentativa. transient=true agenda nova tentativa com backoff (ou DeadLetter ao esgotar); false encerra em Failed. Código e mensagem são sanitizados."));

        Problems(group.MapPost("/tasks/{id:guid}/cancelled", (Guid id, HttpContext context, IWorkQueueService queue, CancellationToken token) => Execute(async () =>
        {
            if (!Attempt(await IngestionEndpoints.ReadBodyAsync<AttemptRequest>(context), out var request, out var rejected)) return rejected!;
            await queue.ConfirmCancelledAsync(WorkerId(context), id, request!.Attempt, token);
            return Results.NoContent();
        })).Produces(204).WithSummary("Confirma o cancelamento solicitado pelo operador."));

        Problems(group.MapPost("/tasks/{id:guid}/release", (Guid id, HttpContext context, IWorkQueueService queue, CancellationToken token) => Execute(async () =>
        {
            if (!Attempt(await IngestionEndpoints.ReadBodyAsync<AttemptRequest>(context), out var request, out var rejected)) return rejected!;
            await queue.ReleaseAsync(WorkerId(context), id, request!.Attempt, token);
            return Results.NoContent();
        })).Produces(204).WithSummary("Devolve a tarefa à fila sem consumir a tentativa (desligamento ordenado)."));

        Problems(group.MapPost("/shutdown", (HttpContext context, IExecutorGateway gateway, CancellationToken token) => Execute(async () =>
        {
            await gateway.ShutdownAsync(Identity(context), token);
            return Results.NoContent();
        })).Produces(204).WithSummary("Encerramento controlado: devolve tudo o que o executor segura e o marca Offline."));
    }

    private static ExecutorIdentity Identity(HttpContext context) => (ExecutorIdentity)context.Items[ExecutorAuth.IdentityItem]!;
    private static string WorkerId(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!).ToString("D");

    private static bool Attempt((AttemptRequest? Body, IResult? Rejected) read, out AttemptRequest? request, out IResult? rejected)
    {
        (request, rejected) = read;
        return rejected is null;
    }

    private static AgentTask ToAgentTask(ClaimedWorkTask task)
    {
        using var document = JsonDocument.Parse(task.Payload);
        return new(task.Id, task.Type.ToString(), task.PayloadVersion, document.RootElement.Clone(), task.Attempt, task.MaxAttempts, task.CorrelationId, task.LeaseUntil);
    }

    private static RouteHandlerBuilder Problems(RouteHandlerBuilder route) =>
        route.ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(409).ProducesProblem(413).ProducesProblem(415).ProducesProblem(429).ProducesProblem(503);

    private static async Task<IResult> Execute(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (WorkQueueException error)
        {
            var (status, title, code) = error.Failure switch
            {
                WorkQueueFailure.Validation => (400, "Dados inválidos.", "Validation"),
                WorkQueueFailure.NotFound => (404, "Tarefa não encontrada.", "NotFound"),
                WorkQueueFailure.LeaseLost => (409, "Este executor não possui mais o lease da tarefa.", "LeaseLost"),
                WorkQueueFailure.Conflict => (409, "A tarefa não está em estado que permita a operação.", "Conflict"),
                WorkQueueFailure.Unauthorized => (401, "Credencial do executor inválida.", "Unauthorized"),
                _ => (503, "Serviço indisponível. Tente novamente.", "Unavailable"),
            };
            return Problem(status, title, code);
        }
        catch (ExecutorException error)
        {
            var (status, title, code) = error.Failure switch
            {
                ExecutorFailure.Validation => (400, "Dados inválidos.", "Validation"),
                ExecutorFailure.Unauthorized => (401, "Credencial do executor inválida.", "Unauthorized"),
                ExecutorFailure.NotFound => (404, "Executor não encontrado.", "NotFound"),
                ExecutorFailure.Conflict => (409, "Conflito.", "Conflict"),
                _ => (503, "Serviço indisponível. Tente novamente.", "Unavailable"),
            };
            return Problem(status, title, code);
        }
    }

    private static IResult Problem(int status, string title, string code) =>
        Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
