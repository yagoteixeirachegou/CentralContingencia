using System.Globalization;
using System.Net;
using System.Text.Json;
using CentralContingencia.Application.AdsPower;
using CentralContingencia.Application.Ingestion;
using CentralContingencia.Domain.Profiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CentralContingencia.Infrastructure.AdsPower;

public sealed class AdsPowerConnectionOptions
{
    /// <summary>API local do AdsPower. Restrita a este computador até existir um conector seguro aprovado.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:50325";
    /// <summary>Chave da API local; vem de configuração protegida, nunca do repositório.</summary>
    public string? ApiKey { get; set; }
    public int RequestIntervalMs { get; set; } = 500;
    public int ProfilePageSize { get; set; } = 100;
    public int MaxPages { get; set; } = 500;
    public override string ToString() => "[adspower connection]";
}

/// <summary>Leitura somente de grupos e perfis. Lê apenas os campos listados em <see cref="AdsPowerProfile"/>.</summary>
public sealed class AdsPowerLocalApiClient(HttpClient http, IOptions<AdsPowerConnectionOptions> options) : IAdsPowerProfileSource, IAdsPowerGroupReader
{
    private static readonly string[] LocalHosts = ["127.0.0.1", "localhost", "local.adspower.net"];
    private readonly AdsPowerConnectionOptions settings = options.Value;

    public async Task<AdsPowerSnapshot> ReadAsync(CancellationToken token)
    {
        var baseUri = ResolveBase();
        var groups = new List<AdsPowerGroup>(); var profiles = new List<AdsPowerProfile>();
        var size = Math.Clamp(settings.ProfilePageSize, 1, 100);
        for (var page = 1; page <= settings.MaxPages; page++)
        {
            var list = await ListAsync(baseUri, "api/v1/group/list", page, 200, token, first: page == 1);
            groups.AddRange(list.Select(item => new AdsPowerGroup(Text(item, "group_id"), Text(item, "group_name"))));
            if (list.Count < 200) break;
        }
        for (var page = 1; page <= settings.MaxPages; page++)
        {
            var list = await ListAsync(baseUri, "api/v1/user/list", page, size, token, first: false);
            // Somente estes campos são lidos (inclui nomes de tags e data de criação); username, password, remark, proxy e demais ficam fora.
            profiles.AddRange(list.Select(item => new AdsPowerProfile(Text(item, "user_id"), NullIfEmpty(Text(item, "serial_number")),
                Text(item, "name"), Text(item, "group_id"), Text(item, "group_name"), TagNames(item), Instant(item, "created_time"))));
            if (list.Count < size) break;
        }
        return new(groups, profiles);
    }

    /// <summary>Maior "Perfil N" de UM grupo (nome exato). Só lê nome e id; nenhuma credencial é lida. Nome ambíguo ou ausente é recusado.</summary>
    public async Task<AdsPowerGroupSnapshot> ReadGroupAsync(string groupName, CancellationToken token)
    {
        var baseUri = ResolveBase(); var wanted = groupName.Trim();
        var matches = new List<AdsPowerGroup>();
        for (var page = 1; page <= settings.MaxPages; page++)
        {
            var list = await ListAsync(baseUri, "api/v1/group/list", page, 200, token, first: page == 1);
            matches.AddRange(list.Select(item => new AdsPowerGroup(Text(item, "group_id"), Text(item, "group_name")))
                .Where(group => group.Id.Length > 0 && string.Equals(group.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase)));
            if (list.Count < 200) break;
        }
        if (matches.Count == 0) throw new AdsPowerException(AdsPowerFailure.GroupNotFound);
        if (matches.Count > 1) throw new AdsPowerException(AdsPowerFailure.Failed); // dois grupos com o mesmo nome: não adivinhar
        var group = matches[0]; var size = Math.Clamp(settings.ProfilePageSize, 1, 100); var count = 0; var highest = 0;
        for (var page = 1; page <= settings.MaxPages; page++)
        {
            var list = await ListAsync(baseUri, "api/v1/user/list", page, size, token, first: false, filter: "group_id=" + Uri.EscapeDataString(group.Id));
            foreach (var item in list) { count++; if (ProfileNumber.TryParseLeading(Text(item, "name"), out var number) && number > highest) highest = number; }
            if (list.Count < size) break;
        }
        return new(group.Id, group.Name, count, highest);
    }

