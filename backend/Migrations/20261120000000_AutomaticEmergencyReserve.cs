using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    // A reserva de emergência passou a ser só automática (caixinha identificada pelo nome). Marcações manuais
    // antigas (vínculo sem meta, conta vinculada e exclusões da regra automática) deixam de existir para não
    // influenciar a reserva. Vínculos a metas, investimentos e caixinhas não mudam.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261120000000_AutomaticEmergencyReserve")]
    public partial class AutomaticEmergencyReserve : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "PlanningAllocations" WHERE "GoalId" IS NULL;
                UPDATE "EmergencyReserves" SET "BankAccountId" = NULL, "ExcludedAutoReserveIdsJson" = '[]';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
