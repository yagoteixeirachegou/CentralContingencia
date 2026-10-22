using System.Security.Cryptography;
using System.Text.Json;
using CentralContingencia.Application.Identity;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Application.Proxies.Import;
using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Profiles;

namespace CentralContingencia.Application.Profiles.Recovery;

public sealed class ProfileRecoveryService(IProfileRecoveryStore store, IImportConfirmationProtector protector,
    CsvPreviewLimits limits, TimeProvider clock) : IProfileRecoveryService
{
    public async Task<RecoveryPreview> PreviewAsync(ProxyActor actor, ReadOnlyMemory<byte> csv, bool skip, int page, int pageSize, CancellationToken token)
    {
        Authorize(actor); limits.Validate();
        if (page is < 1 or > 10000 || pageSize is < 1 or > 100) throw new CsvPreviewException(CsvPreviewFailure.InvalidPage);
        var document = RecoveryCsvReader.Read(csv, skip, limits, token);
        var snapshot = await store.ReadAsync(document.Candidates, token);
        var rows = Evaluate(document, snapshot, clock.GetUtcNow());
        var expires = clock.GetUtcNow().AddMinutes(15); var hash = PlanFingerprint(csv, skip, rows, snapshot);
        try { return new(Summary(rows), document.HeaderIssues, page, pageSize, rows.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
            rows.Any(r => r.Outcome is "Invalid" or "Conflict") || rows.Count == 0 ? null : protector.Issue(actor.Id, hash, expires), expires); }
        finally { CryptographicOperations.ZeroMemory(hash); }
    }
    public async Task<RecoveryReport> CommitAsync(ProxyActor actor, ReadOnlyMemory<byte> csv, bool skip, string proof, Guid key, string correlation, CancellationToken token)
    {
        Authorize(actor); limits.Validate();
        if (key == Guid.Empty || proof is null || proof.Length > 4096) throw new RecoveryException(RecoveryFailure.Confirmation);
        if (csv.Length > limits.MaxFileBytes) throw new CsvPreviewException(CsvPreviewFailure.LimitExceeded);
        var fileHash = FileFingerprint(csv, skip);
        try
        {
            await using var transaction = await store.BeginAsync(token);
            var previous = await transaction.FindAsync(actor.Id, key, token);
            if (previous is not null)
            {
                if (!protector.MatchesFingerprint(previous.ProtectedFingerprint, fileHash)) throw new RecoveryException(RecoveryFailure.IdempotencyConflict);
                return Report(previous, true);
            }
            var document = RecoveryCsvReader.Read(csv, skip, limits, token);
            var snapshot = await transaction.ReadAsync(document.Candidates, token);
            var rows = Evaluate(document, snapshot, clock.GetUtcNow());
            var hash = PlanFingerprint(csv, skip, rows, snapshot);
            try { if (!protector.Validate(proof, actor.Id, hash, clock.GetUtcNow())) throw new RecoveryException(RecoveryFailure.Confirmation); }
            finally { CryptographicOperations.ZeroMemory(hash); }
            if (rows.Count == 0 || rows.Any(r => r.Outcome is "Invalid" or "Conflict")) throw new RecoveryException(RecoveryFailure.Validation);
            var lines = rows.Select(r => new ProfileRecoveryLine(r.Line, r.Outcome == "Proposed" ? "Recovered" : r.Outcome,
                r.ProxyId, r.Outcome == "Proposed" ? Guid.NewGuid() : r.ProfileId, r.Outcome == "Proposed" ? Guid.NewGuid() : r.AllocationId)).ToArray();
            var batch = ProfileRecoveryBatch.Create(actor.Id, key, protector.ProtectFingerprint(fileHash), clock.GetUtcNow(), correlation, lines);
            await transaction.SaveAsync(batch, document.Candidates, token); return Report(batch, false);
        }
        finally { CryptographicOperations.ZeroMemory(fileHash); }
    }
    private static void Authorize(ProxyActor actor)
    { if (actor.Id == Guid.Empty || !actor.Roles.Contains(AccessRoles.Admin)) throw new RecoveryException(RecoveryFailure.Forbidden); }
    private static List<RecoveryRow> Evaluate(RecoveryDocument document, RecoverySnapshot snapshot, DateTimeOffset now)
    {
        var rows = document.Rows.ToDictionary(r => r.Line);
        var duplicateNames = document.Candidates.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).SelectMany(g => g).Select(c => c.Line).ToHashSet();
        var duplicateEndpoints = document.Candidates.GroupBy(c => (c.Host, c.Port)).Where(g => g.Count() > 1).SelectMany(g => g).Select(c => c.Line).ToHashSet();
        foreach (var candidate in document.Candidates)
        {
            var row = rows[candidate.Line];
            void Conflict(string code, string message) => row = row with { Outcome = "Conflict", Issues = [new(row.Line, null, code, message)] };
            var matches = snapshot.Proxies.Where(p => p.Host.Value == candidate.Host && p.Port.Value == candidate.Port).ToArray();
            if (matches.Length == 1) row = row with { ProxyId = matches[0].Id };
            if (duplicateNames.Contains(row.Line)) Conflict("DuplicateProfile", "Perfil repetido no CSV; não unir perfis automaticamente.");
            else if (duplicateEndpoints.Contains(row.Line)) Conflict("DuplicateProxy", "Mesmo endpoint repetido no CSV; revise os vínculos propostos.");
            else if (matches.Length != 1) Conflict(matches.Length == 0 ? "ProxyNotFound" : "AmbiguousProxy", "Proxy não encontrada ou endpoint corresponde a múltiplas proxies; conciliação explícita necessária.");
            else
            {
                var proxy = matches[0];
                var active = snapshot.Allocations.Where(a => a.ProxyId == proxy.Id && (a.Status == AllocationStatus.Confirmed || a.Status == AllocationStatus.Reserved && a.ReservationExpiresAt > now)).ToArray();
                var profiles = snapshot.Profiles.Where(p => string.Equals(p.Name.Value, candidate.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (active.Length == 1 && active[0].Status == AllocationStatus.Confirmed && profiles.Length == 1 && profiles[0].Id == active[0].ProfileId &&
                    profiles[0].Status == ProfileStatus.Active && profiles[0].Name.Value == candidate.Name && profiles[0].Platform1 == candidate.Platform1 && profiles[0].Platform2 == candidate.Platform2)
                    row = row with { Outcome = "AlreadyLinked", ProfileId = profiles[0].Id, AllocationId = active[0].Id,
                        Issues = [new(row.Line, null, "AlreadyLinked", "Vínculo confirmado e atribuições já correspondem; nenhuma alteração será feita.")] };
                else if (active.Length > 0) Conflict("ActiveAllocation", "Proxy possui reserva ou vínculo ativo; não será sobrescrito.");
                else if (profiles.Length > 0) Conflict("ExistingProfile", "Nome já cadastrado; nome não comprova identidade. Não reutilizar nem alterar automaticamente.");
                else if (!proxy.IsAvailable(now, false)) Conflict("UnavailableProxy", "Proxy vencida, bloqueada ou indisponível; nenhuma nova alocação permitida.");
            }
            rows[candidate.Line] = row;
        }
        return rows.Values.OrderBy(r => r.Line).ToList();
    }
    private static RecoverySummary Summary(IReadOnlyCollection<RecoveryRow> rows) => new(rows.Count, rows.Count(r => r.ProxyId.HasValue),
        rows.Where(r => r.ProfileName is not null).Select(r => r.ProfileName!).Distinct(StringComparer.OrdinalIgnoreCase).Count(), rows.Where(r => r.Outcome == "Proposed").Sum(r => (r.Platform1 is null ? 0 : 1) + (r.Platform2 is null ? 0 : 1)),
        rows.Count(r => r.Outcome is "Ignored" or "AlreadyLinked"), rows.Count(r => r.Outcome == "Invalid"), rows.Count(r => r.Outcome == "Conflict"), rows.Count(r => r.Outcome == "Proposed"));
    private static RecoveryReport Report(ProfileRecoveryBatch batch, bool replay) => new(batch.Id, replay, batch.Total, batch.Recovered, batch.Ignored,
        batch.Lines.OrderBy(l => l.Line).Select(l => new RecoveryResult(l.Line, l.Outcome, l.ProxyId, l.ProfileId, l.AllocationId)).ToArray());
    private static byte[] FileFingerprint(ReadOnlyMemory<byte> csv, bool skip)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("CentralContingencia.ProfileRecovery.File.v1"u8); hash.AppendData([skip ? (byte)1 : (byte)0]); hash.AppendData(csv.Span); return hash.GetHashAndReset();
    }
    private static byte[] PlanFingerprint(ReadOnlyMemory<byte> csv, bool skip, IReadOnlyList<RecoveryRow> rows, RecoverySnapshot snapshot)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var file = FileFingerprint(csv, skip);
        var plan = JsonSerializer.SerializeToUtf8Bytes(new { rows,
            proxies = snapshot.Proxies.OrderBy(p => p.Id).Select(p => new { p.Id, p.Status, p.UpdatedAt, p.ExpiresAt }),
            profiles = snapshot.Profiles.OrderBy(p => p.Id).Select(p => new { p.Id, p.Status, p.UpdatedAt }),
            allocations = snapshot.Allocations.OrderBy(a => a.Id).Select(a => new { a.Id, a.Status, a.UpdatedAt, a.ReservationExpiresAt }) });
        try { hash.AppendData("CentralContingencia.ProfileRecovery.Plan.v1"u8); hash.AppendData(file); hash.AppendData(plan); return hash.GetHashAndReset(); }
        finally { CryptographicOperations.ZeroMemory(file); CryptographicOperations.ZeroMemory(plan); }
    }
}
