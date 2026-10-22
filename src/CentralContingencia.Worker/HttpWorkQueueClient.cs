using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Domain.WorkQueue;

namespace CentralContingencia.Worker;

/// <summary>
/// Transporte HTTPS do worker remoto: implementa as mesmas portas que o modo local (banco direto), mas só fala com /api/v1/agent.
/// O executor é sempre o da credencial; o parâmetro workerId das portas é ignorado. Erros de transporte viram
/// <see cref="WorkQueueException"/> sem texto de rede (que pode conter host), e Retry-After é preservado.
/// </summary>
public sealed class HttpWorkQueueClient(HttpClient http) : IWorkQueueService, IExecutorSession
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<ClaimedWorkTask>> ClaimAsync(string workerId, IReadOnlyCollection<WorkTaskType> types, int max, CancellationToken token) => ClaimCoreAsync(types, max, null, token);

    public Task<IReadOnlyList<ClaimedWorkTask>> ClaimAsync(string workerId, IReadOnlyCollection<WorkTaskType> types, int max, Guid claimKey, CancellationToken token) => ClaimCoreAsync(types, max, claimKey, token);

    private async Task<IReadOnlyList<ClaimedWorkTask>> ClaimCoreAsync(IReadOnlyCollection<WorkTaskType> types, int max, Guid? claimKey, CancellationToken token)
    {
        var list = await Send<AgentTaskList>(HttpMethod.Post, "tasks/claim", new AgentClaimRequest(types.Select(t => t.ToString()).ToArray(), max), token, claimKey);
        return list.Tasks.Select(ToClaimed).ToList();
    }

    public async Task<IReadOnlyList<ClaimedWorkTask>> HeldAsync(string workerId, CancellationToken token) =>
        (await Send<AgentTaskList>(HttpMethod.Get, "tasks/held", null, token)).Tasks.Select(ToClaimed).ToList();

    public async Task<bool> HeartbeatAsync(string workerId, Guid id, int attempt, CancellationToken token) =>
        (await Send<CancellationStatus>(HttpMethod.Post, $"tasks/{id}/heartbeat", new AttemptRequest(attempt), token)).CancelRequested;

    public async Task<bool> IsCancelRequestedAsync(string workerId, Guid id, int attempt, CancellationToken token) =>
        (await Send<CancellationStatus>(HttpMethod.Get, $"tasks/{id}/cancellation?attempt={attempt}", null, token)).CancelRequested;

    public async Task CompleteAsync(string workerId, Guid id, int attempt, string? resultJson, CancellationToken token)
    {
        JsonElement? result = null;
        if (resultJson is not null) { using var document = JsonDocument.Parse(resultJson); result = document.RootElement.Clone(); }
        await Send<object>(HttpMethod.Post, $"tasks/{id}/complete", new AgentCompleteRequest(attempt, result), token);
    }

    public async Task<WorkTaskStatus> FailAsync(string workerId, Guid id, int attempt, string code, string? message, bool transient, CancellationToken token)
    {
        var response = await Send<AgentFailResponse>(HttpMethod.Post, $"tasks/{id}/fail", new AgentFailRequest(attempt, code, message, transient), token);
        return Enum.Parse<WorkTaskStatus>(response.Status);
    }

    public Task ConfirmCancelledAsync(string workerId, Guid id, int attempt, CancellationToken token) =>
        Send<object>(HttpMethod.Post, $"tasks/{id}/cancelled", new AttemptRequest(attempt), token);

    public Task ReleaseAsync(string workerId, Guid id, int attempt, CancellationToken token) =>
        Send<object>(HttpMethod.Post, $"tasks/{id}/release", new AttemptRequest(attempt), token);

    /// <summary>A recuperação de leases vencidos é feita pela API; o worker remoto não a executa.</summary>
    public Task<int> RecoverExpiredAsync(CancellationToken token) => Task.FromResult(0);

    public async Task<ExecutorConfig?> GetConfigAsync(CancellationToken token) => await Send<ExecutorConfig>(HttpMethod.Get, "config", null, token);

    public Task HeartbeatAsync(ExecutorState state, string workerVersion, CancellationToken token) =>
        Send<object>(HttpMethod.Post, "heartbeat", new ExecutorHeartbeatRequest(state.ToString(), workerVersion), token);

    public Task ShutdownAsync(CancellationToken token) => Send<object>(HttpMethod.Post, "shutdown", null, token);

    // Tipos de exceção (nunca mensagens): distingue TLS/certificado, DNS e conexão recusada no log sem vazar host.
    private static string Cause(HttpRequestException error) => error.InnerException is { } inner ? error.GetType().Name + ">" + inner.GetType().Name : error.GetType().Name;

    private static ClaimedWorkTask ToClaimed(AgentTask task) =>
        new(task.Id, Enum.Parse<WorkTaskType>(task.Type), task.PayloadVersion, task.Payload.GetRawText(), task.Attempt, task.MaxAttempts, task.CorrelationId, task.LeaseUntil);

    private async Task<T> Send<T>(HttpMethod method, string path, object? body, CancellationToken token, Guid? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(method, "api/v1/agent/" + path);
        if (idempotencyKey is { } key) request.Headers.Add("Idempotency-Key", key.ToString());
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, token); }
        catch (HttpRequestException error) { throw new WorkQueueException(WorkQueueFailure.Unavailable, error, null, Cause(error)); }
        catch (TaskCanceledException error) when (!token.IsCancellationRequested) { throw new WorkQueueException(WorkQueueFailure.Unavailable, error, null, "timeout"); }
        using (response)
        {
            if (!response.IsSuccessStatusCode) throw await Map(response, token);
            if (typeof(T) == typeof(object) || response.StatusCode == HttpStatusCode.NoContent) return default!;
            try { return await response.Content.ReadFromJsonAsync<T>(Json, token) ?? throw new WorkQueueException(WorkQueueFailure.Unavailable); }
            catch (JsonException error) { throw new WorkQueueException(WorkQueueFailure.Unavailable, error); }
        }
    }

    private static async Task<WorkQueueException> Map(HttpResponseMessage response, CancellationToken token)
    {
        var retryAfter = response.Headers.RetryAfter is { } header ? header.Delta ?? (header.Date is { } date ? date - DateTimeOffset.UtcNow : null) : null;
        if (retryAfter is { } wait) retryAfter = wait < TimeSpan.Zero ? TimeSpan.Zero : wait > TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : wait;
        string? code = null;
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            try { code = (await response.Content.ReadFromJsonAsync<JsonElement>(Json, token)).GetProperty("code").GetString(); }
            catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or HttpRequestException) { }
        }
        var failure = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => WorkQueueFailure.Unauthorized,
            HttpStatusCode.NotFound => WorkQueueFailure.NotFound,
            HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnsupportedMediaType => WorkQueueFailure.Validation,
            HttpStatusCode.Conflict => code == "LeaseLost" ? WorkQueueFailure.LeaseLost : WorkQueueFailure.Conflict,
            _ => WorkQueueFailure.Unavailable, // 429, 5xx e qualquer outro: transitório do ponto de vista do worker
        };
        return new(failure, null, retryAfter, "http_" + (int)response.StatusCode);
    }
}
