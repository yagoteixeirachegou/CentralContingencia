using System.Security.Cryptography;
using CentralContingencia.Application.Proxies.Import;
using Microsoft.AspNetCore.DataProtection;

namespace CentralContingencia.Infrastructure.Proxies;

public sealed class DataProtectionImportConfirmation(IDataProtectionProvider provider) : IImportConfirmationProtector
{
    private readonly IDataProtector proof = provider.CreateProtector("CentralContingencia.CsvImport.Confirmation.v1");
    private readonly IDataProtector fingerprintProtector = provider.CreateProtector("CentralContingencia.CsvImport.Fingerprint.v1");
    public string Issue(Guid actorId, byte[] fingerprint, DateTimeOffset expiresAt)
    {
        var payload = new byte[56];
        try
        {
            actorId.TryWriteBytes(payload.AsSpan(0,16));
            BitConverter.TryWriteBytes(payload.AsSpan(16,8), expiresAt.UtcTicks);
            fingerprint.CopyTo(payload,24);
            return Convert.ToBase64String(proof.Protect(payload));
        }
        finally { CryptographicOperations.ZeroMemory(payload); }
    }
    public bool Validate(string token, Guid actorId, byte[] fingerprint, DateTimeOffset now)
    {
        byte[]? payload = null;
        try
        {
            if (token.Length > 4096) return false;
            payload = proof.Unprotect(Convert.FromBase64String(token));
            return payload.Length == 56 && new Guid(payload.AsSpan(0,16)) == actorId &&
                BitConverter.ToInt64(payload,16) > now.UtcTicks &&
                CryptographicOperations.FixedTimeEquals(payload.AsSpan(24,32),fingerprint);
        }
        catch (Exception error) when (error is CryptographicException or FormatException or ArgumentException) { return false; }
        finally { if (payload is not null) CryptographicOperations.ZeroMemory(payload); }
    }
    public string ProtectFingerprint(byte[] fingerprint) => Convert.ToBase64String(fingerprintProtector.Protect(fingerprint));
    public bool MatchesFingerprint(string protectedFingerprint, byte[] fingerprint)
    {
        byte[]? original = null;
        try
        {
            original = fingerprintProtector.Unprotect(Convert.FromBase64String(protectedFingerprint));
            return CryptographicOperations.FixedTimeEquals(original,fingerprint);
        }
        catch (Exception error) when (error is CryptographicException or FormatException) { return false; }
        finally { if (original is not null) CryptographicOperations.ZeroMemory(original); }
    }
}
