using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CentralContingencia.Application.Identity;
using Microsoft.AspNetCore.Antiforgery;

namespace CentralContingencia.Api.Identity;

public static class AuthEndpoints
{
    private static Guid UserId(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Autenticação e usuários");
        group.MapGet("/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new CsrfTokenResponse(antiforgery.GetAndStoreTokens(context).RequestToken!));
        }).AllowAnonymous().Produces<CsrfTokenResponse>().WithSummary("Obtém token CSRF vinculado à identidade atual.");
        group.MapPost("/auth/login", async (LoginRequest request, IAuthenticationService auth, HttpContext context) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            return await auth.LoginAsync(request, context.TraceIdentifier)
                ? Results.NoContent() : Results.Problem(statusCode: 401, title: "Credenciais inválidas ou acesso temporariamente bloqueado.");
        }).AllowAnonymous().RequireRateLimiting("login").Produces(204).ProducesProblem(401)
          .ProducesValidationProblem().ProducesProblem(429).WithSummary("Login interno por e-mail e senha; exige X-CSRF-TOKEN.");
        group.MapGet("/auth/me", async (IAuthenticationService auth, HttpContext context) =>
        {
            var user = await auth.GetUserAsync(UserId(context));
            return user is null ? Results.Unauthorized() : Results.Ok(user);
        }).RequireAuthorization().Produces<AuthenticatedUser>().ProducesProblem(401).WithSummary("Consulta a sessão atual.");
        group.MapPost("/auth/logout", async (IAuthenticationService auth, HttpContext context) =>
        {
            await auth.LogoutAsync(UserId(context), context.TraceIdentifier);
            return Results.NoContent();
        }).RequireAuthorization().Produces(204).ProducesProblem(400).ProducesProblem(401)
          .WithSummary("Encerra todas as sessões do usuário; exige CSRF.");
        group.MapPost("/auth/change-password", async (ChangePasswordRequest request, IAuthenticationService auth, HttpContext context) =>
        {
            var errors = Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var result = await auth.ChangePasswordAsync(UserId(context), request, context.TraceIdentifier);
            return result.Succeeded ? Results.NoContent() : Results.Problem(statusCode: 400, title: result.Error);
        }).RequireAuthorization().RequireRateLimiting("sensitive").Produces(204)
          .ProducesProblem(400).ProducesProblem(401).ProducesProblem(429).WithSummary("Altera senha e revoga sessões; exige CSRF e novo login.");
        group.MapPost("/users", async (CreateUserRequest request, IAuthenticationService auth, HttpContext context) =>
        {
            var errors = Validate(request);
            if (!AccessRoles.All.Contains(request.Role)) errors["Role"] = ["Papel inválido."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var result = await auth.CreateUserAsync(UserId(context), request, context.TraceIdentifier);
            return result.Succeeded ? Results.StatusCode(201) : Results.Problem(statusCode: 400, title: result.Error);
        }).RequireAuthorization(AccessPolicies.Administration).RequireRateLimiting("sensitive").Produces(201)
          .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(429)
          .WithSummary("Cria usuário interno, exclusivamente Admin; exige CSRF.");
    }
    private static Dictionary<string, string[]> Validate(object request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);
        return results.SelectMany(r => r.MemberNames.Select(name => (name, error: r.ErrorMessage ?? "Campo inválido.")))
            .GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.Select(r => r.error).ToArray());
    }
}
public sealed record CsrfTokenResponse(string Token);
