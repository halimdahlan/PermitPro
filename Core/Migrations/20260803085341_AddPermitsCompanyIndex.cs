using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PermitPro.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPermitsCompanyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Permits_CompanyId",
                table: "Permits");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_CompanyId_IsDeleted_CreatedWhen",
                table: "Permits",
                columns: new[] { "CompanyId", "IsDeleted", "CreatedWhen" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Permits_CompanyId_IsDeleted_CreatedWhen",
                table: "Permits");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_CompanyId",
                table: "Permits",
                column: "CompanyId");
        }
    }
}
