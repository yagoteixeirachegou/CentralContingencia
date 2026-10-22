using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Domain.Audit;

namespace CentralContingencia.Application.Proxies.Import;

public sealed class CsvImportCandidate(int line, ProxyHost host, ProxyPort port, string username, string password,
    string region, ProxyStatus status, DateTimeOffset expiresAt) : IDisposable
{
    public int Line { get; } = line;
    public ProxyHost Host { get; } = host;
    public ProxyPort Port { get; } = port;
    [JsonIgnore] public string Username { get; private set; } = username;
    [JsonIgnore] public string Password { get; private set; } = password;
    public string Region { get; } = region;
    public ProxyStatus Status { get; } = status;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
    public void Dispose() { Username = ""; Password = ""; }
    public override string ToString() => "[CSV import candidate]";
}
public sealed class CsvAnalysis(CsvPreviewResponse preview, IReadOnlyList<CsvPreviewRow> rows, IReadOnlyList<CsvImportCandidate> candidates) : IDisposable
{
    public CsvPreviewResponse Preview { get; } = preview;
    public IReadOnlyList<CsvPreviewRow> Rows { get; } = rows;
    public IReadOnlyList<CsvImportCandidate> Candidates { get; } = candidates;
    public void Dispose() { foreach (var candidate in Candidates) candidate.Dispose(); }
    public override string ToString() => "[CSV analysis]";
}
public interface IImportConfirmationProtector
{
    string Issue(Guid actorId, byte[] fingerprint, DateTimeOffset expiresAt);
    bool Validate(string proof, Guid actorId, byte[] fingerprint, DateTimeOffset now);
    string ProtectFingerprint(byte[] fingerprint);
    bool MatchesFingerprint(string protectedFingerprint, byte[] fingerprint);
}
public interface IProxyImportTransaction : IAsyncDisposable
{
    Task<ProxyImportBatch?> FindAsync(Guid actorId, Guid key, CancellationToken token);
    Task SaveAsync(ProxyImportBatch batch, IReadOnlyList<Proxy> proxies, IReadOnlyList<ProxyAuditEvent> events, CancellationToken token);
}
public interface IProxyImportStore
{
    Task<IProxyImportTransaction> BeginAsync(CancellationToken token);
}
public sealed record CsvImportItem(int Line, Guid? ProxyId, string Outcome);
public sealed record CsvImportReport(Guid? ImportId, bool Replayed, int Total, int Imported, int Ignored, int Invalid,
    int Conflicting, IReadOnlyList<CsvImportItem> Items);
public interface IProxyCsvImportService
{
    Task<CsvImportReport> CommitAsync(ProxyActor actor, ReadOnlyMemory<byte> csv, string proof, Guid idempotencyKey,
        string? defaultRegion, string correlationId, CancellationToken token, bool skipRowsWithoutConnection = false, int? referenceYear = null);
}
public enum CsvImportFailure { Confirmation, IdempotencyConflict, Validation, Persistence, EncryptionUnavailable }
public sealed class CsvImportException(CsvImportFailure failure, CsvImportReport? report = null) : Exception("Importação CSV recusada.")
{
    public CsvImportFailure Failure { get; } = failure;
    public CsvImportReport? Report { get; } = report;
}
public static class CsvImportFingerprint
{
    public static byte[] Compute(ReadOnlyMemory<byte> csv, string? defaultRegion, bool skipRowsWithoutConnection = false, int? referenceYear = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("CentralContingencia.CsvImport.v1"u8);
        hash.AppendData(BitConverter.GetBytes(csv.Length));
        hash.AppendData(csv.Span);
        var region = Encoding.UTF8.GetBytes(defaultRegion ?? "");
        try {
            hash.AppendData(BitConverter.GetBytes(region.Length)); hash.AppendData(region);
            // False preserves existing fingerprints/receipts exactly. True is explicitly bound to the proof.
            if (skipRowsWithoutConnection) hash.AppendData("SkipRowsWithoutConnection:v1:true"u8);
            // An absent year preserves previous proofs and receipts. Explicit years bind interpretation.
            if (referenceYear.HasValue)
            {
                hash.AppendData("ReferenceYear:v1"u8);
                hash.AppendData(BitConverter.GetBytes(referenceYear.Value));
            }
            return hash.GetHashAndReset();
        }
        finally { CryptographicOperations.ZeroMemory(region); }
    }
}
