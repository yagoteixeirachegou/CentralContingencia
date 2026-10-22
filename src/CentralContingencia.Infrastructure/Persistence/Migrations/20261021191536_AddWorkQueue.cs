using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CentralContingencia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "work");

            migrationBuilder.CreateTable(
                name: "Tasks",
                schema: "work",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PayloadVersion = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    Result = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastError = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkerId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    HeartbeatAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelRequestedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                    table.CheckConstraint("CK_Tasks_Attempts", "\"AttemptCount\" >= 0 AND \"MaxAttempts\" BETWEEN 1 AND 10 AND \"AttemptCount\" <= \"MaxAttempts\"");
                    table.CheckConstraint("CK_Tasks_Cancel", "(\"CancelRequestedAt\" IS NULL) = (\"CancelRequestedBy\" IS NULL)");
                    table.CheckConstraint("CK_Tasks_Completed", "(\"Status\" IN ('Succeeded','Failed','Cancelled','DeadLetter')) = (\"CompletedAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_Tasks_Fingerprint", "length(\"RequestFingerprint\") = 64 AND length(btrim(\"CorrelationId\")) > 0");
                    table.CheckConstraint("CK_Tasks_Ids", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"CreatedBy\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdempotencyKey\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Tasks_Lease", "(\"Status\" = 'Running') = (\"WorkerId\" IS NOT NULL AND \"LeaseUntil\" IS NOT NULL AND \"HeartbeatAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_Tasks_Status", "\"Status\" IN ('Pending','Running','Succeeded','Failed','Cancelled','DeadLetter')");
                    table.CheckConstraint("CK_Tasks_Type", "\"Type\" IN ('SimulatedProcessing')");
                    table.CheckConstraint("CK_Tasks_Version", "\"PayloadVersion\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "WorkTaskEvents",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkerId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTaskEvents", x => x.Id);
                    table.CheckConstraint("CK_WorkTaskEvents_Ids", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"TaskId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_CreatedAt",
                schema: "work",
                table: "Tasks",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_CreatedBy_IdempotencyKey",
                schema: "work",
                table: "Tasks",
                columns: new[] { "CreatedBy", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Pending",
                schema: "work",
                table: "Tasks",
                columns: new[] { "AvailableAt", "CreatedAt" },
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Running",
                schema: "work",
                table: "Tasks",
                column: "LeaseUntil",
                filter: "\"Status\" = 'Running'");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Status_CreatedAt",
                schema: "work",
                table: "Tasks",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskEvents_Sequence",
                schema: "audit",
                table: "WorkTaskEvents",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskEvents_TaskId_OccurredAt",
                schema: "audit",
                table: "WorkTaskEvents",
                columns: new[] { "TaskId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tasks",
                schema: "work");

            migrationBuilder.DropTable(
                name: "WorkTaskEvents",
                schema: "audit");
        }
    }
}
