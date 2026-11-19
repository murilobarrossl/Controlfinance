using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    // Custos do funcionário deixaram de ter vínculo bancário: só esses vínculos saem. Encargos (EmployeeId nulo)
    // mantêm os deles, transações não mudam e os eventos de auditoria ficam como histórico.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261119233000_RemoveEmployeeCostLinks")]
    public partial class RemoveEmployeeCostLinks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "PayrollObligationPayments"
                WHERE "ObligationId" IN (SELECT "Id" FROM "PayrollObligations" WHERE "EmployeeId" IS NOT NULL);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
