using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PermitPro.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPermitRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedWhen",
                table: "Permits",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Permits",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PermitRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleType = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastRunWhen = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastRunSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedWhen = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedWhen = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedWhen = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PermitRules_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PermitRuleRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermitRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstRunWhen = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastRunWhen = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RunCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitRuleRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PermitRuleRuns_PermitRules_PermitRuleId",
                        column: x => x.PermitRuleId,
                        principalTable: "PermitRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PermitRuleRuns_Permits_PermitId",
                        column: x => x.PermitId,
                        principalTable: "Permits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PermitRuleRuns_PermitId",
                table: "PermitRuleRuns",
                column: "PermitId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitRuleRuns_PermitRuleId_PermitId",
                table: "PermitRuleRuns",
                columns: new[] { "PermitRuleId", "PermitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PermitRules_CompanyId_RuleType",
                table: "PermitRules",
                columns: new[] { "CompanyId", "RuleType" },
                unique: true,
                filter: "[IsDeleted] = 0");

            // Old-system permits saved before 2026-08 have their start/end dates only in the PermitForm
            // JSON. Copy them into the columns (same UTC value the app saves today) so the Overdue rule
            // and other queries can rely on Permits.StartDateTime/EndDateTime. Not reverted by Down().
            migrationBuilder.Sql(@"
UPDATE Permits
SET StartDateTime = TRY_CONVERT(datetime2, JSON_VALUE(PermitForm, '$.general.startDateTime'), 127)
WHERE StartDateTime IS NULL AND ISJSON(PermitForm) = 1
  AND TRY_CONVERT(datetime2, JSON_VALUE(PermitForm, '$.general.startDateTime'), 127) IS NOT NULL;

UPDATE Permits
SET EndDateTime = TRY_CONVERT(datetime2, JSON_VALUE(PermitForm, '$.general.endDateTime'), 127)
WHERE EndDateTime IS NULL AND ISJSON(PermitForm) = 1
  AND TRY_CONVERT(datetime2, JSON_VALUE(PermitForm, '$.general.endDateTime'), 127) IS NOT NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PermitRuleRuns");

            migrationBuilder.DropTable(
                name: "PermitRules");

            migrationBuilder.DropColumn(
                name: "ArchivedWhen",
                table: "Permits");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Permits");
        }
    }
}
