using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    public partial class InvestmentRatesAndReserveAccount : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BankAccountId",
                table: "EmergencyReserves",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyReserves_BankAccountId",
                table: "EmergencyReserves",
                column: "BankAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_EmergencyReserves_BankAccounts_BankAccountId",
                table: "EmergencyReserves",
                column: "BankAccountId",
                principalTable: "BankAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.CreateTable(
                name: "InvestmentRateProfiles",
                columns: table => new
                {
                    BankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Value = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    IncomeTaxExempt = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentRateProfiles", x => x.BankAccountId);
                    table.ForeignKey(
                        name: "FK_InvestmentRateProfiles_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvestmentRateProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentRateProfiles_UserId",
                table: "InvestmentRateProfiles",
                column: "UserId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvestmentRateProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_EmergencyReserves_BankAccounts_BankAccountId",
                table: "EmergencyReserves");

            migrationBuilder.DropIndex(
                name: "IX_EmergencyReserves_BankAccountId",
                table: "EmergencyReserves");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "EmergencyReserves");
        }
    }
}
