using System.Text.RegularExpressions;

namespace CentralContingencia.Domain.WorkQueue;

public enum ExecutorStatus { Active, Revoked }

/// <summary>Estado operacional informado pelo próprio executor no heartbeat. Offline também é o estado inicial e o de encerramento controlado.</summary>
public enum ExecutorState { Offline, Idle, Busy, Draining }

public enum ExecutorError { Revoked, Invalid }

public sealed class ExecutorStateException(ExecutorError error) : InvalidOperationException("Operação inválida para o estado do executor.")
{
    public ExecutorError Error { get; } = error;
}

/// <summary>
/// Identidade de um worker (independente do Identity dos usuários). Guarda somente o hash da credencial; a credencial em si
/// só existe no momento do provisionamento/rotação. Revogado é definitivo: um executor revogado nunca volta a operar
/// (para reativar, provisiona-se outro).
/// </summary>
public sealed partial class WorkExecutor
{
    public const int NameMaxLength = 100;
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string NormalizedName { get; private set; } = "";
    public ExecutorStatus Status { get; private set; }
    public string CredentialHash { get; private set; } = "";
    public string AllowedTypes { get; private set; } = "";
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CredentialRotatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? RevokedBy { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }
    public ExecutorState State { get; private set; }
    public string? WorkerVersion { get; private set; }
    private WorkExecutor() { }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 ._\-]{1,98}[A-Za-z0-9]$")]
    private static partial Regex NamePattern();
    [GeneratedRegex(@"^[A-Za-z0-9._\-+ ]{1,32}$")]
    private static partial Regex VersionPattern();
    [GeneratedRegex(@"^[0-9a-f]{64}$")]
    private static partial Regex HashPattern();

    public IReadOnlyList<WorkTaskType> AllowedTypeList => AllowedTypes.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<WorkTaskType>).ToList();
    public bool IsRevoked => Status == ExecutorStatus.Revoked;

    public static WorkExecutor Provision(Guid id, string name, IReadOnlyCollection<WorkTaskType> allowedTypes, string credentialHash, Guid createdBy, DateTimeOffset now)
    {
        if (id == Guid.Empty || createdBy == Guid.Empty) throw new ArgumentException("Identificador inválido.");
        if (name is null || !NamePattern().IsMatch(name)) throw new ArgumentException("Nome inválido.");
        if (allowedTypes.Count == 0 || allowedTypes.Any(t => !Enum.IsDefined(t))) throw new ArgumentException("Tipos permitidos inválidos.");
        EnsureHash(credentialHash);
        return new()
        {
            Id = id, Name = name, NormalizedName = name.ToUpperInvariant(), Status = ExecutorStatus.Active, CredentialHash = credentialHash,
            AllowedTypes = string.Join(',', allowedTypes.Distinct().OrderBy(t => t).Select(t => t.ToString())), CreatedBy = createdBy,
            CreatedAt = now.ToUniversalTime(), State = ExecutorState.Offline,
        };
    }

    public bool CanRun(WorkTaskType type) => AllowedTypeList.Contains(type);

    /// <summary>Troca a credencial: a anterior deixa de valer imediatamente.</summary>
    public void RotateCredential(string newHash, DateTimeOffset now)
    {
        EnsureActive(); EnsureHash(newHash);
        CredentialHash = newHash; CredentialRotatedAt = now.ToUniversalTime();
    }

    public void Revoke(Guid actorId, DateTimeOffset now)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("Ator inválido.");
        if (IsRevoked) return;
        Status = ExecutorStatus.Revoked; RevokedAt = now.ToUniversalTime(); RevokedBy = actorId; State = ExecutorState.Offline;
    }

    /// <summary>Registra comunicação do executor (heartbeat, claim). Executor revogado não registra nada.</summary>
    public void RecordSeen(ExecutorState state, string? workerVersion, DateTimeOffset now)
    {
        EnsureActive();
        if (!Enum.IsDefined(state)) throw new ArgumentOutOfRangeException(nameof(state));
        if (workerVersion is not null && !VersionPattern().IsMatch(workerVersion)) throw new ArgumentException("Versão inválida.");
        LastSeenAt = now.ToUniversalTime(); State = state;
        if (workerVersion is not null) WorkerVersion = workerVersion;
    }

    public bool IsOnline(DateTimeOffset now, TimeSpan window) => !IsRevoked && State != ExecutorState.Offline && LastSeenAt is { } seen && now - seen <= window;

    private void EnsureActive() { if (IsRevoked) throw new ExecutorStateException(ExecutorError.Revoked); }
    private static void EnsureHash(string hash) { if (hash is null || !HashPattern().IsMatch(hash)) throw new ArgumentException("Hash inválido."); }
    public override string ToString() => $"WorkExecutor {Id}";
}

public enum ExecutorAction { Provisioned, CredentialRotated, Revoked }

/// <summary>Auditoria da gestão de executores. Nunca contém credencial, hash nem segredo.</summary>
public sealed class ExecutorEvent
{
    public long Sequence { get; private set; }
    public Guid Id { get; private set; }
    public Guid ExecutorId { get; private set; }
    public ExecutorAction Action { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    private ExecutorEvent() { }
    public static ExecutorEvent Create(WorkExecutor executor, ExecutorAction action, Guid actorId, DateTimeOffset now) =>
        new() { Id = Guid.NewGuid(), ExecutorId = executor.Id, Action = action, ActorId = actorId, OccurredAt = now.ToUniversalTime() };
    public override string ToString() => $"ExecutorEvent {Id}";
}
