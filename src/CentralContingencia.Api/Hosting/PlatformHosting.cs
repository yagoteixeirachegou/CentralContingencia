using System.Globalization;

namespace CentralContingencia.Api.Hosting;

/// <summary>Porta atribuída pela plataforma (Railway injeta <c>PORT</c> em tempo de execução).</summary>
public static class PlatformHosting
{
    /// <summary>
    /// Devolve a URL de escuta a partir de <c>PORT</c>, ou null se não houver <c>PORT</c> ou se <c>ASPNETCORE_URLS</c> já foi definida (explícito vence).
    /// Usa "*" (Kestrel tenta IPv6 dual-stack e cai para IPv4 quando o contêiner não tem IPv6), sem depender de shell no ENTRYPOINT.
    /// </summary>
    public static string? UrlsFromPort(IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration["URLS"]) || !string.IsNullOrWhiteSpace(configuration["urls"])) return null;
        var raw = configuration["PORT"];
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("PORT deve ser um número entre 1 e 65535.");
        return $"http://*:{port}";
    }
}

/// <summary>
/// Sonda de rede para homologação: devolve o que a API enxerga do PRÓPRIO pedido (IP da conexão após o middleware, esquema, host e cabeçalhos de proxy).
/// Serve para descobrir, com evidência, como o proxy da plataforma entrega X-Forwarded-For/X-Real-IP/X-Forwarded-Proto ANTES de configurar a confiança —
/// por isso fica fora de /api/v1 (que exige HTTPS reconhecido) e não depende de login. Só existe se <c>Diagnostics:NetworkProbeToken</c> (segredo de staging, 24+ caracteres)
/// estiver definido; exige o cabeçalho <c>X-Probe-Token</c> igual (comparação em tempo constante) e responde 404 em qualquer outro caso. Nunca ecoa Cookie, Authorization nem corpo.
/// </summary>
public static class NetworkProbe
{
    public const string Setting = "Diagnostics:NetworkProbeToken";
    public const string Header = "X-Probe-Token";
    public const int MinTokenLength = 24;
    private static readonly string[] Headers = ["X-Forwarded-For", "X-Forwarded-Proto", "X-Forwarded-Host", "X-Real-IP", "X-Original-For", "X-Original-Proto", "X-Railway-Edge", "X-Railway-Request-Id", "Forwarded", "CF-Connecting-IP", "True-Client-IP", "Via"];

    public static void MapNetworkProbe(this WebApplication app)
    {
        var token = app.Configuration[Setting];
        if (string.IsNullOrEmpty(token)) return;
        if (token.Length < MinTokenLength) throw new InvalidOperationException($"{Setting} deve ter ao menos {MinTokenLength} caracteres.");
        var expected = System.Text.Encoding.UTF8.GetBytes(token);
        app.MapGet("/diagnostics/network", (HttpContext context) =>
        {
            var presented = System.Text.Encoding.UTF8.GetBytes(context.Request.Headers[Header].ToString());
            if (presented.Length != expected.Length || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(presented, expected)) return Results.NotFound();
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new
            {
                scheme = context.Request.Scheme,
                isHttps = context.Request.IsHttps,
                host = context.Request.Host.Value,
                connectionRemoteIp = context.Connection.RemoteIpAddress?.ToString(),
                forwardedHeadersMiddleware = ReverseProxySettings.From(context.RequestServices.GetRequiredService<IConfiguration>()).Enabled,
                headers = Headers.Where(name => context.Request.Headers.ContainsKey(name)).ToDictionary(name => name, name => context.Request.Headers[name].ToString()),
            });
        }).ExcludeFromDescription();
    }
}
