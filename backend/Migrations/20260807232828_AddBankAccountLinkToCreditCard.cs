using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class AddBankAccountLinkToCreditCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BankAccountId",
                table: "CreditCards",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditCards_BankAccountId",
                table: "CreditCards",
                column: "BankAccountId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CreditCards_BankAccounts_BankAccountId",
                table: "CreditCards",
                column: "BankAccountId",
                principalTable: "BankAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreditCards_BankAccounts_BankAccountId",
                table: "CreditCards");

            migrationBuilder.DropIndex(
                name: "IX_CreditCards_BankAccountId",
                table: "CreditCards");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "CreditCards");
        }
    }
}
