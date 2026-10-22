using System.Security.Cryptography;
using System.Text;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Domain.Proxies;
using Microsoft.Extensions.Configuration;

namespace CentralContingencia.Infrastructure.Proxies;

public sealed class AesGcmProxyPasswordProtector : IProxyPasswordProtector, IDisposable
{
    private readonly Dictionary<string, byte[]> keys = new(StringComparer.Ordinal);
    private readonly string activeKeyId;
    private readonly object gate = new();
    private bool disposed;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public AesGcmProxyPasswordProtector(IConfiguration configuration)
    {
        activeKeyId = configuration["ProxyEncryption:ActiveKeyId"] ?? "";
        try
        {
            foreach (var entry in configuration.GetSection("ProxyEncryption:Keys").GetChildren())
            {
                if (!EncryptedProxyPassword.IsValidKeyId(entry.Key)) throw new FormatException();
                var key = Convert.FromBase64String(entry.Value ?? "");
                if (key.Length != 32)
                {
                    CryptographicOperations.ZeroMemory(key);
                    throw new FormatException();
                }
                // Cada versão representa material novo, nunca um alias da mesma chave.
                if (keys.Values.Any(existing => CryptographicOperations.FixedTimeEquals(existing, key)))
                {
                    CryptographicOperations.ZeroMemory(key);
                    throw new FormatException();
                }
                keys.Add(entry.Key, key);
            }
            if (!EncryptedProxyPassword.IsValidKeyId(activeKeyId) || !keys.ContainsKey(activeKeyId))
                throw new FormatException();
        }
        catch (Exception error) when (error is FormatException or ArgumentException)
        {
            Dispose();
            throw new InvalidOperationException("Configuração de criptografia de proxies ausente ou inválida.");
        }
    }

    public EncryptedProxyPassword Encrypt(Guid proxyId, string password)
    {
        ValidateId(proxyId);
        ProxyRules.ValidatePassword(password);
        byte[] plaintext;
        try { plaintext = StrictUtf8.GetBytes(password); }
        catch (EncoderFallbackException) { throw new ArgumentException("Senha de proxy inválida."); }
        try
        {
            if (plaintext.Length > EncryptedProxyPassword.MaxCiphertextSize)
                throw new ArgumentException("Senha de proxy inválida.");
            var nonce = RandomNumberGenerator.GetBytes(EncryptedProxyPassword.NonceSize);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[EncryptedProxyPassword.TagSize];
            var key = CopyKey(activeKeyId);
            try
            {
                using var aes = new AesGcm(key, EncryptedProxyPassword.TagSize);
                aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(proxyId, activeKeyId));
            }
            finally { CryptographicOperations.ZeroMemory(key); }
            return EncryptedProxyPassword.Create(activeKeyId, nonce, ciphertext, tag);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public string Decrypt(Guid proxyId, EncryptedProxyPassword password)
    {
        ValidateId(proxyId);
        ArgumentNullException.ThrowIfNull(password);
        var plaintext = new byte[password.Ciphertext.Length];
        var key = CopyKey(password.KeyId);
        try
        {
            using var aes = new AesGcm(key, EncryptedProxyPassword.TagSize);
            aes.Decrypt(password.Nonce, password.Ciphertext, password.Tag, plaintext, AssociatedData(proxyId, password.KeyId));
            return StrictUtf8.GetString(plaintext);
        }
        catch (Exception error) when (error is CryptographicException or DecoderFallbackException or ArgumentException)
        {
            throw new CryptographicException("Não foi possível autenticar a senha cifrada do proxy.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[] CopyKey(string keyId)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!keys.TryGetValue(keyId, out var key))
                throw new CryptographicException("Não foi possível autenticar a senha cifrada do proxy.");
            return (byte[])key.Clone();
        }
    }
    private static byte[] AssociatedData(Guid proxyId, string keyId) =>
        Encoding.UTF8.GetBytes($"CentralContingencia:ProxyPassword:v1:{proxyId:N}:{keyId}");
    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identificador de proxy inválido.");
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            foreach (var key in keys.Values) CryptographicOperations.ZeroMemory(key);
            keys.Clear();
            disposed = true;
        }
    }
}
