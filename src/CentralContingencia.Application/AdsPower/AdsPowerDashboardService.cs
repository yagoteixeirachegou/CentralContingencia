namespace CentralContingencia.Application.AdsPower;

/// <summary>Agrega perfis do AdsPower por grupo e tag e mantém o último resultado em memória (sem persistir perfis).</summary>
public sealed class AdsPowerDashboardService(IAdsPowerProfileSource source, AdsPowerOptions options, TimeProvider clock) : IAdsPowerDashboardService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private AdsPowerDashboard? cached;

    public async Task<AdsPowerDashboard> GetAsync(bool refresh, CancellationToken token)
    {
        options.Validate();
        await gate.WaitAsync(token);
        try
        {
            var now = clock.GetUtcNow();
            if (!refresh && cached is { } current && now - current.GeneratedAt < TimeSpan.FromMinutes(options.CacheMinutes))
                return current with { FromCache = true };
            cached = Build(await source.ReadAsync(token), now);
            return cached;
        }
        finally { gate.Release(); }
    }

    private static AdsPowerDashboard Build(AdsPowerSnapshot snapshot, DateTimeOffset now)
    {
        var names = snapshot.Groups.GroupBy(g => g.Id).ToDictionary(g => g.Key, g => g.First().Name);
        foreach (var profile in snapshot.Profiles) names.TryAdd(profile.GroupId, profile.GroupName);
        // Tags com a mesma grafia (sem diferenciar maiúsculas) viram uma só, com o nome da primeira ocorrência.
        var canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var profiles = snapshot.Profiles.Select(p => new AdsPowerProfileSummary(p.UserId, p.SerialNumber, p.Name, p.GroupId,
            names.GetValueOrDefault(p.GroupId, p.GroupName),
            p.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).Select(t => canonical.TryAdd(t, t) ? t : canonical[t])
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), p.CreatedAt)).ToArray();
        var byGroup = profiles.ToLookup(p => p.GroupId);
        var groups = names.Select(group =>
            {
                var created = byGroup[group.Key].Where(p => p.CreatedAt is not null).Select(p => p.CreatedAt!.Value).ToArray();
                return new AdsPowerGroupSummary(group.Key, group.Value, byGroup[group.Key].Count(),
                    created.Length > 0 ? created.Min() : null, created.Length > 0 ? created.Max() : null);
            })
            .OrderByDescending(g => g.Total).ThenBy(g => g.GroupName, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var tags = profiles.SelectMany(p => p.Tags).GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AdsPowerTagSummary(canonical[g.Key], g.Count()))
            .OrderByDescending(t => t.Total).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        return new(now, profiles.Length, groups, tags, profiles, false);
    }
}
