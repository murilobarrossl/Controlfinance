using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class ResetAutoReserveExclusions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A reserva automática passou a incluir toda caixinha com "reserva" no nome. As exclusões
            // anteriores foram feitas sob a regra antiga e são zeradas uma vez, a pedido do responsável
            // pelo produto, para todas essas caixinhas entrarem. Exclusões feitas daqui em diante valem.
            migrationBuilder.Sql("""UPDATE "EmergencyReserves" SET "ExcludedAutoReserveIdsJson" = '[]';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sem volta: as exclusões antigas não são guardadas em outro lugar.
        }
    }
}