    private Uri ResolveBase()
    {
        if (!Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp ||
            !LocalHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)) throw new AdsPowerException(AdsPowerFailure.NotConfigured);
        return uri;
    }

    private async Task<List<JsonElement>> ListAsync(Uri baseUri, string path, int page, int pageSize, CancellationToken token, bool first, string? filter = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            // Respeita o limite de chamadas por segundo da API local.
            if (!first || attempt > 0) await Task.Delay(attempt > 0 ? Math.Max(1200, settings.RequestIntervalMs) : settings.RequestIntervalMs, token);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, $"{path}?page={page}&page_size={pageSize}{(filter is null ? "" : "&" + filter)}"));
            if (!string.IsNullOrWhiteSpace(settings.ApiKey)) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + settings.ApiKey);
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, token); }
            catch (HttpRequestException) { throw new AdsPowerException(AdsPowerFailure.Unreachable); }
            catch (TaskCanceledException) when (!token.IsCancellationRequested) { throw new AdsPowerException(AdsPowerFailure.Unreachable); }
            using (response)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new AdsPowerException(AdsPowerFailure.Unauthorized);
                if (!response.IsSuccessStatusCode) throw new AdsPowerException(AdsPowerFailure.Failed);
                JsonDocument document;
                try { document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token); }
                catch (JsonException) { throw new AdsPowerException(AdsPowerFailure.Failed); }
                using (document)
                {
                    var root = document.RootElement;
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("code", out var code) && code.TryGetInt32(out var value) && value == 0 &&
                        root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("list", out var items) && items.ValueKind == JsonValueKind.Array)
                        return items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object).Select(i => i.Clone()).ToList();
                    var message = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("msg", out var msg) && msg.ValueKind == JsonValueKind.String ? msg.GetString() ?? "" : "";
                    if (attempt == 0 && message.Contains("too many", StringComparison.OrdinalIgnoreCase)) continue;
                    var auth = new[] { "api key", "api-key", "api_key", "apikey", "unauthor", "token", "authoriz" }.Any(term => message.Contains(term, StringComparison.OrdinalIgnoreCase));
                    throw new AdsPowerException(auth ? AdsPowerFailure.Unauthorized : AdsPowerFailure.Failed);
                }
            }
        }
    }

    private static string Text(JsonElement item, string name) => item.TryGetProperty(name, out var value)
        ? value.ValueKind switch { JsonValueKind.String => value.GetString() ?? "", JsonValueKind.Number => value.GetRawText(), _ => "" } : "";
    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
    private static string[] TagNames(JsonElement item) => item.TryGetProperty("fbcc_user_tag", out var tags) && tags.ValueKind == JsonValueKind.Array
        ? tags.EnumerateArray().Where(tag => tag.ValueKind == JsonValueKind.Object).Select(tag => Text(tag, "name").Trim()).Where(name => name.Length > 0).ToArray() : [];
    private static DateTimeOffset? Instant(JsonElement item, string name) =>
        long.TryParse(Text(item, name), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds > 0 && seconds < 253402300799
            ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
}

/// <summary>Resolve o cliente HTTP (transitório) a cada leitura, para o serviço singleton do dashboard.</summary>
public sealed class ScopedAdsPowerSource(IServiceProvider provider) : IAdsPowerProfileSource
{
    public async Task<AdsPowerSnapshot> ReadAsync(CancellationToken token)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AdsPowerLocalApiClient>().ReadAsync(token);
    }
}
