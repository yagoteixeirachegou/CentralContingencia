using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using CentralContingencia.Application.Identity;
using CentralContingencia.Application.Proxies;
using Microsoft.AspNetCore.Mvc;

namespace CentralContingencia.Api.Proxies;

public static class ProxyEndpoints
{
    public static async Task<bool> ValidateBodyAsync(HttpContext context)
    {
        context.Request.EnableBuffering();
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var name = property.Name.ToLowerInvariant();
                if (!names.Add(name) || name is not ("host" or "port" or "username" or "password" or "region" or "status" or "expiresat") ||
                    property.Value.ValueKind == JsonValueKind.Null) return false;
                if (name == "port")
                {
                    if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out _)) return false;
                }
                else if (property.Value.ValueKind != JsonValueKind.String) return false;
                if (name == "expiresat" && !Regex.IsMatch(property.Value.GetString()!, @"(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)) return false;
            }
            return names.Count > 0;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return false; }
        finally { context.Request.Body.Position = 0; }
    }
    private static ProxyActor Actor(HttpContext context) => new(
        Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!),
        context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray());

    public static void MapProxyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/proxies").WithTags("Proxies").RequireAuthorization(AccessPolicies.Operations);
        group.MapGet("", (HttpContext context, IProxyManagementService service, [AsParameters] ProxyQueryParameters query, CancellationToken token) =>
            Execute(async () => Results.Ok(await service.ListAsync(Actor(context), query.ToQuery(), false, token))))
            .Produces<ProxyPage>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .WithSummary("Lista proxies com paginação, filtros e ordenação limitada; credenciais mascaradas.");
        group.MapGet("/available", (HttpContext context, IProxyManagementService service, [AsParameters] ProxyQueryParameters query, CancellationToken token) =>
            Execute(async () => Results.Ok(await service.ListAsync(Actor(context), query.ToQuery(), true, token))))
            .Produces<ProxyPage>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .WithSummary("Lista proxies ativos, não vencidos e sem reserva/alocação confirmada.");
        group.MapGet("/{id:guid}", (Guid id, HttpContext context, IProxyManagementService service, CancellationToken token) =>
            Execute(async () => Results.Ok(await service.GetAsync(Actor(context), id, token))))
            .Produces<ProxyResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .WithSummary("Consulta proxy por UUID sem revelar credenciais.");
        group.MapPost("/{id:guid}/credentials/reveal", (Guid id, HttpContext context, IProxyManagementService service, CancellationToken token) =>
            Execute(async () =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(await service.RevealCredentialsAsync(Actor(context), id, context.TraceIdentifier, token));
            })).RequireAuthorization(AccessPolicies.Administration).RequireRateLimiting("sensitive")
            .Produces<ProxyCredentials>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(429).ProducesProblem(503)
            .WithSummary("Revela usuário e senha de UMA proxy; somente Admin, CSRF obrigatório, sem cache e auditado.");
        group.MapPost("", (CreateProxyRequest request, HttpContext context, IProxyManagementService service, CancellationToken token) =>
            Execute(async () =>
            {
                var result = await service.CreateAsync(Actor(context), request, context.TraceIdentifier, token);
                return Results.Created($"/api/v1/proxies/{result.Id}", result);
            })).RequireAuthorization(AccessPolicies.Management).RequireRateLimiting("sensitive")
            .Produces<ProxyResponse>(201).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .ProducesProblem(409).ProducesProblem(429).ProducesProblem(503)
            .WithSummary("Cria proxy; Admin/Manager, CSRF obrigatório, senha somente na entrada.");
        group.MapPatch("/{id:guid}", (Guid id, UpdateProxyRequest request, HttpContext context, IProxyManagementService service, CancellationToken token) =>
            Execute(async () => Results.Ok(await service.UpdateAsync(Actor(context), id, request, context.TraceIdentifier, token))))
            .RequireAuthorization(AccessPolicies.Management).RequireRateLimiting("sensitive")
            .Produces<ProxyResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .ProducesProblem(404).ProducesProblem(409).ProducesProblem(429).ProducesProblem(503)
            .WithSummary("Altera campos informados; Admin/Manager, CSRF obrigatório. Ausentes preservam valor; null é recusado.");
    }
    private static async Task<IResult> Execute(Func<Task<IResult>> operation)
    {
        try { return await operation(); }
        catch (ProxyOperationException error)
        {
            return error.Failure switch
            {
                ProxyFailure.Validation => Results.Problem(statusCode: 400, title: "Dados de proxy ou parâmetros inválidos."),
                ProxyFailure.Forbidden => Results.Problem(statusCode: 403, title: "Acesso negado."),
                ProxyFailure.NotFound => Results.Problem(statusCode: 404, title: "Proxy não encontrado."),
                ProxyFailure.Conflict => Results.Problem(statusCode: 409, title: "Conflito de gravação. Consulte o proxy e tente novamente."),
                _ => Results.Problem(statusCode: 503, title: "Criptografia de proxies indisponível.")
            };
        }
    }
}
public sealed class ProxyQueryParameters
{
    public int? Page { get; init; }
    public int? PageSize { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? Region { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? ExpiresBefore { get; init; }
    public string? Sort { get; init; }
    public string? Direction { get; init; }
    public ProxyQuery ToQuery() => new(Page ?? 1, PageSize ?? 20, Host, Port, Region, Status, ExpiresBefore, Sort ?? "createdAt", Direction ?? "desc");
}
