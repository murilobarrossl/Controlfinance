using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace ControlFinance.API.Migrations
{
    public partial class InvestmentReserveResource : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(name: "ImportedInvestmentId", table: "EmergencyReserves", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "ImportedReservedBalanceId", table: "EmergencyReserves", type: "uuid", nullable: true);
            migrationBuilder.CreateIndex(name: "IX_EmergencyReserves_ImportedInvestmentId", table: "EmergencyReserves", column: "ImportedInvestmentId");
            migrationBuilder.CreateIndex(name: "IX_EmergencyReserves_ImportedReservedBalanceId", table: "EmergencyReserves", column: "ImportedReservedBalanceId");
            migrationBuilder.AddForeignKey(name: "FK_Reserve_ImportedInvestment", table: "EmergencyReserves", column: "ImportedInvestmentId", principalTable: "ImportedInvestments", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
            migrationBuilder.AddForeignKey(name: "FK_Reserve_ImportedBalance", table: "EmergencyReserves", column: "ImportedReservedBalanceId", principalTable: "ImportedReservedBalances", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
            migrationBuilder.AddCheckConstraint(name: "CK_EmergencyReserve_OneResource", table: "EmergencyReserves",
                sql: "(\"BankAccountId\" IS NULL OR \"ImportedInvestmentId\" IS NULL) AND (\"BankAccountId\" IS NULL OR \"ImportedReservedBalanceId\" IS NULL) AND (\"ImportedInvestmentId\" IS NULL OR \"ImportedReservedBalanceId\" IS NULL)");
        }
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Preserve os vínculos de reserva ao reverter o código. A remoção exige um plano explícito de preservação.");
    }
}
