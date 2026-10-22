using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralContingencia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIngestionBatchGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdsPowerGroupId",
                schema: "profiles",
                table: "IngestionBatches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdsPowerGroupName",
                schema: "profiles",
                table: "IngestionBatches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngestionBatches_AdsPowerGroupId",
                schema: "profiles",
                table: "IngestionBatches",
                column: "AdsPowerGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // O grupo de cada lote define a numeração; não descartar sem plano de recuperação revisado.
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM profiles.\"IngestionBatches\" WHERE \"AdsPowerGroupId\" IS NOT NULL) THEN RAISE EXCEPTION 'Downgrade requires an explicitly reviewed ingestion-group recovery plan'; END IF; END; $$;");
            migrationBuilder.DropIndex(
                name: "IX_IngestionBatches_AdsPowerGroupId",
                schema: "profiles",
                table: "IngestionBatches");

            migrationBuilder.DropColumn(
                name: "AdsPowerGroupId",
                schema: "profiles",
                table: "IngestionBatches");

            migrationBuilder.DropColumn(
                name: "AdsPowerGroupName",
                schema: "profiles",
                table: "IngestionBatches");
        }
    }
}
