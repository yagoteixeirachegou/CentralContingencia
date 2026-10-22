using System.Text.Json;
using CentralContingencia.Domain.WorkQueue;

namespace CentralContingencia.Application.WorkQueue;

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateWorkTaskRequest(string? Type, JsonElement Payload, int? MaxAttempts);

/// <summary>Visão pública da tarefa: sem worker, lease, fingerprint nem texto de erro (somente o código sanitizado).</summary>
public sealed record WorkTaskResponse(Guid Id, string Type, string Status, int PayloadVersion, JsonElement Payload, JsonElement? Result,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, DateTimeOffset? NextAttemptAt,
    int AttemptCount, int MaxAttempts, string? LastErrorCode, string CorrelationId, bool CancelRequested,
    Guid? ExecutorId, string? ExecutorName, DateTimeOffset LastActivityAt);

public sealed record WorkTaskPage(IReadOnlyList<WorkTaskResponse> Items, int Page, int PageSize, int Total);
public sealed record CreatedWorkTask(WorkTaskResponse Task, bool Created);

/// <summary>Tarefa reservada por um worker. Carrega o necessário para executar e para provar o lease nas chamadas seguintes.</summary>
public sealed record ClaimedWorkTask(Guid Id, WorkTaskType Type, int PayloadVersion, string Payload, int Attempt, int MaxAttempts, string CorrelationId, DateTimeOffset LeaseUntil);

public enum WorkQueueFailure { Validation, NotFound, Conflict, LeaseLost, Unavailable, Unauthorized }
public sealed class WorkQueueException(WorkQueueFailure failure, Exception? inner = null, TimeSpan? retryAfter = null, string? detail = null) : Exception("Operação da fila indisponível.", inner)
{
    /// <summary>Causa técnica SEGURA para log (ex.: "http_503", "HttpRequestException>AuthenticationException"): nunca host, URL nem mensagem de rede.</summary>
    public string? Detail { get; } = detail;
    public WorkQueueFailure Failure { get; } = failure;
    /// <summary>Espera pedida pelo servidor (Retry-After); o cliente a respeita antes de tentar de novo.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed record WorkTaskFilter(WorkTaskStatus? Status, WorkTaskType? Type, int Page, int PageSize);
public sealed record StoredCreate(WorkTask Task, bool Created);

/// <summary>
/// Persistência da fila. Cada operação é uma transação que trava a linha, aplica a transição do domínio e grava a auditoria no mesmo commit.
/// </summary>
public interface IWorkTaskStore
{
    /// <summary>Idempotente por (criador, chave). Mesma chave com conteúdo diferente → Conflict.</summary>
    Task<StoredCreate> CreateAsync(WorkTask task, DateTimeOffset now, CancellationToken token);
    Task<WorkTask?> GetAsync(Guid id, CancellationToken token);
    Task<(IReadOnlyList<WorkTask> Items, int Total)> ListAsync(WorkTaskFilter filter, CancellationToken token);
    Task<WorkTask> RequestCancelAsync(Guid id, Guid actorId, DateTimeOffset now, CancellationToken token);
    /// <summary>Reserva até <paramref name="max"/> tarefas Pending disponíveis (SELECT … FOR UPDATE SKIP LOCKED).</summary>
    Task<IReadOnlyList<WorkTask>> ClaimAsync(string workerId, IReadOnlyCollection<WorkTaskType> types, int max, DateTimeOffset now, TimeSpan lease, Guid? claimKey, CancellationToken token);
    /// <returns>true se o cancelamento foi solicitado.</returns>
    Task<bool> HeartbeatAsync(Guid id, string workerId, int attempt, DateTimeOffset now, TimeSpan lease, CancellationToken token);
    Task CompleteAsync(Guid id, string workerId, int attempt, string? resultJson, DateTimeOffset now, CancellationToken token);
    Task<WorkTaskStatus> FailAsync(Guid id, string workerId, int attempt, string code, string? message, bool transient, DateTimeOffset now, Func<int, TimeSpan> retryDelay, CancellationToken token);
    Task ConfirmCancelledAsync(Guid id, string workerId, int attempt, DateTimeOffset now, CancellationToken token);
    Task ReleaseAsync(Guid id, string workerId, int attempt, DateTimeOffset now, CancellationToken token);
    /// <summary>Recupera tarefas Running com lease vencido (pula linhas travadas por outro processo).</summary>
    Task<int> RecoverExpiredAsync(DateTimeOffset now, Func<int, TimeSpan> retryDelay, int limit, CancellationToken token);
    /// <summary>Tarefas Running cujo lease vigente pertence a este executor.</summary>
    Task<IReadOnlyList<WorkTask>> ListHeldAsync(string workerId, DateTimeOffset now, CancellationToken token);
    /// <summary>Consulta somente leitura do pedido de cancelamento; exige lease vigente.</summary>
    Task<bool> IsCancelRequestedAsync(Guid id, string workerId, int attempt, DateTimeOffset now, CancellationToken token);
}

/// <summary>Caso de uso do cliente (API). Nunca aceita estado, lease ou tentativas.</summary>
public interface IWorkTaskService
{
    Task<CreatedWorkTask> CreateAsync(Guid actorId, Guid idempotencyKey, CreateWorkTaskRequest request, string correlationId, CancellationToken token);
    Task<WorkTaskResponse> GetAsync(Guid id, CancellationToken token);
    Task<WorkTaskPage> ListAsync(string? status, string? type, int page, int pageSize, CancellationToken token);
    Task<WorkTaskResponse> CancelAsync(Guid actorId, Guid id, CancellationToken token);
}

/// <summary>Caso de uso do worker. Toda chamada após o claim prova a posse pelo lease (worker + tentativa + validade).</summary>
public interface IWorkQueueService
{
    Task<IReadOnlyList<ClaimedWorkTask>> ClaimAsync(string workerId, IReadOnlyCollection<WorkTaskType> types, int max, CancellationToken token);
    /// <summary>Claim idempotente: repetir a mesma chave (resposta perdida) devolve as tarefas que ESTE executor já segura com lease vigente, sem reservar outras nem tocar leases alheios.</summary>
    Task<IReadOnlyList<ClaimedWorkTask>> ClaimAsync(string workerId, IReadOnlyCollection<WorkTaskType> types, int max, Guid claimKey, CancellationToken token) => ClaimAsync(workerId, types, max, token);
    Task<bool> HeartbeatAsync(string workerId, Guid id, int attempt, CancellationToken token);
    Task CompleteAsync(string workerId, Guid id, int attempt, string? resultJson, CancellationToken token);
    Task<WorkTaskStatus> FailAsync(string workerId, Guid id, int attempt, string code, string? message, bool transient, CancellationToken token);
    Task ConfirmCancelledAsync(string workerId, Guid id, int attempt, CancellationToken token);
    Task ReleaseAsync(string workerId, Guid id, int attempt, CancellationToken token);
    Task<int> RecoverExpiredAsync(CancellationToken token);
    Task<IReadOnlyList<ClaimedWorkTask>> HeldAsync(string workerId, CancellationToken token);
    Task<bool> IsCancelRequestedAsync(string workerId, Guid id, int attempt, CancellationToken token);
}
