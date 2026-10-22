using System.Text;

namespace CentralContingencia.Application.Proxies.Import;

internal sealed class CsvRecord(int line, int endLine, string[] fields)
{
    public int Line { get; } = line;
    public int EndLine { get; } = endLine;
    public string[] Fields { get; } = fields;
    public override string ToString() => "[CSV record]";
}
internal static class BoundedCsvParser
{
    public static (char Delimiter, List<CsvRecord> Records) Parse(ReadOnlyMemory<byte> csv, CsvPreviewLimits limits, CancellationToken token)
    {
        if (csv.Length > limits.MaxFileBytes) throw new CsvPreviewException(CsvPreviewFailure.LimitExceeded);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(csv.Span); }
        catch (DecoderFallbackException) { throw new CsvPreviewException(CsvPreviewFailure.InvalidFile); }
        if (text.StartsWith('\uFEFF')) text = text[1..];
        if (string.IsNullOrWhiteSpace(text)) throw new CsvPreviewException(CsvPreviewFailure.InvalidFile);
        var delimiter = DetectDelimiter(text);
        var records = new List<CsvRecord>();
        var fields = new List<string>(); var field = new StringBuilder();
        var line = 1; var startLine = 1; var quoted = false; var closed = false; var started = false;
        for (var i = 0; i < text.Length; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            var c = text[i];
            if (c == '\0') throw new CsvPreviewException(CsvPreviewFailure.InvalidFile, line);
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else if (c is '\r' or '\n') { NewLine(ref i); field.Append('\n'); }
                else field.Append(c);
            }
            else if (c == delimiter) { FinishField(); started = true; }
            else if (c is '\r' or '\n') { FinishRecord(); NewLine(ref i); startLine = line; }
            else if (c == '"' && field.Length == 0 && !closed) { quoted = true; started = true; }
            else
            {
                if (closed || c == '"') throw new CsvPreviewException(CsvPreviewFailure.InvalidFile, line);
                field.Append(c); started = true;
            }
            if (field.Length > limits.MaxFieldCharacters) throw new CsvPreviewException(CsvPreviewFailure.LimitExceeded, line);
        }
        if (quoted) throw new CsvPreviewException(CsvPreviewFailure.InvalidFile, startLine);
        if (started || field.Length > 0 || fields.Count > 0 || closed) FinishRecord();
        return (delimiter, records);

        void NewLine(ref int index)
        {
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
            line++;
            if (line > limits.MaxPhysicalLines && index + 1 < text.Length) throw new CsvPreviewException(CsvPreviewFailure.LimitExceeded, line);
        }
        void FinishField()
        {
            fields.Add(field.ToString()); field.Clear(); closed = false;
            if (fields.Count > limits.MaxColumns) throw new CsvPreviewException(CsvPreviewFailure.LimitExceeded, line);
        }
        void FinishRecord()
        {
            FinishField(); records.Add(new(startLine, line, fields.ToArray())); fields.Clear(); started = false;
            if (records.Count > limits.MaxRows + 1) throw new CsvPreviewException(CsvPreviewFailure.LimitExceeded, startLine);
        }
    }
    private static char DetectDelimiter(string text)
    {
        var quoted = false; var commas = 0; var semicolons = 0;
        foreach (var c in text)
        {
            if (c == '"') quoted = !quoted;
            if (!quoted && c is '\r' or '\n') break;
            if (!quoted && c == ',') commas++;
            if (!quoted && c == ';') semicolons++;
        }
        if (commas > 0 && semicolons > 0) throw new CsvPreviewException(CsvPreviewFailure.InvalidFile);
        return commas > 0 ? ',' : ';';
    }
}
