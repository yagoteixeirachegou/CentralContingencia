using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.Domain.Profiles;

public enum ProfileStatus { Active, Closed }

public sealed class OperationalProfile
{
    public Guid Id { get; private set; }
    public ProfileName Name { get; private set; } = null!;
    public string? Platform { get; private set; }
    public string? Platform1 { get; private set; }
    public string? Platform2 { get; private set; }
    public ProfileStatus Status { get; private set; } = ProfileStatus.Active;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    /// <summary>Id do perfil no AdsPower, informado pela integração ao confirmar a criação.</summary>
    public string? AdsPowerId { get; private set; }
    public string? AdsPowerSerialNumber { get; private set; }
    /// <summary>Lote de ingestão que criou o perfil; nulo para perfis cadastrados por pessoas ou recuperados.</summary>
    public Guid? IngestionBatchId { get; private set; }
    private OperationalProfile() { }
    private OperationalProfile(Guid id, ProfileName name, string? platform, DateTimeOffset now)
    { Id = id; Name = name; Platform = platform; CreatedAt = UpdatedAt = now.ToUniversalTime(); }
    public static OperationalProfile Create(Guid id, ProfileName name, string? platform, DateTimeOffset now, string? platform1 = null, string? platform2 = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identificador de perfil inválido.");
        ArgumentNullException.ThrowIfNull(name);
        if (platform is not null) ProxyRules.ValidateText(platform, 100);
        var first = ProfileAssignments.Validate(platform1);
        var second = ProfileAssignments.Validate(platform2);
        ProxyRules.ValidateTimestamp(now);
        return new(id, name, platform?.Trim(), now) { Platform1 = first, Platform2 = second };
    }
    public void MarkIngested(Guid batchId)
    {
        if (batchId == Guid.Empty) throw new ArgumentException("Lote de ingestão inválido.");
        if (IngestionBatchId is not null) throw new InvalidOperationException("Perfil já pertence a um lote de ingestão.");
        IngestionBatchId = batchId;
    }
    /// <summary>Vincula o perfil ao AdsPower. Repetir os mesmos valores é aceito (retorna false); trocar por outros é recusado.</summary>
    public bool LinkAdsPower(string adsPowerId, string? serialNumber, DateTimeOffset now)
    {
        if (adsPowerId is null || !AdsPowerIdPattern.IsMatch(adsPowerId)) throw new ArgumentException("Id do AdsPower inválido.");
        if (serialNumber is not null && !SerialPattern.IsMatch(serialNumber)) throw new ArgumentException("Número de série do AdsPower inválido.");
        ValidateTime(now);
        if (Status != ProfileStatus.Active) throw new InvalidOperationException("Perfil encerrado não pode ser vinculado ao AdsPower.");
        if (AdsPowerId is not null)
        {
            if (AdsPowerId == adsPowerId && (serialNumber is null || serialNumber == AdsPowerSerialNumber)) return false;
            throw new InvalidOperationException("Perfil já está vinculado a outro perfil do AdsPower.");
        }
        AdsPowerId = adsPowerId; AdsPowerSerialNumber = serialNumber; UpdatedAt = now.ToUniversalTime();
        return true;
    }
    private static readonly System.Text.RegularExpressions.Regex AdsPowerIdPattern = new("^[A-Za-z0-9_-]{3,64}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex SerialPattern = new("^[0-9]{1,20}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    internal void ValidateTime(DateTimeOffset now)
    {
        ProxyRules.ValidateTimestamp(now);
        if (now < UpdatedAt) throw new ArgumentException("Timestamp de perfil inválido.");
    }
    public void UpdateDetails(ProfileName name, string? platform, DateTimeOffset now)
        => UpdateDetails(name, platform, Platform1, Platform2, now);

    public void UpdateDetails(ProfileName name, string? platform, string? platform1, string? platform2, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (platform is not null) ProxyRules.ValidateText(platform, 100);
        var first = ProfileAssignments.Validate(platform1);
        var second = ProfileAssignments.Validate(platform2);
        ValidateTime(now);
        if (Status != ProfileStatus.Active) throw new InvalidOperationException("Perfil encerrado não pode ser alterado.");
        Name = name; Platform = platform?.Trim(); Platform1 = first; Platform2 = second; UpdatedAt = now.ToUniversalTime();
    }
    internal void Close(DateTimeOffset now)
    {
        ValidateTime(now);
        if (Status == ProfileStatus.Closed) return;
        Status = ProfileStatus.Closed; ClosedAt = UpdatedAt = now.ToUniversalTime();
    }
    public override string ToString() => $"OperationalProfile {Id}";
}
