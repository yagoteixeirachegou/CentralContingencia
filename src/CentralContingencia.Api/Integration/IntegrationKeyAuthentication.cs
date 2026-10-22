using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CentralContingencia.Api.Integration;

public static class IntegrationAuth
{
    public const string Scheme = "IntegrationKey";
    public const string ProfileIngestionScope = "profile-ingestion";
    public const string ProfileIngestionPolicy = "IntegrationProfileIngestion";
    public const string RateLimit = "integration";
}

/// <summary>Chave de serviço de uma integração (n8n). Somente o hash SHA-256 fica na configuração; a chave nunca é guardada.</summary>
public sealed class IntegrationKeyEntry
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string KeyHash { get; set; } = "";
    public string[] Scopes { get; set; } = [];
    public override string ToString() => "[integration key]";
}
public sealed class IntegrationKeyOptions
{
    public List<IntegrationKeyEntry> Keys { get; set; } = [];
}
/// <summary>Marca endpoints que aceitam somente chave de serviço (nunca cookie), por isso dispensam CSRF.</summary>
public sealed class IntegrationEndpointMetadata;

/// <summary>Autentica "Authorization: Bearer &lt;chave&gt;" comparando o hash em tempo constante. Falhas não informam o motivo.</summary>
public sealed class IntegrationKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions, ILoggerFactory logger, UrlEncoder encoder,
    IOptionsMonitor<IntegrationKeyOptions> keys) : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    private const string Prefix = "Bearer ";
    internal const string Scope = "integration";
    internal const string ThrottledItem = "integration.throttled";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var header) || header.Count != 1 || header[0] is not { } value ||
            !value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return Task.FromResult(AuthenticateResult.NoResult());
        var throttle = Context.RequestServices.GetRequiredService<AuthFailureThrottle>();
        var address = Context.Connection.RemoteIpAddress;
        if (throttle.BlockedFor(Scope, address) is { } wait) { Context.Items[ThrottledItem] = wait; return Task.FromResult(AuthenticateResult.Fail("Muitas tentativas.")); }
        var token = value[Prefix.Length..].Trim();
        if (token.Length is < 32 or > 256) { throttle.RecordFailure(Scope, address); return Task.FromResult(AuthenticateResult.Fail("Chave inválida.")); }
        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        IntegrationKeyEntry? match = null;
        // Percorre todas as chaves, sem interromper no primeiro acerto, para não variar o tempo de resposta.
        foreach (var entry in keys.CurrentValue.Keys)
        {
            if (entry.Id == Guid.Empty || entry.KeyHash.Length != 64 || !entry.KeyHash.All(Uri.IsHexDigit)) continue;
            if (CryptographicOperations.FixedTimeEquals(presented, Convert.FromHexString(entry.KeyHash))) match = entry;
        }
        if (match is null) { throttle.RecordFailure(Scope, address); return Task.FromResult(AuthenticateResult.Fail("Chave inválida.")); }
        var identity = new ClaimsIdentity(IntegrationAuth.Scheme, ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new(ClaimTypes.NameIdentifier, match.Id.ToString())); identity.AddClaim(new(ClaimTypes.Name, match.Name)); identity.AddClaim(new(ClaimTypes.Role, "Integration"));
        foreach (var scope in match.Scopes) identity.AddClaim(new("scope", scope));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), IntegrationAuth.Scheme)));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Context.Items[ThrottledItem] is TimeSpan wait)
        {
            Response.Headers.RetryAfter = Math.Ceiling(wait.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await Results.Problem(statusCode: 429, title: "Muitas tentativas. Aguarde e tente novamente.").ExecuteAsync(Context);
            return;
        }
        await base.HandleChallengeAsync(properties);
    }
}
