using System.Text.RegularExpressions;

namespace CentralContingencia.Domain.WorkQueue;

/// <summary>Garante que erro de tarefa nunca carregue segredo, endereço de conexão ou texto livre longo.</summary>
public static partial class WorkErrorSanitizer
{
    public const int MaxMessageLength = 300;
    public const int MaxCodeLength = 64;

    [GeneratedRegex(@"(?i)\b(password|passwd|senha|token|secret|cookie|authorization|bearer|api[-_ ]?key|host|server|user(name| id)?|uid|pwd)\b\s*[:=]?\s*\S+")]
    private static partial Regex Labeled();
    [GeneratedRegex(@"[A-Za-z0-9+/_=\-]{24,}")]
    private static partial Regex Opaque();
    [GeneratedRegex(@"\S+@\S+")]
    private static partial Regex Email();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,63}$")]
    private static partial Regex CodePattern();

    public static string Code(string? code) => code is not null && CodePattern().IsMatch(code) ? code : "error";

    public static string Message(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "";
        var text = new string(message.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        text = Labeled().Replace(text, "[redacted]");
        text = Email().Replace(text, "[redacted]");
        text = Opaque().Replace(text, "[redacted]");
        text = Spaces().Replace(text, " ").Trim();
        return text.Length <= MaxMessageLength ? text : text[..MaxMessageLength];
    }
}
