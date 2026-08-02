using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PermitPro.Core.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFormTemplateIsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FormTemplates_CompanyId_TemplateType_Active",
                table: "FormTemplates");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "FormTemplates");

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplates_CompanyId",
                table: "FormTemplates",
                column: "CompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FormTemplates_CompanyId",
                table: "FormTemplates");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "FormTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplates_CompanyId_TemplateType_Active",
                table: "FormTemplates",
                columns: new[] { "CompanyId", "TemplateType", "IsActive" },
                unique: true,
                filter: "[IsActive] = 1");
        }
    }
}
