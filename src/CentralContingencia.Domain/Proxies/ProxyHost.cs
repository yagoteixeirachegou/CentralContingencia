using System.Net;
using System.Net.Sockets;

namespace CentralContingencia.Domain.Proxies;

public sealed record ProxyHost
{
    public string Value { get; }
    private ProxyHost(string value) => Value = value;

    public static ProxyHost Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253 || value != value.Trim() || value.Contains('[') || value.Contains(']'))
            throw new ArgumentException("Host de proxy inválido.");
        if (IPAddress.TryParse(value, out var address))
        {
            // Não aceitar IPv4 abreviado, numérico, octal ou IPv6 com zone ID local.
            if (value.Contains('%') || (address.AddressFamily == AddressFamily.InterNetwork && value != address.ToString()))
                throw new ArgumentException("Host de proxy inválido.");
            return new ProxyHost(address.ToString());
        }
        if (value.All(c => char.IsAsciiDigit(c) || c == '.') || value.Contains(':'))
            throw new ArgumentException("Host de proxy inválido.");
        var labels = value.Split('.');
        if (labels.Any(label => label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-' ||
            label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            throw new ArgumentException("Host de proxy inválido.");
        return new ProxyHost(value.ToLowerInvariant());
    }

    public override string ToString() => "[proxy host]";
}
