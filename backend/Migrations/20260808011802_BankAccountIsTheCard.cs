using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class BankAccountIsTheCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AddColumn<Guid>(
                name: "BankAccountId",
                table: "Installments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Brand",
                table: "BankAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClosingDay",
                table: "BankAccounts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreditLimitEncrypted",
                table: "BankAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentInvoiceAmountEncrypted",
                table: "BankAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DueDay",
                table: "BankAccounts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvoiceDueDate",
                table: "BankAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UsedLimitEncrypted",
                table: "BankAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Installments_BankAccountId",
                table: "Installments",
                column: "BankAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Installments_BankAccounts_BankAccountId",
                table: "Installments",
                column: "BankAccountId",
                principalTable: "BankAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Installments_BankAccounts_BankAccountId",
                table: "Installments");

            migrationBuilder.DropIndex(
                name: "IX_Installments_BankAccountId",
                table: "Installments");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "Installments");

            migrationBuilder.DropColumn(
                name: "Brand",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "ClosingDay",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "CreditLimitEncrypted",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "CurrentInvoiceAmountEncrypted",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "DueDay",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "InvoiceDueDate",
                table: "BankAccounts");

            migrationBuilder.DropColumn(
                name: "UsedLimitEncrypted",
                table: "BankAccounts");

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
    }
}
