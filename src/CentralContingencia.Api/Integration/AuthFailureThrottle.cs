using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace CentralContingencia.Api.Integration;

/// <summary>
/// Freio para tentativas de autenticação por credencial de serviço INVÁLIDAS (canal dos executores e chave do n8n).
/// A autorização responde 401 antes do middleware de rate limiting, então o limitador comum não vê essas tentativas; este freio as conta por escopo e cliente
/// e, ao exceder o limite na janela, responde 429 SEM consultar o banco. O cliente é o <c>RemoteIpAddress</c> já corrigido pelo UseForwardedHeaders, ou seja,
/// o IP real somente quando a conexão veio de um proxy confiável configurado (um X-Forwarded-For forjado por origem não confiável é ignorado).
/// Memória limitada; contagem por processo (com várias réplicas cada uma conta separado).
/// </summary>
public sealed class AuthFailureThrottle(TimeProvider clock, int limit, TimeSpan window)
{
    public const int MaxEntries = 50_000;
    private readonly ConcurrentDictionary<string, (DateTimeOffset Start, int Count)> entries = new();

    public int Limit { get; } = limit >= 1 ? limit : throw new ArgumentOutOfRangeException(nameof(limit));
    public TimeSpan Window { get; } = window >= TimeSpan.FromSeconds(1) ? window : throw new ArgumentOutOfRangeException(nameof(window));

    /// <summary>Tempo restante de bloqueio, ou null se o cliente pode tentar.</summary>
    public TimeSpan? BlockedFor(string scope, IPAddress? address)
    {
        var key = Key(scope, address);
        if (!entries.TryGetValue(key, out var entry)) return null;
        var left = entry.Start + Window - clock.GetUtcNow();
        if (left <= TimeSpan.Zero) { entries.TryRemove(new KeyValuePair<string, (DateTimeOffset, int)>(key, entry)); return null; }
        return entry.Count >= Limit ? left : null;
    }

    public void RecordFailure(string scope, IPAddress? address)
    {
        var now = clock.GetUtcNow();
        if (entries.Count >= MaxEntries) Purge(now);
        entries.AddOrUpdate(Key(scope, address), _ => (now, 1), (_, entry) => now - entry.Start >= Window ? (now, 1) : (entry.Start, entry.Count + 1));
    }

    private void Purge(DateTimeOffset now)
    {
        foreach (var pair in entries) if (now - pair.Value.Start >= Window) entries.TryRemove(pair);
        // Ainda cheio (ataque distribuído): descarta o excedente mais antigo em vez de crescer sem limite.
        if (entries.Count >= MaxEntries)
            foreach (var pair in entries.OrderBy(p => p.Value.Start).Take(entries.Count - MaxEntries + 1000)) entries.TryRemove(pair);
    }

    // IPv6 é agrupado por /64 (um cliente controla o prefixo inteiro); IPv4-mapeado vira IPv4.
    internal static string Key(string scope, IPAddress? address)
    {
        if (address is null) return scope + ":unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return scope + ":" + new IPAddress(bytes.Take(8).Concat(new byte[8]).ToArray()) + "/64";
        }
        return scope + ":" + address;
    }
}
