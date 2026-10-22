using CentralContingencia.Domain.Profiles;

namespace CentralContingencia.Application.Warmup;

public sealed record RecordHealthCheckRequest(string AdsPowerProfileId, string ProfileName, string AdsPowerGroupId, HealthCheckOutcome Outcome);
public sealed record HealthCheckResponse(Guid Id, string AdsPowerProfileId, string ProfileName, string AdsPowerGroupId, int Day, HealthCheckOutcome Outcome,
    bool HasImpediment, DateTimeOffset CheckedAt, Guid ActorId);
public sealed record HealthCheckProgress(string AdsPowerProfileId, int CompletedDays, int CycleLength, bool Completed, bool HasImpediment, HealthCheckOutcome? LastOutcome, DateTimeOffset? LastCheckedAt);
public sealed record HealthCheckHistory(string AdsPowerProfileId, HealthCheckProgress Progress, IReadOnlyList<HealthCheckResponse> Checks);

public enum HealthCheckFailure { Validation, Conflict, Unavailable }
public sealed class HealthCheckException(HealthCheckFailure failure) : Exception("Operação de verificação indisponível.")
{
    public HealthCheckFailure Failure { get; } = failure;
}

public interface IHealthCheckStore
{
    Task<IReadOnlyList<ProfileHealthCheck>> ListByProfileAsync(string adsPowerProfileId, CancellationToken token);
    Task<IReadOnlyList<ProfileHealthCheck>> ListByProfilesAsync(IReadOnlyCollection<string> adsPowerProfileIds, CancellationToken token);
    /// <summary>Grava a verificação. Os índices únicos (perfil+dia da esteira, perfil+data UTC) são a garantia final contra duplicidade concorrente.</summary>
    Task AddAsync(ProfileHealthCheck check, CancellationToken token);
}

public interface IHealthCheckService
{
    Task<HealthCheckResponse> RecordAsync(Guid actorId, RecordHealthCheckRequest request, CancellationToken token);
    Task<HealthCheckHistory> HistoryAsync(string adsPowerProfileId, CancellationToken token);
    Task<IReadOnlyList<HealthCheckProgress>> ProgressAsync(IReadOnlyCollection<string> adsPowerProfileIds, CancellationToken token);
}
