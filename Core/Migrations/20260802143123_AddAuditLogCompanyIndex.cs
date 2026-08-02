using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PermitPro.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogCompanyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CompanyId_LogType_CreatedWhen",
                table: "AuditLogs",
                columns: new[] { "CompanyId", "LogType", "CreatedWhen" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_CompanyId_LogType_CreatedWhen",
                table: "AuditLogs");
        }
    }
}
