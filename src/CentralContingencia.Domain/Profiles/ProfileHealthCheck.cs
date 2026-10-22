using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.Domain.Profiles;

public enum HealthCheckOutcome { Ok, Captcha, VerificationRequested, LoginRequired, IdentityConfirmation, Checkpoint, AccountRestriction, NavigationError }

/// <summary>
/// Resultado de UMA verificação de impedimentos em um perfil do AdsPower (nome comercial: Aquecimento).
/// A verificação é somente leitura: abre o perfil, carrega uma página, lê o estado e registra o resultado.
/// Cada perfil tem no máximo uma verificação por dia UTC e <see cref="CycleLength"/> verificações por esteira.
/// </summary>
public sealed class ProfileHealthCheck
{
    public const int CycleLength = 7;
    public Guid Id { get; private set; }
    public Guid ActorId { get; private set; }
    public string AdsPowerProfileId { get; private set; } = "";
    public string ProfileName { get; private set; } = "";
    public string AdsPowerGroupId { get; private set; } = "";
    public int Day { get; private set; }
    public HealthCheckOutcome Outcome { get; private set; }
    public DateTimeOffset CheckedAt { get; private set; }
    public DateOnly CheckedOn { get; private set; }
    private ProfileHealthCheck() { }
    public bool HasImpediment => Outcome != HealthCheckOutcome.Ok;
    public static ProfileHealthCheck Create(Guid actorId, string adsPowerProfileId, string profileName, string adsPowerGroupId, int day, HealthCheckOutcome outcome, DateTimeOffset now)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("Ator inválido.");
        if (day is < 1 or > CycleLength) throw new ArgumentException($"O dia da esteira deve estar entre 1 e {CycleLength}.");
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        ProxyRules.ValidateText(adsPowerProfileId, 64); ProxyRules.ValidateText(profileName, 200); ProxyRules.ValidateText(adsPowerGroupId, 64);
        ProxyRules.ValidateTimestamp(now);
        var utc = now.ToUniversalTime();
        return new() { Id = Guid.NewGuid(), ActorId = actorId, AdsPowerProfileId = adsPowerProfileId, ProfileName = profileName, AdsPowerGroupId = adsPowerGroupId,
            Day = day, Outcome = outcome, CheckedAt = utc, CheckedOn = DateOnly.FromDateTime(utc.UtcDateTime) };
    }
    public override string ToString() => $"ProfileHealthCheck {Id}";
}
