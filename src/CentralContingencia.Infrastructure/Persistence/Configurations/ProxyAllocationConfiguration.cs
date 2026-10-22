using CentralContingencia.Domain.Allocations;
using CentralContingencia.Domain.Profiles;
using CentralContingencia.Domain.Proxies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CentralContingencia.Infrastructure.Persistence.Configurations;

public sealed class ProxyAllocationConfiguration : IEntityTypeConfiguration<ProxyAllocation>
{
    public void Configure(EntityTypeBuilder<ProxyAllocation> entity)
    {
        entity.ToTable("ProxyAllocations", "allocations", table =>
        {
            table.HasCheckConstraint("CK_Allocations_Ids", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ProfileId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ProxyId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ActorId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdempotencyKey\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_Allocations_Times", "isfinite(\"CreatedAt\") AND isfinite(\"UpdatedAt\") AND isfinite(\"ReservationExpiresAt\") AND \"UpdatedAt\" >= \"CreatedAt\" AND \"ReservationExpiresAt\" > \"CreatedAt\" AND (\"ConfirmedAt\" IS NULL OR (isfinite(\"ConfirmedAt\") AND \"ConfirmedAt\" >= \"CreatedAt\" AND \"ConfirmedAt\" < \"ReservationExpiresAt\" AND \"ConfirmedAt\" <= \"UpdatedAt\")) AND (\"EndedAt\" IS NULL OR (isfinite(\"EndedAt\") AND \"EndedAt\" >= \"CreatedAt\" AND \"EndedAt\" = \"UpdatedAt\"))");
            table.HasCheckConstraint("CK_Allocations_State", "(\"Status\" = 'Reserved' AND \"ConfirmedAt\" IS NULL AND \"EndedAt\" IS NULL AND \"EndReason\" IS NULL) OR (\"Status\" = 'Confirmed' AND \"ConfirmedAt\" IS NOT NULL AND \"EndedAt\" IS NULL AND \"EndReason\" IS NULL) OR (\"Status\" = 'Released' AND \"EndedAt\" IS NOT NULL AND \"EndReason\" IS NOT NULL AND \"EndReason\" IN ('Released','ProfileClosed')) OR (\"Status\" = 'Expired' AND \"ConfirmedAt\" IS NULL AND \"EndedAt\" IS NOT NULL AND \"EndedAt\" >= \"ReservationExpiresAt\" AND \"EndReason\" = 'ReservationExpired' AND \"EndReason\" IS NOT NULL)");
        });
        entity.HasKey(a => a.Id); entity.Property(a => a.Id).ValueGeneratedNever();
        entity.Ignore(a => a.IsActive);
        entity.Property(a => a.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(a => a.EndReason).HasConversion<string>().HasMaxLength(32);
        entity.Property<uint>("xmin").IsRowVersion();
        entity.HasOne<OperationalProfile>().WithMany().HasForeignKey(a => a.ProfileId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Proxy>().WithMany().HasForeignKey(a => a.ProxyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(a => a.ProxyId).IsUnique().HasFilter("\"Status\" IN ('Reserved', 'Confirmed')").HasDatabaseName("UX_ProxyAllocations_ActiveProxy");
        entity.HasIndex(a => a.ProfileId).IsUnique().HasFilter("\"Status\" IN ('Reserved', 'Confirmed')").HasDatabaseName("UX_ProxyAllocations_ActiveProfile");
        entity.HasIndex(a => new { a.ActorId, a.IdempotencyKey }).IsUnique();
        entity.HasIndex(a => new { a.ProfileId, a.CreatedAt });
        entity.HasIndex(a => new { a.ProxyId, a.CreatedAt });
    }
}
