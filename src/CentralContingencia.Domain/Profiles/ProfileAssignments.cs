namespace CentralContingencia.Domain.Profiles;

// Stable labels shared by the two independent optional slots; no proxy semantics.
public static class ProfileAssignments
{
    public static IReadOnlyList<string> Catalog { get; } = Array.AsReadOnly(new[]
    {
        "DARKPOST", "MODERAÇÃO", "ANUNCIANTE", "HOST PÁGINAS", "FLAG", "BACKUP PIXEL",
        "ESTOURADO", "RESTRITA", "BUGADO", "ATIVO (LIVRE)", "ESTEIRA DE PERFIL",
        "MINERAÇÃO", "FLAG (LIVRE)", "TIKTOK", "BM", "X1", "GOOGLE ADS", "INFOPRODUTO"
    });

    public static string? Validate(string? value)
    {
        if (value is null) return null;
        var normalized = value.Trim();
        if (!Catalog.Contains(normalized, StringComparer.Ordinal))
            throw new ArgumentException("Atribuição de perfil inválida.");
        return normalized;
    }
}
