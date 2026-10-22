using CentralContingencia.Application.Allocations;
using CentralContingencia.Application.Ingestion;
using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Audit;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CentralContingencia.Infrastructure.Allocations;

/// <summary>
/// Ingestão de perfis por uma integração. Toda mutação roda em uma transação que serializa com as demais alocações
/// (mesmo bloqueio de tabela do <see cref="EfAllocationStore"/>); os índices únicos parciais continuam sendo a garantia final.
/// </summary>
public sealed class EfIngestionStore(DbContextOptions<CentralContingenciaDbContext> options, TimeProvider clock, ReservationOptions reservation) : IIngestionStore
{
    // Principal reservado do sistema para expirações; não é conta de pessoa nem credencial.
    private static readonly Guid ExpiryActor = Guid.Parse("00000000-0000-0000-0000-000000000001");
    // Maior "Perfil N" que a PRÓPRIA Central já reservou ou confirmou na ingestão DESTE grupo do AdsPower. Cobre o intervalo entre a reserva
    // e o aparecimento do perfil no AdsPower. Perfis de outros grupos, de teste ou recuperados da planilha nunca entram na conta; os abandonados
    // (reserva liberada ou vencida sem confirmação) também não, e o número volta a ser usado, como na planilha.
    private const string MaxNumberSql = "SELECT COALESCE(MAX((regexp_match(p.\"Name\", {0}, 'i'))[1]::int), 0) AS \"Value\" FROM profiles.\"OperationalProfiles\" p " +
        "JOIN profiles.\"IngestionBatches\" b ON b.\"Id\" = p.\"IngestionBatchId\" WHERE b.\"AdsPowerGroupId\" = {1} " +
        "AND (p.\"Status\" = 'Active' OR EXISTS (SELECT 1 FROM allocations.\"ProxyAllocations\" a WHERE a.\"ProfileId\" = p.\"Id\" AND a.\"ConfirmedAt\" IS NOT NULL))";
    private readonly AllocationDomainService domain = new();

    private static IQueryable<Proxy> FreeProxies(CentralContingenciaDbContext context, DateTimeOffset now) =>
        context.Proxies.Where(p => p.Status == ProxyStatus.Active && p.CreatedAt <= now && now < p.ExpiresAt &&
            !context.ProxyAllocations.Any(a => a.ProxyId == p.Id && (a.Status == AllocationStatus.Confirmed ||
                (a.Status == AllocationStatus.Reserved && a.ReservationExpiresAt > now))));

    // Próximo número = maior entre o piso lido do AdsPower e o já usado pela Central neste grupo, mais 1.
    private static async Task<int> NextNumberAsync(CentralContingenciaDbContext context, string groupId, int floor, CancellationToken token) =>
        Math.Max(floor, await context.Database.SqlQueryRaw<int>(MaxNumberSql, ProfileNumber.Pattern, groupId).SingleAsync(token)) + 1;

    public async Task<IngestionCapacity> CapacityAsync(string groupId, int numberFloor, CancellationToken token)
    {
        await using var context = new CentralContingenciaDbContext(options);
        try
        {
            var available = await FreeProxies(context, clock.GetUtcNow()).CountAsync(token);
            return new(available, await NextNumberAsync(context, groupId, numberFloor, token));
        }
        catch (NpgsqlException error) { throw AllocationPersistenceErrors.Translate(error); }
    }

