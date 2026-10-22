using CentralContingencia.Application.Allocations;
using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.UnitTests;

public class ReservationReliabilityTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    [Theory]
    [InlineData(1)][InlineData(15)][InlineData(1440)]
    public void DeadlineUsesConfiguredDurationAndNeverExceedsProxyValidity(int minutes)
    {
        var policy = new ReservationOptions { ReservationMinutes = minutes };
        Assert.Equal(Now.AddMinutes(minutes), policy.Deadline(Now, Now.AddDays(2), null));
        Assert.Equal(Now.AddSeconds(30), policy.Deadline(Now, Now.AddSeconds(30), null));
        Assert.Equal(Now.AddSeconds(10), policy.Deadline(Now, Now.AddDays(2), Now.AddSeconds(10)));
        Assert.Equal(AllocationPersistenceFailure.Validation,
            Assert.Throws<AllocationPersistenceException>(() => policy.Deadline(Now, Now.AddDays(2), Now.AddMinutes(minutes).AddTicks(1))).Failure);
    }
    [Theory]
    [InlineData(0)][InlineData(-1)][InlineData(1441)]
    public void InvalidConfigurationFailsInsteadOfCreatingUnboundedReservations(int minutes)
    {
        Assert.Throws<InvalidOperationException>(() => new ReservationOptions { ReservationMinutes = minutes }.Validate());
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void ConfirmationReplayPreservesHistoryEvenAfterProxyBecomesUnavailable(bool blocked)
    {
        var domain = new AllocationDomainService();
        var profile = OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create("Sintético"), "Sintética", Now);
        var proxy = Proxy.Create(Guid.NewGuid(), ProxyHost.Create("synthetic.test"), ProxyPort.Create(8080), "synthetic-user",
            EncryptedProxyPassword.Create("unit", new byte[12], [1], new byte[16]), "BR", Now.AddHours(1), Now);
        proxy.ChangeStatus(ProxyStatus.Active, Now);
        var allocation = domain.Reserve(Guid.NewGuid(), profile, proxy, Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddMinutes(15), []);
        domain.Confirm(allocation, profile, proxy, Now.AddMinutes(1));
        if (blocked) proxy.ChangeStatus(ProxyStatus.Suspended, Now.AddMinutes(2));
        domain.Confirm(allocation, profile, proxy, Now.AddHours(2));
        Assert.Equal(Now.AddMinutes(1), allocation.ConfirmedAt); Assert.Equal(allocation.ConfirmedAt, allocation.UpdatedAt);
        var other = OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create("Outro"), "Sintética", Now);
        Assert.Throws<InvalidOperationException>(() => domain.Confirm(allocation, other, proxy, Now.AddHours(2)));
        domain.Release(allocation, Now.AddHours(2));
        Assert.Throws<InvalidOperationException>(() => domain.Confirm(allocation, profile, proxy, Now.AddHours(2)));
    }
}
