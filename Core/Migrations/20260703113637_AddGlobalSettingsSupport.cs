using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PermitPro.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalSettingsSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsUsingGlobal",
                table: "AppSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_CategoryId_Key_Global",
                table: "AppSettings",
                columns: new[] { "CategoryId", "Key" },
                unique: true,
                filter: "[CompanyId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppSettingCategories_Name_Global",
                table: "AppSettingCategories",
                column: "Name",
                unique: true,
                filter: "[CompanyId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppSettings_CategoryId_Key_Global",
                table: "AppSettings");

            migrationBuilder.DropIndex(
                name: "IX_AppSettingCategories_Name_Global",
                table: "AppSettingCategories");

            migrationBuilder.DropColumn(
                name: "IsUsingGlobal",
                table: "AppSettings");
        }
    }
}
