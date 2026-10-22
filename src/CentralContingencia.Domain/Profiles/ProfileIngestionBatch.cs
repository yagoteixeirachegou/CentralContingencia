using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.Domain.Profiles;

/// <summary>Pedido de ingestão de perfis feito por uma integração. Registra o pedido para tornar a repetição idempotente.</summary>
public sealed class ProfileIngestionBatch
{
    public const int MaxCount = 50;
    public Guid Id { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public int Requested { get; private set; }
    public string? Platform1 { get; private set; }
    /// <summary>Grupo do AdsPower onde os perfis do lote são numerados e criados (a numeração não cruza grupos).</summary>
    public string? AdsPowerGroupId { get; private set; }
    public string? AdsPowerGroupName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    private ProfileIngestionBatch() { }
    public static ProfileIngestionBatch Create(Guid id, Guid actorId, Guid idempotencyKey, int requested, string? platform1, DateTimeOffset now, string? adsPowerGroupId = null, string? adsPowerGroupName = null)
    {
        if (id == Guid.Empty || actorId == Guid.Empty || idempotencyKey == Guid.Empty) throw new ArgumentException("Identificadores de ingestão inválidos.");
        if (requested is < 1 or > MaxCount) throw new ArgumentException($"A quantidade deve estar entre 1 e {MaxCount}.");
        ProxyRules.ValidateTimestamp(now);
        if (adsPowerGroupId is not null) ProxyRules.ValidateText(adsPowerGroupId, 64);
        if (adsPowerGroupName is not null) ProxyRules.ValidateText(adsPowerGroupName, 200);
        return new() { Id = id, ActorId = actorId, IdempotencyKey = idempotencyKey, Requested = requested, Platform1 = ProfileAssignments.Validate(platform1), AdsPowerGroupId = adsPowerGroupId, AdsPowerGroupName = adsPowerGroupName, CreatedAt = now.ToUniversalTime() };
    }
    public override string ToString() => $"ProfileIngestionBatch {Id}";
}
