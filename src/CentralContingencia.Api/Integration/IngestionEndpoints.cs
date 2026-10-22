using System.Security.Claims;
using System.Text.Json.Serialization;
using CentralContingencia.Application.AdsPower;
using CentralContingencia.Application.Allocations;
using CentralContingencia.Application.Ingestion;

namespace CentralContingencia.Api.Integration;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ReserveIngestionBody
{
    public int? Count { get; init; }
    public string? Platform1 { get; init; }
    public override string ToString() => "[reserve ingestion]";
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ConfirmIngestionBody
{
    public string? AdsPowerId { get; init; }
    public string? SerialNumber { get; init; }
    public override string ToString() => "[confirm ingestion]";
}

public static class IngestionEndpoints
{
    private static IngestionActor Actor(HttpContext c) => new(Guid.Parse(c.User.FindFirstValue(ClaimTypes.NameIdentifier)!), c.User.FindFirstValue(ClaimTypes.Name) ?? "integration");

    public static void MapIngestionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/integration/profile-ingestions").WithTags("Integração: ingestão de perfis")
            .RequireAuthorization(IntegrationAuth.ProfileIngestionPolicy).RequireRateLimiting(IntegrationAuth.RateLimit).WithMetadata(new IntegrationEndpointMetadata());

        Problems(group.MapGet("/availability", (IProfileIngestionService service, CancellationToken token) => Execute(async () => Results.Ok(await service.GetAvailabilityAsync(token))))
            .Produces<IngestionAvailability>(200)
            .WithSummary("Proxies livres, o grupo de ingestão do AdsPower (teste ou produção) e o próximo nome de perfil: maior \"Perfil N\" DESSE grupo + 1. Não devolve credenciais."));

        Problems(group.MapPost("/reserve", (HttpContext c, IProfileIngestionService service, CancellationToken token) => Execute(async () =>
        {
            var (body, rejected) = await ReadBodyAsync<ReserveIngestionBody>(c);
            if (rejected is not null) return rejected;
            if (!Guid.TryParse(c.Request.Headers["Idempotency-Key"].ToString(), out var key) || key == Guid.Empty || body!.Count is null)
                return Results.Problem(statusCode: 400, title: "Informe count e um Idempotency-Key UUID.");
            return Results.Ok(await service.ReserveAsync(Actor(c), new(body.Count.Value, body.Platform1), key, c.TraceIdentifier, token));
        })).Produces<IngestionReservation>(200)
            .WithSummary("Reserva até count proxies livres (ordem da planilha), cria os perfis \"Perfil N\" e devolve a conexão SOMENTE das proxies reservadas. " +
                "Entrega parcial: se faltar proxy, atende as possíveis e lista as pendentes com o motivo (status Complete, Partial ou Empty). Exige Idempotency-Key UUID; a repetição devolve o mesmo resultado."));

        Problems(group.MapPost("/{allocationId:guid}/confirm", (Guid allocationId, HttpContext c, IProfileIngestionService service, CancellationToken token) => Execute(async () =>
        {
            var (body, rejected) = await ReadBodyAsync<ConfirmIngestionBody>(c);
            if (rejected is not null) return rejected;
            if (string.IsNullOrWhiteSpace(body!.AdsPowerId)) return Results.Problem(statusCode: 400, title: "Informe adsPowerId.");
            await service.ConfirmAsync(Actor(c), allocationId, new(body.AdsPowerId, body.SerialNumber), c.TraceIdentifier, token);
            return Results.NoContent();
        })).Produces(204).WithSummary("Confirma a reserva (proxy em uso) e grava o id e o número de série do perfil criado no AdsPower. Repetir os mesmos valores é aceito."));

        Problems(group.MapPost("/batches/{batchId:guid}/release", (Guid batchId, HttpContext c, IProfileIngestionService service, CancellationToken token) => Execute(async () =>
            Results.Ok(await service.ReleaseBatchAsync(Actor(c), batchId, c.TraceIdentifier, token)))).Produces<IngestionBatchRelease>(200)
            .WithSummary("Desfaz TODAS as reservas ainda não confirmadas do lote (proxies voltam a ficar livres e os números são reaproveitados). Perfis já confirmados não são tocados. Repetir é seguro. Use quando o fluxo falhar antes de criar os perfis."));

