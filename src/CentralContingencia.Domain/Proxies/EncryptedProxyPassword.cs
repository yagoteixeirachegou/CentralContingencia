namespace CentralContingencia.Domain.Proxies;

// Envelope opaco: nenhum segredo em texto claro pertence ao agregado.
public sealed class EncryptedProxyPassword : IEquatable<EncryptedProxyPassword>
{
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int MaxCiphertextSize = 1024;
    private byte[] nonce = [];
    private byte[] ciphertext = [];
    private byte[] tag = [];
    public string KeyId { get; private set; } = "";
    public byte[] Nonce => (byte[])nonce.Clone();
    public byte[] Ciphertext => (byte[])ciphertext.Clone();
    public byte[] Tag => (byte[])tag.Clone();

    private EncryptedProxyPassword() { }
    public static EncryptedProxyPassword Create(string keyId, byte[] nonce, byte[] ciphertext, byte[] tag)
    {
        if (!IsValidKeyId(keyId)) throw new ArgumentException("Identificador de chave inválido.");
        if (nonce is null || nonce.Length != NonceSize || tag is null || tag.Length != TagSize ||
            ciphertext is null || ciphertext.Length is < 1 or > MaxCiphertextSize)
            throw new ArgumentException("Envelope criptográfico inválido.");
        return new() { KeyId = keyId, nonce = (byte[])nonce.Clone(), ciphertext = (byte[])ciphertext.Clone(), tag = (byte[])tag.Clone() };
    }

    public static bool IsValidKeyId(string? value) => value is { Length: >= 1 and <= 64 } &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    public bool Equals(EncryptedProxyPassword? other) => other is not null && KeyId == other.KeyId &&
        nonce.SequenceEqual(other.nonce) && ciphertext.SequenceEqual(other.ciphertext) && tag.SequenceEqual(other.tag);
    public override bool Equals(object? obj) => obj is EncryptedProxyPassword other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(KeyId, Convert.ToBase64String(nonce));
    public override string ToString() => "[encrypted proxy password]";
}
