using CentralContingencia.Domain.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CentralContingencia.Infrastructure.Persistence.Configurations;

public sealed class ProfileIngestionBatchConfiguration : IEntityTypeConfiguration<ProfileIngestionBatch>
{
    public void Configure(EntityTypeBuilder<ProfileIngestionBatch> entity)
    {
        entity.ToTable("IngestionBatches", "profiles", table =>
        {
            table.HasCheckConstraint("CK_IngestionBatches_Ids", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ActorId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdempotencyKey\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_IngestionBatches_Requested", $"\"Requested\" BETWEEN 1 AND {ProfileIngestionBatch.MaxCount}");
            table.HasCheckConstraint("CK_IngestionBatches_Time", "isfinite(\"CreatedAt\")");
        });
        entity.HasKey(b => b.Id); entity.Property(b => b.Id).ValueGeneratedNever();
        entity.Property(b => b.Platform1).HasMaxLength(100);
        entity.Property(b => b.AdsPowerGroupId).HasMaxLength(64);
        entity.Property(b => b.AdsPowerGroupName).HasMaxLength(200);
        entity.HasIndex(b => b.AdsPowerGroupId);
        entity.HasIndex(b => new { b.ActorId, b.IdempotencyKey }).IsUnique();
        entity.HasIndex(b => b.CreatedAt);
    }
}
