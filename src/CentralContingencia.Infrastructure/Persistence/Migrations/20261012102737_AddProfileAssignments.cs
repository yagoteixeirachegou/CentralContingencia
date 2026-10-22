using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralContingencia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Platform",
                schema: "profiles",
                table: "OperationalProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<string>(
                name: "Platform1",
                schema: "profiles",
                table: "OperationalProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Platform2",
                schema: "profiles",
                table: "OperationalProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Profiles_Platform1",
                schema: "profiles",
                table: "OperationalProfiles",
                sql: "\"Platform1\" IS NULL OR \"Platform1\" IN ('DARKPOST','MODERAÇÃO','ANUNCIANTE','HOST PÁGINAS','FLAG','BACKUP PIXEL','ESTOURADO','RESTRITA','BUGADO','ATIVO (LIVRE)','ESTEIRA DE PERFIL','MINERAÇÃO','FLAG (LIVRE)','TIKTOK','BM','X1','GOOGLE ADS')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Profiles_Platform2",
                schema: "profiles",
                table: "OperationalProfiles",
                sql: "\"Platform2\" IS NULL OR \"Platform2\" IN ('DARKPOST','MODERAÇÃO','ANUNCIANTE','HOST PÁGINAS','FLAG','BACKUP PIXEL','ESTOURADO','RESTRITA','BUGADO','ATIVO (LIVRE)','ESTEIRA DE PERFIL','MINERAÇÃO','FLAG (LIVRE)','TIKTOK','BM','X1','GOOGLE ADS')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never invent legacy values to satisfy the former NOT NULL constraint.
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM profiles.\"OperationalProfiles\" WHERE \"Platform\" IS NULL) THEN RAISE EXCEPTION 'Downgrade requires an explicitly reviewed legacy-data recovery plan'; END IF; END; $$;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_Profiles_Platform1",
                schema: "profiles",
                table: "OperationalProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Profiles_Platform2",
                schema: "profiles",
                table: "OperationalProfiles");

            migrationBuilder.DropColumn(
                name: "Platform1",
                schema: "profiles",
                table: "OperationalProfiles");

            migrationBuilder.DropColumn(
                name: "Platform2",
                schema: "profiles",
                table: "OperationalProfiles");

            migrationBuilder.AlterColumn<string>(
                name: "Platform",
                schema: "profiles",
                table: "OperationalProfiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }
    }
}
