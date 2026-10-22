using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.Application.Proxies;

public static class ProxyInputRules
{
    public static ProxyStatus ParseStatus(string value) => value switch
    {
        "Active" => ProxyStatus.Active, "Suspended" => ProxyStatus.Suspended, "Disabled" => ProxyStatus.Disabled,
        _ => throw new ArgumentException("Estado inválido.")
    };
    public static DateTimeOffset ValidateExpiry(DateTimeOffset? value, DateTimeOffset now)
    {
        if (value is null || value <= now) throw new ArgumentException("Vencimento inválido.");
        ProxyRules.ValidateTimestamp(value.Value);
        return value.Value.ToUniversalTime();
    }
}
