using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.Domain.Profiles;

public sealed class ProfileRecoveryLine
{
    public Guid BatchId { get; private set; }
    public int Line { get; private set; }
    public Guid? ProxyId { get; private set; }
    public Guid? ProfileId { get; private set; }
    public Guid? AllocationId { get; private set; }
    public string Outcome { get; private set; } = "";
    private ProfileRecoveryLine() { }
    public ProfileRecoveryLine(int line, string outcome, Guid? proxyId = null, Guid? profileId = null, Guid? allocationId = null)
    {
        if (line < 2 || outcome is not ("Recovered" or "Ignored" or "AlreadyLinked") ||
            proxyId == Guid.Empty || profileId == Guid.Empty || allocationId == Guid.Empty ||
            (outcome is "Recovered" or "AlreadyLinked") && (!proxyId.HasValue || !profileId.HasValue || !allocationId.HasValue))
            throw new ArgumentException("Linha de recuperação inválida.");
        Line = line; Outcome = outcome; ProxyId = proxyId; ProfileId = profileId; AllocationId = allocationId;
    }
}

// Durable receipt and sanitized batch audit. Never contains source metadata or credentials.
public sealed class ProfileRecoveryBatch
{
    public Guid Id { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public string ProtectedFingerprint { get; private set; } = "";
    public DateTimeOffset OccurredAt { get; private set; }
    public string CorrelationId { get; private set; } = "";
    public int Total { get; private set; }
    public int Recovered { get; private set; }
    public int Ignored { get; private set; }
    private readonly List<ProfileRecoveryLine> lines = [];
    public IReadOnlyCollection<ProfileRecoveryLine> Lines => lines.AsReadOnly();
    private ProfileRecoveryBatch() { }
    public static ProfileRecoveryBatch Create(Guid actor, Guid key, string fingerprint, DateTimeOffset now,
        string correlation, IReadOnlyCollection<ProfileRecoveryLine> lines)
    {
        if (actor == Guid.Empty || key == Guid.Empty || string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length > 2048 ||
            lines.Count is < 1 or > 10000 || lines.Select(l => l.Line).Distinct().Count() != lines.Count)
            throw new ArgumentException("Recuperação inválida.");
        ProxyRules.ValidateTimestamp(now); ProxyRules.ValidateText(correlation, 128);
        var batch = new ProfileRecoveryBatch { Id = Guid.NewGuid(), ActorId = actor, IdempotencyKey = key,
            ProtectedFingerprint = fingerprint, OccurredAt = now.ToUniversalTime(), CorrelationId = correlation,
            Total = lines.Count, Recovered = lines.Count(l => l.Outcome == "Recovered"), Ignored = lines.Count(l => l.Outcome != "Recovered") };
        batch.lines.AddRange(lines); return batch;
    }
    public override string ToString() => "[profile recovery receipt]";
}
