using CentralContingencia.Infrastructure.Hosting;

namespace CentralContingencia.Api.Hosting;

/// <summary>Impede que a API suba fora de Development com configuração insegura ou ausente. Mensagens citam só chaves, nunca valores.</summary>
public static class ProductionConfiguration
{
    public static IReadOnlyList<string> Problems(IConfiguration configuration)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
            problems.Add("ConnectionStrings:DefaultConnection é obrigatório (sem ele a API usaria o PostgreSQL local de desenvolvimento).");
        var dataProtection = configuration.GetSection(DataProtectionSettings.Section).Get<DataProtectionSettings>() ?? new();
        if (dataProtection.Store != DataProtectionSettings.DatabaseStore)
            problems.Add("DataProtection:Store deve ser Database (chaves em armazenamento persistente).");
        if (string.IsNullOrWhiteSpace(dataProtection.CertificateBase64))
            problems.Add("DataProtection:CertificateBase64 é obrigatório (chaves devem ser cifradas em repouso).");
        if (!ReverseProxySettings.From(configuration).Enabled)
            problems.Add("ReverseProxy:Enabled deve ser true, com KnownProxies ou KnownNetworks (HTTPS e IP real dependem do proxy confiável).");
        var hosts = configuration["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(hosts) || hosts.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains("*"))
            problems.Add("AllowedHosts deve listar explicitamente o domínio público (não use \"*\").");
        return problems;
    }

    public static void EnsureValid(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment()) return;
        var problems = Problems(configuration);
        if (problems.Count > 0)
            throw new InvalidOperationException("Configuração de produção inválida:" + Environment.NewLine + string.Join(Environment.NewLine, problems.Select(p => "- " + p)));
    }
}
