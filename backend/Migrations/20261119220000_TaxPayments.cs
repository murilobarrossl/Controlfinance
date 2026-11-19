using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261119220000_TaxPayments")]
    public partial class TaxPayments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "PolpTaxPayment", table: "Transactions", type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<string>(name: "TaxAuthority", table: "Transactions", type: "character varying(10)", maxLength: 10, nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PolpTaxPayment", table: "Transactions");
            migrationBuilder.DropColumn(name: "TaxAuthority", table: "Transactions");
        }
    }
}
