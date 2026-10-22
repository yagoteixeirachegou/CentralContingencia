using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CentralContingencia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastWorkerId",
                schema: "work",
                table: "Tasks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExecutorEvents",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExecutorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutorEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Executors",
                schema: "work",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CredentialHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AllowedTypes = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CredentialRotatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    WorkerVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Executors", x => x.Id);
                    table.CheckConstraint("CK_Executors_Hash", "\"CredentialHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_Executors_Ids", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"CreatedBy\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Executors_Revoked", "(\"Status\" = 'Revoked') = (\"RevokedAt\" IS NOT NULL AND \"RevokedBy\" IS NOT NULL)");
                    table.CheckConstraint("CK_Executors_State", "\"State\" IN ('Offline','Idle','Busy','Draining')");
                    table.CheckConstraint("CK_Executors_Status", "\"Status\" IN ('Active','Revoked')");
                    table.CheckConstraint("CK_Executors_Types", "length(btrim(\"AllowedTypes\")) > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutorEvents_ExecutorId_OccurredAt",
                schema: "audit",
                table: "ExecutorEvents",
                columns: new[] { "ExecutorId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutorEvents_Sequence",
                schema: "audit",
                table: "ExecutorEvents",
                column: "Sequence",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Executors_NormalizedName",
                schema: "work",
                table: "Executors",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutorEvents",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "Executors",
                schema: "work");

            migrationBuilder.DropColumn(
                name: "LastWorkerId",
                schema: "work",
                table: "Tasks");
        }
    }
}