    public Task<IngestionBatchResult> ReserveBatchAsync(IngestionBatchRequest request, CancellationToken token) => Execute(async context =>
    {
        var existing = await context.ProfileIngestionBatches.SingleOrDefaultAsync(b => b.ActorId == request.ActorId && b.IdempotencyKey == request.IdempotencyKey, token);
        if (existing is not null)
        {
            // Mesma chave com outro pedido é um erro do cliente; o mesmo pedido devolve o resultado original.
            if (existing.Requested != request.Count || existing.Platform1 != request.Platform1 || existing.AdsPowerGroupId != request.GroupId) throw new AllocationPersistenceException(AllocationPersistenceFailure.Conflict);
            return new IngestionBatchResult(existing, true, await LoadBatchAsync(context, existing.Id, token));
        }
        var now = clock.GetUtcNow();
        await RecoverExpiredAsync(context, now, token);
        // Libera os índices únicos parciais antes de inserir; ambos os saves compartilham a transação.
        await context.SaveChangesAsync(token);
        var batch = AllocationPersistenceErrors.Domain(() => ProfileIngestionBatch.Create(Guid.NewGuid(), request.ActorId, request.IdempotencyKey, request.Count, request.Platform1, now, request.GroupId, request.GroupName));
        context.ProfileIngestionBatches.Add(batch);

        // Ordem da planilha original (linha do CSV importado); proxies sem linha vêm depois, por data de cadastro.
        var ids = await FreeProxies(context, now)
            .Select(p => new { p.Id, p.CreatedAt, Line = context.Set<ProxyImportLine>().Where(l => l.ProxyId == p.Id).Min(l => (int?)l.Line) })
            .OrderBy(x => x.Line == null).ThenBy(x => x.Line).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => x.Id).Take(request.Count).ToListAsync(token);
        var created = new List<IngestionAllocation>();
        if (ids.Count > 0)
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM proxies.\"Proxies\" WHERE \"Id\" = ANY({ids.ToArray()}) FOR UPDATE", token);
            var proxies = await context.Proxies.Where(p => ids.Contains(p.Id)).ToListAsync(token);
            var history = await context.ProxyAllocations.Where(a => ids.Contains(a.ProxyId)).ToListAsync(token);
            var next = await NextNumberAsync(context, request.GroupId, request.NumberFloor, token);
            foreach (var proxy in ids.Select(id => proxies.Single(p => p.Id == id)))
            {
                var profile = AllocationPersistenceErrors.Domain(() =>
                {
                    var fresh = OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create(ProfileNumber.Format(next++)), null, now, batch.Platform1);
                    fresh.MarkIngested(batch.Id);
                    return fresh;
                });
                var deadline = reservation.Deadline(now, proxy.ExpiresAt, null);
                var allocation = AllocationPersistenceErrors.Domain(() => domain.Reserve(Guid.NewGuid(), profile, proxy, request.ActorId, Guid.NewGuid(), now, deadline,
                    history.Where(a => a.ProxyId == proxy.Id).ToArray()));
                context.OperationalProfiles.Add(profile); context.ProxyAllocations.Add(allocation);
                context.OperationalAuditEvents.Add(new(request.ActorId, profile.Id, null, OperationalAction.ProfileCreated, now, request.CorrelationId));
                context.OperationalAuditEvents.Add(new(request.ActorId, profile.Id, allocation.Id, OperationalAction.AllocationReserved, now, request.CorrelationId));
                created.Add(new(profile, allocation, proxy));
            }
        }
        return new IngestionBatchResult(batch, false, created);
    }, token);

    public async Task ConfirmAsync(Guid allocationId, Guid actorId, string adsPowerId, string? serialNumber, string correlationId, CancellationToken token)
    {
        var accepted = await Execute(async context =>
        {
            var identity = await context.ProxyAllocations.Where(a => a.Id == allocationId).Select(a => new { a.ProfileId, a.ProxyId }).SingleOrDefaultAsync(token)
                ?? throw new AllocationPersistenceException(AllocationPersistenceFailure.NotFound);
            var profile = await EfAllocationStore.LockProfile(context, identity.ProfileId, token);
            var proxy = await EfAllocationStore.LockProxy(context, identity.ProxyId, token);
            await EnsureOwnedAsync(context, profile, actorId, token);
            var now = clock.GetUtcNow();
            await RecoverExpiredAsync(context, now, token);
            var allocation = await context.ProxyAllocations.SingleAsync(a => a.Id == allocationId, token);
            // A reserva venceu: a recuperação (e sua auditoria) é gravada e a confirmação é recusada depois do commit.
            if (allocation.Status == AllocationStatus.Expired) return false;
            var wasConfirmed = allocation.Status == AllocationStatus.Confirmed; var linked = false;
            AllocationPersistenceErrors.Domain(() =>
            {
                domain.Confirm(allocation, profile, proxy, now);
                linked = profile.LinkAdsPower(adsPowerId, serialNumber, now);
            });
            if (!wasConfirmed) context.OperationalAuditEvents.Add(new(actorId, profile.Id, allocation.Id, OperationalAction.AllocationConfirmed, now, correlationId));
            if (linked) context.OperationalAuditEvents.Add(new(actorId, profile.Id, null, OperationalAction.ProfileUpdated, now, correlationId));
            return true;
        }, token);
        if (!accepted) throw new AllocationPersistenceException(AllocationPersistenceFailure.Conflict);
    }

    public Task ReleaseAsync(Guid allocationId, Guid actorId, string correlationId, CancellationToken token) => Execute(async context =>
    {
        var identity = await context.ProxyAllocations.Where(a => a.Id == allocationId).Select(a => new { a.ProfileId, a.ProxyId }).SingleOrDefaultAsync(token)
            ?? throw new AllocationPersistenceException(AllocationPersistenceFailure.NotFound);
        var profile = await EfAllocationStore.LockProfile(context, identity.ProfileId, token);
        await EfAllocationStore.LockProxy(context, identity.ProxyId, token);
        await EnsureOwnedAsync(context, profile, actorId, token);
        var now = clock.GetUtcNow();
        await RecoverExpiredAsync(context, now, token);
        var history = await context.ProxyAllocations.Where(a => a.ProfileId == profile.Id).ToListAsync(token);
        var allocation = history.Single(a => a.Id == allocationId);
        // Repetir a liberação (ou liberar o que já venceu) não faz nada; liberar o que já foi confirmado não é permitido aqui.
        if (allocation.Status is AllocationStatus.Expired or AllocationStatus.Released) return true;
        if (allocation.Status == AllocationStatus.Confirmed) throw new AllocationPersistenceException(AllocationPersistenceFailure.Conflict);
        AllocationPersistenceErrors.Domain(() => domain.CloseProfile(profile, history, now));
        context.OperationalAuditEvents.Add(new(actorId, profile.Id, null, OperationalAction.ProfileClosed, now, correlationId));
        context.OperationalAuditEvents.Add(new(actorId, profile.Id, allocation.Id, OperationalAction.AllocationReleased, now, correlationId));
        return true;
    }, token);

    public Task<IngestionBatchRelease> ReleaseBatchAsync(Guid batchId, Guid actorId, string correlationId, CancellationToken token) => Execute(async context =>
    {
        // Lote de outra credencial ou inexistente: mesma resposta (404), sem revelar que existe.
        if (!await context.ProfileIngestionBatches.AnyAsync(b => b.Id == batchId && b.ActorId == actorId, token))
            throw new AllocationPersistenceException(AllocationPersistenceFailure.NotFound);
        var now = clock.GetUtcNow();
        await RecoverExpiredAsync(context, now, token);
        var profiles = await context.OperationalProfiles.Where(p => p.IngestionBatchId == batchId).OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(token);
        foreach (var profile in profiles) await EfAllocationStore.LockProfile(context, profile.Id, token);
        var profileIds = profiles.Select(p => p.Id).ToList();
        var history = await context.ProxyAllocations.Where(a => profileIds.Contains(a.ProfileId)).ToListAsync(token);
        int released = 0, confirmed = 0, ended = 0;
        foreach (var profile in profiles)
        {
            var own = history.Where(a => a.ProfileId == profile.Id).ToList();
            var live = own.SingleOrDefault(a => a.Status == AllocationStatus.Reserved);
            if (live is not null)
            {
                AllocationPersistenceErrors.Domain(() => domain.CloseProfile(profile, own, now));
                context.OperationalAuditEvents.Add(new(actorId, profile.Id, null, OperationalAction.ProfileClosed, now, correlationId));
                context.OperationalAuditEvents.Add(new(actorId, profile.Id, live.Id, OperationalAction.AllocationReleased, now, correlationId));
                released++;
            }
            else if (own.Any(a => a.Status == AllocationStatus.Confirmed)) confirmed++;
            else ended++;
        }
        return new IngestionBatchRelease(batchId, released, confirmed, ended);
    }, token);

    /// <summary>A credencial só age sobre perfis criados por ela mesma; qualquer outro recurso aparece como inexistente.</summary>
    private static async Task EnsureOwnedAsync(CentralContingenciaDbContext context, OperationalProfile profile, Guid actorId, CancellationToken token)
    {
        if (profile.IngestionBatchId is not { } batchId || !await context.ProfileIngestionBatches.AnyAsync(b => b.Id == batchId && b.ActorId == actorId, token))
            throw new AllocationPersistenceException(AllocationPersistenceFailure.NotFound);
    }

    private static async Task<List<IngestionAllocation>> LoadBatchAsync(CentralContingenciaDbContext context, Guid batchId, CancellationToken token)
    {
        var profiles = await context.OperationalProfiles.AsNoTracking().Where(p => p.IngestionBatchId == batchId).ToListAsync(token);
        var profileIds = profiles.Select(p => p.Id).ToList();
        var allocations = await context.ProxyAllocations.AsNoTracking().Where(a => profileIds.Contains(a.ProfileId)).ToListAsync(token);
        var proxyIds = allocations.Select(a => a.ProxyId).Distinct().ToList();
        var proxies = await context.Proxies.AsNoTracking().Where(p => proxyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, token);
        return profiles.Select(profile =>
        {
            var allocation = allocations.Where(a => a.ProfileId == profile.Id).OrderBy(a => a.CreatedAt).First();
            return new IngestionAllocation(profile, allocation, proxies[allocation.ProxyId]);
        }).ToList();
    }

    /// <summary>Expira todas as reservas vencidas e encerra os perfis de ingestão que nunca foram confirmados, liberando número e proxy.</summary>
    private async Task RecoverExpiredAsync(CentralContingenciaDbContext context, DateTimeOffset now, CancellationToken token)
    {
        var expired = await context.ProxyAllocations.Where(a => a.Status == AllocationStatus.Reserved && a.ReservationExpiresAt <= now).ToListAsync(token);
        foreach (var allocation in expired)
        {
            AllocationPersistenceErrors.Domain(() => domain.ExpireReservation(allocation, now));
            context.OperationalAuditEvents.Add(new(ExpiryActor, allocation.ProfileId, allocation.Id, OperationalAction.AllocationExpired, now, "reservation-expiry"));
            var profile = await context.OperationalProfiles.SingleAsync(p => p.Id == allocation.ProfileId, token);
            if (profile.IngestionBatchId is null || profile.Status != ProfileStatus.Active || profile.AdsPowerId is not null) continue;
            var history = await context.ProxyAllocations.Where(a => a.ProfileId == profile.Id).ToListAsync(token);
            if (history.Any(a => a.ConfirmedAt is not null)) continue;
            AllocationPersistenceErrors.Domain(() => domain.CloseProfile(profile, history, now));
            context.OperationalAuditEvents.Add(new(ExpiryActor, profile.Id, null, OperationalAction.ProfileClosed, now, "reservation-expiry"));
        }
    }

    private async Task<T> Execute<T>(Func<CentralContingenciaDbContext, Task<T>> action, CancellationToken token)
    {
        // Contexto dedicado: nunca salva alterações pendentes de outros fluxos.
        await using var context = new CentralContingenciaDbContext(options);
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(token);
            await context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", token);
            await context.Database.ExecuteSqlRawAsync("LOCK TABLE allocations.\"ProxyAllocations\" IN SHARE ROW EXCLUSIVE MODE", token);
            var result = await action(context);
            await context.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return result;
        }
        catch (Exception error) when (error is DbUpdateException or NpgsqlException) { throw AllocationPersistenceErrors.Translate(error); }
    }
}
