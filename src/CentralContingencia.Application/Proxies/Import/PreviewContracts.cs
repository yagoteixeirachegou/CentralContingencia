namespace CentralContingencia.Application.Proxies.Import;

public sealed class CsvPreviewLimits
{
    public int MaxFileBytes { get; init; } = 2 * 1024 * 1024;
    public int MaxRows { get; init; } = 5000;
    public int MaxPhysicalLines { get; init; } = 10000;
    public int MaxFieldCharacters { get; init; } = 4096;
    public int MaxColumns { get; init; } = 32;
    public void Validate()
    {
        if (MaxFileBytes is < 1024 or > 10 * 1024 * 1024 || MaxRows is < 1 or > 10000 ||
            MaxPhysicalLines < MaxRows || MaxPhysicalLines > 50000 || MaxFieldCharacters is < 1024 or > 8192 || MaxColumns is < 10 or > 64)
            throw new InvalidOperationException("Limites de pré-visualização CSV inválidos.");
    }
}
public sealed record CsvIssue(int Line, int? Column, string Code, string Message);
public sealed record CsvColumn(int Position, string? Field);
public sealed record CsvPreviewSummary(int Total, int Valid, int Invalid, int Duplicates, int Empty, int Conflicts, int Warnings);
// Apenas campos tipados/mascarados. Metadados textuais não são ecoados nesta etapa.
public sealed record CsvPreviewRow(int Line, int EndLine, string? HostMasked, int? Port, string UsernameMasked,
    string PasswordMasked, string? Status, DateTimeOffset? ExpiresAt, bool ProfilePresent, bool PlatformPresent,
    bool RegionPresent, bool AdsPowerLotPresent, bool Valid, bool Duplicate, bool ExistingConflict,
    IReadOnlyList<CsvIssue> Errors, IReadOnlyList<CsvIssue> Warnings);
public sealed record CsvPreviewResponse(string Delimiter, IReadOnlyList<CsvColumn> Columns, IReadOnlyList<CsvIssue> HeaderIssues,
    CsvPreviewSummary Summary, int Page, int PageSize, IReadOnlyList<CsvPreviewRow> Items)
{
    public string? ConfirmationToken { get; init; }
    public DateTimeOffset? ConfirmationExpiresAt { get; init; }
}
public sealed record ProxyEndpointKey(string Host, int Port);
public interface IProxyImportConflictReader
{
    Task<IReadOnlySet<ProxyEndpointKey>> FindExistingAsync(IReadOnlyCollection<ProxyEndpointKey> endpoints, CancellationToken token);
}
public interface IProxyCsvPreviewService
{
    Task<CsvPreviewResponse> PreviewAsync(ProxyActor actor, ReadOnlyMemory<byte> csv, int page, int pageSize, CancellationToken token, string? defaultRegion = null, bool skipRowsWithoutConnection = false, int? referenceYear = null);
}
public enum CsvPreviewFailure { InvalidFile, LimitExceeded, Forbidden, InvalidPage, InvalidRegion, InvalidReferenceYear }
public static class CsvImportOptions
{
    public static void ValidateReferenceYear(int? referenceYear)
    {
        if (referenceYear is < 1 or > 9999) throw new CsvPreviewException(CsvPreviewFailure.InvalidReferenceYear);
    }
}
public sealed class CsvPreviewException(CsvPreviewFailure failure, int line = 1)
    : Exception("Pré-visualização CSV recusada.")
{
    public CsvPreviewFailure Failure { get; } = failure;
    public int Line { get; } = line;
}
