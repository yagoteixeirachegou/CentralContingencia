using System.Security.Claims;
using System.Text.Json;
using CentralContingencia.Application.Allocations;
using CentralContingencia.Application.Identity;
using CentralContingencia.Application.Proxies;
using Microsoft.AspNetCore.Mvc;

namespace CentralContingencia.Api.Operations;

public sealed record OperationalBodyMetadata(string[] Fields);
public static class OperationalEndpoints
{
    private static ProxyActor Actor(HttpContext c) => new(Guid.Parse(c.User.FindFirstValue(ClaimTypes.NameIdentifier)!), c.User.FindAll(ClaimTypes.Role).Select(r => r.Value).ToArray());
    public static void MapOperationalEndpoints(this WebApplication app)
    {
        var profiles = app.MapGroup("/api/v1/profiles").WithTags("Perfis operacionais").RequireAuthorization(AccessPolicies.Operations);
        Read(profiles.MapGet("", (HttpContext c, IOperationalManagementService s, [AsParameters] OperationalQueryParameters q, CancellationToken t) => Execute(async () => Results.Ok(await s.ListAsync(Actor(c), q.ProfileQuery(), t)))))
            .Produces<ProfilePage>().WithSummary("Lista perfis; filtros exatos, paginação e ordenação limitada.");
        Read(profiles.MapGet("/{id:guid}", (Guid id, HttpContext c, IOperationalManagementService s, CancellationToken t) => Execute(async () => Results.Ok(await s.GetAsync(Actor(c), id, t)))))
            .Produces<ProfileResponse>().WithSummary("Consulta perfil operacional por UUID.");
        Write(profiles.MapPost("", (CreateProfileRequest request, HttpContext c, IOperationalManagementService s, CancellationToken t) => Execute(async () =>
        { var result = await s.CreateAsync(Actor(c), request, c.TraceIdentifier, t); return Results.Created($"/api/v1/profiles/{result.Id}", result); })), "name", "platform", "platform1", "platform2")
            .Produces<ProfileResponse>(201).WithSummary("Cria perfil com platform1/platform2 opcionais; platform legado preservado; Admin/Manager e CSRF.");
        Write(profiles.MapPatch("/{id:guid}", (Guid id, UpdateProfileRequest request, HttpContext c, IOperationalManagementService s, CancellationToken t) => Execute(async () => Results.Ok(await s.UpdateAsync(Actor(c), id, request, c.TraceIdentifier, t)))), "name", "platform", "platform1", "platform2")
            .Produces<ProfileResponse>().WithSummary("Atualiza perfil; slots ausentes preservados e null limpa platform1/platform2. Perfil encerrado retorna 409.");
        Write(profiles.MapPost("/{id:guid}/close", (Guid id, HttpContext c, IOperationalManagementService s, CancellationToken t) => Execute(async () =>
        { await s.CloseAsync(Actor(c), id, c.TraceIdentifier, t); return Results.NoContent(); })))
            .Produces(204).WithSummary("Encerra perfil e sua alocação atomicamente; envie objeto JSON vazio.");
        Read(profiles.MapGet("/{id:guid}/allocations", (Guid id, HttpContext c, IOperationalManagementService s, [AsParameters] OperationalQueryParameters q, CancellationToken t) => Execute(async () => Results.Ok(await s.HistoryAsync(Actor(c), id, q.AllocationQuery(), t)))))
            .Produces<AllocationPage>().WithSummary("Histórico paginado do perfil; inclui reservas e alocações encerradas.");
        var allocations = app.MapGroup("/api/v1/allocations").WithTags("Alocações").RequireAuthorization(AccessPolicies.Operations);
        Read(allocations.MapGet("", (HttpContext c, IOperationalManagementService s, [AsParameters] OperationalQueryParameters q, CancellationToken t) => Execute(async () => Results.Ok(await s.ListAllocationsAsync(Actor(c), q.AllocationQuery(), t)))))
            .Produces<AllocationPage>().WithSummary("Lista alocações sem credenciais de proxy.");
        Read(allocations.MapGet("/{id:guid}", (Guid id, HttpContext c, IOperationalManagementService s, CancellationToken t) => Execute(async () => Results.Ok(await s.GetAllocationAsync(Actor(c), id, t)))))
            .Produces<AllocationResponse>();
        Write(allocations.MapPost("/reserve", (ReserveAllocationRequest request, HttpContext c, IOperationalManagementService s, CancellationToken t) => Execute(async () =>
        {
            var values = c.Request.Headers["Idempotency-Key"];
            if (values.Count != 1 || !Guid.TryParse(values[0], out var key) || key == Guid.Empty) return Results.Problem(statusCode: 400, title: "Informe Idempotency-Key UUID válido.");
            var result = await s.ReserveAsync(Actor(c), request, key, c.TraceIdentifier, t);
            return Results.Created($"/api/v1/allocations/{result.Id}", result);
        })), "profileId", "proxyId", "reservationExpiresAt").Produces<AllocationResponse>(201)
            .WithSummary("Reserva exclusiva; prazo opcional limitado ao TTL configurado (padrão 15 minutos) e validade da proxy. Exige Idempotency-Key; chave repetida retorna 409.");
        Write(allocations.MapPost("/{id:guid}/confirm", (Guid id, HttpContext c, IOperationalManagementService s, CancellationToken t) => Execute(async () =>
        { await s.ConfirmAsync(Actor(c), id, c.TraceIdentifier, t); return Results.NoContent(); }))).Produces(204).WithSummary("Confirma reserva válida; repetição de Confirmed retorna 204 sem nova auditoria. Reserva expirada retorna 409 após recuperação auditada. Envie JSON vazio.");
        Write(allocations.MapPost("/{id:guid}/release", (Guid id, HttpContext c, IOperationalManagementService s, CancellationToken t) => Execute(async () =>
        { await s.ReleaseAsync(Actor(c), id, c.TraceIdentifier, t); return Results.NoContent(); }))).Produces(204).WithSummary("Encerra alocação preservando histórico; envie JSON vazio.");
    }
    private static RouteHandlerBuilder Read(RouteHandlerBuilder route) => route.ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(503);
    private static RouteHandlerBuilder Write(RouteHandlerBuilder route, params string[] fields) => Read(route)
        .RequireAuthorization(AccessPolicies.Management).RequireRateLimiting("sensitive").ProducesProblem(409).ProducesProblem(415).ProducesProblem(429).ProducesProblem(500)
        .WithMetadata(new OperationalBodyMetadata(fields));
    private static async Task<IResult> Execute(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (AllocationPersistenceException error)
        {
            var status = error.Failure switch { AllocationPersistenceFailure.Validation => 400, AllocationPersistenceFailure.Forbidden => 403,
                AllocationPersistenceFailure.NotFound => 404, AllocationPersistenceFailure.Conflict => 409, AllocationPersistenceFailure.AuditFailure => 500, _ => 503 };
            var title = error.Failure switch { AllocationPersistenceFailure.Validation => "Dados ou parâmetros operacionais inválidos.", AllocationPersistenceFailure.Forbidden => "Acesso negado.",
                AllocationPersistenceFailure.NotFound => "Perfil, proxy ou alocação não encontrado.", AllocationPersistenceFailure.Conflict => "Conflito operacional. Consulte o estado atual dos recursos.", AllocationPersistenceFailure.AuditFailure => "Falha interna ao registrar a operação.", _ => "Persistência operacional indisponível." };
            return Results.Problem(statusCode: status, title: title);
        }
    }
    public static async Task<bool> ValidateBodyAsync(HttpContext context, OperationalBodyMetadata metadata)
    {
        context.Request.EnableBuffering();
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in document.RootElement.EnumerateObject())
            {
                var nullableSlot = p.Name.Equals("platform1", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("platform2", StringComparison.OrdinalIgnoreCase);
                if (!seen.Add(p.Name) || !metadata.Fields.Contains(p.Name, StringComparer.OrdinalIgnoreCase) ||
                    (p.Value.ValueKind != JsonValueKind.String && !(nullableSlot && p.Value.ValueKind == JsonValueKind.Null))) return false;
                if (p.Name.Equals("reservationExpiresAt", StringComparison.OrdinalIgnoreCase) &&
                    !System.Text.RegularExpressions.Regex.IsMatch(p.Value.GetString()!, "(Z|[+-][0-9]{2}:[0-9]{2})$")) return false;
            }
            return metadata.Fields.Length == 0 ? seen.Count == 0 : seen.Count > 0;
        }
        catch (JsonException) { return false; }
        finally { context.Request.Body.Position = 0; }
    }
}
public sealed class OperationalQueryParameters
{
    public int? Page { get; init; } public int? PageSize { get; init; }
    public string? Name { get; init; } public string? Platform { get; init; } public string? Status { get; init; }
    public Guid? ProfileId { get; init; } public Guid? ProxyId { get; init; }
    public string? Sort { get; init; } public string? Direction { get; init; }
    public ProfileQuery ProfileQuery() => new(Page ?? 1, PageSize ?? 20, Name, Platform, Status, Sort ?? "createdAt", Direction ?? "desc");
    public AllocationQuery AllocationQuery() => new(Page ?? 1, PageSize ?? 20, ProfileId, ProxyId, Status, Sort ?? "createdAt", Direction ?? "desc");
}
