namespace CentralContingencia.Api.Hosting;

/// <summary>
/// Funcionalidades que dependem de AdsPower ou n8n em localhost (dashboard do AdsPower, ingestão de perfis, envio de TXT).
/// Ligadas por padrão apenas em Development; fora dele respondem 503 até existir um conector seguro aprovado.
/// </summary>
public static class LocalIntegrations
{
    public const string Setting = "LocalIntegrations:Enabled";
    public static readonly string[] RoutePrefixes = ["/api/v1/adspower", "/api/v1/integration", "/api/v1/profile-ingestions"];

    public static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment) =>
        configuration.GetValue<bool?>(Setting) ?? environment.IsDevelopment();

    public static void MapUnavailable(this WebApplication app)
    {
        foreach (var prefix in RoutePrefixes)
            app.Map(prefix + "/{**rest}", () => Results.Problem(statusCode: 503,
                title: "Esta integração depende do AdsPower/n8n locais e não está disponível neste ambiente.",
                extensions: new Dictionary<string, object?> { ["code"] = "LocalIntegrationUnavailable" })).ExcludeFromDescription();
    }
}
