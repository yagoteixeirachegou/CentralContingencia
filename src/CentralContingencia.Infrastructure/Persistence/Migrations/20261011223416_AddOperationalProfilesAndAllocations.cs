using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralContingencia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalProfilesAndAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "profiles");

            migrationBuilder.EnsureSchema(
                name: "allocations");

            migrationBuilder.CreateTable(
                name: "OperationalProfiles",
                schema: "profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Platform = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalProfiles", x => x.Id);
                    table.CheckConstraint("CK_Profiles_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Profiles_Name", "length(btrim(\"Name\")) BETWEEN 1 AND 200 AND \"Name\" = btrim(\"Name\") AND \"Name\" !~ '[[:cntrl:]]'");
                    table.CheckConstraint("CK_Profiles_Platform", "length(btrim(\"Platform\")) BETWEEN 1 AND 100 AND \"Platform\" = btrim(\"Platform\") AND \"Platform\" !~ '[[:cntrl:]]'");
                    table.CheckConstraint("CK_Profiles_State", "(\"Status\" = 'Active' AND \"ClosedAt\" IS NULL) OR (\"Status\" = 'Closed' AND \"ClosedAt\" IS NOT NULL AND \"ClosedAt\" = \"UpdatedAt\")");
                    table.CheckConstraint("CK_Profiles_Times", "isfinite(\"CreatedAt\") AND isfinite(\"UpdatedAt\") AND \"UpdatedAt\" >= \"CreatedAt\" AND (\"ClosedAt\" IS NULL OR (isfinite(\"ClosedAt\") AND \"ClosedAt\" >= \"CreatedAt\"))");
                });

            migrationBuilder.CreateTable(
                name: "ProxyAllocations",
                schema: "allocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProxyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReservationExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProxyAllocations", x => x.Id);
                    table.CheckConstraint("CK_Allocations_Ids", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ProfileId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ProxyId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ActorId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"IdempotencyKey\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Allocations_State", "(\"Status\" = 'Reserved' AND \"ConfirmedAt\" IS NULL AND \"EndedAt\" IS NULL AND \"EndReason\" IS NULL) OR (\"Status\" = 'Confirmed' AND \"ConfirmedAt\" IS NOT NULL AND \"EndedAt\" IS NULL AND \"EndReason\" IS NULL) OR (\"Status\" = 'Released' AND \"EndedAt\" IS NOT NULL AND \"EndReason\" IS NOT NULL AND \"EndReason\" IN ('Released','ProfileClosed')) OR (\"Status\" = 'Expired' AND \"ConfirmedAt\" IS NULL AND \"EndedAt\" IS NOT NULL AND \"EndedAt\" >= \"ReservationExpiresAt\" AND \"EndReason\" = 'ReservationExpired' AND \"EndReason\" IS NOT NULL)");
                    table.CheckConstraint("CK_Allocations_Times", "isfinite(\"CreatedAt\") AND isfinite(\"UpdatedAt\") AND isfinite(\"ReservationExpiresAt\") AND \"UpdatedAt\" >= \"CreatedAt\" AND \"ReservationExpiresAt\" > \"CreatedAt\" AND (\"ConfirmedAt\" IS NULL OR (isfinite(\"ConfirmedAt\") AND \"ConfirmedAt\" >= \"CreatedAt\" AND \"ConfirmedAt\" < \"ReservationExpiresAt\" AND \"ConfirmedAt\" <= \"UpdatedAt\")) AND (\"EndedAt\" IS NULL OR (isfinite(\"EndedAt\") AND \"EndedAt\" >= \"CreatedAt\" AND \"EndedAt\" = \"UpdatedAt\"))");
                    table.ForeignKey(
                        name: "FK_ProxyAllocations_OperationalProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalSchema: "profiles",
                        principalTable: "OperationalProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProxyAllocations_Proxies_ProxyId",
                        column: x => x.ProxyId,
                        principalSchema: "proxies",
                        principalTable: "Proxies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalProfiles_Status_CreatedAt",
                schema: "profiles",
                table: "OperationalProfiles",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProxyAllocations_ActorId_IdempotencyKey",
                schema: "allocations",
                table: "ProxyAllocations",
                columns: new[] { "ActorId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProxyAllocations_ProfileId_CreatedAt",
                schema: "allocations",
                table: "ProxyAllocations",
                columns: new[] { "ProfileId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProxyAllocations_ProxyId_CreatedAt",
                schema: "allocations",
                table: "ProxyAllocations",
                columns: new[] { "ProxyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_ProxyAllocations_ActiveProfile",
                schema: "allocations",
                table: "ProxyAllocations",
                column: "ProfileId",
                unique: true,
                filter: "\"Status\" IN ('Reserved', 'Confirmed')");

            migrationBuilder.CreateIndex(
                name: "UX_ProxyAllocations_ActiveProxy",
                schema: "allocations",
                table: "ProxyAllocations",
                column: "ProxyId",
                unique: true,
                filter: "\"Status\" IN ('Reserved', 'Confirmed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProxyAllocations",
                schema: "allocations");

            migrationBuilder.DropTable(
                name: "OperationalProfiles",
                schema: "profiles");
        }
    }
}
