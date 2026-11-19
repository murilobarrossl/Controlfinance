using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261120020000_CardRecurringSnapshots")]
    public partial class CardRecurringSnapshots : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "PolpRecurringsEncrypted", table: "BankAccounts", type: "text", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "PolpRecurringsSyncedAt", table: "BankAccounts", type: "timestamp with time zone", nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PolpRecurringsEncrypted", table: "BankAccounts");
            migrationBuilder.DropColumn(name: "PolpRecurringsSyncedAt", table: "BankAccounts");
        }
    }
}
