using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CentralContingencia.Api.Integration;
using CentralContingencia.Api.WorkQueue;
using CentralContingencia.Application.WorkQueue;
using CentralContingencia.Domain.WorkQueue;
using CentralContingencia.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CentralContingencia.UnitTests;

public class AuthFailureThrottleTests
{
    private static readonly IPAddress Attacker = IPAddress.Parse("203.0.113.9");
    private static readonly IPAddress Legit = IPAddress.Parse("203.0.113.10");

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan span) => now += span;
    }

    [Fact]
    public void BlocksOnlyAfterTheLimitAndOnlyThatClientAndScope()
    {
        var throttle = new AuthFailureThrottle(new Clock(), 3, TimeSpan.FromSeconds(60));
        for (var i = 0; i < 2; i++) throttle.RecordFailure("executor", Attacker);
        Assert.Null(throttle.BlockedFor("executor", Attacker));
        throttle.RecordFailure("executor", Attacker);
        Assert.NotNull(throttle.BlockedFor("executor", Attacker));
        Assert.Null(throttle.BlockedFor("executor", Legit));
        Assert.Null(throttle.BlockedFor("integration", Attacker));
    }

    [Fact]
    public void BlockExpiresWithTheWindowAndReportsRemainingTime()
    {
        var clock = new Clock(); var throttle = new AuthFailureThrottle(clock, 2, TimeSpan.FromSeconds(60));
        throttle.RecordFailure("executor", Attacker); throttle.RecordFailure("executor", Attacker);
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(TimeSpan.FromSeconds(40), throttle.BlockedFor("executor", Attacker));
        clock.Advance(TimeSpan.FromSeconds(41));
        Assert.Null(throttle.BlockedFor("executor", Attacker));
        throttle.RecordFailure("executor", Attacker); // nova janela começa do zero
        Assert.Null(throttle.BlockedFor("executor", Attacker));
    }

    [Fact]
    public void Ipv6IsGroupedBy64AndMappedIpv4CountsAsIpv4()
    {
        var throttle = new AuthFailureThrottle(new Clock(), 2, TimeSpan.FromSeconds(60));
        throttle.RecordFailure("x", IPAddress.Parse("2001:db8:1:2::1")); throttle.RecordFailure("x", IPAddress.Parse("2001:db8:1:2:ffff::9"));
        Assert.NotNull(throttle.BlockedFor("x", IPAddress.Parse("2001:db8:1:2:abcd::1")));
        Assert.Null(throttle.BlockedFor("x", IPAddress.Parse("2001:db8:1:3::1")));
        throttle.RecordFailure("x", IPAddress.Parse("::ffff:198.51.100.5")); throttle.RecordFailure("x", IPAddress.Parse("198.51.100.5"));
        Assert.NotNull(throttle.BlockedFor("x", IPAddress.Parse("198.51.100.5")));
    }

    [Fact]
    public void UnknownAddressIsCountedTooAndMemoryIsBounded()
    {
        var throttle = new AuthFailureThrottle(new Clock(), 1, TimeSpan.FromSeconds(60));
        throttle.RecordFailure("x", null);
        Assert.NotNull(throttle.BlockedFor("x", null));
        for (var i = 0; i < AuthFailureThrottle.MaxEntries + 500; i++) throttle.RecordFailure("x", new IPAddress(new byte[] { 10, (byte)(i >> 16), (byte)(i >> 8), (byte)i }));
        // não explode nem cresce sem limite; o cliente mais recente continua contado
        Assert.Null(throttle.BlockedFor("x", IPAddress.Parse("192.0.2.1")));
    }

    [Fact]
    public void InvalidSettingsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthFailureThrottle(TimeProvider.System, 0, TimeSpan.FromSeconds(60)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AuthFailureThrottle(TimeProvider.System, 5, TimeSpan.Zero));
    }
}

public class TlsTrustTests : IDisposable
{
    private readonly X509Certificate2 ca, otherCa, leaf, wrongNameLeaf;
    private readonly string pemPath = Path.Combine(Path.GetTempPath(), "p6-ca-" + Guid.NewGuid().ToString("N") + ".pem");

