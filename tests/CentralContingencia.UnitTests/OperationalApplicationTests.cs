using CentralContingencia.Application.Allocations;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Domain.Audit;
using CentralContingencia.Domain.Profiles;

namespace CentralContingencia.UnitTests;

public class OperationalApplicationTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Profiles : IOperationalProfileStore
    {
        public OperationalProfile? Value; public OperationalAuditContext? Audit; public int Reads; public int Writes;
        public Task AddAsync(OperationalProfile profile, CancellationToken token, OperationalAuditContext? audit = null)
        { Value = profile; Audit = audit; Writes++; return Task.CompletedTask; }
        public Task<OperationalProfile?> FindAsync(Guid id, CancellationToken token) { Reads++; return Task.FromResult(Value?.Id == id ? Value : null); }
        public Task<ProfilePage> QueryAsync(ProfileQuery query, CancellationToken token) { Reads++; return Task.FromResult(new ProfilePage([], query.Page, query.PageSize, 0)); }
        public Task<OperationalProfile> UpdateAsync(Guid id, string? name, string? platform, CancellationToken token, OperationalAuditContext audit,
            bool platform1Specified = false, string? platform1 = null, bool platform2Specified = false, string? platform2 = null) => throw new InvalidOperationException("Write must not reach this test store.");
    }
    [Theory]
    [InlineData("Admin")][InlineData("Manager")]
    public async Task CreationUsesDomainAndAuditsResponsibleActor(string role)
    {
        var profiles = new Profiles(); var service = new OperationalManagementService(profiles, null!, new Clock()); var actor = new ProxyActor(Guid.NewGuid(), [role]);
        var result = await service.CreateAsync(actor, new() { Name = " Perfil ", Platform = " Sintética " }, "unit", default);
        Assert.Equal("Perfil", result.Name); Assert.Equal("Sintética", result.Platform); Assert.Equal("Active", result.Status);
        Assert.Equal(Now, result.CreatedAt); Assert.Equal(actor.Id, profiles.Audit!.ActorId); Assert.Equal(1, profiles.Writes);
    }
    [Theory]
    [InlineData("Operator")][InlineData("Integration")][InlineData("unknown")]
    public async Task LeastPrivilegeIsEnforcedBeforeEveryWrite(string role)
    {
        var profiles = new Profiles(); var service = new OperationalManagementService(profiles, null!, new Clock()); var actor = new ProxyActor(Guid.NewGuid(), [role]); var id = Guid.NewGuid();
        Func<Task>[] operations = [() => service.CreateAsync(actor, new() { Name = "Perfil", Platform = "Sintética" }, "unit", default),
            () => service.UpdateAsync(actor, id, new() { Name = "Perfil" }, "unit", default), () => service.CloseAsync(actor, id, "unit", default),
            () => service.ReserveAsync(actor, new() { ProfileId = id, ProxyId = id, ReservationExpiresAt = Now.AddMinutes(1) }, id, "unit", default),
            () => service.ConfirmAsync(actor, id, "unit", default), () => service.ReleaseAsync(actor, id, "unit", default)];
        foreach (var action in operations) Assert.Equal(AllocationPersistenceFailure.Forbidden, (await Assert.ThrowsAsync<AllocationPersistenceException>(action)).Failure);
        Assert.Equal(0, profiles.Writes); Assert.Equal(0, profiles.Reads);
    }
    [Theory]
    [InlineData("", "Sintética")][InlineData("\n", "Sintética")][InlineData("Perfil", "")]
    public async Task InvalidDomainMetadataDoesNotReachPersistence(string name, string platform)
    {
        var profiles = new Profiles(); var service = new OperationalManagementService(profiles, null!, new Clock());
        var error = await Assert.ThrowsAsync<AllocationPersistenceException>(() => service.CreateAsync(new(Guid.NewGuid(), ["Admin"]), new() { Name = name, Platform = platform }, "unit", default));
        Assert.Equal(AllocationPersistenceFailure.Validation, error.Failure); Assert.Equal(0, profiles.Writes);
    }
    [Fact]
    public async Task UnknownRoleCannotReadButOperatorCanConsultMissingResource()
    {
        var profiles = new Profiles(); var service = new OperationalManagementService(profiles, null!, new Clock()); var id = Guid.NewGuid();
        Assert.Equal(AllocationPersistenceFailure.Forbidden, (await Assert.ThrowsAsync<AllocationPersistenceException>(() => service.GetAsync(new(id, ["unknown"]), id, default))).Failure);
        Assert.Equal(0, profiles.Reads);
        Assert.Equal(AllocationPersistenceFailure.NotFound, (await Assert.ThrowsAsync<AllocationPersistenceException>(() => service.GetAsync(new(id, ["Operator"]), id, default))).Failure);
        Assert.Equal(1, profiles.Reads);
    }
    [Fact]
    public async Task InvalidPaginationAndSortAreRejectedBeforeQuerying()
    {
        var profiles = new Profiles(); var service = new OperationalManagementService(profiles, null!, new Clock()); var actor = new ProxyActor(Guid.NewGuid(), ["Admin"]);
        foreach (var query in new[] { new ProfileQuery(Page: 0), new ProfileQuery(PageSize: 101), new ProfileQuery(Sort: "password"), new ProfileQuery(Direction: "DROP") })
            Assert.Equal(AllocationPersistenceFailure.Validation, (await Assert.ThrowsAsync<AllocationPersistenceException>(() => service.ListAsync(actor, query, default))).Failure);
        Assert.Equal(0, profiles.Reads);
    }
    [Fact]
    public void ProfileUpdateIsAtomicAndClosedProfileRemainsTerminal()
    {
        var profile = OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create("Original"), "Sintética", Now);
        Assert.Throws<ArgumentException>(() => profile.UpdateDetails(ProfileName.Create("Novo"), "\n", Now)); Assert.Equal("Original", profile.Name.Value);
        profile.UpdateDetails(ProfileName.Create("Novo"), "Atualizada", Now.AddSeconds(1)); Assert.Equal("Novo", profile.Name.Value);
        new CentralContingencia.Domain.Allocations.AllocationDomainService().CloseProfile(profile, [], Now.AddSeconds(2));
        Assert.Throws<InvalidOperationException>(() => profile.UpdateDetails(ProfileName.Create("Outro"), "Atualizada", Now.AddSeconds(3)));
        Assert.Equal("Novo", profile.Name.Value); Assert.Equal(Now.AddSeconds(2), profile.UpdatedAt);
    }
    [Fact]
    public void AuditRequiresCorrectResourceAndExcludesMetadata()
    {
        var actor = Guid.NewGuid(); var profile = Guid.NewGuid(); var allocation = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new OperationalAuditEvent(actor, profile, null, OperationalAction.AllocationReserved, Now, "unit"));
        Assert.Throws<ArgumentException>(() => new OperationalAuditEvent(actor, profile, allocation, OperationalAction.ProfileCreated, Now, "unit"));
        Assert.Throws<ArgumentException>(() => new OperationalAuditEvent(Guid.Empty, profile, null, OperationalAction.ProfileCreated, Now, "unit"));
        var audit = new OperationalAuditEvent(actor, profile, allocation, OperationalAction.AllocationReleased, Now.ToOffset(TimeSpan.FromHours(-3)), "unit");
        Assert.Equal(TimeSpan.Zero, audit.OccurredAt.Offset); Assert.DoesNotContain("unit", audit.ToString());
    }
}
