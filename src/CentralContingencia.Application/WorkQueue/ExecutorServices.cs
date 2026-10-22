using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CentralContingencia.Domain.WorkQueue;

namespace CentralContingencia.Application.WorkQueue;

/// <summary>Formato da credencial: <c>ce1.&lt;ExecutorId (N)&gt;.&lt;segredo base64url de 32 bytes&gt;</c>. Só o SHA-256 do segredo é persistido.</summary>
public static partial class ExecutorCredentials
{
    public const string Prefix = "ce1";
    [GeneratedRegex(@"^ce1\.([0-9a-f]{32})\.([A-Za-z0-9_\-]{43})$")]
    private static partial Regex Pattern();

    public static (string Credential, string Hash) Generate(Guid executorId)
    {
        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));
        return ($"{Prefix}.{executorId:N}.{secret}", Hash(secret));
    }

    public static bool TryParse(string? credential, out Guid executorId, out string secret)
    {
        executorId = default; secret = "";
        if (credential is null || credential.Length > 100 || Pattern().Match(credential) is not { Success: true } match) return false;
        executorId = Guid.ParseExact(match.Groups[1].Value, "N"); secret = match.Groups[2].Value;
        return executorId != Guid.Empty;
    }

    public static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    public static bool Verify(string secret, string storedHash) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(secret)), Encoding.ASCII.GetBytes(storedHash.PadRight(64, '0')[..64]));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class ExecutorAdminService(IExecutorStore store, WorkQueueOptions options, TimeProvider clock) : IExecutorAdminService
{
    public async Task<ProvisionedExecutor> ProvisionAsync(Guid actorId, ProvisionExecutorRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var types = new List<WorkTaskType>();
        foreach (var name in request.AllowedTypes ?? [nameof(WorkTaskType.SimulatedProcessing)])
            types.Add(WorkTaskPayloads.TryParseType(name, out var type) ? type : throw new ExecutorException(ExecutorFailure.Validation));
        var id = Guid.NewGuid();
        var (credential, hash) = ExecutorCredentials.Generate(id);
        WorkExecutor executor;
        try { executor = WorkExecutor.Provision(id, request.Name ?? "", types, hash, actorId, clock.GetUtcNow()); }
        catch (ArgumentException) { throw new ExecutorException(ExecutorFailure.Validation); }
        await store.AddAsync(executor, ExecutorEvent.Create(executor, ExecutorAction.Provisioned, actorId, clock.GetUtcNow()), token);
        return new(ToView(executor, 0), credential);
    }

    public async Task<IReadOnlyList<ExecutorView>> ListAsync(CancellationToken token)
    {
        var executors = await store.ListAsync(token);
        var running = await store.RunningCountsAsync(token);
        return executors.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Select(e => ToView(e, running.GetValueOrDefault(e.Id.ToString("D")))).ToList();
    }

    public async Task<ExecutorView> GetAsync(Guid id, CancellationToken token)
    {
        var executor = await store.GetAsync(id, token) ?? throw new ExecutorException(ExecutorFailure.NotFound);
        return ToView(executor, (await store.RunningCountsAsync(token)).GetValueOrDefault(id.ToString("D")));
    }

    public async Task<ProvisionedExecutor> RotateAsync(Guid actorId, Guid id, CancellationToken token)
    {
        var (credential, hash) = ExecutorCredentials.Generate(id);
        var executor = await Mutate(id, e =>
        {
            e.RotateCredential(hash, clock.GetUtcNow());
            return ExecutorEvent.Create(e, ExecutorAction.CredentialRotated, actorId, clock.GetUtcNow());
        }, token);
        return new(ToView(executor, 0), credential);
    }

    public async Task<ExecutorView> RevokeAsync(Guid actorId, Guid id, CancellationToken token)
    {
        var executor = await Mutate(id, e =>
        {
            var wasRevoked = e.IsRevoked;
            e.Revoke(actorId, clock.GetUtcNow());
            return wasRevoked ? null : ExecutorEvent.Create(e, ExecutorAction.Revoked, actorId, clock.GetUtcNow());
        }, token);
        return ToView(executor, 0);
    }

    private async Task<WorkExecutor> Mutate(Guid id, Func<WorkExecutor, ExecutorEvent?> change, CancellationToken token)
    {
        try { return await store.MutateAsync(id, change, token); }
        catch (ExecutorStateException) { throw new ExecutorException(ExecutorFailure.Conflict); }
    }

    private ExecutorView ToView(WorkExecutor e, int running) => new(e.Id, e.Name, e.Status.ToString(),
        e.IsRevoked ? "Revoked" : e.IsOnline(clock.GetUtcNow(), TimeSpan.FromSeconds(options.ExecutorOnlineSeconds)) ? "Online" : "Offline",
        e.State.ToString(), e.LastSeenAt, e.WorkerVersion, e.AllowedTypeList.Select(t => t.ToString()).ToList(), e.CreatedAt, e.CredentialRotatedAt, e.RevokedAt, running);
}

