using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.Application.Ingestion;

/// <summary>Credencial de serviço (por exemplo, o n8n). Não é uma conta de pessoa.</summary>
public sealed record IngestionActor(Guid Id, string Name);

public sealed record IngestionReserveRequest(int Count, string? Platform1);
public sealed record ConfirmIngestionRequest(string AdsPowerId, string? SerialNumber)
{
    public override string ToString() => "[confirm ingestion]";
}

/// <summary>Grupo do AdsPower onde os perfis são numerados e criados. <c>Mode</c> é "Test" ou "Production".</summary>
public sealed record IngestionGroup(string Id, string Name, string Mode);
public sealed record IngestionAvailability(int AvailableProxies, int NextProfileNumber, string NextProfileName, IngestionGroup Group);
/// <summary>Capacidade calculada pelo repositório (proxies livres e próximo número); o serviço junta o grupo.</summary>
public sealed record IngestionCapacity(int AvailableProxies, int NextProfileNumber);

/// <summary>Conexão da proxy reservada. Existe somente na resposta da reserva, para a credencial de serviço do lote.</summary>
public sealed record IngestionProxy(string Host, int Port, string Username, string Password, string Region, DateTimeOffset ExpiresAt)
{
    public override string ToString() => "[ingestion proxy]";
}
public sealed record IngestionItem(Guid AllocationId, Guid ProfileId, string ProfileName, int ProfileNumber, string Status,
    DateTimeOffset ReservationExpiresAt, IngestionProxy? Proxy);
/// <summary>Posição (1..N, na ordem do pedido) que não foi atendida e o motivo.</summary>
public sealed record IngestionPending(int Position, string Reason, string Message);
public sealed record IngestionReservation(Guid BatchId, bool Replayed, int Requested, int Reserved, string Status,
    IngestionGroup Group, IReadOnlyList<IngestionItem> Items, IReadOnlyList<IngestionPending> Pending);

/// <summary>
/// <c>NumberFloor</c> é o maior "Perfil N" encontrado no grupo do AdsPower no momento do pedido. O próximo número é o maior entre ele e
/// o que a própria Central já reservou ou confirmou nesse mesmo grupo, mais 1.
/// </summary>
public sealed record IngestionBatchRequest(Guid ActorId, Guid IdempotencyKey, int Count, string? Platform1, string GroupId, string GroupName, int NumberFloor, string CorrelationId);
public sealed record IngestionAllocation(OperationalProfile Profile, ProxyAllocation Allocation, Proxy Proxy);
public sealed record IngestionBatchResult(ProfileIngestionBatch Batch, bool Replayed, IReadOnlyList<IngestionAllocation> Allocations);

/// <summary>Resultado de liberar um lote: reservas desfeitas, perfis já confirmados (intocados) e os que já tinham terminado (liberados ou vencidos).</summary>
public sealed record IngestionBatchRelease(Guid BatchId, int Released, int Confirmed, int AlreadyEnded);

/// <summary>Fronteira de persistência da ingestão. Cada operação é atômica e serializada com as demais alocações.</summary>
public interface IIngestionStore
{
    Task<IngestionCapacity> CapacityAsync(string groupId, int numberFloor, CancellationToken token);
    Task<IngestionBatchResult> ReserveBatchAsync(IngestionBatchRequest request, CancellationToken token);
    Task ConfirmAsync(Guid allocationId, Guid actorId, string adsPowerId, string? serialNumber, string correlationId, CancellationToken token);
    Task ReleaseAsync(Guid allocationId, Guid actorId, string correlationId, CancellationToken token);
    Task<IngestionBatchRelease> ReleaseBatchAsync(Guid batchId, Guid actorId, string correlationId, CancellationToken token);
}

/// <summary>Leitura somente de um grupo do AdsPower, para numerar os perfis novos.</summary>
public sealed record AdsPowerGroupSnapshot(string GroupId, string GroupName, int ProfileCount, int HighestProfileNumber);
public interface IAdsPowerGroupReader
{
    /// <summary>Procura o grupo pelo nome exato (sem diferenciar maiúsculas) e devolve o maior "Perfil N" dele.
    /// Lança <see cref="CentralContingencia.Application.AdsPower.AdsPowerException"/> se o AdsPower estiver indisponível ou o grupo não existir.</summary>
    Task<AdsPowerGroupSnapshot> ReadGroupAsync(string groupName, CancellationToken token);
}

/// <summary>
/// Em "Test" a ingestão numera e cria perfis somente no grupo de testes; em "Production", somente na esteira de perfil.
/// O padrão é "Test": a produção exige configuração explícita.
/// </summary>
public sealed class ProfileIngestionOptions
{
    public const string Test = "Test", Production = "Production";
    public string Mode { get; set; } = Test;
    public string TestGroupName { get; set; } = "FB Teste | YAGO";
    public string ProductionGroupName { get; set; } = "03 ESTEIRA DE PERFIL";
    public string ActiveGroupName => Mode == Production ? ProductionGroupName : TestGroupName;

    public void Validate()
    {
        if (Mode is not (Test or Production)) throw new InvalidOperationException("ProfileIngestion:Mode deve ser \"Test\" ou \"Production\".");
        foreach (var name in new[] { TestGroupName, ProductionGroupName })
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200 || name.Any(char.IsControl)) throw new InvalidOperationException("Nome de grupo de ingestão inválido.");
        if (string.Equals(TestGroupName.Trim(), ProductionGroupName.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Os grupos de teste e de produção devem ser diferentes.");
    }
}

public interface IProfileIngestionService
{
    Task<IngestionAvailability> GetAvailabilityAsync(CancellationToken token);
    Task<IngestionReservation> ReserveAsync(IngestionActor actor, IngestionReserveRequest request, Guid idempotencyKey, string correlationId, CancellationToken token);
    Task ConfirmAsync(IngestionActor actor, Guid allocationId, ConfirmIngestionRequest request, string correlationId, CancellationToken token);
    Task ReleaseAsync(IngestionActor actor, Guid allocationId, string correlationId, CancellationToken token);
    Task<IngestionBatchRelease> ReleaseBatchAsync(IngestionActor actor, Guid batchId, string correlationId, CancellationToken token);
}