    public TlsTrustTests()
    {
        ca = NewCa("CN=p6-test-ca"); otherCa = NewCa("CN=p6-other-ca");
        leaf = NewLeaf(ca, "localhost"); wrongNameLeaf = NewLeaf(ca, "outro.invalid");
        File.WriteAllText(pemPath, ca.ExportCertificatePem());
    }
    public void Dispose() { ca.Dispose(); otherCa.Dispose(); leaf.Dispose(); wrongNameLeaf.Dispose(); File.Delete(pemPath); }

    private static X509Certificate2 NewCa(string subject, int daysBack = 1)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-daysBack), DateTimeOffset.UtcNow.AddDays(30));
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx, "t"), "t");
    }

    private static X509Certificate2 NewLeaf(X509Certificate2 issuer, string dns)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + dns, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName(dns);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var signed = request.Create(issuer, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10), RandomNumberGenerator.GetBytes(8));
        return X509CertificateLoader.LoadPkcs12(signed.CopyWithPrivateKey(key).Export(X509ContentType.Pfx, "t"), "t");
    }

    [Fact]
    public void ChainSignedByTheConfiguredAuthorityIsAcceptedWhenTheSystemDoesNotTrustIt()
    {
        var roots = TlsTrust.LoadAuthorities(pemPath);
        Assert.True(TlsTrust.Validate(roots, leaf, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void ChainFromAnotherAuthorityIsRejected()
    {
        var roots = new X509Certificate2Collection(otherCa);
        Assert.False(TlsTrust.Validate(roots, leaf, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void HostNameMismatchIsNeverForgivenEvenWithATrustedAuthority()
    {
        var roots = TlsTrust.LoadAuthorities(pemPath);
        Assert.False(TlsTrust.Validate(roots, wrongNameLeaf, null, SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.False(TlsTrust.Validate(roots, leaf, null, SslPolicyErrors.RemoteCertificateNotAvailable));
        Assert.False(TlsTrust.Validate(roots, null, null, SslPolicyErrors.None));
    }

    [Fact]
    public void PublicallyTrustedCertificatesStillPassWithoutErrors() =>
        Assert.True(TlsTrust.Validate(TlsTrust.LoadAuthorities(pemPath), leaf, null, SslPolicyErrors.None));

    [Fact]
    public void ExpiredCertificateFromTheTrustedAuthorityIsRejected()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var oldCa = NewCa("CN=p6-old-ca", 40);
        using var expired = request.Create(oldCa, DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-5), RandomNumberGenerator.GetBytes(8));
        Assert.False(TlsTrust.Validate(new X509Certificate2Collection(oldCa), expired, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void BadAuthorityFilesAreRefusedWithoutEchoingTheirContent()
    {
        var empty = Path.GetTempFileName();
        try
        {
            File.WriteAllText(empty, "SEGREDO-NAO-ECOAR");
            var error = Assert.Throws<InvalidOperationException>(() => TlsTrust.LoadAuthorities(empty));
            Assert.DoesNotContain("SEGREDO", error.Message);
            Assert.Throws<InvalidOperationException>(() => TlsTrust.LoadAuthorities(empty + ".missing"));
        }
        finally { File.Delete(empty); }
    }
}

public class ClaimKeyDomainTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static WorkTask New() => WorkTask.Create(Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), WorkTaskType.SimulatedProcessing, 1, "{\"version\":1}", 3, "c", T0);

    [Fact]
    public void ClaimRemembersTheKey()
    {
        var task = New(); var key = Guid.NewGuid();
        task.Claim("ex", T0, TimeSpan.FromSeconds(60), key);
        Assert.Equal(key, task.ClaimKey);
    }

    [Fact]
    public void ClaimWithoutKeyAndEmptyKeyBehave()
    {
        var plain = New(); plain.Claim("ex", T0, TimeSpan.FromSeconds(60));
        Assert.Null(plain.ClaimKey);
        Assert.Throws<ArgumentException>(() => New().Claim("ex", T0, TimeSpan.FromSeconds(60), Guid.Empty));
    }

    [Fact]
    public void ANewClaimReplacesThePreviousKey()
    {
        var task = New(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        task.Claim("ex", T0, TimeSpan.FromSeconds(60), first);
        task.Fail("ex", 1, T0.AddSeconds(1), "net", null, true, T0.AddSeconds(2));
        task.Claim("ex", T0.AddSeconds(2), TimeSpan.FromSeconds(60), second);
        Assert.Equal(second, task.ClaimKey);
    }
}

public sealed class FlakyRecoveryQueue(int failures, Func<CancellationToken, Task>? hang = null) : IWorkQueueService
{
    private int calls;
    public int Calls => calls;
    public int Successes;
    public Task<int> RecoverExpiredAsync(CancellationToken token)
    {
        var call = Interlocked.Increment(ref calls);
        if (call <= failures) throw new WorkQueueException(WorkQueueFailure.Unavailable);
        if (call == failures + 1 && hang is not null) return hang(token).ContinueWith(_ => 0, TaskScheduler.Default);
        Interlocked.Increment(ref Successes);
        return Task.FromResult(1);
    }
    public Task<IReadOnlyList<ClaimedWorkTask>> ClaimAsync(string workerId, IReadOnlyCollection<WorkTaskType> types, int max, CancellationToken token) => Task.FromResult<IReadOnlyList<ClaimedWorkTask>>([]);
    public Task<IReadOnlyList<ClaimedWorkTask>> HeldAsync(string workerId, CancellationToken token) => Task.FromResult<IReadOnlyList<ClaimedWorkTask>>([]);
    public Task<bool> HeartbeatAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.FromResult(false);
    public Task<bool> IsCancelRequestedAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.FromResult(false);
    public Task CompleteAsync(string workerId, Guid id, int attempt, string? resultJson, CancellationToken token) => Task.CompletedTask;
    public Task<WorkTaskStatus> FailAsync(string workerId, Guid id, int attempt, string code, string? message, bool transient, CancellationToken token) => Task.FromResult(WorkTaskStatus.Failed);
    public Task ConfirmCancelledAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.CompletedTask;
    public Task ReleaseAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.CompletedTask;
}

public class RecoveryServiceTests
{
    private static WorkQueueRecoveryService Build(IWorkQueueService queue, TimeSpan? timeout = null)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => queue);
        return new WorkQueueRecoveryService(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), TimeSpan.FromMilliseconds(40),
            NullLogger<WorkQueueRecoveryService>.Instance, timeout);
    }

    private static async Task Until(Func<bool> condition, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline && !condition()) await Task.Delay(20);
    }

    [Fact]
    public async Task RunsPeriodicallyWithoutAnyTrafficAndSurvivesFailures()
    {
        var queue = new FlakyRecoveryQueue(failures: 3);
        var service = Build(queue);
        await service.StartAsync(CancellationToken.None);
        await Until(() => queue.Successes >= 3);
        await service.StopAsync(CancellationToken.None);
        Assert.True(queue.Calls >= 6);
        Assert.True(queue.Successes >= 3);
    }

    [Fact]
    public async Task AHungDatabaseCallIsCancelledByTheRunTimeoutAndTheLoopContinues()
    {
        var queue = new FlakyRecoveryQueue(failures: 0, hang: async token => { await Task.Delay(Timeout.Infinite, token).ContinueWith(_ => { }); });
        var service = Build(queue, TimeSpan.FromMilliseconds(150));
        await service.StartAsync(CancellationToken.None);
        await Until(() => queue.Successes >= 2);
        await service.StopAsync(CancellationToken.None);
        Assert.True(queue.Successes >= 2);
    }

    [Fact]
    public async Task StopsPromptlyOnShutdown()
    {
        var queue = new FlakyRecoveryQueue(failures: 0);
        var service = Build(queue);
        await service.StartAsync(CancellationToken.None);
        await Task.Delay(120);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await service.StopAsync(CancellationToken.None);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
    }
}

public sealed class KeyedQueue : IWorkQueueService
{
    public List<Guid?> Keys { get; } = [];
    public Func<int, Exception?> Fail { get; set; } = _ => null;
    public Task<IReadOnlyList<ClaimedWorkTask>> ClaimAsync(string workerId, IReadOnlyCollection<WorkTaskType> types, int max, CancellationToken token)
    { lock (Keys) Keys.Add(null); return Task.FromResult<IReadOnlyList<ClaimedWorkTask>>([]); }
    public Task<IReadOnlyList<ClaimedWorkTask>> ClaimAsync(string workerId, IReadOnlyCollection<WorkTaskType> types, int max, Guid claimKey, CancellationToken token)
    {
        int index; lock (Keys) { Keys.Add(claimKey); index = Keys.Count; }
        return Fail(index) is { } error ? Task.FromException<IReadOnlyList<ClaimedWorkTask>>(error) : Task.FromResult<IReadOnlyList<ClaimedWorkTask>>([]);
    }
    public Task<IReadOnlyList<ClaimedWorkTask>> HeldAsync(string workerId, CancellationToken token) => Task.FromResult<IReadOnlyList<ClaimedWorkTask>>([]);
    public Task<bool> HeartbeatAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.FromResult(false);
    public Task<bool> IsCancelRequestedAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.FromResult(false);
    public Task CompleteAsync(string workerId, Guid id, int attempt, string? resultJson, CancellationToken token) => Task.CompletedTask;
    public Task<WorkTaskStatus> FailAsync(string workerId, Guid id, int attempt, string code, string? message, bool transient, CancellationToken token) => Task.FromResult(WorkTaskStatus.Failed);
    public Task ConfirmCancelledAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.CompletedTask;
    public Task ReleaseAsync(string workerId, Guid id, int attempt, CancellationToken token) => Task.CompletedTask;
    public Task<int> RecoverExpiredAsync(CancellationToken token) => Task.FromResult(0);
}

public class WorkerClaimKeyTests
{
    private static async Task Until(Func<bool> condition, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline && !condition()) await Task.Delay(20);
    }

    private static WorkerService Build(KeyedQueue queue)
    {
        var services = new ServiceCollection();
        services.AddScoped<IWorkQueueService>(_ => queue);
        var provider = services.BuildServiceProvider();
        return new WorkerService(provider.GetRequiredService<IServiceScopeFactory>(),
            [new DelegateExecutor((_, _) => Task.FromResult<TaskOutcome>(new TaskOutcome.Success(null)))],
            new WorkerOptions { PollIntervalMs = 20, ErrorBackoffMaxSeconds = 1, HeartbeatSeconds = 1, ShutdownGraceSeconds = 5 }, TimeProvider.System, NullLogger<WorkerService>.Instance);
    }

    [Fact]
    public async Task AFailedClaimIsRetriedWithTheSameKeyAndASuccessfulOneRotatesIt()
    {
        var queue = new KeyedQueue { Fail = call => call <= 2 ? new WorkQueueException(WorkQueueFailure.Unavailable) : null };
        var worker = Build(queue);
        await worker.StartAsync(CancellationToken.None);
        await Until(() => { lock (queue.Keys) return queue.Keys.Count >= 5; });
        await worker.StopAsync(CancellationToken.None);
        var keys = queue.Keys.ToList();
        Assert.All(keys, key => Assert.NotNull(key));
        Assert.Equal(keys[0], keys[1]); // resposta perdida → mesma chave
        Assert.Equal(keys[1], keys[2]); // 3ª tentativa (a que deu certo) ainda com a mesma chave
        Assert.NotEqual(keys[2], keys[3]); // depois do sucesso, chave nova
    }

    [Fact]
    public async Task ADefinitiveRejectionDropsTheKey()
    {
        var queue = new KeyedQueue { Fail = call => call == 1 ? new WorkQueueException(WorkQueueFailure.Validation) : null };
        var worker = Build(queue);
        await worker.StartAsync(CancellationToken.None);
        await Until(() => { lock (queue.Keys) return queue.Keys.Count >= 3; });
        await worker.StopAsync(CancellationToken.None);
        Assert.NotEqual(queue.Keys[0], queue.Keys[1]);
    }
}
