using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PermitPro.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPermitTitleColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Permits",
                type: "nvarchar(max)",
                nullable: true);

            // Backfill Title and Description from PermitForm JSON for old-system permits.
            // general.description maps to both Title (truncated) and Description.
            migrationBuilder.Sql(@"
                UPDATE Permits
                SET
                    Title = LEFT(
                        ISNULL(
                            JSON_VALUE(PermitForm, '$.general.description'),
                            CONCAT('PTW', FORMAT(RunningNumber, '000000'))
                        ), 200),
                    Description = JSON_VALUE(PermitForm, '$.general.description')
                WHERE PermitForm IS NOT NULL
                  AND ISJSON(PermitForm) = 1;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Title",
                table: "Permits");
        }
    }
}
