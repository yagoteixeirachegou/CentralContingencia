using System.Text;

namespace CentralContingencia.Domain.Proxies;

// Invariantes compartilhadas pelo agregado, API e pre-visualizacao sem cifragem.
public static class ProxyRules
{
    public static void ValidateUsername(string value) => ValidateText(value, 128);
    public static void ValidateRegion(string value) => ValidateText(value, 100);
    public static void ValidateText(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Any(char.IsControl))
            throw new ArgumentException("Metadado de proxy inválido.");
    }
    public static void ValidateTimestamp(DateTimeOffset value)
    {
        if (value.UtcDateTime == DateTime.MinValue || value.UtcDateTime == DateTime.MaxValue)
            throw new ArgumentException("Timestamp de proxy inválido.");
    }
    public static void ValidatePassword(string value)
    {
        try
        {
            if (string.IsNullOrEmpty(value) || value.Length > EncryptedProxyPassword.MaxCiphertextSize ||
                new UTF8Encoding(false, true).GetByteCount(value) > EncryptedProxyPassword.MaxCiphertextSize)
                throw new ArgumentException("Senha de proxy inválida.");
        }
        catch (EncoderFallbackException) { throw new ArgumentException("Senha de proxy inválida."); }
    }
}
