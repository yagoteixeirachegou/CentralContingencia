using System.Security.Cryptography;
using CentralContingencia.Application.Allocations;
using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Infrastructure.Allocations;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CentralContingencia.IntegrationTests;

[Collection("Authentication")]
public class AllocationPersistenceTests(AuthFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private DbContextOptions<CentralContingenciaDbContext> Options => new DbContextOptionsBuilder<CentralContingenciaDbContext>().UseNpgsql(fixture.ConnectionString).Options;
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = AllocationPersistenceTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static OperationalProfile Profile() => OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create("Perfil sintético"), "Plataforma sintética", Now);
    private static Proxy Proxy()
    {
        var proxy = CentralContingencia.Domain.Proxies.Proxy.Create(Guid.NewGuid(), ProxyHost.Create("synthetic.test"), ProxyPort.Create(8080), "synthetic-user",
            EncryptedProxyPassword.Create("unit", RandomNumberGenerator.GetBytes(12), [1, 2], RandomNumberGenerator.GetBytes(16)), "BR", Now.AddDays(1), Now);
        proxy.ChangeStatus(ProxyStatus.Active, Now); return proxy;
    }
    private async Task<(OperationalProfile[] Profiles, Proxy[] Proxies)> Seed()
    {
        var profiles = new[] { Profile(), Profile() }; var proxies = new[] { Proxy(), Proxy() };
        await using var db = new CentralContingenciaDbContext(Options);
        db.OperationalProfiles.AddRange(profiles); db.Proxies.AddRange(proxies); await db.SaveChangesAsync();
        return (profiles, proxies);
    }
    private static AllocationReservation Request(OperationalProfile profile, Proxy proxy) => new(Guid.NewGuid(), profile.Id, proxy.Id, Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(10));
    [PostgresFact]
    public async Task MaterializationClosureReuseAndHistoryPreserveProxyRows()
    {
        var (profiles, proxies) = await Seed(); var clock = new Clock(); var store = new EfAllocationStore(Options, clock);
        var original = proxies[0];
        var first = await store.ReserveAsync(Request(profiles[0], original), default);
        await store.ConfirmAsync(first.Id, default);
        clock.Now = Now.AddMinutes(1); await store.CloseProfileAsync(profiles[0].Id, default);
        var closed = await new EfOperationalProfileStore(Options).FindAsync(profiles[0].Id, default);
        Assert.Equal(profiles[0].Name, closed!.Name); Assert.Equal(ProfileStatus.Closed, closed.Status);
        Assert.Equal(clock.Now, closed.ClosedAt); Assert.Equal(profiles[0].Platform, closed.Platform);
        var history = Assert.Single(await store.HistoryAsync(profiles[0].Id, default));
        Assert.Equal(AllocationStatus.Released, history.Status); Assert.Equal(AllocationEndReason.ProfileClosed, history.EndReason);
        Assert.Equal(Now, history.ConfirmedAt); Assert.Equal(clock.Now, history.EndedAt); Assert.False(history.IsActive);
        var next = await store.ReserveAsync(Request(profiles[1], original), default); Assert.NotEqual(first.Id, next.Id);
        await using var db = new CentralContingenciaDbContext(Options); var persisted = await db.Proxies.AsNoTracking().SingleAsync(p => p.Id == original.Id);
        Assert.Equal(original.Password, persisted.Password); Assert.Equal(original.Status, persisted.Status);
        Assert.Equal(original.UpdatedAt, persisted.UpdatedAt); Assert.Equal(original.Username, persisted.Username);
        Assert.Equal(original.ExpiresAt, persisted.ExpiresAt);
        Assert.Equal(2, await db.ProxyAllocations.CountAsync(a => a.ProxyId == original.Id));
    }
    [PostgresTheory]
    [InlineData(true)][InlineData(false)]
    public async Task ConcurrentReservationsHaveExactlyOneWinner(bool sameProxy)
    {
        var (profiles, proxies) = await Seed(); var store = new EfAllocationStore(Options, new Clock());
        var requests = new[] { Request(profiles[0], proxies[0]), Request(sameProxy ? profiles[1] : profiles[0], sameProxy ? proxies[0] : proxies[1]) };
        async Task<bool> Attempt(AllocationReservation request)
        {
            try { await store.ReserveAsync(request, default); return true; }
            catch (AllocationPersistenceException error)
            {
                Assert.Equal(AllocationPersistenceFailure.Conflict, error.Failure); Assert.Null(error.InnerException);
                Assert.DoesNotContain("synthetic-user", error.ToString()); return false;
            }
        }
        var outcomes = await Task.WhenAll(requests.Select(Attempt)); Assert.Single(outcomes, outcome => outcome);
        await using var db = new CentralContingenciaDbContext(Options);
        Assert.Equal(1, await db.ProxyAllocations.CountAsync(a => a.Id == requests[0].Id || a.Id == requests[1].Id));
    }
    [PostgresTheory]
    [InlineData(true)][InlineData(false)]
    public async Task DatabasePartialIndexesRejectBypassingRepository(bool sameProxy)
    {
        var (profiles, proxies) = await Seed(); var domain = new AllocationDomainService();
        ProxyAllocation Allocation(OperationalProfile profile, Proxy proxy) => domain.Reserve(Guid.NewGuid(), profile, proxy, Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddMinutes(10), []);
        await using (var first = new CentralContingenciaDbContext(Options))
        { first.ProxyAllocations.Add(Allocation(profiles[0], proxies[0])); await first.SaveChangesAsync(); }
        await using var second = new CentralContingenciaDbContext(Options);
        second.ProxyAllocations.Add(Allocation(sameProxy ? profiles[1] : profiles[0], sameProxy ? proxies[0] : proxies[1]));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal(sameProxy ? "UX_ProxyAllocations_ActiveProxy" : "UX_ProxyAllocations_ActiveProfile", postgres.ConstraintName);
    }
    [PostgresFact]
    public async Task UniqueViolationIsTranslatedAndDuplicateKeyDoesNotInsert()
    {
        var profile = Profile(); var profileStore = new EfOperationalProfileStore(Options);
        await profileStore.AddAsync(profile, default);
        var duplicate = await Assert.ThrowsAsync<AllocationPersistenceException>(() => profileStore.AddAsync(profile, default));
        Assert.Equal(AllocationPersistenceFailure.Conflict, duplicate.Failure); Assert.Null(duplicate.InnerException);
        Assert.DoesNotContain(profile.Name.Value, duplicate.ToString());
        var (profiles, proxies) = await Seed(); var store = new EfAllocationStore(Options, new Clock()); var request = Request(profiles[0], proxies[0]);
        await store.ReserveAsync(request, default); await store.ReleaseAsync(request.Id, default);
        var error = await Assert.ThrowsAsync<AllocationPersistenceException>(() => store.ReserveAsync(request with { Id = Guid.NewGuid(), ProfileId = profiles[1].Id, ProxyId = proxies[1].Id }, default));
        Assert.Equal(AllocationPersistenceFailure.Conflict, error.Failure);
    }
    [PostgresFact]
    public async Task ConcurrentDuplicateKeysAcrossDifferentResourcesAreRejectedByDatabaseAndTranslated()
    {
        var (profiles, proxies) = await Seed(); var store = new EfAllocationStore(Options, new Clock());
        var first = Request(profiles[0], proxies[0]);
        var second = Request(profiles[1], proxies[1]) with { ActorId = first.ActorId, IdempotencyKey = first.IdempotencyKey };
        async Task<bool> Attempt(AllocationReservation request)
        {
            try { await store.ReserveAsync(request, default); return true; }
            catch (AllocationPersistenceException error)
            { Assert.Equal(AllocationPersistenceFailure.Conflict, error.Failure); Assert.Null(error.InnerException); return false; }
        }
        Assert.Single(await Task.WhenAll(Attempt(first), Attempt(second)), result => result);
        await using var db = new CentralContingenciaDbContext(Options);
        Assert.Equal(1, await db.ProxyAllocations.CountAsync(a => a.ActorId == first.ActorId && a.IdempotencyKey == first.IdempotencyKey));
    }
    [PostgresFact]
    public async Task ExpirationAndReleaseAllowReuseWithoutErasingHistory()
    {
        var (profiles, proxies) = await Seed(); var clock = new Clock(); var store = new EfAllocationStore(Options, clock);
        var first = await store.ReserveAsync(Request(profiles[0], proxies[0]), default);
        var error = await Assert.ThrowsAsync<AllocationPersistenceException>(() => store.ExpireAsync(first.Id, default)); Assert.Equal(AllocationPersistenceFailure.Conflict, error.Failure);
        clock.Now = first.ReservationExpiresAt; await store.ExpireAsync(first.Id, default);
        var next = await store.ReserveAsync(Request(profiles[0], proxies[1]) with { ReservationExpiresAt = clock.Now.AddMinutes(10) }, default);
        await store.ReleaseAsync(next.Id, default);
        var history = await store.HistoryAsync(profiles[0].Id, default);
        Assert.Equal(2, history.Count); Assert.Contains(history, a => a.Status == AllocationStatus.Expired); Assert.Contains(history, a => a.Status == AllocationStatus.Released);
    }
    [PostgresFact]
    public async Task ClosedBlockedExpiredMissingAndInvalidResourcesAreRejected()
    {
        var (profiles, proxies) = await Seed(); var clock = new Clock(); var store = new EfAllocationStore(Options, clock);
        await store.CloseProfileAsync(profiles[0].Id, default);
        Assert.Equal(AllocationPersistenceFailure.Conflict, (await Assert.ThrowsAsync<AllocationPersistenceException>(() => store.ReserveAsync(Request(profiles[0], proxies[0]), default))).Failure);
        await using (var db = new CentralContingenciaDbContext(Options))
        { var proxy = await db.Proxies.SingleAsync(p => p.Id == proxies[0].Id); proxy.ChangeStatus(ProxyStatus.Suspended, Now); await db.SaveChangesAsync(); }
        Assert.Equal(AllocationPersistenceFailure.Conflict, (await Assert.ThrowsAsync<AllocationPersistenceException>(() => store.ReserveAsync(Request(profiles[1], proxies[0]), default))).Failure);
        clock.Now = proxies[1].ExpiresAt;
        Assert.Equal(AllocationPersistenceFailure.Conflict, (await Assert.ThrowsAsync<AllocationPersistenceException>(() => store.ReserveAsync(Request(profiles[1], proxies[1]), default))).Failure);
        clock.Now = Now;
        Assert.Equal(AllocationPersistenceFailure.Validation, (await Assert.ThrowsAsync<AllocationPersistenceException>(() => store.ReserveAsync(Request(profiles[1], proxies[1]) with { ActorId = Guid.Empty }, default))).Failure);
        Assert.Equal(AllocationPersistenceFailure.NotFound, (await Assert.ThrowsAsync<AllocationPersistenceException>(() => store.ReserveAsync(Request(profiles[1], proxies[1]) with { ProfileId = Guid.NewGuid() }, default))).Failure);
    }
    [PostgresFact]
    public async Task ForeignKeysPreventDeletingHistoricalResourcesAndChecksRejectCorruption()
    {
        var (profiles, proxies) = await Seed(); var store = new EfAllocationStore(Options, new Clock());
        var allocation = await store.ReserveAsync(Request(profiles[0], proxies[0]), default); await store.ReleaseAsync(allocation.Id, default);
        await using var db = new CentralContingenciaDbContext(Options);
        foreach (var sql in new[] { $"DELETE FROM profiles.\"OperationalProfiles\" WHERE \"Id\" = '{profiles[0].Id}'", $"DELETE FROM proxies.\"Proxies\" WHERE \"Id\" = '{proxies[0].Id}'" })
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql))).SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE allocations.\"ProxyAllocations\" SET \"Status\" = 'Reserved' WHERE \"Id\" = {allocation.Id}"))).SqlState);
        Assert.NotNull(await store.FindAsync(allocation.Id, default));
    }
    [PostgresTheory]
    [InlineData(false)][InlineData(true)]
    public async Task ReservationWaitsForProxyLockAndRechecksItsUpdatedStateOrTime(bool expireWhileWaiting)
    {
        var (profiles, proxies) = await Seed(); var clock = new Clock(); var store = new EfAllocationStore(Options, clock);
        await using var blocker = new CentralContingenciaDbContext(Options); await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM proxies.\"Proxies\" WHERE \"Id\" = {proxies[0].Id} FOR UPDATE");
        var attempt = store.ReserveAsync(Request(profiles[0], proxies[0]), default);
        await Task.Delay(100); Assert.False(attempt.IsCompleted);
        if (expireWhileWaiting) clock.Now = proxies[0].ExpiresAt;
        else
        { var proxy = await blocker.Proxies.SingleAsync(p => p.Id == proxies[0].Id); proxy.ChangeStatus(ProxyStatus.Disabled, Now); await blocker.SaveChangesAsync(); }
        await transaction.CommitAsync();
        Assert.Equal(AllocationPersistenceFailure.Conflict, (await Assert.ThrowsAsync<AllocationPersistenceException>(() => attempt)).Failure);
        Assert.Empty(await store.HistoryAsync(profiles[0].Id, default));
    }
    [PostgresFact]
    public async Task AdditiveMigrationPreservesProxyAlreadyPresentInPreviousSchema()
    {
        await using var isolated = new AuthFixture { InitialMigration = "20261011165123_AddProxyImportBatches", ProvisionAdmin = false };
        await isolated.InitializeAsync();
        var options = new DbContextOptionsBuilder<CentralContingenciaDbContext>().UseNpgsql(isolated.ConnectionString).Options;
        var proxy = Proxy();
        await using var db = new CentralContingenciaDbContext(options);
        db.Proxies.Add(proxy); await db.SaveChangesAsync();
        Assert.Contains("20261011223416_AddOperationalProfilesAndAllocations", await db.Database.GetPendingMigrationsAsync());
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        var preserved = await db.Proxies.SingleAsync(p => p.Id == proxy.Id);
        Assert.Equal(proxy.Password, preserved.Password); Assert.Equal(proxy.Host, preserved.Host);
        Assert.Equal(proxy.Username, preserved.Username); Assert.Equal(proxy.Status, preserved.Status);
        Assert.Equal(proxy.ExpiresAt, preserved.ExpiresAt); Assert.Equal(proxy.UpdatedAt, preserved.UpdatedAt);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(0, await db.OperationalProfiles.CountAsync()); Assert.Equal(0, await db.ProxyAllocations.CountAsync());
    }
    [PostgresFact]
    public async Task FailureSavingAllocationClosureRollsBackProfileToo()
    {
        var (profiles, proxies) = await Seed(); var store = new EfAllocationStore(Options, new Clock());
        var allocation = await store.ReserveAsync(Request(profiles[0], proxies[0]), default);
        await using var db = new CentralContingenciaDbContext(Options);
        // Fault injection only in the disposable database; no homologated SQL is issued.
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE allocations.\"ProxyAllocations\" ADD CONSTRAINT test_allocation_closure_failure CHECK (\"EndReason\" IS DISTINCT FROM 'ProfileClosed') NOT VALID");
        try
        {
            var error = await Assert.ThrowsAsync<AllocationPersistenceException>(() => store.CloseProfileAsync(profiles[0].Id, default));
            Assert.Equal(AllocationPersistenceFailure.Validation, error.Failure);
            Assert.Equal(ProfileStatus.Active, (await new EfOperationalProfileStore(Options).FindAsync(profiles[0].Id, default))!.Status);
            Assert.True((await store.FindAsync(allocation.Id, default))!.IsActive);
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE allocations.\"ProxyAllocations\" DROP CONSTRAINT test_allocation_closure_failure"); }
    }
}
