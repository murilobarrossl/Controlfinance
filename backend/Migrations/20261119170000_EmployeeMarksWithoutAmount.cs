using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    // Valor pago passa a vir só do banco: a marcação manual do funcionário vira só status. Os valores
    // declarados antes continuam nos eventos de auditoria (PreviousManualAmount/NewManualAmount).
    [DbContext(typeof(AppDbContext))]
    [Migration("20261119170000_EmployeeMarksWithoutAmount")]
    public partial class EmployeeMarksWithoutAmount : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ManualAmountEncrypted", table: "EmployeePaymentMarks");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "ManualAmountEncrypted", table: "EmployeePaymentMarks", type: "text", nullable: true);
        }
    }
}
