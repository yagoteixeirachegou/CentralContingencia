namespace CentralContingencia.Domain.Proxies;

public enum ProxyImportOutcome { Imported, Empty, Duplicate, Conflict, Invalid, NotImported }
public sealed class ProxyImportLine
{
    public Guid BatchId { get; private set; }
    public int Line { get; private set; }
    public Guid? ProxyId { get; private set; }
    public ProxyImportOutcome Outcome { get; private set; }
    private ProxyImportLine() { }
    public ProxyImportLine(int line, Guid? proxyId, ProxyImportOutcome outcome)
    {
        if (line < 2 || !Enum.IsDefined(outcome) || (outcome == ProxyImportOutcome.Imported) != proxyId.HasValue || proxyId == Guid.Empty)
            throw new ArgumentException("Linha de importação inválida.");
        Line = line; ProxyId = proxyId; Outcome = outcome;
    }
}
// Recibo durável e auditoria da operação; não armazena CSV ou credenciais.
public sealed class ProxyImportBatch
{
    public Guid Id { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public string ProtectedFingerprint { get; private set; } = "";
    public DateTimeOffset OccurredAt { get; private set; }
    public string CorrelationId { get; private set; } = "";
    public int Total { get; private set; }
    public int Imported { get; private set; }
    public int Ignored { get; private set; }
    public int Invalid { get; private set; }
    public int Conflicting { get; private set; }
    private readonly List<ProxyImportLine> lines = [];
    public IReadOnlyCollection<ProxyImportLine> Lines => lines.AsReadOnly();
    private ProxyImportBatch() { }
    public static ProxyImportBatch Create(Guid actor, Guid key, string fingerprint, DateTimeOffset now,
        string correlation, IReadOnlyCollection<ProxyImportLine> lines)
    {
        if (actor == Guid.Empty || key == Guid.Empty || string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length > 2048 ||
            string.IsNullOrWhiteSpace(correlation) || correlation.Length > 128 || lines.Count is < 1 or > 10000 ||
            lines.Select(l => l.Line).Distinct().Count() != lines.Count || lines.Any(l => l.Outcome is ProxyImportOutcome.Invalid or ProxyImportOutcome.NotImported))
            throw new ArgumentException("Lote de importação inválido.");
        ProxyRules.ValidateTimestamp(now);
        var batch = new ProxyImportBatch { Id = Guid.NewGuid(), ActorId = actor, IdempotencyKey = key,
            ProtectedFingerprint = fingerprint, OccurredAt = now.ToUniversalTime(), CorrelationId = correlation, Total = lines.Count };
        batch.lines.AddRange(lines);
        batch.Imported = lines.Count(l => l.Outcome == ProxyImportOutcome.Imported);
        batch.Ignored = lines.Count(l => l.Outcome is ProxyImportOutcome.Empty or ProxyImportOutcome.Duplicate or ProxyImportOutcome.NotImported);
        batch.Invalid = lines.Count(l => l.Outcome == ProxyImportOutcome.Invalid);
        batch.Conflicting = lines.Count(l => l.Outcome == ProxyImportOutcome.Conflict);
        return batch;
    }
    public override string ToString() => "[proxy import batch]";
}
