using System.Text.Json;
using System.Text.Json.Serialization;
using CentralContingencia.Domain.WorkQueue;

namespace CentralContingencia.Application.WorkQueue;

// ---- Administração (cookie + CSRF; Admin) -------------------------------------------------------------------------------------------------

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProvisionExecutorRequest(string? Name, string[]? AllowedTypes);

/// <summary>Visão pública de um executor. Nunca inclui credencial nem hash.</summary>
public sealed record ExecutorView(Guid Id, string Name, string Status, string Connection, string State, DateTimeOffset? LastSeenAt, string? WorkerVersion,
    IReadOnlyList<string> AllowedTypes, DateTimeOffset CreatedAt, DateTimeOffset? CredentialRotatedAt, DateTimeOffset? RevokedAt, int RunningTasks);

/// <summary>Resposta de provisionamento/rotação: a credencial aparece uma única vez e não pode ser recuperada depois.</summary>
public sealed record ProvisionedExecutor(ExecutorView Executor, string Credential)
{
    public override string ToString() => "[provisioned executor]";
}

public enum ExecutorFailure { Validation, NotFound, Conflict, Unauthorized, Unavailable }
public sealed class ExecutorException(ExecutorFailure failure, Exception? inner = null) : Exception("Operação de executor indisponível.", inner)
{
    public ExecutorFailure Failure { get; } = failure;
}

// ---- Canal do executor (Bearer; /api/v1/agent) --------------------------------------------------------------------------------------------

public sealed record ExecutorIdentity(Guid Id, string Name, IReadOnlyList<WorkTaskType> AllowedTypes);

public sealed record ExecutorConfig(Guid ExecutorId, string Name, IReadOnlyList<string> AllowedTypes, int HeartbeatSeconds, int LeaseSeconds, int MaxClaim, DateTimeOffset ServerTime);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExecutorHeartbeatRequest(string? State, string? WorkerVersion);
public sealed record ExecutorHeartbeatResponse(DateTimeOffset ServerTime);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentClaimRequest(string[]? Types, int? Max);

/// <summary>Tarefa entregue ao executor. O payload é o já validado/canônico; não há credenciais de ninguém.</summary>
public sealed record AgentTask(Guid Id, string Type, int PayloadVersion, JsonElement Payload, int Attempt, int MaxAttempts, string CorrelationId, DateTimeOffset LeaseUntil);
public sealed record AgentTaskList(IReadOnlyList<AgentTask> Tasks);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AttemptRequest(int Attempt);
public sealed record CancellationStatus(bool CancelRequested);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentCompleteRequest(int Attempt, JsonElement? Result);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentFailRequest(int Attempt, string? Code, string? Message, bool Transient);
public sealed record AgentFailResponse(string Status);

public interface IExecutorStore
{
    /// <summary>Nome já usado (sem distinguir caixa) → Conflict.</summary>
    Task AddAsync(WorkExecutor executor, ExecutorEvent auditEvent, CancellationToken token);
    Task<WorkExecutor?> GetAsync(Guid id, CancellationToken token);
    Task<IReadOnlyList<WorkExecutor>> ListAsync(CancellationToken token);
    Task<IReadOnlyDictionary<string, int>> RunningCountsAsync(CancellationToken token);
    Task<IReadOnlyDictionary<string, string>> NamesAsync(IReadOnlyCollection<string> ids, CancellationToken token);
    /// <summary>Trava a linha, aplica a mudança do domínio e grava o evento (se houver) no mesmo commit.</summary>
    Task<WorkExecutor> MutateAsync(Guid id, Func<WorkExecutor, ExecutorEvent?> change, CancellationToken token);
}

public interface IExecutorAdminService
{
    Task<ProvisionedExecutor> ProvisionAsync(Guid actorId, ProvisionExecutorRequest request, CancellationToken token);
    Task<IReadOnlyList<ExecutorView>> ListAsync(CancellationToken token);
    Task<ExecutorView> GetAsync(Guid id, CancellationToken token);
    Task<ProvisionedExecutor> RotateAsync(Guid actorId, Guid id, CancellationToken token);
    Task<ExecutorView> RevokeAsync(Guid actorId, Guid id, CancellationToken token);
}

/// <summary>Lado servidor do canal do executor: autenticação, presença e claim restrito aos tipos permitidos. Operações de tarefa passam por <see cref="IWorkQueueService"/>.</summary>
public interface IExecutorGateway
{
    /// <returns>null se a credencial é inválida, foi trocada ou o executor está revogado.</returns>
    Task<ExecutorIdentity?> AuthenticateAsync(string credential, CancellationToken token);
    ExecutorConfig GetConfig(ExecutorIdentity identity);
    Task HeartbeatAsync(ExecutorIdentity identity, ExecutorHeartbeatRequest request, CancellationToken token);
    Task<IReadOnlyList<ClaimedWorkTask>> ClaimAsync(ExecutorIdentity identity, AgentClaimRequest request, Guid? claimKey, CancellationToken token);
    /// <summary>Devolve à fila (sem consumir tentativa) tudo o que o executor segura e o marca Offline.</summary>
    Task ShutdownAsync(ExecutorIdentity identity, CancellationToken token);
}

/// <summary>Lado worker: o que ele precisa além de <see cref="IWorkQueueService"/>. No modo local (banco direto) é uma implementação nula.</summary>
public interface IExecutorSession
{
    Task<ExecutorConfig?> GetConfigAsync(CancellationToken token);
    Task HeartbeatAsync(ExecutorState state, string workerVersion, CancellationToken token);
    Task ShutdownAsync(CancellationToken token);
}

public sealed class NullExecutorSession : IExecutorSession
{
    public Task<ExecutorConfig?> GetConfigAsync(CancellationToken token) => Task.FromResult<ExecutorConfig?>(null);
    public Task HeartbeatAsync(ExecutorState state, string workerVersion, CancellationToken token) => Task.CompletedTask;
    public Task ShutdownAsync(CancellationToken token) => Task.CompletedTask;
}
