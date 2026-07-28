using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PermitPro.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificateFormBuilder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CertificateDataJson",
                table: "Certificates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FormTemplateId",
                table: "Certificates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_FormTemplateId",
                table: "Certificates",
                column: "FormTemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_Certificates_FormTemplates_FormTemplateId",
                table: "Certificates",
                column: "FormTemplateId",
                principalTable: "FormTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Certificates_FormTemplates_FormTemplateId",
                table: "Certificates");

            migrationBuilder.DropIndex(
                name: "IX_Certificates_FormTemplateId",
                table: "Certificates");

            migrationBuilder.DropColumn(
                name: "CertificateDataJson",
                table: "Certificates");

            migrationBuilder.DropColumn(
                name: "FormTemplateId",
                table: "Certificates");
        }
    }
}
