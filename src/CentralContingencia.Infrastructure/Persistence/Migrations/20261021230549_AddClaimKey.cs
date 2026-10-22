using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralContingencia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClaimKey",
                schema: "work",
                table: "Tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Claim",
                schema: "work",
                table: "Tasks",
                columns: new[] { "WorkerId", "ClaimKey" },
                filter: "\"Status\" = 'Running' AND \"ClaimKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_Claim",
                schema: "work",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ClaimKey",
                schema: "work",
                table: "Tasks");
        }
    }
}
