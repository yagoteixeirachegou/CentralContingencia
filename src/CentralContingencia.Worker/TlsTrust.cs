using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace CentralContingencia.Worker;

/// <summary>
/// Confiança TLS por processo para uma CA privada, SEM desligar a validação: a cadeia precisa terminar em uma das CAs informadas, o certificado precisa estar
/// dentro da validade e o nome do host precisa coincidir. Nada é instalado no repositório de certificados do Windows.
/// </summary>
public static class TlsTrust
{
    public static X509Certificate2Collection LoadAuthorities(string path)
    {
        var roots = new X509Certificate2Collection();
        try { roots.ImportFromPemFile(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or ArgumentException)
        { throw new InvalidOperationException("Worker:Api:TrustedCaFile não pôde ser lido como PEM."); }
        if (roots.Count == 0) throw new InvalidOperationException("Worker:Api:TrustedCaFile não contém certificados.");
        return roots;
    }

    public static bool Validate(X509Certificate2Collection roots, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (certificate is null) return false;
        // Nome do host divergente ou certificado ausente nunca são perdoados.
        if ((errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0) return false;
        if (errors == SslPolicyErrors.None) return true;
        using var leaf = new X509Certificate2(certificate);
        using var custom = new X509Chain();
        custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        custom.ChainPolicy.CustomTrustStore.AddRange(roots);
        custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // CA privada normalmente não publica CRL/OCSP
        if (chain is not null) foreach (var element in chain.ChainElements.Skip(1)) custom.ChainPolicy.ExtraStore.Add(element.Certificate);
        return custom.Build(leaf);
    }
}
