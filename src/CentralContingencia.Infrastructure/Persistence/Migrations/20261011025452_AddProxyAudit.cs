using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralContingencia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProxyAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "proxies",
                table: "Proxies",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "ProxyEvents",
                schema: "audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProxyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProxyEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProxyEvents_ActorId",
                schema: "audit",
                table: "ProxyEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_ProxyEvents_ProxyId_OccurredAt",
                schema: "audit",
                table: "ProxyEvents",
                columns: new[] { "ProxyId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProxyEvents",
                schema: "audit");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "proxies",
                table: "Proxies");
        }
    }
}
