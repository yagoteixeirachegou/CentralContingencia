using System.Globalization;
using System.Text;
using CentralContingencia.Application.Proxies.Import;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.Application.Profiles.Recovery;

internal sealed record RecoveryDocument(List<RecoveryCandidate> Candidates, List<RecoveryRow> Rows, List<CsvIssue> HeaderIssues);
internal static class RecoveryCsvReader
{
    public static RecoveryDocument Read(ReadOnlyMemory<byte> csv, bool skip, CsvPreviewLimits limits, CancellationToken token)
    {
        var (_, records) = BoundedCsvParser.Parse(csv, limits, token);
        try
        {
            var names = records[0].Fields.Select(Normalize).ToArray();
            var mapping = new Dictionary<string, int>(); var issues = new List<CsvIssue>();
            var supported = new[] { "PERFIL", "PLATAFORMA", "PLATAFORMA 1", "PLATAFORMA 2", "PROXY", "PORTA", "USUARIO", "SENHA", "STATUS", "DATA DE VENCIMENTO", "DATA DE VALIDADE", "REGIAO", "ADSPOWER LOTE" };
            foreach (var group in names.Select((name, i) => (name, i)).GroupBy(x => x.name))
            {
                var indexes = group.Select(x => x.i).ToArray();
                if (group.Key == "PLATAFORMA" && indexes.Length <= 2 && !names.Contains("PLATAFORMA 1") && !names.Contains("PLATAFORMA 2"))
                {
                    mapping["PLATAFORMA 1"] = indexes[0];
                    if (indexes.Length == 2) mapping["PLATAFORMA 2"] = indexes[1];
                    issues.Add(new(1, null, "PlatformSlotsByPosition", "PLATAFORMA original: primeira ocorrência é atribuição 1, segunda é atribuição 2; vazios permanecem ausentes."));
                }
                else if (indexes.Length > 1 || group.Key == "PLATAFORMA") issues.Add(new(1, null, "DuplicateHeader", "Cabeçalhos duplicados ou combinação de plataformas ambígua."));
                else if (supported.Contains(group.Key)) mapping[group.Key] = indexes[0];
                else issues.Add(new(1, indexes[0] + 1, "UnknownHeader", "Coluna desconhecida ignorada."));
            }
            foreach (var required in new[] { "PERFIL", "PROXY", "PORTA" })
                if (!mapping.ContainsKey(required)) issues.Add(new(1, null, "MissingHeader", "Ausente: " + required));
            issues.Add(new(1, null, "ExistingProxyAuthoritative", "Somente perfil e atribuições serão recuperados. Status, validade, região e credenciais existentes não serão alterados; lote não será persistido."));
            issues.Add(new(1, null, "CurrentAssociationOnly", "Confirmação registra associação atual no momento da recuperação; não reconstrói datas históricas da planilha."));
            var blocked = issues.Any(i => i.Code is "MissingHeader" or "DuplicateHeader");
            var rows = new List<RecoveryRow>(); var candidates = new List<RecoveryCandidate>();
            foreach (var record in records.Skip(1))
            {
                token.ThrowIfCancellationRequested();
                string Value(string field) => mapping.TryGetValue(field, out var index) && index < record.Fields.Length ? record.Fields[index] : "";
                RecoveryRow Row(string outcome, string? name = null, string? first = null, string? second = null, string? code = null) => new(record.Line, outcome, null, name, first, second, null, null,
                    code is null ? [] : [new(record.Line, null, code, Message(code))]);
                if (blocked || record.Fields.Length != names.Length) { rows.Add(Row("Invalid", code: "InvalidStructure")); continue; }
                var empty = record.Fields.All(string.IsNullOrWhiteSpace);
                var noConnection = new[] { "PROXY", "PORTA", "USUARIO", "SENHA" }.All(f => string.IsNullOrWhiteSpace(Value(f)));
                if (empty || skip && noConnection) { rows.Add(Row("Ignored", code: "NoConnectionSkipped")); continue; }
                if (string.IsNullOrWhiteSpace(Value("PERFIL")))
                {
                    rows.Add(Row(string.IsNullOrWhiteSpace(Value("PLATAFORMA 1")) && string.IsNullOrWhiteSpace(Value("PLATAFORMA 2")) ? "Ignored" : "Invalid", code: "ProfileAbsent")); continue;
                }
                try
                {
                    var name = ProfileName.Create(Value("PERFIL")).Value;
                    var host = ProxyHost.Create(Value("PROXY")).Value;
                    var port = ProxyPort.Create(int.TryParse(Value("PORTA"), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).Value;
                    string? Slot(string field) => string.IsNullOrWhiteSpace(Value(field)) ? null : ProfileAssignments.Validate(Value(field));
                    var first = Slot("PLATAFORMA 1"); var second = Slot("PLATAFORMA 2");
                    candidates.Add(new(record.Line, host, port, name, first, second)); rows.Add(Row("Proposed", name, first, second));
                }
                catch (ArgumentException) { rows.Add(Row("Invalid", code: "InvalidMetadataOrEndpoint")); }
            }
            return new(candidates, rows, issues);
        }
        finally { foreach (var record in records) Array.Clear(record.Fields); }
    }
    internal static string Normalize(string value) => string.Join(' ', new string(value.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
        .ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string Message(string code) => code switch
    {
        "NoConnectionSkipped" => "Linha vazia ou sem conexão ignorada explicitamente.",
        "ProfileAbsent" => "Perfil ausente; não criar vínculo. Atribuições sem perfil são inválidas.",
        "InvalidStructure" => "Estrutura inválida; confira cabeçalhos e quantidade de colunas.",
        _ => "Perfil, atribuição, host ou porta inválidos; revise os campos sem enviar credenciais na resposta."
    };
}
