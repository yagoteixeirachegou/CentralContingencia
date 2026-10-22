using CentralContingencia.Domain.WorkQueue;

namespace CentralContingencia.Application.WorkQueue;

/// <summary>Seção <c>WorkQueue</c>. Lease, tentativas e backoff são política do servidor; o cliente não os escolhe (exceto maxAttempts dentro do limite).</summary>
public sealed class WorkQueueOptions
{
    public const string Section = "WorkQueue";
    public int LeaseSeconds { get; set; } = 60;
    public int RetryBaseDelaySeconds { get; set; } = 5;
    public int RetryMaxDelaySeconds { get; set; } = 300;
    public int DefaultMaxAttempts { get; set; } = 3;
    /// <summary>Janela em que um executor sem comunicação ainda conta como online.</summary>
    public int ExecutorOnlineSeconds { get; set; } = 60;
    /// <summary>Intervalo de heartbeat recomendado aos executores remotos.</summary>
    public int ExecutorHeartbeatSeconds { get; set; } = 20;
    public int ExecutorMaxClaim { get; set; } = 5;

    public TimeSpan Lease => TimeSpan.FromSeconds(LeaseSeconds);

    /// <summary>Backoff exponencial determinístico: base · 2^(tentativa-1), limitado ao máximo.</summary>
    public TimeSpan RetryDelay(int attempt)
    {
        var exponent = Math.Clamp(attempt - 1, 0, 20);
        var seconds = Math.Min((double)RetryMaxDelaySeconds, RetryBaseDelaySeconds * Math.Pow(2, exponent));
        return TimeSpan.FromSeconds(seconds);
    }

    public void Validate()
    {
        if (LeaseSeconds is < 5 or > 3600 || RetryBaseDelaySeconds is < 0 or > 3600 || RetryMaxDelaySeconds is < 0 or > 86400 ||
            RetryMaxDelaySeconds < RetryBaseDelaySeconds || DefaultMaxAttempts is < 1 or > WorkTask.MaxAttemptsLimit ||
            ExecutorHeartbeatSeconds < 1 || ExecutorHeartbeatSeconds * 3 > LeaseSeconds || ExecutorOnlineSeconds < ExecutorHeartbeatSeconds * 2 || ExecutorOnlineSeconds > 86400 ||
            ExecutorMaxClaim is < 1 or > 50)
            throw new InvalidOperationException("WorkQueue: valores fora do intervalo permitido.");
    }
}
