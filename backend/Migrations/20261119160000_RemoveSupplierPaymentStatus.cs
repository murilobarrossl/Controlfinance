using System;
using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    // Fornecedor deixa de ter qualquer controle de pagamento: marcações e eventos de auditoria de
    // fornecedor são apagados. Os de funcionário ficam.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261119160000_RemoveSupplierPaymentStatus")]
    public partial class RemoveSupplierPaymentStatus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DELETE FROM "PayeePaymentMarkEvents" WHERE "PayeeKind" = 'Supplier';""");
            migrationBuilder.DropTable(name: "SupplierPaymentMarks");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "SupplierPaymentMarks", columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                ReferenceMonth = table.Column<DateOnly>(type: "date", nullable: false),
                Paid = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_SupplierPaymentMarks", x => x.Id);
                table.ForeignKey(name: "FK_SupplierPaymentMark_Supplier", columns: x => new { x.UserId, x.SupplierId }, principalTable: "Suppliers", principalColumns: new[] { "UserId", "Id" }, onDelete: ReferentialAction.Cascade);
            });
            migrationBuilder.CreateIndex(name: "IX_SupplierPaymentMarks_UserId_SupplierId_ReferenceMonth", table: "SupplierPaymentMarks", columns: new[] { "UserId", "SupplierId", "ReferenceMonth" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_SupplierPaymentMarks_UserId_ReferenceMonth", table: "SupplierPaymentMarks", columns: new[] { "UserId", "ReferenceMonth" });
        }
    }
}
