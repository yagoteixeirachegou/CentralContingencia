using CentralContingencia.Application.Allocations;
using CentralContingencia.Application.Proxies;
using CentralContingencia.Domain.Profiles;

namespace CentralContingencia.UnitTests;

public class OperationalGridTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
    [Theory]
    [InlineData(null, null)][InlineData("DARKPOST", null)][InlineData(null, "TIKTOK")]
    [InlineData("DARKPOST", "TIKTOK")][InlineData("FLAG", "FLAG")]
    public void SlotsAreOptionalAndIndependent(string? first, string? second)
    {
        var profile = OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create("Synthetic"), null, Now, first, second);
        Assert.Null(profile.Platform); Assert.Equal(first, profile.Platform1); Assert.Equal(second, profile.Platform2);
        profile.UpdateDetails(profile.Name, "Legacy free text", Now.AddSeconds(1));
        Assert.Equal(first, profile.Platform1); Assert.Equal(second, profile.Platform2);
        profile.UpdateDetails(profile.Name, profile.Platform, null, null, Now.AddSeconds(2));
        Assert.Null(profile.Platform1); Assert.Null(profile.Platform2);
    }
    [Theory]
    [InlineData("")][InlineData("Unknown")][InlineData("ativo")][InlineData("ATIVO")]
    public void InvalidAssignmentsCannotPartiallyMutateProfile(string value)
    {
        var profile = OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create("Original"), "Legacy", Now, "DARKPOST");
        Assert.Throws<ArgumentException>(() => profile.UpdateDetails(ProfileName.Create("Changed"), "Changed", "TIKTOK", value, Now.AddSeconds(1)));
        Assert.Equal("Original", profile.Name.Value); Assert.Equal("DARKPOST", profile.Platform1); Assert.Equal(Now, profile.UpdatedAt);
    }
    [Fact]
    public void EntireApprovedCatalogIsAcceptedWithoutAProxyDependency()
    {
        Assert.Equal(18, ProfileAssignments.Catalog.Count);
        foreach (var assignment in ProfileAssignments.Catalog)
            Assert.Equal(assignment, OperationalProfile.Create(Guid.NewGuid(), ProfileName.Create("Synthetic"), null, Now, assignment, assignment).Platform2);
    }
    private sealed class Reader : IOperationalGridReader
    {
        public int Calls;
        public Task<OperationalGridPage> QueryAsync(OperationalGridQuery query, CancellationToken token)
        { Calls++; return Task.FromResult(new OperationalGridPage([], query.Page, query.PageSize, 0, Now)); }
    }
    [Theory]
    [InlineData("Admin", true)][InlineData("Manager", true)][InlineData("Operator", true)]
    [InlineData("Integration", false)][InlineData("unknown", false)]
    public async Task AuthorizationPrecedesPersistence(string role, bool allowed)
    {
        var reader = new Reader(); var service = new OperationalGridService(reader);
        if (allowed) await service.ListAsync(new(Guid.NewGuid(), [role]), new(), default);
        else Assert.Equal(AllocationPersistenceFailure.Forbidden, (await Assert.ThrowsAsync<AllocationPersistenceException>(() => service.ListAsync(new(Guid.NewGuid(), [role]), new(), default))).Failure);
        Assert.Equal(allowed ? 1 : 0, reader.Calls);
    }
    [Fact]
    public async Task UnsafeQueriesAreRejectedBeforePersistence()
    {
        var reader = new Reader(); var service = new OperationalGridService(reader);
        foreach (var query in new[] { new OperationalGridQuery(Page: 0), new OperationalGridQuery(PageSize: 101),
            new OperationalGridQuery(Sort: "password"), new OperationalGridQuery(Direction: "DROP"),
            new OperationalGridQuery(Availability: "Active"), new OperationalGridQuery(Platform: "Unknown"),
            new OperationalGridQuery(Profile: "\n"), new OperationalGridQuery(Region: new string('a', 101)) })
            Assert.Equal(AllocationPersistenceFailure.Validation, (await Assert.ThrowsAsync<AllocationPersistenceException>(() => service.ListAsync(new(Guid.NewGuid(), ["Admin"]), query, default))).Failure);
        Assert.Equal(0, reader.Calls);
    }
}