public sealed class ExecutorGateway(IExecutorStore store, IWorkQueueService queue, WorkQueueOptions options, TimeProvider clock) : IExecutorGateway
{
    private static readonly string DummyHash = new('0', 64);

    public async Task<ExecutorIdentity?> AuthenticateAsync(string credential, CancellationToken token)
    {
        if (!ExecutorCredentials.TryParse(credential, out var id, out var secret)) return null;
        var executor = await store.GetAsync(id, token);
        // Compara sempre (inclusive com executor inexistente) para não variar o tempo de resposta.
        var valid = ExecutorCredentials.Verify(secret, executor?.CredentialHash ?? DummyHash);
        return executor is { IsRevoked: false } && valid ? new(executor.Id, executor.Name, executor.AllowedTypeList) : null;
    }

    public ExecutorConfig GetConfig(ExecutorIdentity identity) => new(identity.Id, identity.Name, identity.AllowedTypes.Select(t => t.ToString()).ToList(),
        options.ExecutorHeartbeatSeconds, options.LeaseSeconds, options.ExecutorMaxClaim, clock.GetUtcNow());

    public async Task HeartbeatAsync(ExecutorIdentity identity, ExecutorHeartbeatRequest request, CancellationToken token)
    {
        if (request.State is null || !Enum.GetNames<ExecutorState>().Contains(request.State) || Enum.Parse<ExecutorState>(request.State) == ExecutorState.Offline)
            throw new ExecutorException(ExecutorFailure.Validation);
        await Seen(identity.Id, Enum.Parse<ExecutorState>(request.State), request.WorkerVersion, token);
    }

    public async Task<IReadOnlyList<ClaimedWorkTask>> ClaimAsync(ExecutorIdentity identity, AgentClaimRequest request, Guid? claimKey, CancellationToken token)
    {
        var requested = new List<WorkTaskType>();
        foreach (var name in request.Types ?? identity.AllowedTypes.Select(t => t.ToString()).ToArray())
            requested.Add(WorkTaskPayloads.TryParseType(name, out var type) ? type : throw new ExecutorException(ExecutorFailure.Validation));
        // O executor só recebe tipos que o administrador autorizou, qualquer que seja o pedido.
        var allowed = requested.Where(identity.AllowedTypes.Contains).Distinct().ToList();
        var max = request.Max ?? 1;
        if (max < 1 || max > options.ExecutorMaxClaim) throw new ExecutorException(ExecutorFailure.Validation);
        var executor = await Seen(identity.Id, null, null, token);
        if (allowed.Count == 0 || executor.State == ExecutorState.Draining) return [];
        return claimKey is { } key ? await queue.ClaimAsync(identity.Id.ToString("D"), allowed, max, key, token) : await queue.ClaimAsync(identity.Id.ToString("D"), allowed, max, token);
    }

    public async Task ShutdownAsync(ExecutorIdentity identity, CancellationToken token)
    {
        var workerId = identity.Id.ToString("D");
        foreach (var held in await queue.HeldAsync(workerId, token))
        {
            try { await queue.ReleaseAsync(workerId, held.Id, held.Attempt, token); }
            catch (WorkQueueException error) when (error.Failure is WorkQueueFailure.LeaseLost or WorkQueueFailure.Conflict or WorkQueueFailure.NotFound) { }
        }
        await Seen(identity.Id, ExecutorState.Offline, null, token);
    }

    private async Task<WorkExecutor> Seen(Guid id, ExecutorState? state, string? version, CancellationToken token)
    {
        try
        {
            return await store.MutateAsync(id, e =>
            {
                e.RecordSeen(state ?? (e.State == ExecutorState.Offline ? ExecutorState.Idle : e.State), version, clock.GetUtcNow());
                return null;
            }, token);
        }
        catch (ExecutorStateException error) { throw new ExecutorException(error.Error == ExecutorError.Revoked ? ExecutorFailure.Unauthorized : ExecutorFailure.Validation); }
        catch (ArgumentException) { throw new ExecutorException(ExecutorFailure.Validation); }
    }
}
