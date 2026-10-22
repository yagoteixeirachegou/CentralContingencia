using CentralContingencia.Domain.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CentralContingencia.Infrastructure.Persistence.Configurations;

public sealed class OperationalProfileConfiguration : IEntityTypeConfiguration<OperationalProfile>
{
    public void Configure(EntityTypeBuilder<OperationalProfile> entity)
    {
        entity.ToTable("OperationalProfiles", "profiles", table =>
        {
            table.HasCheckConstraint("CK_Profiles_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_Profiles_Name", "length(btrim(\"Name\")) BETWEEN 1 AND 200 AND \"Name\" = btrim(\"Name\") AND \"Name\" !~ '[[:cntrl:]]'");
            table.HasCheckConstraint("CK_Profiles_Platform", "length(btrim(\"Platform\")) BETWEEN 1 AND 100 AND \"Platform\" = btrim(\"Platform\") AND \"Platform\" !~ '[[:cntrl:]]'");
            table.HasCheckConstraint("CK_Profiles_State", "(\"Status\" = 'Active' AND \"ClosedAt\" IS NULL) OR (\"Status\" = 'Closed' AND \"ClosedAt\" IS NOT NULL AND \"ClosedAt\" = \"UpdatedAt\")");
            table.HasCheckConstraint("CK_Profiles_Times", "isfinite(\"CreatedAt\") AND isfinite(\"UpdatedAt\") AND \"UpdatedAt\" >= \"CreatedAt\" AND (\"ClosedAt\" IS NULL OR (isfinite(\"ClosedAt\") AND \"ClosedAt\" >= \"CreatedAt\"))");
        });
        entity.HasKey(p => p.Id); entity.Property(p => p.Id).ValueGeneratedNever();
        entity.Property(p => p.Name).HasConversion(n => n.Value, value => ProfileName.Create(value)).HasMaxLength(200).IsRequired();
        entity.Property(p => p.Platform).HasMaxLength(100).IsRequired(false);
        entity.Property(p => p.Platform1).HasMaxLength(100);
        entity.Property(p => p.Platform2).HasMaxLength(100);
        var allowed = string.Join(",", ProfileAssignments.Catalog.Select(label => "'" + label.Replace("'", "''") + "'"));
        entity.ToTable("OperationalProfiles", "profiles", table =>
        {
            table.HasCheckConstraint("CK_Profiles_Platform1", $"\"Platform1\" IS NULL OR \"Platform1\" IN ({allowed})");
            table.HasCheckConstraint("CK_Profiles_Platform2", $"\"Platform2\" IS NULL OR \"Platform2\" IN ({allowed})");
            table.HasCheckConstraint("CK_Profiles_AdsPower", "(\"AdsPowerId\" IS NULL OR \"AdsPowerId\" ~ '^[A-Za-z0-9_-]{3,64}$') AND (\"AdsPowerSerialNumber\" IS NULL OR (\"AdsPowerId\" IS NOT NULL AND \"AdsPowerSerialNumber\" ~ '^[0-9]{1,20}$'))");
        });
        entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(p => p.AdsPowerId).HasMaxLength(64);
        entity.Property(p => p.AdsPowerSerialNumber).HasMaxLength(20);
        entity.Property<uint>("xmin").IsRowVersion();
        entity.HasIndex(p => new { p.Status, p.CreatedAt });
        entity.HasIndex(p => p.AdsPowerId).IsUnique().HasFilter("\"AdsPowerId\" IS NOT NULL").HasDatabaseName("UX_Profiles_AdsPowerId");
        entity.HasOne<ProfileIngestionBatch>().WithMany().HasForeignKey(p => p.IngestionBatchId).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(p => p.IngestionBatchId).HasFilter("\"IngestionBatchId\" IS NOT NULL");
    }
}
