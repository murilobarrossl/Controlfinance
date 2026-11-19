using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261120010000_RemovePayrollCharges")]
    public partial class RemovePayrollCharges : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove apenas encargos. EmployeeCost, funcionários, transações e a auditoria dos custos ficam.
            migrationBuilder.Sql("""
                DELETE FROM "PayrollObligationEvents" WHERE "Kind" IN ('Inss', 'Fgts', 'Irrf', 'OtherCharge');
                DELETE FROM "PayrollObligations" WHERE "Kind" IN ('Inss', 'Fgts', 'Irrf', 'OtherCharge');
                """);

            // Os vínculos dos custos já foram retirados por RemoveEmployeeCostLinks.
            migrationBuilder.DropTable(name: "PayrollObligationPayments");
            migrationBuilder.DropIndex(name: "IX_PayrollObligations_UserId_Competence", table: "PayrollObligations");
            migrationBuilder.DropIndex(name: "IX_PayrollObligations_UserId_Kind_Competence", table: "PayrollObligations");
            migrationBuilder.DropColumn(name: "Competence", table: "PayrollObligations");
            migrationBuilder.DropColumn(name: "DueDate", table: "PayrollObligations");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restaura somente a estrutura; os registros de encargos excluídos não são recuperáveis.
            migrationBuilder.AddColumn<DateOnly>(name: "Competence", table: "PayrollObligations", type: "date", nullable: true);
            migrationBuilder.AddColumn<DateOnly>(name: "DueDate", table: "PayrollObligations", type: "date", nullable: true);
            migrationBuilder.CreateIndex(name: "IX_PayrollObligations_UserId_Competence", table: "PayrollObligations",
                columns: new[] { "UserId", "Competence" });
            migrationBuilder.CreateIndex(name: "IX_PayrollObligations_UserId_Kind_Competence", table: "PayrollObligations",
                columns: new[] { "UserId", "Kind", "Competence" }, unique: true, filter: "\"Kind\" IN ('Inss', 'Fgts', 'Irrf')");

            migrationBuilder.CreateTable(
                name: "PayrollObligationPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObligationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountEncrypted = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollObligationPayments", x => x.Id);
                    table.ForeignKey(name: "FK_PayrollObligationPayments_PayrollObligations_ObligationId", column: x => x.ObligationId,
                        principalTable: "PayrollObligations", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(name: "FK_PayrollObligationPayments_Transactions_TransactionId", column: x => x.TransactionId,
                        principalTable: "Transactions", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });
            migrationBuilder.CreateIndex(name: "IX_PayrollObligationPayments_ObligationId_TransactionId", table: "PayrollObligationPayments",
                columns: new[] { "ObligationId", "TransactionId" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_PayrollObligationPayments_TransactionId", table: "PayrollObligationPayments", column: "TransactionId");
            migrationBuilder.CreateIndex(name: "IX_PayrollObligationPayments_UserId_TransactionId", table: "PayrollObligationPayments",
                columns: new[] { "UserId", "TransactionId" });
        }
    }
}
