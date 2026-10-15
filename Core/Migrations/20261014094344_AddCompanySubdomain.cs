using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PermitPro.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanySubdomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Subdomain",
                table: "Companies",
                type: "nvarchar(63)",
                maxLength: 63,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Companies_Subdomain",
                table: "Companies",
                column: "Subdomain",
                unique: true,
                filter: "[Subdomain] IS NOT NULL");

            // IOTM is reached at iotm.permitpro.app. Other companies: Super Admin → Companies → Edit.
            migrationBuilder.Sql("UPDATE [Companies] SET [Subdomain] = N'iotm' WHERE [Name] = N'IOTM' AND [Subdomain] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Companies_Subdomain",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Subdomain",
                table: "Companies");
        }
    }
}
