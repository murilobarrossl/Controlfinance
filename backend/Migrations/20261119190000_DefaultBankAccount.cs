using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261119190000_DefaultBankAccount")]
    public partial class DefaultBankAccount : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "IsDefault", table: "BankAccounts", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.CreateIndex(name: "IX_BankAccounts_UserId_Default", table: "BankAccounts", column: "UserId", unique: true, filter: "\"IsDefault\"");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_BankAccounts_UserId_Default", table: "BankAccounts");
            migrationBuilder.DropColumn(name: "IsDefault", table: "BankAccounts");
        }
    }
}
