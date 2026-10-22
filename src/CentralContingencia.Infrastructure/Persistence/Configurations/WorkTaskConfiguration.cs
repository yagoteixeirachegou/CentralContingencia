using CentralContingencia.Domain.WorkQueue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CentralContingencia.Infrastructure.Persistence.Configurations;

public sealed class WorkTaskConfiguration : IEntityTypeConfiguration<WorkTask>
{
    private const string Zero = "'00000000-0000-0000-0000-000000000000'::uuid";

    public void Configure(EntityTypeBuilder<WorkTask> entity)
    {
        entity.ToTable("Tasks", "work", table =>
        {
            table.HasCheckConstraint("CK_Tasks_Ids", $"\"Id\" <> {Zero} AND \"CreatedBy\" <> {Zero} AND \"IdempotencyKey\" <> {Zero}");
            table.HasCheckConstraint("CK_Tasks_Status", "\"Status\" IN ('Pending','Running','Succeeded','Failed','Cancelled','DeadLetter')");
            table.HasCheckConstraint("CK_Tasks_Type", "\"Type\" IN ('SimulatedProcessing')");
            table.HasCheckConstraint("CK_Tasks_Attempts", $"\"AttemptCount\" >= 0 AND \"MaxAttempts\" BETWEEN 1 AND {WorkTask.MaxAttemptsLimit} AND \"AttemptCount\" <= \"MaxAttempts\"");
            // Running ⇔ há dono e lease; qualquer outro estado não pode reter worker nem lease.
            table.HasCheckConstraint("CK_Tasks_Lease", "(\"Status\" = 'Running') = (\"WorkerId\" IS NOT NULL AND \"LeaseUntil\" IS NOT NULL AND \"HeartbeatAt\" IS NOT NULL)");
            table.HasCheckConstraint("CK_Tasks_Completed", "(\"Status\" IN ('Succeeded','Failed','Cancelled','DeadLetter')) = (\"CompletedAt\" IS NOT NULL)");
            table.HasCheckConstraint("CK_Tasks_Cancel", "(\"CancelRequestedAt\" IS NULL) = (\"CancelRequestedBy\" IS NULL)");
            table.HasCheckConstraint("CK_Tasks_Fingerprint", "length(\"RequestFingerprint\") = 64 AND length(btrim(\"CorrelationId\")) > 0");
            table.HasCheckConstraint("CK_Tasks_Version", "\"PayloadVersion\" >= 1");
        });
        entity.HasKey(t => t.Id); entity.Property(t => t.Id).ValueGeneratedNever();
        entity.Property(t => t.Type).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(t => t.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(t => t.Payload).HasColumnType("jsonb").IsRequired();
        entity.Property(t => t.Result).HasColumnType("jsonb");
        entity.Property(t => t.LastErrorCode).HasMaxLength(WorkErrorSanitizer.MaxCodeLength);
        entity.Property(t => t.LastError).HasMaxLength(WorkErrorSanitizer.MaxMessageLength);
        entity.Property(t => t.CorrelationId).HasMaxLength(64).IsRequired();
        entity.Property(t => t.RequestFingerprint).HasMaxLength(64).IsRequired();
        entity.Property(t => t.WorkerId).HasMaxLength(100);
        entity.Property(t => t.LastWorkerId).HasMaxLength(100);
        // Reentrega idempotente do claim: busca só entre tarefas em execução.
        entity.HasIndex(t => new { t.WorkerId, t.ClaimKey }).HasDatabaseName("IX_Tasks_Claim").HasFilter("\"Status\" = 'Running' AND \"ClaimKey\" IS NOT NULL");
        entity.Ignore(t => t.IsTerminal); entity.Ignore(t => t.CancelRequested);
        entity.HasIndex(t => new { t.CreatedBy, t.IdempotencyKey }).IsUnique();
        // Índices parciais mantêm o claim e a recuperação baratos mesmo com muito histórico.
        entity.HasIndex(t => new { t.AvailableAt, t.CreatedAt }).HasDatabaseName("IX_Tasks_Pending").HasFilter("\"Status\" = 'Pending'");
        entity.HasIndex(t => t.LeaseUntil).HasDatabaseName("IX_Tasks_Running").HasFilter("\"Status\" = 'Running'");
        entity.HasIndex(t => new { t.Status, t.CreatedAt });
        entity.HasIndex(t => t.CreatedAt);
    }
}

public sealed class WorkTaskEventConfiguration : IEntityTypeConfiguration<WorkTaskEvent>
{
    public void Configure(EntityTypeBuilder<WorkTaskEvent> entity)
    {
        entity.ToTable("WorkTaskEvents", "audit", table =>
        {
            table.HasCheckConstraint("CK_WorkTaskEvents_Ids", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"TaskId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        });
        entity.HasKey(e => e.Id); entity.Property(e => e.Id).ValueGeneratedNever();
        entity.Property(e => e.Sequence).UseIdentityByDefaultColumn();
        entity.HasIndex(e => e.Sequence).IsUnique();
        entity.Property(e => e.Action).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(e => e.WorkerId).HasMaxLength(100);
        entity.Property(e => e.Code).HasMaxLength(WorkErrorSanitizer.MaxCodeLength);
        entity.Property(e => e.CorrelationId).HasMaxLength(64).IsRequired();
        entity.HasIndex(e => new { e.TaskId, e.OccurredAt });
    }
}

public sealed class WorkExecutorConfiguration : IEntityTypeConfiguration<WorkExecutor>
{
    public void Configure(EntityTypeBuilder<WorkExecutor> entity)
    {
        entity.ToTable("Executors", "work", table =>
        {
            table.HasCheckConstraint("CK_Executors_Ids", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"CreatedBy\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_Executors_Status", "\"Status\" IN ('Active','Revoked')");
            table.HasCheckConstraint("CK_Executors_State", "\"State\" IN ('Offline','Idle','Busy','Draining')");
            table.HasCheckConstraint("CK_Executors_Hash", "\"CredentialHash\" ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("CK_Executors_Revoked", "(\"Status\" = 'Revoked') = (\"RevokedAt\" IS NOT NULL AND \"RevokedBy\" IS NOT NULL)");
            table.HasCheckConstraint("CK_Executors_Types", "length(btrim(\"AllowedTypes\")) > 0");
        });
        entity.HasKey(e => e.Id); entity.Property(e => e.Id).ValueGeneratedNever();
        entity.Property(e => e.Name).HasMaxLength(WorkExecutor.NameMaxLength).IsRequired();
        entity.Property(e => e.NormalizedName).HasMaxLength(WorkExecutor.NameMaxLength).IsRequired();
        entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(e => e.State).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(e => e.CredentialHash).HasMaxLength(64).IsRequired();
        entity.Property(e => e.AllowedTypes).HasMaxLength(256).IsRequired();
        entity.Property(e => e.WorkerVersion).HasMaxLength(32);
        entity.Ignore(e => e.AllowedTypeList); entity.Ignore(e => e.IsRevoked);
        entity.HasIndex(e => e.NormalizedName).IsUnique();
    }
}

public sealed class ExecutorEventConfiguration : IEntityTypeConfiguration<ExecutorEvent>
{
    public void Configure(EntityTypeBuilder<ExecutorEvent> entity)
    {
        entity.ToTable("ExecutorEvents", "audit");
        entity.HasKey(e => e.Id); entity.Property(e => e.Id).ValueGeneratedNever();
        entity.Property(e => e.Sequence).UseIdentityByDefaultColumn();
        entity.HasIndex(e => e.Sequence).IsUnique();
        entity.Property(e => e.Action).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.HasIndex(e => new { e.ExecutorId, e.OccurredAt });
    }
}
