using CentralContingencia.Domain.WorkQueue;

namespace CentralContingencia.UnitTests;

public class WorkTaskTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);
    private const string Worker = "worker-a";

    private static WorkTask NewTask(int maxAttempts = 3) =>
        WorkTask.Create(Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), WorkTaskType.SimulatedProcessing, 1, "{\"version\":1}", maxAttempts, "corr-1", T0);

    private static WorkTask Running(int maxAttempts = 3)
    {
        var task = NewTask(maxAttempts);
        task.Claim(Worker, T0, Lease);
        return task;
    }

    [Fact]
    public void CreateStartsPendingAndAvailable()
    {
        var task = NewTask();
        Assert.Equal(WorkTaskStatus.Pending, task.Status);
        Assert.Equal(0, task.AttemptCount);
        Assert.Equal(T0, task.AvailableAt);
        Assert.Null(task.WorkerId);
        Assert.Null(task.LeaseUntil);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void CreateRejectsMaxAttemptsOutOfRange(int max) => Assert.Throws<ArgumentOutOfRangeException>(() => NewTask(max));

    [Fact]
    public void CreateRejectsEmptyIdsAndBadFingerprint()
    {
        Assert.Throws<ArgumentException>(() => WorkTask.Create(Guid.Empty, Guid.NewGuid(), new string('a', 64), WorkTaskType.SimulatedProcessing, 1, "{}", 3, "c", T0));
        Assert.Throws<ArgumentException>(() => WorkTask.Create(Guid.NewGuid(), Guid.Empty, new string('a', 64), WorkTaskType.SimulatedProcessing, 1, "{}", 3, "c", T0));
        Assert.Throws<ArgumentException>(() => WorkTask.Create(Guid.NewGuid(), Guid.NewGuid(), "curto", WorkTaskType.SimulatedProcessing, 1, "{}", 3, "c", T0));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkTask.Create(Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), (WorkTaskType)99, 1, "{}", 3, "c", T0));
    }

    [Fact]
    public void ClaimSetsLeaseAttemptAndOwner()
    {
        var task = Running();
        Assert.Equal(WorkTaskStatus.Running, task.Status);
        Assert.Equal(Worker, task.WorkerId);
        Assert.Equal(1, task.AttemptCount);
        Assert.Equal(T0 + Lease, task.LeaseUntil);
        Assert.Equal(T0, task.HeartbeatAt);
        Assert.Equal(T0, task.StartedAt);
    }

    [Fact]
    public void ClaimIsRefusedBeforeBackoffEndsAndForNonPending()
    {
        var task = Running();
        task.Fail(Worker, 1, T0, "x", null, true, T0.AddSeconds(30));
        Assert.Equal(WorkTaskStatus.Pending, task.Status);
        Assert.Equal(WorkTaskError.InvalidTransition, Assert.Throws<WorkTaskStateException>(() => task.Claim("w2", T0.AddSeconds(10), Lease)).Error);
        task.Claim("w2", T0.AddSeconds(30), Lease);
        Assert.Equal(2, task.AttemptCount);
        Assert.Throws<WorkTaskStateException>(() => task.Claim("w3", T0.AddSeconds(31), Lease));
    }

    [Fact]
    public void HeartbeatExtendsLeaseAndNeverShortensIt()
    {
        var task = Running();
        Assert.False(task.Heartbeat(Worker, 1, T0.AddSeconds(50), Lease));
        Assert.Equal(T0.AddSeconds(110), task.LeaseUntil);
        task.Heartbeat(Worker, 1, T0.AddSeconds(51), TimeSpan.FromSeconds(5));
        Assert.Equal(T0.AddSeconds(110), task.LeaseUntil);
        Assert.Equal(T0.AddSeconds(51), task.HeartbeatAt);
    }

    [Fact]
    public void HeartbeatReportsCancelRequest()
    {
        var task = Running();
        task.RequestCancel(Guid.NewGuid(), T0.AddSeconds(5));
        Assert.True(task.Heartbeat(Worker, 1, T0.AddSeconds(10), Lease));
    }

    [Fact]
    public void OperationsRequireTheCurrentLease()
    {
        var task = Running();
        void Lost(Action action) => Assert.Equal(WorkTaskError.LeaseLost, Assert.Throws<WorkTaskStateException>(action).Error);
        Lost(() => task.Heartbeat("other", 1, T0.AddSeconds(1), Lease));
        Lost(() => task.Heartbeat(Worker, 2, T0.AddSeconds(1), Lease));
        Lost(() => task.Succeed("other", 1, T0.AddSeconds(1), null));
        Lost(() => task.Fail(Worker, 9, T0.AddSeconds(1), "x", null, true, T0));
        Lost(() => task.Release("other", 1, T0.AddSeconds(1)));
        // Lease vencido, mesmo sem recuperação, também é perda de posse.
        Lost(() => task.Succeed(Worker, 1, T0 + Lease, null));
        Assert.Equal(WorkTaskStatus.Running, task.Status);
    }

    [Fact]
    public void SucceedIsTerminalAndClearsLease()
    {
        var task = Running();
        task.Succeed(Worker, 1, T0.AddSeconds(5), "{\"ok\":true}");
        Assert.Equal(WorkTaskStatus.Succeeded, task.Status);
        Assert.True(task.IsTerminal);
        Assert.Equal("{\"ok\":true}", task.Result);
        Assert.Null(task.WorkerId); Assert.Null(task.LeaseUntil); Assert.Equal(T0.AddSeconds(5), task.CompletedAt);
        Assert.Throws<WorkTaskStateException>(() => task.Succeed(Worker, 1, T0.AddSeconds(6), null));
    }

    [Fact]
    public void PermanentFailureEndsInFailed()
    {
        var task = Running();
        Assert.Equal(WorkTaskStatus.Failed, task.Fail(Worker, 1, T0.AddSeconds(1), "bad_input", "Entrada inválida.", false, T0));
        Assert.Equal("bad_input", task.LastErrorCode);
    }

    [Fact]
    public void TransientFailureRetriesWithBackoffThenDeadLetters()
    {
        var task = Running(maxAttempts: 2);
        Assert.Equal(WorkTaskStatus.Pending, task.Fail(Worker, 1, T0.AddSeconds(1), "net", null, true, T0.AddSeconds(31)));
        Assert.Equal(T0.AddSeconds(31), task.AvailableAt);
        Assert.Null(task.WorkerId);
        task.Claim("w2", T0.AddSeconds(31), Lease);
        Assert.Equal(WorkTaskStatus.DeadLetter, task.Fail("w2", 2, T0.AddSeconds(32), "net", null, true, T0.AddSeconds(100)));
        Assert.True(task.IsTerminal);
        Assert.Equal(2, task.AttemptCount);
    }

    [Fact]
    public void RetryDelayInThePastBecomesImmediate()
    {
        var task = Running();
        task.Fail(Worker, 1, T0.AddSeconds(10), "net", null, true, T0);
        Assert.Equal(T0.AddSeconds(10), task.AvailableAt);
    }

    [Fact]
    public void ErrorIsSanitized()
    {
        var task = Running();
        task.Fail(Worker, 1, T0.AddSeconds(1), "Bad Code!", "falha password=hunter2 host=db.internal user@example.com abcdefghijklmnopqrstuvwxyz0123456789\nlinha", false, T0);
        Assert.Equal("error", task.LastErrorCode);
        Assert.DoesNotContain("hunter2", task.LastError);
        Assert.DoesNotContain("db.internal", task.LastError);
        Assert.DoesNotContain("example.com", task.LastError);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", task.LastError);
        Assert.DoesNotContain("\n", task.LastError);
        Assert.True(task.LastError!.Length <= WorkErrorSanitizer.MaxMessageLength);
    }

    [Fact]
    public void LongMessageIsTruncated() => Assert.Equal(WorkErrorSanitizer.MaxMessageLength, WorkErrorSanitizer.Message(string.Join(" ", Enumerable.Repeat("palavra", 200))).Length);

    [Fact]
    public void CancelPendingIsImmediate()
    {
        var task = NewTask();
        var actor = Guid.NewGuid();
        task.RequestCancel(actor, T0.AddSeconds(1));
        Assert.Equal(WorkTaskStatus.Cancelled, task.Status);
        Assert.Equal(actor, task.CancelRequestedBy);
        Assert.Throws<WorkTaskStateException>(() => task.Claim(Worker, T0.AddSeconds(2), Lease));
        task.RequestCancel(actor, T0.AddSeconds(3)); // idempotente
    }

    [Fact]
    public void CancelRunningIsCooperative()
    {
        var task = Running();
        task.RequestCancel(Guid.NewGuid(), T0.AddSeconds(2));
        Assert.Equal(WorkTaskStatus.Running, task.Status);
        Assert.True(task.CancelRequested);
        task.ConfirmCancelled(Worker, 1, T0.AddSeconds(3));
        Assert.Equal(WorkTaskStatus.Cancelled, task.Status);
    }

    [Fact]
    public void ConfirmCancelledWithoutRequestIsRefused()
    {
        var task = Running();
        Assert.Equal(WorkTaskError.InvalidTransition, Assert.Throws<WorkTaskStateException>(() => task.ConfirmCancelled(Worker, 1, T0.AddSeconds(1))).Error);
    }

    [Fact]
    public void PendingCancelRequestWinsOverRetryAndRelease()
    {
        var failing = Running();
        failing.RequestCancel(Guid.NewGuid(), T0.AddSeconds(1));
        Assert.Equal(WorkTaskStatus.Cancelled, failing.Fail(Worker, 1, T0.AddSeconds(2), "net", null, true, T0));
        var released = Running();
        released.RequestCancel(Guid.NewGuid(), T0.AddSeconds(1));
        Assert.Equal(WorkTaskStatus.Cancelled, released.Release(Worker, 1, T0.AddSeconds(2)));
    }

    [Fact]
    public void CancelAfterTerminalIsConflictExceptWhenAlreadyCancelled()
    {
        var done = Running();
        done.Succeed(Worker, 1, T0.AddSeconds(1), null);
        Assert.Throws<WorkTaskStateException>(() => done.RequestCancel(Guid.NewGuid(), T0.AddSeconds(2)));
        var dead = Running(1);
        dead.Fail(Worker, 1, T0.AddSeconds(1), "x", null, true, T0);
        Assert.Equal(WorkTaskStatus.DeadLetter, dead.Status);
        Assert.Throws<WorkTaskStateException>(() => dead.RequestCancel(Guid.NewGuid(), T0.AddSeconds(2)));
    }

    [Fact]
    public void ReleaseReturnsToQueueWithoutConsumingTheAttempt()
    {
        var task = Running();
        Assert.Equal(WorkTaskStatus.Pending, task.Release(Worker, 1, T0.AddSeconds(5)));
        Assert.Equal(0, task.AttemptCount);
        Assert.Null(task.WorkerId);
    }

    [Fact]
    public void RecoverExpiredLeaseRequeuesThenDeadLetters()
    {
        var task = Running(maxAttempts: 2);
        Assert.Throws<WorkTaskStateException>(() => task.RecoverExpiredLease(T0.AddSeconds(30), T0));
        Assert.Equal(WorkTaskStatus.Pending, task.RecoverExpiredLease(T0 + Lease, T0 + Lease));
        Assert.Equal("lease_expired", task.LastErrorCode);
        task.Claim("w2", T0 + Lease, Lease);
        Assert.Equal(WorkTaskStatus.DeadLetter, task.RecoverExpiredLease(T0 + Lease + Lease, T0 + Lease + Lease));
    }

    [Fact]
    public void RecoverExpiredLeaseHonorsCancelRequest()
    {
        var task = Running();
        task.RequestCancel(Guid.NewGuid(), T0.AddSeconds(1));
        Assert.Equal(WorkTaskStatus.Cancelled, task.RecoverExpiredLease(T0 + Lease, T0 + Lease));
    }

    [Fact]
    public void ReleasedAttemptsCannotUnderflow()
    {
        var task = Running();
        task.Release(Worker, 1, T0.AddSeconds(1));
        Assert.Throws<WorkTaskStateException>(() => task.Release(Worker, 0, T0.AddSeconds(2)));
        Assert.Equal(0, task.AttemptCount);
    }

    [Fact]
    public void AuditEventCarriesOnlyIdsAndSanitizedCode()
    {
        var task = Running();
        var evt = WorkTaskEvent.Create(task, WorkTaskAction.Claimed, null, Worker, T0, "Bad Code!");
        Assert.Equal("error", evt.Code);
        Assert.Equal(task.Id, evt.TaskId);
        Assert.Equal(1, evt.Attempt);
    }
}
