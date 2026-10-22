using CentralContingencia.Domain.Proxies;

namespace CentralContingencia.UnitTests;

public class ProxyDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static EncryptedProxyPassword Envelope() => EncryptedProxyPassword.Create("test-v1", new byte[12], [1, 2], new byte[16]);
    private static Proxy Create(DateTimeOffset? expiry = null) => Proxy.Create(Guid.NewGuid(), ProxyHost.Create("proxy.example.test"),
        ProxyPort.Create(8080), "synthetic-user", Envelope(), "BR", expiry ?? Now.AddDays(1), Now);

    [Theory]
    [InlineData("PROXY.Example.test", "proxy.example.test")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("2001:0DB8::1", "2001:db8::1")]
    public void HostNormalizesValidDnsAndIp(string input, string expected) => Assert.Equal(expected, ProxyHost.Create(input).Value);

    [Theory]
    [InlineData("")][InlineData(" ")][InlineData("http://proxy.test")][InlineData("user@proxy.test")]
    [InlineData("proxy.test:80")][InlineData("proxy.test/path")][InlineData("bad_host")][InlineData("-bad.test")]
    [InlineData("bad-.test")][InlineData("a..test")][InlineData("999.1.1.1")][InlineData("127.1")]
    [InlineData("2130706433")][InlineData("[::1]")][InlineData("fe80::1%1")][InlineData("proxy.test ")]
    public void HostRejectsInvalidInput(string value) => Assert.Throws<ArgumentException>(() => ProxyHost.Create(value));

    [Fact]
    public void HostRejectsOversizedNamesAndLabels()
    {
        Assert.Throws<ArgumentException>(() => ProxyHost.Create(new string('a', 64) + ".test"));
        Assert.Throws<ArgumentException>(() => ProxyHost.Create(new string('a', 254)));
    }

    [Theory]
    [InlineData(0)][InlineData(-1)][InlineData(65536)]
    public void PortRejectsInvalidRange(int port) => Assert.Throws<ArgumentOutOfRangeException>(() => ProxyPort.Create(port));

    [Theory]
    [InlineData(1)][InlineData(65535)]
    public void PortAcceptsBoundaries(int port) => Assert.Equal(port, ProxyPort.Create(port).Value);

    [Fact]
    public void CreationIsDisabledAndNormalizesTimestamps()
    {
        var proxy = Proxy.Create(Guid.NewGuid(), ProxyHost.Create("proxy.test"), ProxyPort.Create(1), "user", Envelope(),
            "BR", Now.AddDays(1).ToOffset(TimeSpan.FromHours(-3)), Now.ToOffset(TimeSpan.FromHours(-3)));
        Assert.Equal(ProxyStatus.Disabled, proxy.Status);
        Assert.False(proxy.IsAvailable(Now, false));
        Assert.Equal(TimeSpan.Zero, proxy.ExpiresAt.Offset);
        Assert.Equal(Now, proxy.CreatedAt);
        Assert.Equal(proxy.CreatedAt, proxy.UpdatedAt);
    }

    [Fact]
    public void AvailabilityRequiresActiveValidAndUnallocatedProxy()
    {
        var proxy = Create();
        proxy.ChangeStatus(ProxyStatus.Active, Now);
        Assert.True(proxy.IsAvailable(Now, false));
        Assert.False(proxy.IsAvailable(Now, true));
        Assert.False(proxy.IsAvailable(proxy.ExpiresAt, false));
        Assert.False(proxy.IsAvailable(proxy.ExpiresAt.AddTicks(1), false));
        Assert.False(proxy.IsAvailable(Now.AddTicks(-1), false));
        proxy.ChangeStatus(ProxyStatus.Suspended, Now.AddMinutes(1));
        Assert.False(proxy.IsAvailable(Now.AddMinutes(1), false));
        proxy.ChangeStatus(ProxyStatus.Disabled, Now.AddMinutes(2));
        Assert.False(proxy.IsAvailable(Now.AddMinutes(2), false));
        Assert.Equal(Now.AddMinutes(2), proxy.UpdatedAt);
    }

    [Fact]
    public void ExpiredProxyCanBeRepresentedButNeverAvailable()
    {
        var proxy = Create(Now.AddDays(-1));
        proxy.ChangeStatus(ProxyStatus.Active, Now);
        Assert.False(proxy.IsAvailable(Now, false));
    }

    [Fact]
    public void RejectsInvalidIdentityMetadataStateAndBackwardTime()
    {
        Assert.Throws<ArgumentException>(() => Proxy.Create(Guid.Empty, ProxyHost.Create("p.test"), ProxyPort.Create(1), "u", Envelope(), "BR", Now, Now));
        foreach (var value in new[] { "", " ", "line\nvalue", new string('a', 129) })
            Assert.Throws<ArgumentException>(() => Proxy.Create(Guid.NewGuid(), ProxyHost.Create("p.test"), ProxyPort.Create(1), value, Envelope(), "BR", Now, Now));
        Assert.Throws<ArgumentException>(() => Proxy.Create(Guid.NewGuid(), ProxyHost.Create("p.test"), ProxyPort.Create(1), "u", Envelope(), new string('a', 101), Now, Now));
        var proxy = Create();
        Assert.Throws<ArgumentException>(() => proxy.ChangeStatus((ProxyStatus)999, Now));
        Assert.Throws<ArgumentException>(() => proxy.ChangeStatus(ProxyStatus.Active, Now.AddTicks(-1)));
        Assert.Throws<ArgumentNullException>(() => proxy.ReplaceEncryptedPassword(null!, Now));
        Assert.Throws<ArgumentException>(() => proxy.ReplaceEncryptedPassword(Envelope(), Now.AddTicks(-1)));
        Assert.Equal(ProxyStatus.Disabled, proxy.Status);
    }

    [Fact]
    public void RejectsTimestampsMappedToPostgresInfinity()
    {
        Assert.Throws<ArgumentException>(() => Create(DateTimeOffset.MaxValue));
        Assert.Throws<ArgumentException>(() => Create(DateTimeOffset.MinValue));
        Assert.Throws<ArgumentException>(() => Create().ChangeStatus(ProxyStatus.Active, DateTimeOffset.MaxValue));
    }

    [Fact]
    public void EnvelopeProtectsBuffersAndProvidesValueEquality()
    {
        var nonce = new byte[12]; var cipher = new byte[] { 1, 2 }; var tag = new byte[16];
        var password = EncryptedProxyPassword.Create("test-v1", nonce, cipher, tag);
        nonce[0] = cipher[0] = tag[0] = 99;
        password.Nonce[0] = password.Ciphertext[0] = password.Tag[0] = 77;
        Assert.Equal(Envelope(), password);
        Assert.Equal(Envelope().GetHashCode(), password.GetHashCode());
        Assert.DoesNotContain("synthetic-user", Create().ToString());
        Assert.Equal("[encrypted proxy password]", password.ToString());
    }

    [Fact]
    public void EnvelopeRejectsMalformedComponents()
    {
        foreach (var key in new[] { "", "bad key", new string('a', 65) })
            Assert.Throws<ArgumentException>(() => EncryptedProxyPassword.Create(key, new byte[12], [1], new byte[16]));
        Assert.Throws<ArgumentException>(() => EncryptedProxyPassword.Create("v1", new byte[11], [1], new byte[16]));
        Assert.Throws<ArgumentException>(() => EncryptedProxyPassword.Create("v1", new byte[12], [1], new byte[15]));
        Assert.Throws<ArgumentException>(() => EncryptedProxyPassword.Create("v1", new byte[12], [], new byte[16]));
        Assert.Throws<ArgumentException>(() => EncryptedProxyPassword.Create("v1", new byte[12], new byte[1025], new byte[16]));
    }
}