        Problems(group.MapPost("/{allocationId:guid}/release", (Guid allocationId, HttpContext c, IProfileIngestionService service, CancellationToken token) => Execute(async () =>
        {
            await service.ReleaseAsync(Actor(c), allocationId, c.TraceIdentifier, token);
            return Results.NoContent();
        })).Produces(204).WithSummary("Cancela uma reserva ainda não confirmada: a proxy volta a ficar livre e o número do perfil pode ser reutilizado. Reserva já confirmada retorna 409."));
    }

    private const int MaxBodyBytes = 4096;
    // Leitura manual: em desenvolvimento o ASP.NET lança exceção em corpo inválido (500); aqui é sempre 400/413/415.
    internal static async Task<(T? Body, IResult? Rejected)> ReadBodyAsync<T>(HttpContext context, int maxBytes = MaxBodyBytes) where T : class
    {
        if (context.Request.ContentLength > maxBytes) return (null, Results.Problem(statusCode: 413, title: "Corpo da requisição grande demais."));
        if (!context.Request.HasJsonContentType()) return (null, Results.Problem(statusCode: 415, title: "Envie um objeto JSON."));
        try
        {
            using var limited = new MemoryStream(); await context.Request.Body.CopyToAsync(limited, context.RequestAborted);
            if (limited.Length > maxBytes) return (null, Results.Problem(statusCode: 413, title: "Corpo da requisição grande demais."));
            limited.Position = 0;
            var body = await System.Text.Json.JsonSerializer.DeserializeAsync<T>(limited, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web), context.RequestAborted);
            return body is null ? (null, Results.Problem(statusCode: 400, title: "Objeto JSON inválido.")) : (body, null);
        }
        catch (System.Text.Json.JsonException) { return (null, Results.Problem(statusCode: 400, title: "Objeto JSON inválido.")); }
    }

    private static RouteHandlerBuilder Problems(RouteHandlerBuilder route) => route.ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(429).ProducesProblem(500).ProducesProblem(503);

    internal static async Task<IResult> Execute(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (AllocationPersistenceException error)
        {
            var (status, title) = error.Failure switch
            {
                AllocationPersistenceFailure.Validation => (400, "Dados da requisição inválidos."),
                AllocationPersistenceFailure.Forbidden => (403, "Acesso negado."),
                AllocationPersistenceFailure.NotFound => (404, "Reserva não encontrada."),
                AllocationPersistenceFailure.Conflict => (409, "Conflito: o estado mudou, a reserva venceu ou a chave de idempotência já foi usada com outro pedido."),
                AllocationPersistenceFailure.AuditFailure => (500, "Falha interna ao registrar a operação."),
                _ => (503, "Serviço indisponível. Tente novamente."),
            };
            return Results.Problem(statusCode: status, title: title);
        }
        catch (AdsPowerException error)
        {
            // A numeração depende do grupo no AdsPower: sem ele, nada é reservado e nenhum número é inventado.
            var (status, title) = error.Failure switch
            {
                AdsPowerFailure.GroupNotFound => (503, "O grupo de ingestão não foi encontrado no AdsPower. Confira o nome do grupo configurado."),
                AdsPowerFailure.Unreachable => (503, "Não foi possível falar com o AdsPower para numerar os perfis. Abra o aplicativo e tente novamente."),
                AdsPowerFailure.NotConfigured => (503, "O endereço do AdsPower não está configurado para este computador."),
                AdsPowerFailure.Unauthorized => (502, "O AdsPower recusou a chave de API. Confira a chave configurada."),
                _ => (502, "O AdsPower devolveu uma resposta inesperada ao numerar os perfis."),
            };
            return Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = error.Failure.ToString() });
        }
    }
}
