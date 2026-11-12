using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    public partial class PreservePolpOriginalCardName : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No backfill from editable Name: existing rows have no trustworthy provenance.
            migrationBuilder.AddColumn<string>(
                name: "PolpOriginalNameEncrypted",
                table: "BankAccounts",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PolpOriginalNameEncrypted",
                table: "BankAccounts");
        }
    }
}
