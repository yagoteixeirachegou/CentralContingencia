using System.Security.Cryptography;
using CentralContingencia.Application.Identity;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Domain.Audit;

namespace CentralContingencia.Application.Proxies.Import;

public sealed class ProxyCsvImportService(ProxyCsvPreviewService analyzer, IProxyImportStore store,
    IImportConfirmationProtector confirmation, Func<IProxyPasswordProtector> encryption, TimeProvider clock, CsvPreviewLimits limits) : IProxyCsvImportService
{
    public async Task<CsvImportReport> CommitAsync(ProxyActor actor, ReadOnlyMemory<byte> csv, string proof, Guid idempotencyKey,
        string? defaultRegion, string correlationId, CancellationToken token, bool skipRowsWithoutConnection = false, int? referenceYear = null)
    {
        if (actor.Id == Guid.Empty || !actor.Roles.Contains(AccessRoles.Admin)) throw new CsvPreviewException(CsvPreviewFailure.Forbidden);
        if (csv.Length > limits.MaxFileBytes) throw new CsvPreviewException(CsvPreviewFailure.LimitExceeded);
        CsvImportOptions.ValidateReferenceYear(referenceYear);
        if (defaultRegion is not null)
        {
            try { ProxyRules.ValidateRegion(defaultRegion); }
            catch (ArgumentException) { throw new CsvPreviewException(CsvPreviewFailure.InvalidRegion); }
        }
        if (idempotencyKey == Guid.Empty || string.IsNullOrWhiteSpace(proof) || proof.Length > 4096) throw new CsvImportException(CsvImportFailure.Confirmation);
        var fingerprint = CsvImportFingerprint.Compute(csv, defaultRegion, skipRowsWithoutConnection, referenceYear);
        try
        {
            // Prova sempre validada, inclusive no replay; prova expirada exige nova prévia.
            if (!confirmation.Validate(proof, actor.Id, fingerprint, clock.GetUtcNow())) throw new CsvImportException(CsvImportFailure.Confirmation);
            await using var transaction = await store.BeginAsync(token);
            var previous = await transaction.FindAsync(actor.Id, idempotencyKey, token);
            if (previous is not null)
            {
                if (!confirmation.MatchesFingerprint(previous.ProtectedFingerprint, fingerprint)) throw new CsvImportException(CsvImportFailure.IdempotencyConflict);
                return Report(previous, true);
            }
            using var analysis = await analyzer.AnalyzeAsync(actor, csv, 1, 100, token, defaultRegion, skipRowsWithoutConnection, referenceYear);
            var lines = analysis.Rows.Select(row => new ProxyImportLine(row.Line, null,
                row.Errors.Any(e => e.Code == "EmptyRow") ? ProxyImportOutcome.Empty : row.Errors.Any(e => e.Code != "DuplicateRecord") ? ProxyImportOutcome.Invalid : row.Duplicate ? ProxyImportOutcome.Duplicate :
                !row.Valid ? ProxyImportOutcome.Invalid : row.ExistingConflict ? ProxyImportOutcome.Conflict : ProxyImportOutcome.NotImported)).ToList();
            var invalidHeaders = analysis.Preview.HeaderIssues.Any(e => e.Code is "MissingHeader" or "DuplicateHeader");
            if (invalidHeaders || lines.Count == 0 || lines.Any(l => l.Outcome == ProxyImportOutcome.Invalid))
                throw new CsvImportException(CsvImportFailure.Validation, Report(null, false, lines));
            var now = clock.GetUtcNow(); var proxies = new List<Proxy>(); var events = new List<ProxyAuditEvent>();
            var lineIndexes = lines.Select((line, index) => (line.Line, index)).ToDictionary(item=>item.Line,item=>item.index);
            foreach (var candidate in analysis.Candidates)
            {
                token.ThrowIfCancellationRequested();
                var index = lineIndexes[candidate.Line];
                if (lines[index].Outcome == ProxyImportOutcome.Conflict) continue;
                try { ProxyInputRules.ValidateExpiry(candidate.ExpiresAt, now); }
                catch (ArgumentException)
                {
                    lines[index] = new(candidate.Line, null, ProxyImportOutcome.Invalid);
                    throw new CsvImportException(CsvImportFailure.Validation, Report(null, false, lines));
                }
                var id = Guid.NewGuid(); EncryptedProxyPassword password;
                try { password = encryption().Encrypt(id, candidate.Password); }
                catch (Exception error) when (error is InvalidOperationException or CryptographicException)
                { throw new CsvImportException(CsvImportFailure.EncryptionUnavailable); }
                var proxy = Proxy.Create(id, candidate.Host, candidate.Port, candidate.Username, password, candidate.Region, candidate.ExpiresAt, now);
                proxy.ChangeStatus(candidate.Status, now); proxies.Add(proxy);
                events.Add(new(actor.Id, id, ProxyAuditAction.Created, now, correlationId));
                lines[index] = new(candidate.Line, id, ProxyImportOutcome.Imported);
            }
            var batch = ProxyImportBatch.Create(actor.Id, idempotencyKey, confirmation.ProtectFingerprint(fingerprint), now, correlationId, lines);
            await transaction.SaveAsync(batch, proxies, events, token);
            return Report(batch, false);
        }
        finally { CryptographicOperations.ZeroMemory(fingerprint); }
    }
    private static CsvImportReport Report(ProxyImportBatch batch, bool replayed) => new(batch.Id, replayed, batch.Total,
        batch.Imported, batch.Ignored, batch.Invalid, batch.Conflicting, batch.Lines.OrderBy(l=>l.Line).Select(l => new CsvImportItem(l.Line, l.ProxyId, l.Outcome.ToString())).ToArray());
    private static CsvImportReport Report(Guid? id, bool replayed, IReadOnlyList<ProxyImportLine> lines) => new(id, replayed, lines.Count,
        0, lines.Count(l => l.Outcome is ProxyImportOutcome.Empty or ProxyImportOutcome.Duplicate or ProxyImportOutcome.NotImported or ProxyImportOutcome.Imported),
        lines.Count(l => l.Outcome == ProxyImportOutcome.Invalid), lines.Count(l => l.Outcome == ProxyImportOutcome.Conflict),
        lines.Select(l => new CsvImportItem(l.Line, null, (l.Outcome == ProxyImportOutcome.Imported ? ProxyImportOutcome.NotImported : l.Outcome).ToString())).ToArray());
}
