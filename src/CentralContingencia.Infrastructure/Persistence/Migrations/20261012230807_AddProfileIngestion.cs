using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralContingencia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdsPowerId",
                schema: "profiles",
                table: "OperationalProfiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdsPowerSerialNumber",
                schema: "profiles",
                table: "OperationalProfiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IngestionBatchId",
                schema: "profiles",
                table: "OperationalProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IngestionBatches",
                schema: "profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Requested = table.Column<int>(type: "integer", nullable: false),
                    Platform1 = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionBatches", x => x.Id);
                    table.CheckConstraint("CK_IngestionBatches_Ids", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ActorId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdempotencyKey\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_IngestionBatches_Requested", "\"Requested\" BETWEEN 1 AND 50");
                    table.CheckConstraint("CK_IngestionBatches_Time", "isfinite(\"CreatedAt\")");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalProfiles_IngestionBatchId",
                schema: "profiles",
                table: "OperationalProfiles",
                column: "IngestionBatchId",
                filter: "\"IngestionBatchId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Profiles_AdsPowerId",
                schema: "profiles",
                table: "OperationalProfiles",
                column: "AdsPowerId",
                unique: true,
                filter: "\"AdsPowerId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Profiles_AdsPower",
                schema: "profiles",
                table: "OperationalProfiles",
                sql: "(\"AdsPowerId\" IS NULL OR \"AdsPowerId\" ~ '^[A-Za-z0-9_-]{3,64}$') AND (\"AdsPowerSerialNumber\" IS NULL OR (\"AdsPowerId\" IS NOT NULL AND \"AdsPowerSerialNumber\" ~ '^[0-9]{1,20}$'))");

            migrationBuilder.CreateIndex(
                name: "IX_IngestionBatches_ActorId_IdempotencyKey",
                schema: "profiles",
                table: "IngestionBatches",
                columns: new[] { "ActorId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngestionBatches_CreatedAt",
                schema: "profiles",
                table: "IngestionBatches",
                column: "CreatedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_OperationalProfiles_IngestionBatches_IngestionBatchId",
                schema: "profiles",
                table: "OperationalProfiles",
                column: "IngestionBatchId",
                principalSchema: "profiles",
                principalTable: "IngestionBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nunca descartar vínculos com o AdsPower nem o histórico de lotes: exige plano de recuperação revisado.
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM profiles.\"OperationalProfiles\" WHERE \"AdsPowerId\" IS NOT NULL OR \"IngestionBatchId\" IS NOT NULL) OR EXISTS (SELECT 1 FROM profiles.\"IngestionBatches\") THEN RAISE EXCEPTION 'Downgrade requires an explicitly reviewed ingestion-data recovery plan'; END IF; END; $$;");

            migrationBuilder.DropForeignKey(
                name: "FK_OperationalProfiles_IngestionBatches_IngestionBatchId",
                schema: "profiles",
                table: "OperationalProfiles");

            migrationBuilder.DropTable(
                name: "IngestionBatches",
                schema: "profiles");

            migrationBuilder.DropIndex(
                name: "IX_OperationalProfiles_IngestionBatchId",
                schema: "profiles",
                table: "OperationalProfiles");

            migrationBuilder.DropIndex(
                name: "UX_Profiles_AdsPowerId",
                schema: "profiles",
                table: "OperationalProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Profiles_AdsPower",
                schema: "profiles",
                table: "OperationalProfiles");

            migrationBuilder.DropColumn(
                name: "AdsPowerId",
                schema: "profiles",
                table: "OperationalProfiles");

            migrationBuilder.DropColumn(
                name: "AdsPowerSerialNumber",
                schema: "profiles",
                table: "OperationalProfiles");

            migrationBuilder.DropColumn(
                name: "IngestionBatchId",
                schema: "profiles",
                table: "OperationalProfiles");
        }
    }
}
