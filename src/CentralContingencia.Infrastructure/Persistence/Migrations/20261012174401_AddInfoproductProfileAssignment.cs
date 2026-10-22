using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralContingencia.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInfoproductProfileAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Profiles_Platform1",
                schema: "profiles",
                table: "OperationalProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Profiles_Platform2",
                schema: "profiles",
                table: "OperationalProfiles");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Profiles_Platform1",
                schema: "profiles",
                table: "OperationalProfiles",
                sql: "\"Platform1\" IS NULL OR \"Platform1\" IN ('DARKPOST','MODERAÇÃO','ANUNCIANTE','HOST PÁGINAS','FLAG','BACKUP PIXEL','ESTOURADO','RESTRITA','BUGADO','ATIVO (LIVRE)','ESTEIRA DE PERFIL','MINERAÇÃO','FLAG (LIVRE)','TIKTOK','BM','X1','GOOGLE ADS','INFOPRODUTO')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Profiles_Platform2",
                schema: "profiles",
                table: "OperationalProfiles",
                sql: "\"Platform2\" IS NULL OR \"Platform2\" IN ('DARKPOST','MODERAÇÃO','ANUNCIANTE','HOST PÁGINAS','FLAG','BACKUP PIXEL','ESTOURADO','RESTRITA','BUGADO','ATIVO (LIVRE)','ESTEIRA DE PERFIL','MINERAÇÃO','FLAG (LIVRE)','TIKTOK','BM','X1','GOOGLE ADS','INFOPRODUTO')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Profiles_Platform1",
                schema: "profiles",
                table: "OperationalProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Profiles_Platform2",
                schema: "profiles",
                table: "OperationalProfiles");

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
    }
}
