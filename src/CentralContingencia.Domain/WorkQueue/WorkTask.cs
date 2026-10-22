namespace CentralContingencia.Domain.WorkQueue;

/// <summary>
/// Tarefa assíncrona da fila (PostgreSQL é a fonte de verdade). O cliente nunca altera estado, lease ou tentativas:
/// só o domínio, acionado pelo caso de uso, muda <see cref="Status"/>.
/// Pending → Running (claim) → Succeeded | Failed | Cancelled | DeadLetter, ou Running → Pending (retry/recuperação/liberação).
/// O lease é a prova de posse: sem lease vigente (worker, tentativa, validade) nenhuma operação do worker é aceita.
/// </summary>
public sealed class WorkTask
{
    public const int MaxAttemptsLimit = 10;
    public Guid Id { get; private set; }
    public WorkTaskType Type { get; private set; }
    public WorkTaskStatus Status { get; private set; }
    public int PayloadVersion { get; private set; }
    public string Payload { get; private set; } = "";
    public string? Result { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset AvailableAt { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LastError { get; private set; }
    public string CorrelationId { get; private set; } = "";
    public Guid IdempotencyKey { get; private set; }
    public string RequestFingerprint { get; private set; } = "";
    public Guid CreatedBy { get; private set; }
    public string? WorkerId { get; private set; }
    /// <summary>Último executor que reservou a tarefa; permanece após o fim do lease (diferente de <see cref="WorkerId"/>, que só existe enquanto Running).</summary>
    public string? LastWorkerId { get; private set; }
    /// <summary>Chave de idempotência do claim que reservou a tarefa: permite reentregar ao mesmo executor se a resposta se perdeu.</summary>
    public Guid? ClaimKey { get; private set; }
    public DateTimeOffset? LeaseUntil { get; private set; }
    public DateTimeOffset? HeartbeatAt { get; private set; }
    public DateTimeOffset? CancelRequestedAt { get; private set; }
    public Guid? CancelRequestedBy { get; private set; }
    private WorkTask() { }

    public bool IsTerminal => Status is WorkTaskStatus.Succeeded or WorkTaskStatus.Failed or WorkTaskStatus.Cancelled or WorkTaskStatus.DeadLetter;
    public bool CancelRequested => CancelRequestedAt is not null;

    public static WorkTask Create(Guid createdBy, Guid idempotencyKey, string requestFingerprint, WorkTaskType type, int payloadVersion, string payloadJson,
        int maxAttempts, string correlationId, DateTimeOffset now)
    {
        if (createdBy == Guid.Empty || idempotencyKey == Guid.Empty) throw new ArgumentException("Identificador inválido.");
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (payloadVersion < 1 || string.IsNullOrWhiteSpace(payloadJson)) throw new ArgumentException("Payload inválido.");
        if (maxAttempts is < 1 or > MaxAttemptsLimit) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 64 || correlationId.Any(char.IsControl)) throw new ArgumentException("CorrelationId inválido.");
        if (string.IsNullOrWhiteSpace(requestFingerprint) || requestFingerprint.Length != 64) throw new ArgumentException("Fingerprint inválido.");
        EnsureTimestamp(now);
        return new()
        {
            Id = Guid.NewGuid(), Type = type, Status = WorkTaskStatus.Pending, PayloadVersion = payloadVersion, Payload = payloadJson,
            CreatedAt = now.ToUniversalTime(), AvailableAt = now.ToUniversalTime(), MaxAttempts = maxAttempts, CorrelationId = correlationId,
            IdempotencyKey = idempotencyKey, RequestFingerprint = requestFingerprint, CreatedBy = createdBy,
        };
    }

    /// <summary>Pending → Running. Só tarefas já disponíveis (respeita o backoff) podem ser reservadas.</summary>
    public void Claim(string workerId, DateTimeOffset now, TimeSpan lease, Guid? claimKey = null)
    {
        if (claimKey == Guid.Empty) throw new ArgumentException("Chave de claim inválida.");
        ValidateWorker(workerId); ValidateLease(lease); EnsureTimestamp(now);
        if (Status != WorkTaskStatus.Pending || AvailableAt > now || AttemptCount >= MaxAttempts) throw new WorkTaskStateException(WorkTaskError.InvalidTransition);
        Status = WorkTaskStatus.Running; WorkerId = workerId; LastWorkerId = workerId; AttemptCount++; ClaimKey = claimKey;
        StartedAt = now.ToUniversalTime(); HeartbeatAt = now.ToUniversalTime(); LeaseUntil = now.ToUniversalTime() + lease;
    }

    /// <summary>Repetição do mesmo sucesso pelo mesmo executor e tentativa (resposta perdida): deve ser aceita sem novo efeito.</summary>
    public bool IsSucceededBy(string workerId, int attempt) => Status == WorkTaskStatus.Succeeded && LastWorkerId == workerId && AttemptCount == attempt;

    /// <summary>Repetição da mesma falha (mesmo código) pelo mesmo executor e tentativa, qualquer que tenha sido o destino (retry, Failed, DeadLetter, Cancelled).</summary>
    public bool IsFailureReportedBy(string workerId, int attempt, string code) =>
        Status != WorkTaskStatus.Running && Status != WorkTaskStatus.Succeeded && LastWorkerId == workerId && AttemptCount == attempt && LastErrorCode == WorkErrorSanitizer.Code(code);

    public bool IsCancellationConfirmedBy(string workerId, int attempt) => Status == WorkTaskStatus.Cancelled && CancelRequested && LastWorkerId == workerId && AttemptCount == attempt;

    public bool OwnsLease(string workerId, int attempt, DateTimeOffset now) =>
        Status == WorkTaskStatus.Running && WorkerId == workerId && AttemptCount == attempt && LeaseUntil > now;

    /// <summary>Renova o lease (nunca o encurta) e informa se o cancelamento foi solicitado.</summary>
    public bool Heartbeat(string workerId, int attempt, DateTimeOffset now, TimeSpan lease)
    {
        ValidateLease(lease); EnsureLease(workerId, attempt, now);
        var until = now.ToUniversalTime() + lease;
        HeartbeatAt = now.ToUniversalTime();
        if (until > LeaseUntil) LeaseUntil = until;
        return CancelRequested;
    }

    public void Succeed(string workerId, int attempt, DateTimeOffset now, string? resultJson)
    {
        EnsureLease(workerId, attempt, now);
        Result = resultJson; Finish(WorkTaskStatus.Succeeded, now);
    }

    /// <summary>Falha de uma tentativa. Permanente → Failed; transitória → Pending (com backoff) ou DeadLetter ao esgotar as tentativas; cancelamento pendente prevalece.</summary>
    public WorkTaskStatus Fail(string workerId, int attempt, DateTimeOffset now, string code, string? message, bool transient, DateTimeOffset retryAt)
    {
        EnsureLease(workerId, attempt, now);
        RecordError(code, message);
        if (CancelRequested) Finish(WorkTaskStatus.Cancelled, now);
        else if (!transient) Finish(WorkTaskStatus.Failed, now);
        else if (AttemptCount < MaxAttempts) Requeue(retryAt < now ? now : retryAt);
        else Finish(WorkTaskStatus.DeadLetter, now);
        return Status;
    }

    public void ConfirmCancelled(string workerId, int attempt, DateTimeOffset now)
    {
        EnsureLease(workerId, attempt, now);
        if (!CancelRequested) throw new WorkTaskStateException(WorkTaskError.InvalidTransition);
        Finish(WorkTaskStatus.Cancelled, now);
    }

    /// <summary>Devolve a tarefa à fila sem contar a tentativa (ex.: desligamento ordenado do worker).</summary>
    public WorkTaskStatus Release(string workerId, int attempt, DateTimeOffset now)
    {
        EnsureLease(workerId, attempt, now);
        if (CancelRequested) { Finish(WorkTaskStatus.Cancelled, now); return Status; }
        AttemptCount--; Requeue(now);
        return Status;
    }

    /// <summary>Pending → Cancelled na hora; Running → pedido cooperativo (o worker confirma). Repetir é seguro.</summary>
    public void RequestCancel(Guid actorId, DateTimeOffset now)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("Ator inválido.");
        EnsureTimestamp(now);
        if (Status == WorkTaskStatus.Cancelled || (Status == WorkTaskStatus.Running && CancelRequested)) return;
        if (Status is not (WorkTaskStatus.Pending or WorkTaskStatus.Running)) throw new WorkTaskStateException(WorkTaskError.InvalidTransition);
        CancelRequestedAt = now.ToUniversalTime(); CancelRequestedBy = actorId;
        if (Status == WorkTaskStatus.Pending) Finish(WorkTaskStatus.Cancelled, now);
    }

