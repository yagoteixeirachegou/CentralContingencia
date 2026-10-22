using CentralContingencia.Application.Warmup;
using CentralContingencia.Domain.Profiles;
using Xunit;

namespace CentralContingencia.UnitTests;

public sealed class HealthCheckTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class MemoryStore : IHealthCheckStore
    {
        public List<ProfileHealthCheck> Items { get; } = [];
        public Task<IReadOnlyList<ProfileHealthCheck>> ListByProfileAsync(string id, CancellationToken token) => Task.FromResult<IReadOnlyList<ProfileHealthCheck>>(Items.Where(c => c.AdsPowerProfileId == id).ToList());
        public Task<IReadOnlyList<ProfileHealthCheck>> ListByProfilesAsync(IReadOnlyCollection<string> ids, CancellationToken token) => Task.FromResult<IReadOnlyList<ProfileHealthCheck>>(Items.Where(c => ids.Contains(c.AdsPowerProfileId)).ToList());
        public Task AddAsync(ProfileHealthCheck check, CancellationToken token) { Items.Add(check); return Task.CompletedTask; }
    }

    private static RecordHealthCheckRequest Request(HealthCheckOutcome outcome = HealthCheckOutcome.Ok) => new("k1abc", "Perfil 1256", "g1", outcome);

    [Fact]
    public async Task Record_AssignsSequentialDaysAndAllowsOneCheckPerUtcDay()
    {
        var clock = new FakeTimeProvider(Start); var store = new MemoryStore(); var service = new HealthCheckService(store, clock);
        var first = await service.RecordAsync(Actor, Request(), default);
        Assert.Equal(1, first.Day);
        await Assert.ThrowsAsync<HealthCheckException>(() => service.RecordAsync(Actor, Request(), default));
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(2, (await service.RecordAsync(Actor, Request(), default)).Day);
    }

    [Fact]
    public async Task Record_RejectsEighthCheck()
    {
        var clock = new FakeTimeProvider(Start); var service = new HealthCheckService(new MemoryStore(), clock);
        for (var day = 1; day <= ProfileHealthCheck.CycleLength; day++) { await service.RecordAsync(Actor, Request(), default); clock.Advance(TimeSpan.FromDays(1)); }
        var error = await Assert.ThrowsAsync<HealthCheckException>(() => service.RecordAsync(Actor, Request(), default));
        Assert.Equal(HealthCheckFailure.Conflict, error.Failure);
    }

    [Theory]
    [InlineData("", "Perfil", "g1")]
    [InlineData("k1", "", "g1")]
    [InlineData("k1", "Perfil", "")]
    public async Task Record_RejectsInvalidText(string profileId, string name, string group)
    {
        var service = new HealthCheckService(new MemoryStore(), new FakeTimeProvider(Start));
        var error = await Assert.ThrowsAsync<HealthCheckException>(() => service.RecordAsync(Actor, new(profileId, name, group, HealthCheckOutcome.Ok), default));
        Assert.Equal(HealthCheckFailure.Validation, error.Failure);
    }

    [Fact]
    public async Task Progress_FlagsImpedimentAndCompletion()
    {
        var clock = new FakeTimeProvider(Start); var service = new HealthCheckService(new MemoryStore(), clock);
        await service.RecordAsync(Actor, Request(), default); clock.Advance(TimeSpan.FromDays(1));
        await service.RecordAsync(Actor, Request(HealthCheckOutcome.Checkpoint), default);
        var progress = Assert.Single(await service.ProgressAsync(["k1abc"], default));
        Assert.Equal(2, progress.CompletedDays); Assert.True(progress.HasImpediment); Assert.False(progress.Completed);
        Assert.Equal(HealthCheckOutcome.Checkpoint, progress.LastOutcome);
    }

    [Fact]
    public async Task Progress_RejectsEmptyOrOversizedLists()
    {
        var service = new HealthCheckService(new MemoryStore(), new FakeTimeProvider(Start));
        await Assert.ThrowsAsync<HealthCheckException>(() => service.ProgressAsync([], default));
        await Assert.ThrowsAsync<HealthCheckException>(() => service.ProgressAsync(Enumerable.Range(0, HealthCheckService.MaxProfilesPerQuery + 1).Select(i => $"k{i}").ToList(), default));
    }
}
