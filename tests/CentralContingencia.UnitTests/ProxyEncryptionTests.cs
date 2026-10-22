using System.Security.Cryptography;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Infrastructure.Proxies;
using Microsoft.Extensions.Configuration;

namespace CentralContingencia.UnitTests;

public class ProxyEncryptionTests
{
    private static IConfiguration Config(string active, params (string id, byte[] key)[] keys) =>
        new ConfigurationBuilder().AddInMemoryCollection(keys.ToDictionary(k => $"ProxyEncryption:Keys:{k.id}",
            k => (string?)Convert.ToBase64String(k.key)).Append(new("ProxyEncryption:ActiveKeyId", active))).Build();
    private static AesGcmProxyPasswordProtector Protector() => new(Config("v1", ("v1", RandomNumberGenerator.GetBytes(32))));

    [Fact]
    public void RoundTripPreservesUtf8AndSpacesWithDistinctNonces()
    {
        using var protector = Protector();
        var id = Guid.NewGuid();
        var password = "  synthetic-á-🔑  ";
        var encrypted = protector.Encrypt(id, password);
        Assert.Equal(password, protector.Decrypt(id, encrypted));
        Assert.Equal("v1", encrypted.KeyId);
        Assert.Equal(12, encrypted.Nonce.Length);
        Assert.Equal(16, encrypted.Tag.Length);
        var envelopes = Enumerable.Range(0, 1000).AsParallel().Select(_ => protector.Encrypt(id, password)).ToArray();
        Assert.Equal(1000, envelopes.Select(e => Convert.ToBase64String(e.Nonce)).Distinct().Count());
        Assert.Equal(1000, envelopes.Select(e => Convert.ToBase64String(e.Ciphertext)).Distinct().Count());
    }

    [Theory]
    [InlineData("nonce")][InlineData("ciphertext")][InlineData("tag")][InlineData("identity")]
    public void TamperingOrMovingCiphertextFailsAuthentication(string component)
    {
        using var protector = Protector();
        var id = Guid.NewGuid(); var envelope = protector.Encrypt(id, "synthetic-secret");
        var nonce = envelope.Nonce; var ciphertext = envelope.Ciphertext; var tag = envelope.Tag;
        if (component == "nonce") nonce[0] ^= 1;
        if (component == "ciphertext") ciphertext[0] ^= 1;
        if (component == "tag") tag[0] ^= 1;
        if (component == "identity") id = Guid.NewGuid();
        var error = Assert.Throws<CryptographicException>(() => protector.Decrypt(id,
            EncryptedProxyPassword.Create(envelope.KeyId, nonce, ciphertext, tag)));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("synthetic-secret", error.ToString());
    }

    [Fact]
    public void RotationReadsOldVersionAndWritesOnlyActiveVersion()
    {
        var oldKey = RandomNumberGenerator.GetBytes(32); var newKey = RandomNumberGenerator.GetBytes(32);
        using var old = new AesGcmProxyPasswordProtector(Config("v1", ("v1", oldKey)));
        using var rotated = new AesGcmProxyPasswordProtector(Config("v2", ("v1", oldKey), ("v2", newKey)));
        var id = Guid.NewGuid(); var encrypted = old.Encrypt(id, "synthetic-secret");
        Assert.Equal("synthetic-secret", rotated.Decrypt(id, encrypted));
        Assert.Equal("v2", rotated.Encrypt(id, rotated.Decrypt(id, encrypted)).KeyId);
        using var retired = new AesGcmProxyPasswordProtector(Config("v2", ("v2", newKey)));
        Assert.Throws<CryptographicException>(() => retired.Decrypt(id, encrypted));
        var tampered = EncryptedProxyPassword.Create("v2", encrypted.Nonce, encrypted.Ciphertext, encrypted.Tag);
        Assert.Throws<CryptographicException>(() => rotated.Decrypt(id, tampered));
    }

    [Fact]
    public void KeyIdIsAuthenticatedEvenWhenKeyMaterialMatchesAcrossConfigurations()
    {
        var key = RandomNumberGenerator.GetBytes(32); var id = Guid.NewGuid();
        using var first = new AesGcmProxyPasswordProtector(Config("v1", ("v1", key)));
        using var alias = new AesGcmProxyPasswordProtector(Config("v2", ("v2", key)));
        var envelope = first.Encrypt(id, "synthetic-secret");
        Assert.Throws<CryptographicException>(() => alias.Decrypt(id,
            EncryptedProxyPassword.Create("v2", envelope.Nonce, envelope.Ciphertext, envelope.Tag)));
    }

    [Fact]
    public void WrongKeyFailsAndDisposedProtectorRefusesUse()
    {
        using var first = Protector(); using var wrong = Protector(); var id = Guid.NewGuid();
        var encrypted = first.Encrypt(id, "synthetic-secret");
        Assert.Throws<CryptographicException>(() => wrong.Decrypt(id, encrypted));
        first.Dispose();
        Assert.Throws<ObjectDisposedException>(() => first.Encrypt(id, "synthetic-secret"));
    }

    [Fact]
    public void MissingMalformedWeakOrAliasedKeysFailWithoutEchoingConfiguration()
    {
        var configurations = new[] { Config("v1"), Config("v1", ("v1", new byte[16])),
            Config("v1", ("v2", new byte[32])), Config("v1", ("v1", new byte[32]), ("v2", new byte[32])),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
                { ["ProxyEncryption:ActiveKeyId"] = "v1", ["ProxyEncryption:Keys:v1"] = "secret-invalid-base64" }).Build() };
        foreach (var config in configurations)
        {
            var error = Assert.Throws<InvalidOperationException>(() => new AesGcmProxyPasswordProtector(config));
            Assert.Null(error.InnerException);
            Assert.DoesNotContain("secret-invalid-base64", error.ToString());
        }
    }

    [Fact]
    public void RejectsEmptyOversizedInvalidUtf8AndEmptyIdentity()
    {
        using var protector = Protector(); var id = Guid.NewGuid();
        foreach (var password in new[] { "", new string('a', 1025), new string('á', 513), "\ud800" })
            Assert.Throws<ArgumentException>(() => protector.Encrypt(id, password));
        Assert.Throws<ArgumentException>(() => protector.Encrypt(Guid.Empty, "synthetic-secret"));
        Assert.Equal(new string('a', 1024), protector.Decrypt(id, protector.Encrypt(id, new string('a', 1024))));
    }
}
