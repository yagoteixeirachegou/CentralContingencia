using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralContingencia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileRecoveryReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProfileRecoveryBatches",
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
                    Recovered = table.Column<int>(type: "integer", nullable: false),
                    Ignored = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileRecoveryBatches", x => x.Id);
                    table.CheckConstraint("CK_Recovery_Correlation", "length(btrim(\"CorrelationId\")) BETWEEN 1 AND 128 AND \"CorrelationId\" !~ '[[:cntrl:]]'");
                    table.CheckConstraint("CK_Recovery_Counts", "\"Total\" BETWEEN 1 AND 10000 AND \"Recovered\" >= 0 AND \"Ignored\" >= 0 AND \"Total\" = \"Recovered\" + \"Ignored\"");
                    table.CheckConstraint("CK_Recovery_Ids", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ActorId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdempotencyKey\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Recovery_Time", "isfinite(\"OccurredAt\")");
                });

            migrationBuilder.CreateTable(
                name: "ProfileRecoveryLines",
                schema: "audit",
                columns: table => new
                {
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Line = table.Column<int>(type: "integer", nullable: false),
                    ProxyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileRecoveryLines", x => new { x.BatchId, x.Line });
                    table.CheckConstraint("CK_RecoveryLine_Outcome", "\"Line\" >= 2 AND \"Outcome\" IN ('Recovered','Ignored','AlreadyLinked') AND (\"Outcome\" = 'Ignored' OR (\"ProxyId\" IS NOT NULL AND \"ProfileId\" IS NOT NULL AND \"AllocationId\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_ProfileRecoveryLines_OperationalProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalSchema: "profiles",
                        principalTable: "OperationalProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProfileRecoveryLines_ProfileRecoveryBatches_BatchId",
                        column: x => x.BatchId,
                        principalSchema: "audit",
                        principalTable: "ProfileRecoveryBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfileRecoveryLines_Proxies_ProxyId",
                        column: x => x.ProxyId,
                        principalSchema: "proxies",
                        principalTable: "Proxies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProfileRecoveryLines_ProxyAllocations_AllocationId",
                        column: x => x.AllocationId,
                        principalSchema: "allocations",
                        principalTable: "ProxyAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileRecoveryBatches_ActorId_IdempotencyKey",
                schema: "audit",
                table: "ProfileRecoveryBatches",
                columns: new[] { "ActorId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileRecoveryBatches_OccurredAt",
                schema: "audit",
                table: "ProfileRecoveryBatches",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileRecoveryLines_AllocationId",
                schema: "audit",
                table: "ProfileRecoveryLines",
                column: "AllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileRecoveryLines_ProfileId",
                schema: "audit",
                table: "ProfileRecoveryLines",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileRecoveryLines_ProxyId",
                schema: "audit",
                table: "ProfileRecoveryLines",
                column: "ProxyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfileRecoveryLines",
                schema: "audit");

            migrationBuilder.DropTable(
                name: "ProfileRecoveryBatches",
                schema: "audit");
        }
    }
}
