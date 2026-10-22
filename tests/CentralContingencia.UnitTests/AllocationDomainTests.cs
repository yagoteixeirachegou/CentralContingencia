using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.UnitTests;

public class AllocationDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly AllocationDomainService service = new();
    private static OperationalProfile Profile() => OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create("Perfil sintético"), "Plataforma sintética", Now);
    private static Proxy Proxy()
    {
        var proxy = CentralContingencia.Domain.Proxies.Proxy.Create(Guid.NewGuid(), ProxyHost.Create("synthetic.test"), ProxyPort.Create(8080), "synthetic-user",
            EncryptedProxyPassword.Create("unit", new byte[12], [1], new byte[16]), "BR", Now.AddDays(1), Now);
        proxy.ChangeStatus(ProxyStatus.Active, Now); return proxy;
    }
    private ProxyAllocation Reserve(OperationalProfile profile, Proxy proxy, IReadOnlyCollection<ProxyAllocation>? existing = null, DateTimeOffset? now = null) =>
        service.Reserve(Guid.NewGuid(), profile, proxy, Guid.NewGuid(), Guid.NewGuid(), now ?? Now, (now ?? Now).AddMinutes(10), existing ?? []);

    [Fact]
    public void ReservationAndConfirmationPreserveSeparateStatusesAndUtc()
    {
        var profile = Profile(); var proxy = Proxy(); var allocation = Reserve(profile, proxy);
        Assert.Equal(AllocationStatus.Reserved, allocation.Status); Assert.True(allocation.IsActive);
        Assert.Equal(profile.Id, allocation.ProfileId); Assert.Equal(proxy.Id, allocation.ProxyId);
        service.Confirm(allocation, profile, proxy, Now.AddMinutes(1).ToOffset(TimeSpan.FromHours(-3)));
        Assert.Equal(AllocationStatus.Confirmed, allocation.Status); Assert.Equal(TimeSpan.Zero, allocation.ConfirmedAt!.Value.Offset);
        Assert.Equal(ProfileStatus.Active, profile.Status); Assert.Equal(ProxyStatus.Active, proxy.Status);
    }
    [Fact]
    public void EnforcesExclusivityOnBothSidesForReservedAndConfirmedAllocations()
    {
        var profile = Profile(); var proxy = Proxy(); var allocation = Reserve(profile, proxy);
        foreach (var confirmed in new[] { false, true })
        {
            if (confirmed) service.Confirm(allocation, profile, proxy, Now.AddMinutes(1));
            Assert.Throws<InvalidOperationException>(() => Reserve(Profile(), proxy, [allocation], Now.AddMinutes(2)));
            Assert.Throws<InvalidOperationException>(() => Reserve(profile, Proxy(), [allocation], Now.AddMinutes(2)));
        }
    }
    [Theory]
    [InlineData(ProxyStatus.Suspended)][InlineData(ProxyStatus.Disabled)]
    public void BlocksNonOperationalProxies(ProxyStatus status)
    {
        var proxy = Proxy(); proxy.ChangeStatus(status, Now);
        Assert.Throws<InvalidOperationException>(() => Reserve(Profile(), proxy));
    }
    [Fact]
    public void BlocksExpiredProxyAndExpiredReservationConfirmation()
    {
        var profile = Profile(); var proxy = Proxy(); var allocation = Reserve(profile, proxy);
        Assert.Throws<InvalidOperationException>(() => Reserve(profile, proxy, now: proxy.ExpiresAt));
        Assert.Throws<InvalidOperationException>(() => service.Confirm(allocation, profile, proxy, allocation.ReservationExpiresAt));
        Assert.True(allocation.IsActive); // Explicit expiration releases exclusivity.
        Assert.Throws<InvalidOperationException>(() => Reserve(Profile(), proxy, [allocation], allocation.ReservationExpiresAt));
        service.ExpireReservation(allocation, allocation.ReservationExpiresAt);
        Assert.Equal(AllocationStatus.Expired, allocation.Status); Assert.False(allocation.IsActive);
        Assert.NotNull(Reserve(profile, proxy, [allocation], allocation.ReservationExpiresAt));
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void ClosingProfileEndsAllocationAndAllowsProxyReuseWithoutDeletingHistory(bool confirmed)
    {
        var profile = Profile(); var proxy = Proxy(); var allocation = Reserve(profile, proxy);
        if (confirmed) service.Confirm(allocation, profile, proxy, Now);
        service.CloseProfile(profile, [allocation], Now.AddMinutes(2));
        Assert.Equal(ProfileStatus.Closed, profile.Status); Assert.Equal(Now.AddMinutes(2), profile.ClosedAt);
        Assert.Equal(AllocationEndReason.ProfileClosed, allocation.EndReason); Assert.Equal(AllocationStatus.Released, allocation.Status);
        Assert.Equal(ProxyStatus.Active, proxy.Status); Assert.True(proxy.IsAvailable(Now.AddMinutes(2), false));
        var replacement = Reserve(Profile(), proxy, [allocation], Now.AddMinutes(3));
        Assert.NotEqual(allocation.Id, replacement.Id); Assert.Equal(profile.Id, allocation.ProfileId);
        Assert.Throws<InvalidOperationException>(() => Reserve(profile, Proxy(), [allocation], Now.AddMinutes(3)));
        service.CloseProfile(profile, [allocation], Now.AddMinutes(3)); Assert.Equal(Now.AddMinutes(2), profile.ClosedAt);
    }
    [Fact]
    public void ClosingUnallocatedProfileAndUnassociatedProxyAreSupported()
    {
        var profile = Profile(); var proxy = Proxy(); service.CloseProfile(profile, [], Now);
        Assert.Equal(ProfileStatus.Closed, profile.Status); Assert.True(proxy.IsAvailable(Now, false));
    }
    [Fact]
    public void ReleaseRetainsHistoryAndAllowsSameProfileToUseAnotherProxy()
    {
        var profile = Profile(); var proxy = Proxy(); var allocation = Reserve(profile, proxy);
        service.Release(allocation, Now.AddMinutes(1));
        Assert.Equal(AllocationEndReason.Released, allocation.EndReason); Assert.False(allocation.IsActive);
        Assert.NotNull(Reserve(profile, Proxy(), [allocation], Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => service.Release(allocation, Now.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => service.Confirm(allocation, profile, proxy, Now.AddMinutes(2)));
    }
    [Fact]
    public void ConfirmationRevalidatesProxyAndResourceIdentity()
    {
        var profile = Profile(); var proxy = Proxy(); var allocation = Reserve(profile, proxy);
        Assert.Throws<InvalidOperationException>(() => service.Confirm(allocation, Profile(), proxy, Now));
        Assert.Throws<InvalidOperationException>(() => service.Confirm(allocation, profile, Proxy(), Now));
        proxy.ChangeStatus(ProxyStatus.Suspended, Now);
        Assert.Throws<InvalidOperationException>(() => service.Confirm(allocation, profile, proxy, Now));
        Assert.Equal(AllocationStatus.Reserved, allocation.Status);
    }
    [Fact]
    public void FailedClosureDoesNotPartiallyMutateProfileOrAllocation()
    {
        var profile = Profile(); var proxy = Proxy(); var allocation = Reserve(profile, proxy);
        service.Confirm(allocation, profile, proxy, Now.AddMinutes(2));
        Assert.Throws<ArgumentException>(() => service.CloseProfile(profile, [allocation], Now.AddMinutes(1)));
        Assert.Equal(ProfileStatus.Active, profile.Status); Assert.Equal(AllocationStatus.Confirmed, allocation.Status);
    }
    [Fact]
    public void ExpirationRejectsEarlyOrConfirmedReservation()
    {
        var profile = Profile(); var proxy = Proxy(); var allocation = Reserve(profile, proxy);
        Assert.Throws<InvalidOperationException>(() => service.ExpireReservation(allocation, Now));
        service.Confirm(allocation, profile, proxy, Now);
        Assert.Throws<InvalidOperationException>(() => service.ExpireReservation(allocation, Now.AddHours(1)));
    }
    [Fact]
    public void RejectsInvalidReservationIdentifiersDeadlinesAndDuplicateKeys()
    {
        var profile = Profile(); var proxy = Proxy(); var allocation = Reserve(profile, proxy);
        Assert.Throws<ArgumentException>(() => service.Reserve(Guid.Empty, profile, proxy, Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddMinutes(1), []));
        Assert.Throws<ArgumentException>(() => service.Reserve(Guid.NewGuid(), profile, proxy, Guid.Empty, Guid.NewGuid(), Now, Now.AddMinutes(1), []));
        Assert.Throws<ArgumentException>(() => service.Reserve(Guid.NewGuid(), profile, proxy, Guid.NewGuid(), Guid.Empty, Now, Now.AddMinutes(1), []));
        foreach (var deadline in new[] { Now, Now.AddTicks(-1), proxy.ExpiresAt.AddTicks(1) })
            Assert.Throws<ArgumentException>(() => service.Reserve(Guid.NewGuid(), profile, proxy, Guid.NewGuid(), Guid.NewGuid(), Now, deadline, []));
        service.Release(allocation, Now);
        Assert.Throws<InvalidOperationException>(() => service.Reserve(Guid.NewGuid(), profile, proxy, allocation.ActorId, allocation.IdempotencyKey, Now, Now.AddMinutes(1), [allocation]));
        Assert.Throws<InvalidOperationException>(() => service.Reserve(allocation.Id, profile, proxy, Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddMinutes(1), [allocation]));
    }
    [Theory]
    [InlineData("")][InlineData(" ")][InlineData("bad\nname")]
    public void ProfileNameRejectsInvalidInput(string value) => Assert.Throws<ArgumentException>(() => ProfileName.Create(value));
    [Fact]
    public void InconsistentSnapshotIsRejectedBeforeClosingAnyAllocation()
    {
        var profile = Profile(); var first = Reserve(profile, Proxy()); var second = Reserve(profile, Proxy());
        Assert.Throws<InvalidOperationException>(() => service.CloseProfile(profile, [first, second], Now));
        Assert.True(first.IsActive); Assert.True(second.IsActive); Assert.Equal(ProfileStatus.Active, profile.Status);
    }
    [Fact]
    public void ClosingProfileDoesNotTouchOtherProfilesOrReuseExpiredProxy()
    {
        var profile = Profile(); var proxy = Proxy(); var allocation = Reserve(profile, proxy);
        var other = Reserve(Profile(), Proxy());
        service.CloseProfile(profile, [allocation, other], proxy.ExpiresAt);
        Assert.False(allocation.IsActive); Assert.True(other.IsActive);
        Assert.Throws<InvalidOperationException>(() => Reserve(Profile(), proxy, [allocation, other], proxy.ExpiresAt));
    }
    [Fact]
    public void HistoricalReleaseCannotBeFollowedByBackwardReservationOrClosure()
    {
        var profile = Profile(); var proxy = Proxy(); var allocation = Reserve(profile, proxy);
        service.Release(allocation, Now.AddMinutes(2));
        Assert.Throws<ArgumentException>(() => Reserve(profile, proxy, [allocation], Now.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => Reserve(Profile(), proxy, [allocation], Now.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => service.CloseProfile(profile, [allocation], Now.AddMinutes(1)));
        Assert.Equal(ProfileStatus.Active, profile.Status);
    }
    [Fact]
    public void ProfileValidatesIdentityMetadataAndDoesNotTreatNameAsUniqueIdentity()
    {
        var first = Profile(); var second = Profile(); Assert.NotEqual(first.Id, second.Id); Assert.Equal(first.Name, second.Name);
        Assert.Equal("Perfil", ProfileName.Create(" Perfil ").Value);
        Assert.Throws<ArgumentException>(() => ProfileName.Create(new string('x', 201)));
        Assert.Throws<ArgumentException>(() => OperationalProfile.Create(Guid.Empty, first.Name, "Synthetic", Now));
        Assert.Throws<ArgumentException>(() => OperationalProfile.Create(Guid.NewGuid(), first.Name, "", Now));
        Assert.Throws<ArgumentException>(() => service.CloseProfile(first, [], Now.AddTicks(-1)));
        Assert.DoesNotContain(first.Name.Value, first.ToString()); Assert.Equal("[profile name]", first.Name.ToString());
    }
}
