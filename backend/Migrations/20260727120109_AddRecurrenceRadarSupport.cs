using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurrenceRadarSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF gerou defaultValue: "" aqui (o conversor HasConversion<string>() não roda o
            // default CLR do enum antes de calcular o default da coluna) — "" não é um nome
            // válido de AccountOwnership, e qualquer leitura de uma linha pré-existente quebraria
            // com Enum.Parse antes até do backfill (Program.cs) ter a chance de rodar. Trocado à
            // mão pro nome do valor 0 do enum (Personal), que o backfill logo em seguida sobrescreve
            // com o chute de verdade por conta.
            migrationBuilder.AddColumn<string>(
                name: "Ownership",
                table: "BankAccounts",
                type: "text",
                nullable: false,
                defaultValue: "Personal");

            migrationBuilder.CreateTable(
                name: "RecurrenceDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    AssumedFrequency = table.Column<string>(type: "text", nullable: true),
                    ReminderRequested = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurrenceDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurrenceDecisions_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecurrenceDecisions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurrenceDecisions_BankAccountId",
                table: "RecurrenceDecisions",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurrenceDecisions_UserId_BankAccountId_NormalizedName",
                table: "RecurrenceDecisions",
                columns: new[] { "UserId", "BankAccountId", "NormalizedName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurrenceDecisions");

            migrationBuilder.DropColumn(
                name: "Ownership",
                table: "BankAccounts");
        }
    }
}
