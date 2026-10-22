using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CentralContingencia.Domain.WorkQueue;

namespace CentralContingencia.Application.WorkQueue;

public sealed class WorkTaskService(IWorkTaskStore store, IExecutorStore executors, WorkQueueOptions options, TimeProvider clock) : IWorkTaskService
{
    public const int MaxPageSize = 100;
    public const int MaxPage = 10000;

    public async Task<CreatedWorkTask> CreateAsync(Guid actorId, Guid idempotencyKey, CreateWorkTaskRequest request, string correlationId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (actorId == Guid.Empty || idempotencyKey == Guid.Empty || !WorkTaskPayloads.TryParseType(request.Type, out var type))
            throw new WorkQueueException(WorkQueueFailure.Validation);
        var maxAttempts = request.MaxAttempts ?? options.DefaultMaxAttempts;
        if (maxAttempts is < 1 or > WorkTask.MaxAttemptsLimit) throw new WorkQueueException(WorkQueueFailure.Validation);
        ValidatedPayload payload;
        try { payload = WorkTaskPayloads.Validate(type, request.Payload); }
        catch (WorkTaskPayloadException) { throw new WorkQueueException(WorkQueueFailure.Validation); }
        WorkTask task;
        try { task = WorkTask.Create(actorId, idempotencyKey, Fingerprint(type, maxAttempts, payload.Json), type, payload.Version, payload.Json, maxAttempts, correlationId, clock.GetUtcNow()); }
        catch (ArgumentException) { throw new WorkQueueException(WorkQueueFailure.Validation); }
        var stored = await store.CreateAsync(task, clock.GetUtcNow(), token);
        return new((await Respond([stored.Task], token))[0], stored.Created);
    }

    public async Task<WorkTaskResponse> GetAsync(Guid id, CancellationToken token) =>
        (await Respond([await store.GetAsync(id, token) ?? throw new WorkQueueException(WorkQueueFailure.NotFound)], token))[0];

    public async Task<WorkTaskPage> ListAsync(string? status, string? type, int page, int pageSize, CancellationToken token)
    {
        if (page is < 1 or > MaxPage || pageSize is < 1 or > MaxPageSize) throw new WorkQueueException(WorkQueueFailure.Validation);
        WorkTaskStatus? parsedStatus = null; WorkTaskType? parsedType = null;
        if (status is not null)
            parsedStatus = Enum.GetNames<WorkTaskStatus>().Contains(status) ? Enum.Parse<WorkTaskStatus>(status) : throw new WorkQueueException(WorkQueueFailure.Validation);
        if (type is not null)
            parsedType = WorkTaskPayloads.TryParseType(type, out var known) ? known : throw new WorkQueueException(WorkQueueFailure.Validation);
        var (items, total) = await store.ListAsync(new(parsedStatus, parsedType, page, pageSize), token);
        return new(await Respond(items, token), page, pageSize, total);
    }

    public async Task<WorkTaskResponse> CancelAsync(Guid actorId, Guid id, CancellationToken token) =>
        (await Respond([await store.RequestCancelAsync(id, actorId, clock.GetUtcNow(), token)], token))[0];

