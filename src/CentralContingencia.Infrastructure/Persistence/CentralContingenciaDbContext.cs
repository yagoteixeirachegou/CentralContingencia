using CentralContingencia.Domain.Audit;
using CentralContingencia.Domain.Proxies;
using CentralContingencia.Infrastructure.Persistence.Configurations;
using CentralContingencia.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CentralContingencia.Infrastructure.Persistence;

public sealed class CentralContingenciaDbContext(DbContextOptions<CentralContingenciaDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<CentralContingencia.Domain.WorkQueue.WorkTask> WorkTasks => Set<CentralContingencia.Domain.WorkQueue.WorkTask>();
    public DbSet<CentralContingencia.Domain.WorkQueue.WorkTaskEvent> WorkTaskEvents => Set<CentralContingencia.Domain.WorkQueue.WorkTaskEvent>();
    public DbSet<CentralContingencia.Domain.WorkQueue.WorkExecutor> WorkExecutors => Set<CentralContingencia.Domain.WorkQueue.WorkExecutor>();
    public DbSet<CentralContingencia.Domain.WorkQueue.ExecutorEvent> ExecutorEvents => Set<CentralContingencia.Domain.WorkQueue.ExecutorEvent>();
    public DbSet<AuthenticationAuditEvent> AuthenticationAuditEvents => Set<AuthenticationAuditEvent>();
    public DbSet<Proxy> Proxies => Set<Proxy>();
    public DbSet<CentralContingencia.Domain.Profiles.OperationalProfile> OperationalProfiles => Set<CentralContingencia.Domain.Profiles.OperationalProfile>();
    public DbSet<CentralContingencia.Domain.Allocations.ProxyAllocation> ProxyAllocations => Set<CentralContingencia.Domain.Allocations.ProxyAllocation>();
    public DbSet<ProxyAuditEvent> ProxyAuditEvents => Set<ProxyAuditEvent>();
    public DbSet<OperationalAuditEvent> OperationalAuditEvents => Set<OperationalAuditEvent>();
    public DbSet<ProxyImportBatch> ProxyImportBatches => Set<ProxyImportBatch>();
    public DbSet<CentralContingencia.Domain.Profiles.ProfileIngestionBatch> ProfileIngestionBatches => Set<CentralContingencia.Domain.Profiles.ProfileIngestionBatch>();
    public DbSet<CentralContingencia.Domain.Profiles.ProfileRecoveryBatch> ProfileRecoveryBatches => Set<CentralContingencia.Domain.Profiles.ProfileRecoveryBatch>();
    public DbSet<CentralContingencia.Domain.Profiles.ProfileHealthCheck> ProfileHealthChecks => Set<CentralContingencia.Domain.Profiles.ProfileHealthCheck>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("identity");
        builder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys", "security");
        builder.ApplyConfiguration(new ProxyConfiguration());
        builder.ApplyConfiguration(new OperationalProfileConfiguration());
        builder.ApplyConfiguration(new ProfileIngestionBatchConfiguration());
        builder.ApplyConfiguration(new ProxyAllocationConfiguration());
        builder.ApplyConfiguration(new ProfileHealthCheckConfiguration());
        builder.ApplyConfiguration(new ProfileRecoveryConfiguration());
        builder.ApplyConfiguration(new WorkTaskConfiguration());
        builder.ApplyConfiguration(new WorkTaskEventConfiguration());
        builder.ApplyConfiguration(new WorkExecutorConfiguration());
        builder.ApplyConfiguration(new ExecutorEventConfiguration());
        ProfileRecoveryConfiguration.ConfigureLines(builder);
        builder.Entity<OperationalAuditEvent>(entity =>
        {
            entity.ToTable("OperationalEvents", "audit", table =>
            {
                table.HasCheckConstraint("CK_OperationalEvents_Ids", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ActorId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ProfileId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND (\"AllocationId\" IS NULL OR \"AllocationId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");
                table.HasCheckConstraint("CK_OperationalEvents_Action", "(\"Action\" IN ('ProfileCreated','ProfileUpdated','ProfileClosed') AND \"AllocationId\" IS NULL) OR (\"Action\" IN ('AllocationReserved','AllocationConfirmed','AllocationReleased','AllocationExpired') AND \"AllocationId\" IS NOT NULL)");
                table.HasCheckConstraint("CK_OperationalEvents_Time", "isfinite(\"OccurredAt\")");
                table.HasCheckConstraint("CK_OperationalEvents_Correlation", "length(btrim(\"CorrelationId\")) BETWEEN 1 AND 128 AND \"CorrelationId\" !~ '[[:cntrl:]]'");
            });
            entity.HasKey(e => e.Id); entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Action).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(128).IsRequired();
            entity.HasIndex(e => new { e.ProfileId, e.OccurredAt }); entity.HasIndex(e => new { e.AllocationId, e.OccurredAt });
        });
        builder.Entity<ProxyImportBatch>(entity =>
        {
            entity.ToTable("ProxyImportBatches", "audit", table =>
            {
                table.HasCheckConstraint("CK_ImportBatch_Counts", "\"Total\" BETWEEN 1 AND 10000 AND \"Imported\" >= 0 AND \"Ignored\" >= 0 AND \"Invalid\" = 0 AND \"Conflicting\" >= 0 AND \"Total\" = \"Imported\" + \"Ignored\" + \"Invalid\" + \"Conflicting\"");
                table.HasCheckConstraint("CK_ImportBatch_Ids", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ActorId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdempotencyKey\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint("CK_ImportBatch_Time", "isfinite(\"OccurredAt\")");
            });
            entity.HasKey(b=>b.Id);entity.Property(b=>b.Id).ValueGeneratedNever();
            entity.Property(b=>b.ProtectedFingerprint).HasMaxLength(2048).IsRequired();
            entity.Property(b=>b.CorrelationId).HasMaxLength(128).IsRequired();
            entity.HasIndex(b=>new {b.ActorId,b.IdempotencyKey}).IsUnique();
            entity.HasIndex(b=>b.OccurredAt);
            entity.HasMany(b=>b.Lines).WithOne().HasForeignKey(l=>l.BatchId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(b=>b.Lines).HasField("lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        builder.Entity<ProxyImportLine>(entity=>
        {
            entity.ToTable("ProxyImportLines","audit",table=>
            {
                table.HasCheckConstraint("CK_ImportLine_Outcome", "\"Line\" >= 2 AND \"Outcome\" IN ('Imported', 'Empty', 'Duplicate', 'Conflict') AND ((\"Outcome\" = 'Imported') = (\"ProxyId\" IS NOT NULL))");
            });
            entity.HasKey(l=>new {l.BatchId,l.Line});
            entity.Property(l=>l.Outcome).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.HasOne<Proxy>().WithMany().HasForeignKey(l=>l.ProxyId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ProxyAuditEvent>(entity =>
        {
            entity.ToTable("ProxyEvents", "audit");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Action).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(128).IsRequired();
            entity.HasIndex(e => new { e.ProxyId, e.OccurredAt });
            entity.HasIndex(e => e.ActorId);
        });
        builder.Entity<ApplicationUser>().Property(u => u.Email).HasMaxLength(254).IsRequired();
        builder.Entity<ApplicationUser>().Property(u => u.NormalizedEmail).HasMaxLength(254).IsRequired();
        builder.Entity<ApplicationUser>().HasIndex(u => u.NormalizedEmail).IsUnique();
        builder.Entity<AuthenticationAuditEvent>(entity =>
        {
            entity.ToTable("AuthenticationEvents", "audit");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Action).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(128).IsRequired();
            entity.HasIndex(e => e.OccurredAt);
        });
    }
}
