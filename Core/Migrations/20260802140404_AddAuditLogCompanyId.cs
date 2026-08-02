using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PermitPro.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogCompanyId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "AuditLogs",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "AuditLogs");
        }
    }
}
