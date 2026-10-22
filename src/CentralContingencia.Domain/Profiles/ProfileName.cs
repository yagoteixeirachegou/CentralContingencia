namespace CentralContingencia.Domain.Profiles;

public sealed record ProfileName
{
    public string Value { get; }
    private ProfileName(string value) => Value = value;
    public static ProfileName Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Any(char.IsControl))
            throw new ArgumentException("Nome de perfil inválido.");
        return new(value.Trim());
    }
    public override string ToString() => "[profile name]";
}
