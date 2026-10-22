using CentralContingencia.Api.Hosting;
using CentralContingencia.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;

namespace CentralContingencia.UnitTests;

public class PlatformHostingTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    [Fact]
    public void PortFromThePlatformBecomesAWildcardUrl() => Assert.Equal("http://*:8123", PlatformHosting.UrlsFromPort(Config(("PORT", "8123"))));

    [Fact]
    public void WithoutPortNothingChanges() => Assert.Null(PlatformHosting.UrlsFromPort(Config()));

    [Theory]
    [InlineData("URLS")]
    [InlineData("urls")]
    public void AnExplicitUrlListWins(string key) => Assert.Null(PlatformHosting.UrlsFromPort(Config(("PORT", "8123"), (key, "https://localhost:7090"))));

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("80;rm")]
    [InlineData("abc")]
    [InlineData("8 080")]
    public void InvalidPortStopsStartupInsteadOfBindingSomethingElse(string port) =>
        Assert.Throws<InvalidOperationException>(() => PlatformHosting.UrlsFromPort(Config(("PORT", port))));

    [Fact]
    public void BlankPortIsIgnored() => Assert.Null(PlatformHosting.UrlsFromPort(Config(("PORT", "  "))));
}

public class MigrationGuardTests
{
    [Theory]
    [InlineData("DROP TABLE work.\"Tasks\";")]
    [InlineData("ALTER TABLE x DROP COLUMN y;")]
    [InlineData("DROP SCHEMA work;")]
    [InlineData("DROP INDEX \"IX_x\";")]
    [InlineData("ALTER TABLE t ALTER COLUMN c TYPE text;")]
    [InlineData("TRUNCATE work.\"Tasks\";")]
    [InlineData("DELETE FROM work.\"Tasks\";")]
    [InlineData("UPDATE work.\"Tasks\" SET \"Status\" = 'x';")]
    [InlineData("drop table lower;")]
    public void DestructiveStatementsAreDetected(string script) => Assert.NotEmpty(MigrationRunner.FindDestructive(script));

    [Theory]
    [InlineData("CREATE TABLE work.\"Executors\" (\"Id\" uuid);")]
    [InlineData("ALTER TABLE work.\"Tasks\" ADD \"ClaimKey\" uuid;")]
    [InlineData("CREATE INDEX \"IX_x\" ON t (c);")]
    [InlineData("INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ('x', '10');")]
    [InlineData("DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = 'x';")]
    public void AdditiveStatementsAreNotFlagged(string script) => Assert.Empty(MigrationRunner.FindDestructive(script));
}
