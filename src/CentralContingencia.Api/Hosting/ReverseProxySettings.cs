using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace CentralContingencia.Api.Hosting;

/// <summary>
/// Seção <c>ReverseProxy</c>. Desligada por padrão: sem ela, cabeçalhos X-Forwarded-* são ignorados.
/// Ligada, só são aceitos de origens listadas explicitamente (nunca "qualquer origem").
/// </summary>
public sealed class ReverseProxySettings
{
    public const string Section = "ReverseProxy";
    public bool Enabled { get; set; }
    /// <summary>Endereços IP exatos do proxy imediato (ex.: "10.0.0.5").</summary>
    public string[] KnownProxies { get; set; } = [];
    /// <summary>Redes em CIDR do proxy imediato (ex.: "10.0.0.0/8").</summary>
    public string[] KnownNetworks { get; set; } = [];
    public int ForwardLimit { get; set; } = 1;

    public static ReverseProxySettings From(IConfiguration configuration) =>
        configuration.GetSection(Section).Get<ReverseProxySettings>() ?? new();

    /// <summary>Valida e converte; erros citam apenas o nome da configuração.</summary>
    public (IReadOnlyList<IPAddress> Proxies, IReadOnlyList<System.Net.IPNetwork> Networks) Parse()
    {
        if (ForwardLimit is < 1 or > 5) throw new InvalidOperationException("ReverseProxy:ForwardLimit deve estar entre 1 e 5.");
        var proxies = new List<IPAddress>();
        foreach (var value in KnownProxies.Where(v => !string.IsNullOrWhiteSpace(v)))
            proxies.Add(IPAddress.TryParse(value.Trim(), out var address) && !IsWildcard(address)
                ? address : throw new InvalidOperationException("ReverseProxy:KnownProxies contém um endereço IP inválido."));
        var networks = new List<System.Net.IPNetwork>();
        foreach (var value in KnownNetworks.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            if (!System.Net.IPNetwork.TryParse(value.Trim(), out var network) || network.PrefixLength == 0)
                throw new InvalidOperationException("ReverseProxy:KnownNetworks contém uma rede CIDR inválida ou ampla demais (/0).");
            networks.Add(network);
        }
        if (Enabled && proxies.Count == 0 && networks.Count == 0)
            throw new InvalidOperationException("ReverseProxy:Enabled exige ReverseProxy:KnownProxies ou ReverseProxy:KnownNetworks.");
        return (proxies, networks);
    }

    private static bool IsWildcard(IPAddress address) => address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);

    public static void Configure(IServiceCollection services, IConfiguration configuration)
    {
        var settings = From(configuration);
        var (proxies, networks) = settings.Parse();
        if (!settings.Enabled) return;
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = settings.ForwardLimit;
            // Substitui a confiança padrão em loopback: só vale o que foi configurado.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in proxies) options.KnownProxies.Add(proxy);
            foreach (var network in networks) options.KnownIPNetworks.Add(network);
        });
    }
}
