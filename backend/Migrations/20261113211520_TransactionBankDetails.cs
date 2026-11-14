using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class TransactionBankDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BankDateTime",
                table: "Transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankDescriptionEncrypted",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BankInstallmentNumber",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BankInstallmentTotal",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankOperationType",
                table: "Transactions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillMonth",
                table: "Transactions",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardNumberEncrypted",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CounterpartyAccountEncrypted",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CounterpartyBankCode",
                table: "Transactions",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CounterpartyNameEncrypted",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalAmountEncrypted",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OriginalCurrency",
                table: "Transactions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankDateTime",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BankDescriptionEncrypted",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BankInstallmentNumber",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BankInstallmentTotal",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BankOperationType",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "BillMonth",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CardNumberEncrypted",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CounterpartyAccountEncrypted",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CounterpartyBankCode",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CounterpartyNameEncrypted",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "OriginalAmountEncrypted",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "OriginalCurrency",
                table: "Transactions");
        }
    }
}
