using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralContingencia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProxyPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "proxies");

            migrationBuilder.CreateTable(
                name: "Proxies",
                schema: "proxies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Username = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PasswordKeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PasswordNonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    PasswordCiphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    PasswordTag = table.Column<byte[]>(type: "bytea", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Proxies", x => x.Id);
                    table.CheckConstraint("CK_Proxies_Host", "length(btrim(\"Host\")) BETWEEN 1 AND 253 AND \"Host\" = lower(\"Host\") AND \"Host\" !~ '[[:space:]/@?#]'");
                    table.CheckConstraint("CK_Proxies_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Proxies_Password", "octet_length(\"PasswordNonce\") = 12 AND octet_length(\"PasswordTag\") = 16 AND octet_length(\"PasswordCiphertext\") BETWEEN 1 AND 1024 AND \"PasswordKeyId\" ~ '^[A-Za-z0-9_.-]{1,64}$'");
                    table.CheckConstraint("CK_Proxies_Port", "\"Port\" BETWEEN 1 AND 65535");
                    table.CheckConstraint("CK_Proxies_Region", "length(btrim(\"Region\")) BETWEEN 1 AND 100 AND \"Region\" !~ '[[:cntrl:]]'");
                    table.CheckConstraint("CK_Proxies_Status", "\"Status\" IN ('Active', 'Suspended', 'Disabled')");
                    table.CheckConstraint("CK_Proxies_Timestamps", "isfinite(\"CreatedAt\") AND isfinite(\"UpdatedAt\") AND isfinite(\"ExpiresAt\") AND \"UpdatedAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_Proxies_Username", "length(btrim(\"Username\")) BETWEEN 1 AND 128 AND \"Username\" !~ '[[:cntrl:]]'");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Proxies_Host_Port",
                schema: "proxies",
                table: "Proxies",
                columns: new[] { "Host", "Port" });

            migrationBuilder.CreateIndex(
                name: "IX_Proxies_Region",
                schema: "proxies",
                table: "Proxies",
                column: "Region");

            migrationBuilder.CreateIndex(
                name: "IX_Proxies_Status_ExpiresAt",
                schema: "proxies",
                table: "Proxies",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Proxies_PasswordKeyId_Nonce",
                schema: "proxies",
                table: "Proxies",
                columns: new[] { "PasswordKeyId", "PasswordNonce" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Proxies",
                schema: "proxies");
        }
    }
}
