using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CentralContingencia.Application.Identity;
using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.Application.Proxies.Import;

public sealed class ProxyCsvPreviewService(IProxyImportConflictReader conflicts, CsvPreviewLimits limits, TimeProvider clock,
    IImportConfirmationProtector? confirmation = null) : IProxyCsvPreviewService
{
    private static readonly string[] Required = ["PERFIL", "PLATAFORMA", "PROXY", "PORTA", "USUARIO", "SENHA", "STATUS", "DATA DE VENCIMENTO"];
    private static readonly string[] Optional = ["REGIAO", "ADSPOWER LOTE"];
    public async Task<CsvPreviewResponse> PreviewAsync(ProxyActor actor, ReadOnlyMemory<byte> csv, int page, int pageSize, CancellationToken token, string? defaultRegion = null, bool skipRowsWithoutConnection = false, int? referenceYear = null)
    {
        using var analysis = await AnalyzeAsync(actor, csv, page, pageSize, token, defaultRegion, skipRowsWithoutConnection, referenceYear);
        if (confirmation is null) return analysis.Preview;
        var fingerprint = CsvImportFingerprint.Compute(csv, defaultRegion, skipRowsWithoutConnection, referenceYear);
        try
        {
            var expires = clock.GetUtcNow().AddMinutes(15);
            return analysis.Preview with { ConfirmationToken = confirmation.Issue(actor.Id, fingerprint, expires), ConfirmationExpiresAt = expires };
        }
        finally { CryptographicOperations.ZeroMemory(fingerprint); }
    }
    public async Task<CsvAnalysis> AnalyzeAsync(ProxyActor actor, ReadOnlyMemory<byte> csv, int page, int pageSize, CancellationToken token, string? defaultRegion = null, bool skipRowsWithoutConnection = false, int? referenceYear = null)
    {
        if (actor.Id == Guid.Empty || !actor.Roles.Contains(AccessRoles.Admin)) throw new CsvPreviewException(CsvPreviewFailure.Forbidden);
        if (page is < 1 or > 10000 || pageSize is < 1 or > 100) throw new CsvPreviewException(CsvPreviewFailure.InvalidPage);
        limits.Validate();
        CsvImportOptions.ValidateReferenceYear(referenceYear);
        if (defaultRegion is not null)
        {
            try { ProxyRules.ValidateRegion(defaultRegion); }
            catch (ArgumentException) { throw new CsvPreviewException(CsvPreviewFailure.InvalidRegion); }
        }
        var (delimiter, records) = BoundedCsvParser.Parse(csv, limits, token);
        var header = records[0]; var columns = new List<CsvColumn>(); var mapping = new Dictionary<string, int>();
        var headerIssues = new List<CsvIssue>();
        var names = header.Fields.Select(Normalize).ToArray();
        for (var i = 0; i < header.Fields.Length; i++)
        {
            var name = names[i]; var recognized = Required.Contains(name) || Optional.Contains(name);
            columns.Add(new(i + 1, recognized ? name : null));
            if (!recognized) headerIssues.Add(new(1, i + 1, "UnknownHeader", "Coluna desconhecida; conteúdo ignorado."));
            else mapping.TryAdd(name, i);
        }
        foreach (var group in names.Select((name, index) => (name, index)).GroupBy(item => item.name).Where(group => group.Count() > 1))
        {
            var positions = group.Select(item => item.index).ToArray();
            var populated = positions.Where(index => records.Skip(1).Any(row => index < row.Fields.Length && !string.IsNullOrWhiteSpace(row.Fields[index]))).ToArray();
            // Only recognized columns and structurally complete records can be resolved safely.
            var safe = (Required.Contains(group.Key) || Optional.Contains(group.Key)) && populated.Length <= 1 &&
                records.Skip(1).All(row => row.Fields.Length == header.Fields.Length);
            if (!safe) { headerIssues.Add(new(1, positions[1] + 1, "DuplicateHeader", "Cabeçalho duplicado ambíguo após normalização.")); continue; }
            var selected = populated.Length == 1 ? populated[0] : positions[0];
            mapping[group.Key] = selected;
            foreach (var ignored in positions.Where(index => index != selected))
            {
                columns[ignored] = new(ignored + 1, null);
                headerIssues.Add(new(1, ignored + 1, "IgnoredEmptyDuplicateHeader", "Coluna duplicada inteiramente vazia ignorada; posições originais preservadas."));
            }
        }
        foreach (var name in Required.Where(name => !mapping.ContainsKey(name)))
            headerIssues.Add(new(1, null, "MissingHeader", $"Cabeçalho obrigatório ausente: {name}."));
        if (!mapping.ContainsKey("REGIAO") && defaultRegion is null) headerIssues.Add(new(1, null, "RegionRequiredByModel", "REGIÃO é opcional no CSV, mas obrigatória no modelo; registros sem região serão inválidos."));
        headerIssues.Add(new(1, null, "UnmappedMetadata", "PERFIL, PLATAFORMA e ADsPower Lote não serão importados nem vinculados aos proxies."));
        var blockingHeaders = headerIssues.Any(i => i.Code is "DuplicateHeader" or "MissingHeader");
        var rows = new List<CsvPreviewRow>(); var endpoints = new Dictionary<int, ProxyEndpointKey>();
        var candidates = new List<CsvImportCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal); var hashKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            foreach (var record in records.Skip(1))
            {
                token.ThrowIfCancellationRequested();
                var errors = new List<CsvIssue>(); var warnings = new List<CsvIssue>();
                string Value(string name) => mapping.TryGetValue(name, out var index) && index < record.Fields.Length ? record.Fields[index] : "";
                var missingRegion = string.IsNullOrWhiteSpace(Value("REGIAO"));
                var region = missingRegion && defaultRegion is not null ? defaultRegion : Value("REGIAO");
                void Error(string name, string code, string message) => errors.Add(new(record.Line, mapping.TryGetValue(name, out var index) ? index + 1 : null, code, message));
                void Validate(string name, Action action, string message) { try { action(); } catch (ArgumentException) { Error(name, "InvalidField", message); } }
                var empty = record.Fields.All(string.IsNullOrWhiteSpace);
                var skipConnection = !empty && skipRowsWithoutConnection && !blockingHeaders && record.Fields.Length == header.Fields.Length &&
                    new[] { "PROXY", "PORTA", "USUARIO", "SENHA" }.All(name => string.IsNullOrWhiteSpace(Value(name)));
                if (empty) errors.Add(new(record.Line, null, "EmptyRow", "Linha vazia."));
                if (skipConnection)
                {
                    errors.Add(new(record.Line, null, "EmptyRow", "Linha sem dados de conexão ignorada por opção explícita."));
                    warnings.Add(new(record.Line, null, "NoConnectionRowSkipped", "Ignorada explicitamente: host, porta, usuário e senha estão vazios."));
                }
                if (blockingHeaders) errors.Add(new(record.Line, null, "InvalidHeaders", "Corrija os cabeçalhos antes de validar este registro."));
                if (record.Fields.Length != header.Fields.Length) errors.Add(new(record.Line, null, "ColumnCount", "Quantidade de campos diferente do cabeçalho."));
                ProxyHost? host = null; ProxyPort? port = null; ProxyStatus? status = null; DateTimeOffset? expiry = null; var duplicate = false;
                if (!empty && !skipConnection && !blockingHeaders && record.Fields.Length == header.Fields.Length)
                {
                    Validate("PROXY", () => host = ProxyHost.Create(Value("PROXY")), "Host inválido; informe DNS ou IP sem URL ou credenciais.");
                    Validate("PORTA", () => port = ProxyPort.Create(int.TryParse(Value("PORTA"), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0), "Porta deve estar entre 1 e 65535.");
                    Validate("USUARIO", () => ProxyRules.ValidateUsername(Value("USUARIO")), "Usuário obrigatório, sem controles e até 128 caracteres.");
                    Validate("SENHA", () => ProxyRules.ValidatePassword(Value("SENHA")), "Senha obrigatória e limitada a 1024 bytes UTF-8.");
                    Validate("REGIAO", () => ProxyRules.ValidateRegion(region), "Região obrigatória no modelo, sem controles e até 100 caracteres.");
                    if (missingRegion && defaultRegion is not null) warnings.Add(new(record.Line, null, "ExplicitDefaultRegion", "Região padrão fornecida explicitamente pelo administrador será utilizada."));
                    Validate("PERFIL", () => ProxyRules.ValidateText(Value("PERFIL"), 200), "Perfil obrigatório, sem controles e até 200 caracteres.");
                    Validate("PLATAFORMA", () => ProxyRules.ValidateText(Value("PLATAFORMA"), 100), "Plataforma obrigatória, sem controles e até 100 caracteres.");
                    if (Value("ADSPOWER LOTE").Length > 0) Validate("ADSPOWER LOTE", () => ProxyRules.ValidateText(Value("ADSPOWER LOTE"), 128), "Lote sem controles e até 128 caracteres.");
                    Validate("STATUS", () => status = ProxyInputRules.ParseStatus(MapStatus(Value("STATUS"))), "Status reconhecidos: Active/ATIVO, Suspended/SUSPENSO e Disabled/INATIVO/DESATIVADO.");
                    var incompleteDate = System.Text.RegularExpressions.Regex.IsMatch(Value("DATA DE VENCIMENTO"), @"\A[0-9]{2}/[0-9]{2}\z");
                    if (incompleteDate && referenceYear is null)
                        Error("DATA DE VENCIMENTO", "ExpiryYearRequired", "Informe ano de referência explicitamente ou use dd/MM/yyyy no CSV. Nenhum ano será inferido.");
                    else
                    {
                        Validate("DATA DE VENCIMENTO", () => expiry = ProxyInputRules.ValidateExpiry(ParseDate(Value("DATA DE VENCIMENTO"), referenceYear), clock.GetUtcNow()), "Vencimento futuro obrigatório e data válida; use dd/MM com ano de referência, dd/MM/yyyy, yyyy-MM-dd ou ISO 8601 com Z/offset.");
                        if (incompleteDate && expiry.HasValue) warnings.Add(new(record.Line, mapping["DATA DE VENCIMENTO"] + 1, "ReferenceYearApplied", "Ano de referência informado explicitamente aplicado à data sem ano; confira o vencimento interpretado."));
                    }
                    if (host is not null && port is not null)
                    {
                        endpoints[record.Line] = new(host.Value, port.Value);
                        if (!errors.Any(e => e.Column == mapping["USUARIO"] + 1))
                        {
                            var bytes = Encoding.UTF8.GetBytes($"{host.Value}\n{port.Value}\n{Value("USUARIO")}");
                            try { duplicate = !seen.Add(Convert.ToHexString(HMACSHA256.HashData(hashKey, bytes))); }
                            finally { CryptographicOperations.ZeroMemory(bytes); }
                            if (duplicate) Error("PROXY", "DuplicateRecord", "Endpoint e usuário repetidos no arquivo; PERFIL não define identidade do proxy.");
                        }
                    }
                    if (record.Fields.Where((_, index) => columns[index].Field is not null && columns[index].Field is not ("SENHA" or "USUARIO"))
                        .Any(v => v.TrimStart().StartsWith('=') || v.TrimStart().StartsWith('+') || v.TrimStart().StartsWith('-') || v.TrimStart().StartsWith('@')))
                        warnings.Add(new(record.Line, null, "FormulaLikeText", "Texto semelhante a fórmula tratado apenas como texto; revisar antes de futura exportação."));
                }
                rows.Add(new(record.Line, record.EndLine, host is null ? null : "***", port?.Value, "***", "***", status?.ToString(), expiry,
                    Value("PERFIL").Length > 0, Value("PLATAFORMA").Length > 0, !missingRegion, Value("ADSPOWER LOTE").Length > 0,
                    errors.Count == 0, duplicate, false, errors, warnings));
                if (errors.Count == 0) candidates.Add(new(record.Line, host!, port!, Value("USUARIO"), Value("SENHA"), region, status!.Value, expiry!.Value));
                // Libera referencias aos campos, incluindo senhas; strings gerenciadas não garantem apagamento.
                Array.Clear(record.Fields);
            }
        }
        catch { foreach (var candidate in candidates) candidate.Dispose(); throw; }
        finally { CryptographicOperations.ZeroMemory(hashKey); foreach (var record in records) Array.Clear(record.Fields); }
        try
        {
        var existing = await conflicts.FindExistingAsync(endpoints.Values.Distinct().ToArray(), token);
        for (var i = 0; i < rows.Count; i++)
            if (endpoints.TryGetValue(rows[i].Line, out var endpoint) && existing.Contains(endpoint))
                rows[i] = rows[i] with { ExistingConflict = true, Warnings = rows[i].Warnings.Append(new(rows[i].Line, null, "ExistingEndpoint", "Host e porta já cadastrados; possível conflito, não prova identidade de usuário ou perfil.")).ToArray() };
        var summary = new CsvPreviewSummary(rows.Count, rows.Count(r => r.Valid), rows.Count(r => !r.Valid), rows.Count(r => r.Duplicate),
            rows.Count(r => r.Errors.Any(e => e.Code == "EmptyRow")), rows.Count(r => r.ExistingConflict),
            rows.Sum(r => r.Warnings.Count) + headerIssues.Count(i => i.Code is not ("MissingHeader" or "DuplicateHeader")));
        var preview = new CsvPreviewResponse(delimiter.ToString(), columns, headerIssues, summary, page, pageSize, rows.Skip((page - 1) * pageSize).Take(pageSize).ToArray());
        return new(preview, rows, candidates);
        }
        catch { foreach (var candidate in candidates) candidate.Dispose(); throw; }
    }
    private static string Normalize(string value) => string.Join(' ', new string(value.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
        .ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string MapStatus(string value) => Normalize(value) switch
    {
        "ATIVO" or "ACTIVE" => "Active", "SUSPENSO" or "SUSPENDED" => "Suspended",
        "INATIVO" or "DESATIVADO" or "DISABLED" => "Disabled", _ => ""
    };
    private static DateTimeOffset? ParseDate(string value, int? referenceYear)
    {
        if (referenceYear.HasValue && System.Text.RegularExpressions.Regex.IsMatch(value, @"\A[0-9]{2}/[0-9]{2}\z"))
            value += "/" + referenceYear.Value.ToString("D4", CultureInfo.InvariantCulture);
        if (DateTime.TryParseExact(value, ["dd/MM/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc));
        // Hora sem timezone é ambígua; offsets precisam ser explícitos.
        if (!(value.EndsWith('Z') || (value.Length >= 6 && value[^3] == ':' && value[^6] is '+' or '-'))) return null;
        return DateTimeOffset.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp) ? timestamp : null;
    }
}
