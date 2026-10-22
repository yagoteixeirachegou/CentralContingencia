using CentralContingencia.Application.Allocations;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Profiles;

namespace CentralContingencia.Application.Ingestion;

public sealed class ProfileIngestionService(IIngestionStore store, Func<IProxyPasswordProtector> protector, IAdsPowerGroupReader groups,
    ProfileIngestionOptions options, TimeProvider clock) : IProfileIngestionService
{
    public const string NoProxyAvailable = "NoProxyAvailable";

    /// <summary>Lê o grupo do modo ativo no AdsPower. A numeração nunca usa perfis de outros grupos.</summary>
    private async Task<(AdsPowerGroupSnapshot Snapshot, IngestionGroup Group)> ReadGroupAsync(CancellationToken token)
    {
        options.Validate();
        var snapshot = await groups.ReadGroupAsync(options.ActiveGroupName, token);
        return (snapshot, new IngestionGroup(snapshot.GroupId, snapshot.GroupName, options.Mode));
    }

    public async Task<IngestionAvailability> GetAvailabilityAsync(CancellationToken token)
    {
        var (snapshot, group) = await ReadGroupAsync(token);
        var capacity = await store.CapacityAsync(snapshot.GroupId, snapshot.HighestProfileNumber, token);
        return new(capacity.AvailableProxies, capacity.NextProfileNumber, ProfileNumber.Format(capacity.NextProfileNumber), group);
    }

    public async Task<IngestionReservation> ReserveAsync(IngestionActor actor, IngestionReserveRequest request, Guid idempotencyKey, string correlationId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(actor); ArgumentNullException.ThrowIfNull(request);
        if (actor.Id == Guid.Empty || idempotencyKey == Guid.Empty || request.Count is < 1 or > ProfileIngestionBatch.MaxCount)
            throw new AllocationPersistenceException(AllocationPersistenceFailure.Validation);
        string? platform1;
        try { platform1 = ProfileAssignments.Validate(request.Platform1); }
        catch (ArgumentException) { throw new AllocationPersistenceException(AllocationPersistenceFailure.Validation); }
        // Falha cedo se a chave de criptografia não existir: nada é reservado sem poder entregar a conexão.
        IProxyPasswordProtector passwords;
        try { passwords = protector(); }
        catch (Exception) { throw new AllocationPersistenceException(AllocationPersistenceFailure.Unavailable); }

        // O AdsPower é lido antes de abrir a transação: não se segura bloqueio no banco esperando outro serviço.
        var (snapshot, group) = await ReadGroupAsync(token);
        var result = await store.ReserveBatchAsync(new(actor.Id, idempotencyKey, request.Count, platform1, snapshot.GroupId, snapshot.GroupName, snapshot.HighestProfileNumber, correlationId), token);
        var now = clock.GetUtcNow();
        var items = new List<IngestionItem>();
        foreach (var entry in result.Allocations)
        {
            ProfileNumber.TryParse(entry.Profile.Name.Value, out var number);
            // A conexão só sai enquanto a reserva está válida; depois de confirmada ou encerrada, não há mais motivo para expô-la.
            var deliver = entry.Allocation.Status == AllocationStatus.Reserved && entry.Allocation.ReservationExpiresAt > now;
            IngestionProxy? proxy = null;
            if (deliver)
            {
                try { proxy = new(entry.Proxy.Host.Value, entry.Proxy.Port.Value, entry.Proxy.Username, passwords.Decrypt(entry.Proxy.Id, entry.Proxy.Password), entry.Proxy.Region, entry.Proxy.ExpiresAt); }
                catch (Exception) { throw new AllocationPersistenceException(AllocationPersistenceFailure.Unavailable); }
            }
            var lapsed = entry.Allocation.Status == AllocationStatus.Reserved && entry.Allocation.ReservationExpiresAt <= now; // vencida, ainda não recuperada
            items.Add(new(entry.Allocation.Id, entry.Profile.Id, entry.Profile.Name.Value, number, lapsed ? nameof(AllocationStatus.Expired) : entry.Allocation.Status.ToString(), entry.Allocation.ReservationExpiresAt, proxy));
        }
        items = [.. items.OrderBy(item => item.ProfileNumber)];
        var requested = result.Batch.Requested;
        // Os atendidos ocupam as primeiras posições; as demais ficam pendentes por falta de proxy.
        var pending = Enumerable.Range(items.Count + 1, requested - items.Count)
            .Select(position => new IngestionPending(position, NoProxyAvailable, "Não há proxy disponível para este perfil.")).ToArray();
        var status = items.Count == 0 ? "Empty" : pending.Length == 0 ? "Complete" : "Partial";
        return new(result.Batch.Id, result.Replayed, requested, items.Count, status, group, items, pending);
    }

    public Task ConfirmAsync(IngestionActor actor, Guid allocationId, ConfirmIngestionRequest request, string correlationId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(actor); ArgumentNullException.ThrowIfNull(request);
        if (actor.Id == Guid.Empty || allocationId == Guid.Empty) throw new AllocationPersistenceException(AllocationPersistenceFailure.Validation);
        return store.ConfirmAsync(allocationId, actor.Id, request.AdsPowerId?.Trim() ?? "", string.IsNullOrWhiteSpace(request.SerialNumber) ? null : request.SerialNumber.Trim(), correlationId, token);
    }

    /// <summary>Desfaz todas as reservas ainda não confirmadas do lote (por exemplo, quando o fluxo falha antes de criar os perfis).</summary>
    public Task<IngestionBatchRelease> ReleaseBatchAsync(IngestionActor actor, Guid batchId, string correlationId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Id == Guid.Empty || batchId == Guid.Empty) throw new AllocationPersistenceException(AllocationPersistenceFailure.Validation);
        return store.ReleaseBatchAsync(batchId, actor.Id, correlationId, token);
    }

    public Task ReleaseAsync(IngestionActor actor, Guid allocationId, string correlationId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Id == Guid.Empty || allocationId == Guid.Empty) throw new AllocationPersistenceException(AllocationPersistenceFailure.Validation);
        return store.ReleaseAsync(allocationId, actor.Id, correlationId, token);
    }
}
