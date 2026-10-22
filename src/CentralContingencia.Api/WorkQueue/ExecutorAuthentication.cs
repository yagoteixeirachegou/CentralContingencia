using System.Security.Claims;
using System.Text.Encodings.Web;
using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Api.Integration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CentralContingencia.Api.WorkQueue;

public static class ExecutorAuth
{
    public const string Scheme = "ExecutorKey";
    public const string Policy = "Executor";
    public const string Scope = "agent-worker";
    public const string RateLimit = "executor";
    internal const string ThrottledItem = "executor.throttled";
    internal const string ThrottleScope = "executor";
    internal const string IdentityItem = "executor.identity";
    internal const string UnavailableItem = "executor.unavailable";
}

/// <summary>
/// Autentica "Authorization: Bearer ce1.&lt;ExecutorId&gt;.&lt;segredo&gt;" (somente este esquema, nunca cookie). A cada requisição consulta o executor:
/// credencial trocada ou executor revogado deixam de valer na hora. Falhas não informam o motivo; indisponibilidade do banco vira 503, não 401.
/// </summary>
public sealed class ExecutorKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    private const string Prefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header) || header.Count != 1 || header[0] is not { } value ||
            !value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var throttle = Context.RequestServices.GetRequiredService<AuthFailureThrottle>();
        var address = Context.Connection.RemoteIpAddress;
        // Cliente que já errou demais: 429 sem consultar o banco.
        if (throttle.BlockedFor(ExecutorAuth.ThrottleScope, address) is { } wait) { Context.Items[ExecutorAuth.ThrottledItem] = wait; return AuthenticateResult.Fail("Muitas tentativas."); }
        try
        {
            var gateway = Context.RequestServices.GetRequiredService<IExecutorGateway>();
            var identity = await gateway.AuthenticateAsync(value[Prefix.Length..].Trim(), Context.RequestAborted);
            if (identity is null) { throttle.RecordFailure(ExecutorAuth.ThrottleScope, address); return AuthenticateResult.Fail("Credencial inválida."); }
            Context.Items[ExecutorAuth.IdentityItem] = identity;
            var claims = new ClaimsIdentity(ExecutorAuth.Scheme, ClaimTypes.Name, ClaimTypes.Role);
            claims.AddClaim(new(ClaimTypes.NameIdentifier, identity.Id.ToString("D")));
            claims.AddClaim(new(ClaimTypes.Name, identity.Name));
            claims.AddClaim(new(ClaimTypes.Role, "Executor"));
            claims.AddClaim(new("scope", ExecutorAuth.Scope));
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(claims), ExecutorAuth.Scheme));
        }
        catch (ExecutorException error) when (error.Failure == ExecutorFailure.Unavailable)
        {
            Context.Items[ExecutorAuth.UnavailableItem] = true;
            return AuthenticateResult.Fail("Indisponível.");
        }
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items[ExecutorAuth.ThrottledItem] is TimeSpan wait)
        {
            Response.Headers.RetryAfter = Math.Ceiling(wait.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await Results.Problem(statusCode: 429, title: "Muitas tentativas. Aguarde e tente novamente.").ExecuteAsync(Context);
            return;
        }
        if (Context.Items.ContainsKey(ExecutorAuth.UnavailableItem))
        {
            Response.Headers.RetryAfter = "5";
            await Results.Problem(statusCode: 503, title: "Serviço indisponível. Tente novamente.").ExecuteAsync(Context);
            return;
        }
        Response.Headers.WWWAuthenticate = "Bearer";
        await Results.Problem(statusCode: 401, title: "Credencial do executor ausente, inválida ou revogada.").ExecuteAsync(Context);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        Results.Problem(statusCode: 403, title: "Acesso negado.").ExecuteAsync(Context);
}
