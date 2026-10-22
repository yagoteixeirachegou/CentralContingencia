using System.Text.RegularExpressions;

namespace CentralContingencia.Domain.Profiles;

/// <summary>Numeração operacional "Perfil N". Só nomes nesse formato participam do cálculo do próximo número.</summary>
public static class ProfileNumber
{
    /// <summary>Mesmo padrão usado pela consulta de persistência (sem diferenciar maiúsculas); até 9 dígitos.</summary>
    public const string Pattern = "^Perfil +([0-9]{1,9})$";
    /// <summary>Leitura tolerante, usada só para descobrir o maior número já usado no AdsPower: aceita sufixos como "Perfil 1250 (revisar)".
    /// Errar para mais é seguro (pula um número); errar para menos poderia repetir um nome.</summary>
    public const string LeadingPattern = "^Perfil +([0-9]{1,9})(?![0-9])";
    private static readonly Regex Matcher = new(Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex LeadingMatcher = new(LeadingPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static bool TryParseLeading(string? name, out int number)
    {
        number = 0;
        if (name is null) return false;
        var match = LeadingMatcher.Match(name.Trim());
        return match.Success && int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number);
    }

    public static bool TryParse(string? name, out int number)
    {
        number = 0;
        if (name is null) return false;
        var match = Matcher.Match(name.Trim());
        return match.Success && int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number);
    }

    public static string Format(int number)
    {
        if (number < 1 || number > 999_999_999) throw new ArgumentOutOfRangeException(nameof(number));
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Perfil {number}");
    }
}
