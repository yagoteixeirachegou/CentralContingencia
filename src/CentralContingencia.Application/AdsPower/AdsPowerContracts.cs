namespace CentralContingencia.Application.AdsPower;

// Somente os campos necessários ao dashboard. Credenciais, observações, proxy e cookies do perfil
// nunca entram nestes tipos: o conector descarta tudo o que não estiver listado aqui.
public sealed record AdsPowerGroup(string Id, string Name);
public sealed record AdsPowerProfile(string UserId, string? SerialNumber, string Name, string GroupId, string GroupName,
    IReadOnlyList<string> Tags, DateTimeOffset? CreatedAt)
{
    public override string ToString() => "[adspower profile]";
}
public sealed record AdsPowerSnapshot(IReadOnlyList<AdsPowerGroup> Groups, IReadOnlyList<AdsPowerProfile> Profiles);

public enum AdsPowerFailure { NotConfigured, Unreachable, Unauthorized, Failed, GroupNotFound }
public sealed class AdsPowerException(AdsPowerFailure failure) : Exception("AdsPower indisponível.")
{
    public AdsPowerFailure Failure { get; } = failure;
}

public interface IAdsPowerProfileSource
{
    Task<AdsPowerSnapshot> ReadAsync(CancellationToken token);
}

/// <summary>A API do AdsPower não informa a data de criação do grupo: usa-se o perfil mais antigo e o mais recente dele.</summary>
public sealed record AdsPowerGroupSummary(string GroupId, string GroupName, int Total, DateTimeOffset? OldestCreatedAt, DateTimeOffset? NewestCreatedAt);
public sealed record AdsPowerTagSummary(string Name, int Total);
public sealed record AdsPowerProfileSummary(string ProfileId, string? SerialNumber, string Name, string GroupId, string GroupName,
    IReadOnlyList<string> Tags, DateTimeOffset? CreatedAt);
public sealed record AdsPowerDashboard(DateTimeOffset GeneratedAt, int Total, IReadOnlyList<AdsPowerGroupSummary> Groups,
    IReadOnlyList<AdsPowerTagSummary> Tags, IReadOnlyList<AdsPowerProfileSummary> Profiles, bool FromCache);

public interface IAdsPowerDashboardService
{
    Task<AdsPowerDashboard> GetAsync(bool refresh, CancellationToken token);
}

public sealed class AdsPowerOptions
{
    public int CacheMinutes { get; set; } = 5;

    public void Validate()
    {
        if (CacheMinutes is < 0 or > 1440) throw new InvalidOperationException("AdsPower:CacheMinutes deve estar entre 0 e 1440.");
    }
}
