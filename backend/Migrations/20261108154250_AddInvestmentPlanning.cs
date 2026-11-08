using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestmentPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmergencyReserves",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentAmountEncrypted = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencyReserves", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_EmergencyReserves_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavingsGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    NameEncrypted = table.Column<string>(type: "text", nullable: false),
                    TargetAmountEncrypted = table.Column<string>(type: "text", nullable: false),
                    CurrentAmountEncrypted = table.Column<string>(type: "text", nullable: false),
                    Deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsGoals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavingsGoals_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavingsGoals_UserId",
                table: "SavingsGoals",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmergencyReserves");

            migrationBuilder.DropTable(
                name: "SavingsGoals");
        }
    }
}
