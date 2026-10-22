using CentralContingencia.Application.Proxies;
using CentralContingencia.Application.Proxies.Import;
using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.Application.Profiles.Recovery;

public sealed record RecoveryCandidate(int Line, string Host, int Port, string Name, string? Platform1, string? Platform2)
{ public override string ToString() => "[recovery candidate]"; }
public sealed record RecoveryRow(int Line, string Outcome, Guid? ProxyId, string? ProfileName,
    string? Platform1, string? Platform2, Guid? ProfileId, Guid? AllocationId, IReadOnlyList<CsvIssue> Issues)
{
    public IReadOnlyList<string> Operations => Outcome == "Proposed" ? ["CreateProfile", "CreateConfirmedAllocation"] : [];
}
public sealed record RecoverySummary(int Total, int ProxiesFound, int ProfilesIdentified, int AssignmentsRecoverable,
    int Ignored, int Invalid, int Conflicts, int Proposed);
public sealed record RecoveryPreview(RecoverySummary Summary, IReadOnlyList<CsvIssue> HeaderIssues,
    int Page, int PageSize, IReadOnlyList<RecoveryRow> Items, string? ConfirmationToken, DateTimeOffset? ConfirmationExpiresAt);
public sealed record RecoveryReport(Guid RecoveryId, bool Replayed, int Total, int Recovered, int Ignored,
    IReadOnlyList<RecoveryResult> Items);
public sealed record RecoveryResult(int Line, string Outcome, Guid? ProxyId, Guid? ProfileId, Guid? AllocationId);
public sealed record RecoverySnapshot(IReadOnlyList<Proxy> Proxies, IReadOnlyList<OperationalProfile> Profiles, IReadOnlyList<ProxyAllocation> Allocations);
public interface IProfileRecoveryTransaction : IAsyncDisposable
{
    Task<ProfileRecoveryBatch?> FindAsync(Guid actor, Guid key, CancellationToken token);
    Task<RecoverySnapshot> ReadAsync(IReadOnlyCollection<RecoveryCandidate> candidates, CancellationToken token);
    Task SaveAsync(ProfileRecoveryBatch batch, IReadOnlyList<RecoveryCandidate> candidates, CancellationToken token);
}
public interface IProfileRecoveryStore
{
    Task<RecoverySnapshot> ReadAsync(IReadOnlyCollection<RecoveryCandidate> candidates, CancellationToken token);
    Task<IProfileRecoveryTransaction> BeginAsync(CancellationToken token);
}
public interface IProfileRecoveryService
{
    Task<RecoveryPreview> PreviewAsync(ProxyActor actor, ReadOnlyMemory<byte> csv, bool skipRowsWithoutConnection, int page, int pageSize, CancellationToken token);
    Task<RecoveryReport> CommitAsync(ProxyActor actor, ReadOnlyMemory<byte> csv, bool skipRowsWithoutConnection, string proof,
        Guid key, string correlation, CancellationToken token);
}
public enum RecoveryFailure { Forbidden, Confirmation, IdempotencyConflict, Validation, Conflict, Persistence }
public sealed class RecoveryException(RecoveryFailure failure) : Exception("Recuperação recusada.")
{ public RecoveryFailure Failure { get; } = failure; }