    internal static string Fingerprint(WorkTaskType type, int maxAttempts, string canonicalPayload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{type}|{maxAttempts}|{canonicalPayload}"))).ToLowerInvariant();

    private async Task<IReadOnlyList<WorkTaskResponse>> Respond(IReadOnlyList<WorkTask> tasks, CancellationToken token)
    {
        var ids = tasks.Select(t => t.LastWorkerId).Where(id => id is not null && Guid.TryParseExact(id, "D", out _)).Select(id => id!).Distinct().ToList();
        var names = ids.Count == 0 ? new Dictionary<string, string>() : (await executors.NamesAsync(ids, token)).ToDictionary(p => p.Key, p => p.Value);
        return tasks.Select(task =>
        {
            var isExecutor = Guid.TryParseExact(task.LastWorkerId, "D", out var executorId);
            var activity = new[] { task.HeartbeatAt, task.CompletedAt, task.StartedAt, task.CreatedAt }.Max()!.Value;
            return new WorkTaskResponse(task.Id, task.Type.ToString(), task.Status.ToString(), task.PayloadVersion, Parse(task.Payload)!.Value, Parse(task.Result),
                task.CreatedAt, task.StartedAt, task.CompletedAt, task.Status == WorkTaskStatus.Pending ? task.AvailableAt : null, task.AttemptCount, task.MaxAttempts,
                task.LastErrorCode, task.CorrelationId, task.CancelRequested, isExecutor ? executorId : null, isExecutor ? names.GetValueOrDefault(task.LastWorkerId!) : null, activity);
        }).ToList();
    }

    private static JsonElement? Parse(string? json)
    {
        if (json is null) return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

public sealed class WorkQueueService(IWorkTaskStore store, WorkQueueOptions options, TimeProvider clock) : IWorkQueueService
{
    public const int MaxClaim = 50;

    public Task<IReadOnlyList<ClaimedWorkTask>> ClaimAsync(string workerId, IReadOnlyCollection<WorkTaskType> types, int max, CancellationToken token) => ClaimCoreAsync(workerId, types, max, null, token);

    public Task<IReadOnlyList<ClaimedWorkTask>> ClaimAsync(string workerId, IReadOnlyCollection<WorkTaskType> types, int max, Guid claimKey, CancellationToken token) =>
        claimKey == Guid.Empty ? throw new WorkQueueException(WorkQueueFailure.Validation) : ClaimCoreAsync(workerId, types, max, claimKey, token);

    private async Task<IReadOnlyList<ClaimedWorkTask>> ClaimCoreAsync(string workerId, IReadOnlyCollection<WorkTaskType> types, int max, Guid? claimKey, CancellationToken token)
    {
        if (types.Count == 0 || types.Any(t => !Enum.IsDefined(t)) || max is < 1 or > MaxClaim) throw new WorkQueueException(WorkQueueFailure.Validation);
        var claimed = await Guard(() => store.ClaimAsync(workerId, types, max, clock.GetUtcNow(), options.Lease, claimKey, token));
        return claimed.Select(t => new ClaimedWorkTask(t.Id, t.Type, t.PayloadVersion, t.Payload, t.AttemptCount, t.MaxAttempts, t.CorrelationId, t.LeaseUntil!.Value)).ToList();
    }

    public Task<bool> HeartbeatAsync(string workerId, Guid id, int attempt, CancellationToken token) =>
        Guard(() => store.HeartbeatAsync(id, workerId, attempt, clock.GetUtcNow(), options.Lease, token));

    public Task CompleteAsync(string workerId, Guid id, int attempt, string? resultJson, CancellationToken token) =>
        Guard(() => store.CompleteAsync(id, workerId, attempt, resultJson, clock.GetUtcNow(), token));

    public Task<WorkTaskStatus> FailAsync(string workerId, Guid id, int attempt, string code, string? message, bool transient, CancellationToken token) =>
        Guard(() => store.FailAsync(id, workerId, attempt, code, message, transient, clock.GetUtcNow(), options.RetryDelay, token));

    public Task ConfirmCancelledAsync(string workerId, Guid id, int attempt, CancellationToken token) =>
        Guard(() => store.ConfirmCancelledAsync(id, workerId, attempt, clock.GetUtcNow(), token));

    public Task ReleaseAsync(string workerId, Guid id, int attempt, CancellationToken token) =>
        Guard(() => store.ReleaseAsync(id, workerId, attempt, clock.GetUtcNow(), token));

    public Task<int> RecoverExpiredAsync(CancellationToken token) =>
        Guard(() => store.RecoverExpiredAsync(clock.GetUtcNow(), options.RetryDelay, 100, token));

    public async Task<IReadOnlyList<ClaimedWorkTask>> HeldAsync(string workerId, CancellationToken token) =>
        (await Guard(() => store.ListHeldAsync(workerId, clock.GetUtcNow(), token))).Select(t => new ClaimedWorkTask(t.Id, t.Type, t.PayloadVersion, t.Payload, t.AttemptCount, t.MaxAttempts, t.CorrelationId, t.LeaseUntil!.Value)).ToList();

    public Task<bool> IsCancelRequestedAsync(string workerId, Guid id, int attempt, CancellationToken token) =>
        Guard(() => store.IsCancelRequestedAsync(id, workerId, attempt, clock.GetUtcNow(), token));

    private static async Task<T> Guard<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (ArgumentException) { throw new WorkQueueException(WorkQueueFailure.Validation); }
    }
    private static async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (ArgumentException) { throw new WorkQueueException(WorkQueueFailure.Validation); }
    }
}
