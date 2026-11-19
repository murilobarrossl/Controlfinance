using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class PayrollObligations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PayrollObligationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObligationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AmountEncrypted = table.Column<string>(type: "text", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaymentAmountEncrypted = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollObligationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollObligationEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollObligations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: true),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    Competence = table.Column<DateOnly>(type: "date", nullable: true),
                    DescriptionEncrypted = table.Column<string>(type: "text", nullable: true),
                    AmountEncrypted = table.Column<string>(type: "text", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollObligations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollObligation_Employee",
                        columns: x => new { x.UserId, x.EmployeeId },
                        principalTable: "Employees",
                        principalColumns: new[] { "UserId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollObligations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollObligationPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObligationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountEncrypted = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollObligationPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollObligationPayments_PayrollObligations_ObligationId",
                        column: x => x.ObligationId,
                        principalTable: "PayrollObligations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayrollObligationPayments_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollObligationEvents_UserId_ObligationId_OccurredAt",
                table: "PayrollObligationEvents",
                columns: new[] { "UserId", "ObligationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollObligationPayments_ObligationId_TransactionId",
                table: "PayrollObligationPayments",
                columns: new[] { "ObligationId", "TransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollObligationPayments_TransactionId",
                table: "PayrollObligationPayments",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollObligationPayments_UserId_TransactionId",
                table: "PayrollObligationPayments",
                columns: new[] { "UserId", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollObligations_UserId_Competence",
                table: "PayrollObligations",
                columns: new[] { "UserId", "Competence" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollObligations_UserId_EmployeeId_Kind_Year",
                table: "PayrollObligations",
                columns: new[] { "UserId", "EmployeeId", "Kind", "Year" },
                unique: true,
                filter: "\"Year\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollObligations_UserId_Kind_Competence",
                table: "PayrollObligations",
                columns: new[] { "UserId", "Kind", "Competence" },
                unique: true,
                filter: "\"Kind\" IN ('Inss', 'Fgts', 'Irrf')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollObligationEvents");

            migrationBuilder.DropTable(
                name: "PayrollObligationPayments");

            migrationBuilder.DropTable(
                name: "PayrollObligations");
        }
    }
}
