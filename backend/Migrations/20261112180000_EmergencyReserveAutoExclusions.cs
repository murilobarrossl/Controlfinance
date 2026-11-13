using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace ControlFinance.API.Migrations
{
    public partial class EmergencyReserveAutoExclusions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.AddColumn<string>(name: "ExcludedAutoReserveIdsJson", table: "EmergencyReserves", type: "text", nullable: false, defaultValue: "[]");

        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.DropColumn(name: "ExcludedAutoReserveIdsJson", table: "EmergencyReserves");
    }
}
