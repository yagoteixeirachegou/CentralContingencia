using CentralContingencia.Domain.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CentralContingencia.Infrastructure.Persistence.Configurations;

public sealed class ProfileHealthCheckConfiguration : IEntityTypeConfiguration<ProfileHealthCheck>
{
    public void Configure(EntityTypeBuilder<ProfileHealthCheck> entity)
    {
        entity.ToTable("HealthChecks", "profiles", table =>
        {
            table.HasCheckConstraint("CK_HealthChecks_Ids", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ActorId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_HealthChecks_Day", $"\"Day\" BETWEEN 1 AND {ProfileHealthCheck.CycleLength}");
            table.HasCheckConstraint("CK_HealthChecks_Time", "isfinite(\"CheckedAt\")");
            table.HasCheckConstraint("CK_HealthChecks_Text", "length(btrim(\"AdsPowerProfileId\")) > 0 AND length(btrim(\"ProfileName\")) > 0 AND length(btrim(\"AdsPowerGroupId\")) > 0");
        });
        entity.HasKey(c => c.Id); entity.Property(c => c.Id).ValueGeneratedNever();
        entity.Property(c => c.AdsPowerProfileId).HasMaxLength(64).IsRequired();
        entity.Property(c => c.ProfileName).HasMaxLength(200).IsRequired();
        entity.Property(c => c.AdsPowerGroupId).HasMaxLength(64).IsRequired();
        entity.Property(c => c.Outcome).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Ignore(c => c.HasImpediment);
        entity.HasIndex(c => new { c.AdsPowerProfileId, c.Day }).IsUnique();
        entity.HasIndex(c => new { c.AdsPowerProfileId, c.CheckedOn }).IsUnique();
        entity.HasIndex(c => c.AdsPowerGroupId);
        entity.HasIndex(c => c.CheckedAt);
    }
}
