namespace CentralContingencia.Domain.Proxies;

public sealed record ProxyPort
{
    public int Value { get; }
    private ProxyPort(int value) => Value = value;
    public static ProxyPort Create(int value) => value is >= 1 and <= 65535
        ? new(value) : throw new ArgumentOutOfRangeException(nameof(value), "Porta de proxy inválida.");
}
