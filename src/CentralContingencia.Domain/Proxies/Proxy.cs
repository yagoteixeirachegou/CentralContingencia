namespace CentralContingencia.Domain.Proxies;

public enum ProxyStatus { Active, Suspended, Disabled }

public sealed class Proxy
{
    public Guid Id { get; private set; }
    public ProxyHost Host { get; private set; } = null!;
    public ProxyPort Port { get; private set; } = null!;
    public string Username { get; private set; } = "";
    public EncryptedProxyPassword Password { get; private set; } = null!;
    public ProxyStatus Status { get; private set; }
    public string Region { get; private set; } = "";
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Proxy() { }
    public static Proxy Create(Guid id, ProxyHost host, ProxyPort port, string username,
        EncryptedProxyPassword password, string region, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identificador de proxy inválido.");
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(password);
        ProxyRules.ValidateUsername(username);
        ProxyRules.ValidateRegion(region);
        ProxyRules.ValidateTimestamp(now);
        ProxyRules.ValidateTimestamp(expiresAt);
        return new() { Id = id, Host = host, Port = port, Username = username, Password = password,
            Region = region, Status = ProxyStatus.Disabled, ExpiresAt = expiresAt.ToUniversalTime(),
            CreatedAt = now.ToUniversalTime(), UpdatedAt = now.ToUniversalTime() };
    }

    public void ChangeStatus(ProxyStatus status, DateTimeOffset now)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentException("Estado operacional inválido.");
        ValidateTime(now);
        Status = status;
        UpdatedAt = now.ToUniversalTime();
    }

    public void UpdateDetails(ProxyHost host, ProxyPort port, string username, string region,
        DateTimeOffset expiresAt, ProxyStatus status, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(port);
        ProxyRules.ValidateUsername(username);
        ProxyRules.ValidateRegion(region);
        ProxyRules.ValidateTimestamp(expiresAt);
        ValidateTime(now);
        if (!Enum.IsDefined(status)) throw new ArgumentException("Estado operacional inválido.");
        Host = host; Port = port; Username = username; Region = region;
        ExpiresAt = expiresAt.ToUniversalTime(); Status = status; UpdatedAt = now.ToUniversalTime();
    }

    // Suporta futura rotação sem alterar o identificador vinculado ao ciphertext.
    public void ReplaceEncryptedPassword(EncryptedProxyPassword password, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(password);
        ValidateTime(now);
        Password = password;
        UpdatedAt = now.ToUniversalTime();
    }

    // Ausência de alocação deve ser verificada pelo futuro caso de uso/transação.
    public bool IsAvailable(DateTimeOffset now, bool hasActiveAllocation) =>
        Status == ProxyStatus.Active && now >= CreatedAt && now < ExpiresAt && !hasActiveAllocation;

    private void ValidateTime(DateTimeOffset now)
    {
        ProxyRules.ValidateTimestamp(now);
        if (now < UpdatedAt) throw new ArgumentException("Timestamp de alteração inválido.");
    }
    public override string ToString() => $"Proxy {Id}";
}
