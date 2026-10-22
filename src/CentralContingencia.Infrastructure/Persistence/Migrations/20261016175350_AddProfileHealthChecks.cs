using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralContingencia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileHealthChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HealthChecks",
                schema: "profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdsPowerProfileId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AdsPowerGroupId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CheckedOn = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HealthChecks", x => x.Id);
                    table.CheckConstraint("CK_HealthChecks_Day", "\"Day\" BETWEEN 1 AND 7");
                    table.CheckConstraint("CK_HealthChecks_Ids", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ActorId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_HealthChecks_Text", "length(btrim(\"AdsPowerProfileId\")) > 0 AND length(btrim(\"ProfileName\")) > 0 AND length(btrim(\"AdsPowerGroupId\")) > 0");
                    table.CheckConstraint("CK_HealthChecks_Time", "isfinite(\"CheckedAt\")");
                });

            migrationBuilder.CreateIndex(
                name: "IX_HealthChecks_AdsPowerGroupId",
                schema: "profiles",
                table: "HealthChecks",
                column: "AdsPowerGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_HealthChecks_AdsPowerProfileId_CheckedOn",
                schema: "profiles",
                table: "HealthChecks",
                columns: new[] { "AdsPowerProfileId", "CheckedOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealthChecks_AdsPowerProfileId_Day",
                schema: "profiles",
                table: "HealthChecks",
                columns: new[] { "AdsPowerProfileId", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HealthChecks_CheckedAt",
                schema: "profiles",
                table: "HealthChecks",
                column: "CheckedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // O histórico de verificações é o rastro de quem verificou o quê e quando; não descartar sem plano de recuperação revisado.
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM profiles.\"HealthChecks\") THEN RAISE EXCEPTION 'Downgrade requires an explicitly reviewed health-check recovery plan'; END IF; END; $$;");
            migrationBuilder.DropTable(
                name: "HealthChecks",
                schema: "profiles");
        }
    }
}
