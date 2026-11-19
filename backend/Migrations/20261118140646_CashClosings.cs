using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class CashClosings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashClosings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    ScopeAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpeningBalanceEncrypted = table.Column<string>(type: "text", nullable: true),
                    IncomeEncrypted = table.Column<string>(type: "text", nullable: false),
                    ExpenseEncrypted = table.Column<string>(type: "text", nullable: false),
                    ClosingBalanceEncrypted = table.Column<string>(type: "text", nullable: true),
                    TransactionCount = table.Column<int>(type: "integer", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReopenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReopenedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashClosings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashClosings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CashClosingItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClosingId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    MovementKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AmountEncrypted = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashClosingItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashClosingItems_CashClosings_ClosingId",
                        column: x => x.ClosingId,
                        principalTable: "CashClosings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashClosingItems_ClosingId_TransactionId",
                table: "CashClosingItems",
                columns: new[] { "ClosingId", "TransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashClosings_UserId_Day",
                table: "CashClosings",
                columns: new[] { "UserId", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_CashClosings_UserId_Day_ScopeAccountId",
                table: "CashClosings",
                columns: new[] { "UserId", "Day", "ScopeAccountId" },
                unique: true,
                filter: "\"ReopenedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashClosingItems");

            migrationBuilder.DropTable(
                name: "CashClosings");
        }
    }
}