    public bool IsLeaseExpired(DateTimeOffset now) => Status == WorkTaskStatus.Running && LeaseUntil <= now;

    /// <summary>Recupera tarefa de worker que sumiu: cancela se havia pedido; senão volta à fila ou vai a DeadLetter.</summary>
    public WorkTaskStatus RecoverExpiredLease(DateTimeOffset now, DateTimeOffset retryAt)
    {
        if (!IsLeaseExpired(now)) throw new WorkTaskStateException(WorkTaskError.InvalidTransition);
        RecordError("lease_expired", "O worker deixou de renovar o lease.");
        if (CancelRequested) Finish(WorkTaskStatus.Cancelled, now);
        else if (AttemptCount < MaxAttempts) Requeue(retryAt < now ? now : retryAt);
        else Finish(WorkTaskStatus.DeadLetter, now);
        return Status;
    }

    private void EnsureLease(string workerId, int attempt, DateTimeOffset now)
    {
        if (!OwnsLease(workerId, attempt, now)) throw new WorkTaskStateException(WorkTaskError.LeaseLost);
    }

    private void RecordError(string code, string? message)
    {
        LastErrorCode = WorkErrorSanitizer.Code(code);
        LastError = WorkErrorSanitizer.Message(message);
    }

    private void Requeue(DateTimeOffset availableAt)
    {
        Status = WorkTaskStatus.Pending; AvailableAt = availableAt.ToUniversalTime();
        WorkerId = null; LeaseUntil = null; HeartbeatAt = null;
    }

    private void Finish(WorkTaskStatus terminal, DateTimeOffset now)
    {
        Status = terminal; CompletedAt = now.ToUniversalTime();
        WorkerId = null; LeaseUntil = null; HeartbeatAt = null;
    }

    private static void ValidateWorker(string workerId)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > 100 || workerId.Any(char.IsControl)) throw new ArgumentException("WorkerId inválido.");
    }
    private static void ValidateLease(TimeSpan lease)
    {
        if (lease < TimeSpan.FromSeconds(1) || lease > TimeSpan.FromHours(1)) throw new ArgumentOutOfRangeException(nameof(lease));
    }
    private static void EnsureTimestamp(DateTimeOffset value)
    {
        if (value.UtcDateTime == DateTime.MinValue || value.UtcDateTime == DateTime.MaxValue) throw new ArgumentException("Timestamp inválido.");
    }
    public override string ToString() => $"WorkTask {Id}";
}
