using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralContingencia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProxyImportBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProxyImportBatches",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    ProtectedFingerprint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    Imported = table.Column<int>(type: "integer", nullable: false),
                    Ignored = table.Column<int>(type: "integer", nullable: false),
                    Invalid = table.Column<int>(type: "integer", nullable: false),
                    Conflicting = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProxyImportBatches", x => x.Id);
                    table.CheckConstraint("CK_ImportBatch_Counts", "\"Total\" BETWEEN 1 AND 10000 AND \"Imported\" >= 0 AND \"Ignored\" >= 0 AND \"Invalid\" = 0 AND \"Conflicting\" >= 0 AND \"Total\" = \"Imported\" + \"Ignored\" + \"Invalid\" + \"Conflicting\"");
                    table.CheckConstraint("CK_ImportBatch_Ids", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ActorId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdempotencyKey\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_ImportBatch_Time", "isfinite(\"OccurredAt\")");
                });

            migrationBuilder.CreateTable(
                name: "ProxyImportLines",
                schema: "audit",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Line = table.Column<int>(type: "integer", nullable: false),
                    ProxyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProxyImportLines", x => new { x.BatchId, x.Line });
                    table.CheckConstraint("CK_ImportLine_Outcome", "\"Line\" >= 2 AND \"Outcome\" IN ('Imported', 'Empty', 'Duplicate', 'Conflict') AND ((\"Outcome\" = 'Imported') = (\"ProxyId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_ProxyImportLines_Proxies_ProxyId",
                        column: x => x.ProxyId,
                        principalSchema: "proxies",
                        principalTable: "Proxies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProxyImportLines_ProxyImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "audit",
                        principalTable: "ProxyImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProxyImportBatches_ActorId_IdempotencyKey",
                schema: "audit",
                table: "ProxyImportBatches",
                columns: new[] { "ActorId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProxyImportBatches_OccurredAt",
                schema: "audit",
                table: "ProxyImportBatches",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_ProxyImportLines_ProxyId",
                schema: "audit",
                table: "ProxyImportLines",
                column: "ProxyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProxyImportLines",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "ProxyImportBatches",
                schema: "audit");
        }
    }
}
