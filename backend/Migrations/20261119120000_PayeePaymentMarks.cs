using System;
using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261119120000_PayeePaymentMarks")]
    public partial class PayeePaymentMarks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "EmployeePaymentMarks", columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                ReferenceMonth = table.Column<DateOnly>(type: "date", nullable: false),
                Paid = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_EmployeePaymentMarks", x => x.Id);
                table.ForeignKey(name: "FK_EmployeePaymentMark_Employee", columns: x => new { x.UserId, x.EmployeeId }, principalTable: "Employees", principalColumns: new[] { "UserId", "Id" }, onDelete: ReferentialAction.Cascade);
            });

            migrationBuilder.CreateTable(name: "SupplierPaymentMarks", columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                ReferenceMonth = table.Column<DateOnly>(type: "date", nullable: false),
                Paid = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_SupplierPaymentMarks", x => x.Id);
                table.ForeignKey(name: "FK_SupplierPaymentMark_Supplier", columns: x => new { x.UserId, x.SupplierId }, principalTable: "Suppliers", principalColumns: new[] { "UserId", "Id" }, onDelete: ReferentialAction.Cascade);
            });

            migrationBuilder.CreateIndex(name: "IX_EmployeePaymentMarks_UserId_EmployeeId_ReferenceMonth", table: "EmployeePaymentMarks", columns: new[] { "UserId", "EmployeeId", "ReferenceMonth" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_EmployeePaymentMarks_UserId_ReferenceMonth", table: "EmployeePaymentMarks", columns: new[] { "UserId", "ReferenceMonth" });
            migrationBuilder.CreateIndex(name: "IX_SupplierPaymentMarks_UserId_SupplierId_ReferenceMonth", table: "SupplierPaymentMarks", columns: new[] { "UserId", "SupplierId", "ReferenceMonth" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_SupplierPaymentMarks_UserId_ReferenceMonth", table: "SupplierPaymentMarks", columns: new[] { "UserId", "ReferenceMonth" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EmployeePaymentMarks");
            migrationBuilder.DropTable(name: "SupplierPaymentMarks");
        }
    }
}
