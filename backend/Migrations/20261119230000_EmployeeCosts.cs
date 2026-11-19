using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    // 13º e férias antigos são descartados (decisão do produto, sem conversão): só linhas desses três tipos e
    // os vínculos e eventos delas. Encargos (Inss/Fgts/Irrf/OtherCharge), transações e funcionários ficam.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261119230000_EmployeeCosts")]
    public partial class EmployeeCosts : Migration
    {
        private const string LegacyKinds = "('ThirteenthFirst', 'ThirteenthSecond', 'Vacation')";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DELETE FROM "PayrollObligationPayments"
                WHERE "ObligationId" IN (SELECT "Id" FROM "PayrollObligations" WHERE "Kind" IN {LegacyKinds});
                DELETE FROM "PayrollObligations" WHERE "Kind" IN {LegacyKinds};
                DELETE FROM "PayrollObligationEvents" WHERE "Kind" IN {LegacyKinds};
                """);

            migrationBuilder.DropIndex(name: "IX_PayrollObligations_UserId_EmployeeId_Kind_Year", table: "PayrollObligations");
            migrationBuilder.DropColumn(name: "Year", table: "PayrollObligations");
            migrationBuilder.DropColumn(name: "PeriodStart", table: "PayrollObligations");
            migrationBuilder.DropColumn(name: "PeriodEnd", table: "PayrollObligations");

            migrationBuilder.AlterColumn<string>(name: "AmountEncrypted", table: "PayrollObligations", type: "text", nullable: true,
                oldClrType: typeof(string), oldType: "text");
            migrationBuilder.AlterColumn<DateOnly>(name: "DueDate", table: "PayrollObligations", type: "date", nullable: true,
                oldClrType: typeof(DateOnly), oldType: "date");

            migrationBuilder.CreateIndex(name: "IX_PayrollObligations_UserId_EmployeeId", table: "PayrollObligations",
                columns: new[] { "UserId", "EmployeeId" });

            migrationBuilder.AddColumn<string>(name: "DescriptionEncrypted", table: "PayrollObligationEvents", type: "text", nullable: true);

            migrationBuilder.AddColumn<bool>(name: "CostsSeeded", table: "Employees", type: "boolean", nullable: false, defaultValue: false);
        }

        // Volta só o esquema; os registros descartados não voltam. Falha se houver gasto sem valor.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CostsSeeded", table: "Employees");
            migrationBuilder.DropColumn(name: "DescriptionEncrypted", table: "PayrollObligationEvents");
            migrationBuilder.DropIndex(name: "IX_PayrollObligations_UserId_EmployeeId", table: "PayrollObligations");

            migrationBuilder.AlterColumn<DateOnly>(name: "DueDate", table: "PayrollObligations", type: "date", nullable: false,
                defaultValue: new DateOnly(1, 1, 1), oldClrType: typeof(DateOnly), oldType: "date", oldNullable: true);
            migrationBuilder.AlterColumn<string>(name: "AmountEncrypted", table: "PayrollObligations", type: "text", nullable: false,
                defaultValue: "", oldClrType: typeof(string), oldType: "text", oldNullable: true);

            migrationBuilder.AddColumn<int>(name: "Year", table: "PayrollObligations", type: "integer", nullable: true);
            migrationBuilder.AddColumn<DateOnly>(name: "PeriodStart", table: "PayrollObligations", type: "date", nullable: true);
            migrationBuilder.AddColumn<DateOnly>(name: "PeriodEnd", table: "PayrollObligations", type: "date", nullable: true);
            migrationBuilder.CreateIndex(name: "IX_PayrollObligations_UserId_EmployeeId_Kind_Year", table: "PayrollObligations",
                columns: new[] { "UserId", "EmployeeId", "Kind", "Year" }, unique: true, filter: "\"Year\" IS NOT NULL");
        }
    }
}
