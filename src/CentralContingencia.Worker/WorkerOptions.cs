using CentralContingencia.Application.WorkQueue;

namespace CentralContingencia.Worker;

public enum WorkerMode
{
    /// <summary>Padrão. Fala somente com a API por HTTPS (/api/v1/agent), com credencial própria; nunca abre conexão com o PostgreSQL.</summary>
    Remote,
    /// <summary>Somente desenvolvimento local: acessa o banco diretamente (Etapa 2B). Recusado fora de Development.</summary>
    LocalDatabase,
}

/// <summary>Conexão do worker remoto com a API. A credencial é segredo: variável de ambiente (<c>Worker__Api__Credential</c>) ou arquivo fora do repositório.</summary>
public sealed class WorkerApiOptions
{
    public string? BaseUrl { get; set; }
    public string? Credential { get; set; }
    public string? CredentialFile { get; set; }
    /// <summary>PEM com a(s) autoridade(s) certificadora(s) PRIVADA(s) que assinam o certificado da API (ex.: CA interna, homologação). A validação TLS continua completa (cadeia, validade e nome do host); a CA só é adicionada como raiz confiável DESTE processo. Vazio = somente a confiança do sistema.</summary>
    public string? TrustedCaFile { get; set; }
    public int RequestTimeoutSeconds { get; set; } = 20;
    public override string ToString() => "[worker api options]";
}

/// <summary>Seção <c>Worker</c>. Em modo local, o lease (<c>WorkQueue:LeaseSeconds</c>) precisa comportar ao menos três heartbeats; em modo remoto o lease vem do servidor.</summary>
public sealed class WorkerOptions
{
    public const string Section = "Worker";
    public WorkerMode Mode { get; set; } = WorkerMode.Remote;
    public WorkerApiOptions Api { get; set; } = new();
    public string? WorkerId { get; set; }
    public int PollIntervalMs { get; set; } = 1000;
    public int MaxConcurrency { get; set; } = 2;
    public int HeartbeatSeconds { get; set; } = 20;
    public int RecoveryIntervalSeconds { get; set; } = 30;
    public int ErrorBackoffMaxSeconds { get; set; } = 60;
    public int ShutdownGraceSeconds { get; set; } = 30;

    public TimeSpan PollInterval => TimeSpan.FromMilliseconds(PollIntervalMs);
    public TimeSpan Heartbeat => TimeSpan.FromSeconds(HeartbeatSeconds);
    public TimeSpan RecoveryInterval => TimeSpan.FromSeconds(RecoveryIntervalSeconds);
    public TimeSpan ShutdownGrace => TimeSpan.FromSeconds(ShutdownGraceSeconds);

    /// <summary>Identidade estável durante a vida do processo; sem configuração, máquina + sufixo aleatório (dois processos nunca compartilham id).</summary>
    public string ResolveWorkerId() => string.IsNullOrWhiteSpace(WorkerId) ? $"{Environment.MachineName}-{Guid.NewGuid():N}" : WorkerId;

    public void Validate(WorkQueueOptions? queue)
    {
        if (PollIntervalMs is < 10 or > 60_000 || MaxConcurrency is < 1 or > 16 || HeartbeatSeconds < 1 || RecoveryIntervalSeconds is < 1 or > 3600 ||
            ErrorBackoffMaxSeconds is < 1 or > 3600 || ShutdownGraceSeconds is < 1 or > 300 || Api.RequestTimeoutSeconds is < 1 or > 300 ||
            (WorkerId is not null && (WorkerId.Length is 0 or > 100 || WorkerId.Any(char.IsControl))))
            throw new InvalidOperationException("Worker: valores fora do intervalo permitido.");
        if (queue is not null && HeartbeatSeconds * 3 > queue.LeaseSeconds)
            throw new InvalidOperationException("Worker: HeartbeatSeconds deve ser no máximo um terço de WorkQueue:LeaseSeconds.");
    }
}
