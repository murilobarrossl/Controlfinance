using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class PlanningAllocationsFromImportedBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanningAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoalId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImportedInvestmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImportedReservedBalanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningAllocations", x => x.Id);
                    table.CheckConstraint("CK_PlanningAllocation_OneResource", "(\"ImportedInvestmentId\" IS NULL) <> (\"ImportedReservedBalanceId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_PlanningAllocations_ImportedInvestments_ImportedInvestmentId",
                        column: x => x.ImportedInvestmentId,
                        principalTable: "ImportedInvestments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanningAllocations_ImportedReservedBalances_ImportedReserv~",
                        column: x => x.ImportedReservedBalanceId,
                        principalTable: "ImportedReservedBalances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanningAllocations_SavingsGoals_GoalId",
                        column: x => x.GoalId,
                        principalTable: "SavingsGoals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanningAllocations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanningAllocations_GoalId",
                table: "PlanningAllocations",
                column: "GoalId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningAllocations_ImportedInvestmentId",
                table: "PlanningAllocations",
                column: "ImportedInvestmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanningAllocations_ImportedReservedBalanceId",
                table: "PlanningAllocations",
                column: "ImportedReservedBalanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanningAllocations_UserId",
                table: "PlanningAllocations",
                column: "UserId");

            // Os investimentos já escolhidos como reserva continuam na reserva. Os valores digitados à mão
            // (valor anotado da reserva e "já guardado" das metas) são descartados de propósito: o
            // planejamento passa a mostrar só saldos informados pelas instituições.
            migrationBuilder.Sql("""
                INSERT INTO "PlanningAllocations" ("Id", "UserId", "GoalId", "ImportedInvestmentId", "ImportedReservedBalanceId", "CreatedAt")
                SELECT md5(random()::text || clock_timestamp()::text || r."UserId"::text)::uuid, r."UserId", NULL,
                       r."ImportedInvestmentId", r."ImportedReservedBalanceId", now()
                FROM "EmergencyReserves" r
                WHERE r."ImportedInvestmentId" IS NOT NULL OR r."ImportedReservedBalanceId" IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Reserve_ImportedBalance",
                table: "EmergencyReserves");

            migrationBuilder.DropForeignKey(
                name: "FK_Reserve_ImportedInvestment",
                table: "EmergencyReserves");

            migrationBuilder.DropIndex(
                name: "IX_EmergencyReserves_ImportedInvestmentId",
                table: "EmergencyReserves");

            migrationBuilder.DropIndex(
                name: "IX_EmergencyReserves_ImportedReservedBalanceId",
                table: "EmergencyReserves");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EmergencyReserve_OneResource",
                table: "EmergencyReserves");

            migrationBuilder.DropColumn(
                name: "CurrentAmountEncrypted",
                table: "SavingsGoals");

            migrationBuilder.DropColumn(
                name: "CurrentAmountEncrypted",
                table: "EmergencyReserves");

            migrationBuilder.DropColumn(
                name: "ImportedInvestmentId",
                table: "EmergencyReserves");

            migrationBuilder.DropColumn(
                name: "ImportedReservedBalanceId",
                table: "EmergencyReserves");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanningAllocations");

            migrationBuilder.AddColumn<string>(
                name: "CurrentAmountEncrypted",
                table: "SavingsGoals",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CurrentAmountEncrypted",
                table: "EmergencyReserves",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ImportedInvestmentId",
                table: "EmergencyReserves",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportedReservedBalanceId",
                table: "EmergencyReserves",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyReserves_ImportedInvestmentId",
                table: "EmergencyReserves",
                column: "ImportedInvestmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EmergencyReserves_ImportedReservedBalanceId",
                table: "EmergencyReserves",
                column: "ImportedReservedBalanceId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmergencyReserve_OneResource",
                table: "EmergencyReserves",
                sql: "(\"BankAccountId\" IS NULL OR \"ImportedInvestmentId\" IS NULL) AND (\"BankAccountId\" IS NULL OR \"ImportedReservedBalanceId\" IS NULL) AND (\"ImportedInvestmentId\" IS NULL OR \"ImportedReservedBalanceId\" IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_Reserve_ImportedBalance",
                table: "EmergencyReserves",
                column: "ImportedReservedBalanceId",
                principalTable: "ImportedReservedBalances",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Reserve_ImportedInvestment",
                table: "EmergencyReserves",
                column: "ImportedInvestmentId",
                principalTable: "ImportedInvestments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
