using CentralContingencia.Application.Identity;
using CentralContingencia.Domain.Audit;
namespace CentralContingencia.UnitTests;
public class AuthenticationAuditTests
{
    [Fact]
    public void AuditUsesUtcAndHasNoFreeformPayload()
    {
        var entry = new AuthenticationAuditEvent(AuthenticationAction.LoginSucceeded, Guid.NewGuid(), Guid.NewGuid(), "correlation");
        Assert.Equal(TimeSpan.Zero, entry.OccurredAt.Offset);
        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.Equal("correlation", entry.CorrelationId);
        Assert.DoesNotContain(typeof(AuthenticationAuditEvent).GetProperties(), p => p.Name is "Email" or "Password" or "Payload" or "Cookie");
    }
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void EmptyCorrelationIsRejected(string value)
        => Assert.Throws<ArgumentException>(() => new AuthenticationAuditEvent(AuthenticationAction.LoginFailed, null, null, value));
    [Fact]
    public void OversizedCorrelationIsRejected()
        => Assert.Throws<ArgumentException>(() => new AuthenticationAuditEvent(AuthenticationAction.LoginFailed, null, null, new string('a', 129)));
    [Fact]
    public void UnknownAuditActionIsRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new AuthenticationAuditEvent((AuthenticationAction)999, null, null, "test"));
    [Fact]
    public void OnlyAuthorizedRolesAreDefined()
        => Assert.Equal(new[] { "Admin", "Manager", "Operator" }, AccessRoles.All);
}
