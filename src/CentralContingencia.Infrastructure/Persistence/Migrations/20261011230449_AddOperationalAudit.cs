using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralContingencia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OperationalEvents",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalEvents", x => x.Id);
                    table.CheckConstraint("CK_OperationalEvents_Action", "(\"Action\" IN ('ProfileCreated','ProfileUpdated','ProfileClosed') AND \"AllocationId\" IS NULL) OR (\"Action\" IN ('AllocationReserved','AllocationConfirmed','AllocationReleased','AllocationExpired') AND \"AllocationId\" IS NOT NULL)");
                    table.CheckConstraint("CK_OperationalEvents_Correlation", "length(btrim(\"CorrelationId\")) BETWEEN 1 AND 128 AND \"CorrelationId\" !~ '[[:cntrl:]]'");
                    table.CheckConstraint("CK_OperationalEvents_Ids", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ActorId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ProfileId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND (\"AllocationId\" IS NULL OR \"AllocationId\" <> '00000000-0000-0000-0000-000000000000'::uuid)");
                    table.CheckConstraint("CK_OperationalEvents_Time", "isfinite(\"OccurredAt\")");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalEvents_AllocationId_OccurredAt",
                schema: "audit",
                table: "OperationalEvents",
                columns: new[] { "AllocationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalEvents_ProfileId_OccurredAt",
                schema: "audit",
                table: "OperationalEvents",
                columns: new[] { "ProfileId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OperationalEvents",
                schema: "audit");
        }
    }
}
