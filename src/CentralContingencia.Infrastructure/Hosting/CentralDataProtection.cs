using System.Security.Cryptography.X509Certificates;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CentralContingencia.Infrastructure.Hosting;

/// <summary>
/// Seção <c>DataProtection</c>. Store=Default mantém o comportamento local (chaves do perfil do usuário do sistema).
/// Store=Database guarda o anel de chaves no PostgreSQL, cifrado por um certificado X.509 fornecido por variável de ambiente.
/// </summary>
public sealed class DataProtectionSettings
{
    public const string Section = "DataProtection";
    public const string DefaultStore = "Default";
    public const string DatabaseStore = "Database";
    public string Store { get; set; } = DefaultStore;
    /// <summary>PFX em Base64. Segredo: somente variável de ambiente ou secret manager.</summary>
    public string? CertificateBase64 { get; set; }
    public string? CertificatePassword { get; set; }
    /// <summary>Certificado anterior, mantido apenas durante a rotação para ler chaves cifradas por ele.</summary>
    public string? PreviousCertificateBase64 { get; set; }
    public string? PreviousCertificatePassword { get; set; }
    public override string ToString() => "[data protection settings]";
}

public static class CentralDataProtection
{
    public const string ApplicationName = "CentralContingencia.P6";

    public static IServiceCollection AddCentralDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(DataProtectionSettings.Section).Get<DataProtectionSettings>() ?? new();
        var builder = services.AddDataProtection().SetApplicationName(ApplicationName);
        if (settings.Store == DataProtectionSettings.DefaultStore) return services;
        if (settings.Store != DataProtectionSettings.DatabaseStore)
            throw new InvalidOperationException("DataProtection:Store deve ser Default ou Database.");
        var current = Load(settings.CertificateBase64, settings.CertificatePassword, "DataProtection:CertificateBase64")
            ?? throw new InvalidOperationException("DataProtection:CertificateBase64 é obrigatório quando DataProtection:Store=Database.");
        builder.PersistKeysToDbContext<CentralContingenciaDbContext>().ProtectKeysWithCertificate(current);
        var previous = Load(settings.PreviousCertificateBase64, settings.PreviousCertificatePassword, "DataProtection:PreviousCertificateBase64");
        if (previous is not null) builder.UnprotectKeysWithAnyCertificate(previous);
        return services;
    }

    private static X509Certificate2? Load(string? base64, string? password, string setting)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        try
        {
            var certificate = X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(base64.Trim()), string.IsNullOrEmpty(password) ? null : password);
            if (!certificate.HasPrivateKey) throw new InvalidOperationException();
            return certificate;
        }
        catch (Exception)
        {
            // A mensagem cita só o nome da configuração, nunca o conteúdo.
            throw new InvalidOperationException($"{setting} não contém um PFX Base64 válido com chave privada (verifique também a senha).");
        }
    }
}
