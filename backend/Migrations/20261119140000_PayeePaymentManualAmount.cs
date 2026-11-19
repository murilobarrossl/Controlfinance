using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261119140000_PayeePaymentManualAmount")]
    public partial class PayeePaymentManualAmount : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "ManualAmountEncrypted", table: "EmployeePaymentMarks", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ManualAmountEncrypted", table: "SupplierPaymentMarks", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "PreviousManualAmountEncrypted", table: "PayeePaymentMarkEvents", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "NewManualAmountEncrypted", table: "PayeePaymentMarkEvents", type: "text", nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ManualAmountEncrypted", table: "EmployeePaymentMarks");
            migrationBuilder.DropColumn(name: "ManualAmountEncrypted", table: "SupplierPaymentMarks");
            migrationBuilder.DropColumn(name: "PreviousManualAmountEncrypted", table: "PayeePaymentMarkEvents");
            migrationBuilder.DropColumn(name: "NewManualAmountEncrypted", table: "PayeePaymentMarkEvents");
        }
    }
}
