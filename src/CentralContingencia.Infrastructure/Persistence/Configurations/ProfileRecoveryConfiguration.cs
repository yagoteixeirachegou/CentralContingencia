using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Domain.Allocations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CentralContingencia.Infrastructure.Persistence.Configurations;

public sealed class ProfileRecoveryConfiguration : IEntityTypeConfiguration<ProfileRecoveryBatch>
{
    public void Configure(EntityTypeBuilder<ProfileRecoveryBatch> entity)
    {
        entity.ToTable("ProfileRecoveryBatches", "audit", table =>
        {
            table.HasCheckConstraint("CK_Recovery_Counts", "\"Total\" BETWEEN 1 AND 10000 AND \"Recovered\" >= 0 AND \"Ignored\" >= 0 AND \"Total\" = \"Recovered\" + \"Ignored\"");
            table.HasCheckConstraint("CK_Recovery_Ids", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ActorId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdempotencyKey\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_Recovery_Time", "isfinite(\"OccurredAt\")");
            table.HasCheckConstraint("CK_Recovery_Correlation", "length(btrim(\"CorrelationId\")) BETWEEN 1 AND 128 AND \"CorrelationId\" !~ '[[:cntrl:]]'");
        });
        entity.HasKey(b => b.Id); entity.Property(b => b.Id).ValueGeneratedNever();
        entity.Property(b => b.ProtectedFingerprint).HasMaxLength(2048).IsRequired();
        entity.Property(b => b.CorrelationId).HasMaxLength(128).IsRequired();
        entity.HasIndex(b => new { b.ActorId, b.IdempotencyKey }).IsUnique(); entity.HasIndex(b => b.OccurredAt);
        entity.HasMany(b => b.Lines).WithOne().HasForeignKey(l => l.BatchId).OnDelete(DeleteBehavior.Cascade);
        entity.Navigation(b => b.Lines).HasField("lines").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
    public static void ConfigureLines(ModelBuilder builder)
    {
        builder.Entity<ProfileRecoveryLine>(entity =>
        {
            entity.ToTable("ProfileRecoveryLines", "audit", table => table.HasCheckConstraint("CK_RecoveryLine_Outcome",
                "\"Line\" >= 2 AND \"Outcome\" IN ('Recovered','Ignored','AlreadyLinked') AND (\"Outcome\" = 'Ignored' OR (\"ProxyId\" IS NOT NULL AND \"ProfileId\" IS NOT NULL AND \"AllocationId\" IS NOT NULL))"));
            entity.HasKey(l => new { l.BatchId, l.Line }); entity.Property(l => l.Outcome).HasMaxLength(16).IsRequired();
            entity.HasOne<Proxy>().WithMany().HasForeignKey(l => l.ProxyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationalProfile>().WithMany().HasForeignKey(l => l.ProfileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProxyAllocation>().WithMany().HasForeignKey(l => l.AllocationId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
