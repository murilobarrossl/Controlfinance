using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    // Fornecedor volta a ter só o status do mês. O valor declarado sai da marcação; os eventos de
    // auditoria já gravados ficam como estão (histórico).
    [DbContext(typeof(AppDbContext))]
    [Migration("20261119150000_SupplierMarksWithoutAmount")]
    public partial class SupplierMarksWithoutAmount : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ManualAmountEncrypted", table: "SupplierPaymentMarks");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "ManualAmountEncrypted", table: "SupplierPaymentMarks", type: "text", nullable: true);
        }
    }
}
