using CentralContingencia.Domain.Profiles;

namespace CentralContingencia.Application.Warmup;

public sealed class HealthCheckService(IHealthCheckStore store, TimeProvider clock) : IHealthCheckService
{
    public const int MaxProfilesPerQuery = 200;

    public async Task<HealthCheckResponse> RecordAsync(Guid actorId, RecordHealthCheckRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.GetUtcNow();
        var existing = await store.ListByProfileAsync(request.AdsPowerProfileId ?? "", token);
        if (existing.Count >= ProfileHealthCheck.CycleLength) throw new HealthCheckException(HealthCheckFailure.Conflict);
        if (existing.Any(c => c.CheckedOn == DateOnly.FromDateTime(now.UtcDateTime))) throw new HealthCheckException(HealthCheckFailure.Conflict);
        ProfileHealthCheck check;
        try { check = ProfileHealthCheck.Create(actorId, request.AdsPowerProfileId ?? "", request.ProfileName ?? "", request.AdsPowerGroupId ?? "", existing.Count + 1, request.Outcome, now); }
        catch (ArgumentException) { throw new HealthCheckException(HealthCheckFailure.Validation); }
        await store.AddAsync(check, token);
        return ToResponse(check);
    }

    public async Task<HealthCheckHistory> HistoryAsync(string adsPowerProfileId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(adsPowerProfileId) || adsPowerProfileId.Length > 64) throw new HealthCheckException(HealthCheckFailure.Validation);
        var checks = (await store.ListByProfileAsync(adsPowerProfileId, token)).OrderBy(c => c.Day).ToList();
        return new(adsPowerProfileId, Progress(adsPowerProfileId, checks), checks.Select(ToResponse).ToList());
    }

    public async Task<IReadOnlyList<HealthCheckProgress>> ProgressAsync(IReadOnlyCollection<string> ids, CancellationToken token)
    {
        if (ids.Count is < 1 or > MaxProfilesPerQuery || ids.Any(i => string.IsNullOrWhiteSpace(i) || i.Length > 64)) throw new HealthCheckException(HealthCheckFailure.Validation);
        var distinct = ids.Distinct().ToList();
        var grouped = (await store.ListByProfilesAsync(distinct, token)).GroupBy(c => c.AdsPowerProfileId).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Day).ToList());
        return distinct.Select(id => Progress(id, grouped.GetValueOrDefault(id) ?? [])).ToList();
    }

    private static HealthCheckProgress Progress(string id, IReadOnlyList<ProfileHealthCheck> ordered)
    {
        var last = ordered.Count == 0 ? null : ordered[^1];
        return new(id, ordered.Count, ProfileHealthCheck.CycleLength, ordered.Count >= ProfileHealthCheck.CycleLength, ordered.Any(c => c.HasImpediment), last?.Outcome, last?.CheckedAt);
    }

    private static HealthCheckResponse ToResponse(ProfileHealthCheck c) =>
        new(c.Id, c.AdsPowerProfileId, c.ProfileName, c.AdsPowerGroupId, c.Day, c.Outcome, c.HasImpediment, c.CheckedAt, c.ActorId);
}
