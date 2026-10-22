using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CentralContingencia.UnitTests;

public class AllocationPersistenceModelTests
{
    [Fact]
    public void ModelProtectsBothActiveResourcesAndKeepsHistoricalForeignKeysRestricted()
    {
        using var db = new CentralContingenciaDbContext(new DbContextOptionsBuilder<CentralContingenciaDbContext>().UseNpgsql("Host=localhost;Database=unused").Options);
        var entity = db.Model.FindEntityType(typeof(ProxyAllocation))!;
        foreach (var field in new[] { nameof(ProxyAllocation.ProxyId), nameof(ProxyAllocation.ProfileId) })
        {
            var index = Assert.Single(entity.GetIndexes(), i => i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == field);
            Assert.Equal("\"Status\" IN ('Reserved', 'Confirmed')", index.GetFilter());
        }
        Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.True(entity.FindProperty("xmin")!.IsConcurrencyToken); Assert.Null(entity.FindProperty(nameof(ProxyAllocation.IsActive)));
        var converter = db.Model.FindEntityType(typeof(OperationalProfile))!.FindProperty(nameof(OperationalProfile.Name))!.GetTypeMapping().Converter!;
        Assert.Equal("Perfil", converter.ConvertToProvider(ProfileName.Create(" Perfil ")));
        Assert.Equal(ProfileName.Create("Perfil"), converter.ConvertFromProvider("Perfil"));
        Assert.Throws<ArgumentException>(() => converter.ConvertFromProvider("\n"));
    }
}
