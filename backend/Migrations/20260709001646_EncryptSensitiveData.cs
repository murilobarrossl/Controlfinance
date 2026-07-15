using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class EncryptSensitiveData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Só adiciona colunas novas (nullable); as antigas em texto puro continuam
            // intactas até o backfill (Program.cs) criptografar e zerá-las explicitamente.
            // Isso evita qualquer ALTER COLUMN que possa mascarar dado não criptografado
            // como se já estivesse protegido.

            migrationBuilder.DropIndex(
                name: "IX_Users_Document",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "Document",
                table: "Users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(14)",
                oldMaxLength: 14);

            migrationBuilder.AddColumn<string>(
                name: "DocumentHash",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AmountEncrypted",
                table: "Transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TotalAmountEncrypted",
                table: "Installments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstallmentAmountEncrypted",
                table: "Installments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UsedLimitEncrypted",
                table: "CreditCards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreditLimitEncrypted",
                table: "CreditCards",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BalanceEncrypted",
                table: "BankAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_DocumentHash",
                table: "Users",
                column: "DocumentHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_DocumentHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DocumentHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AmountEncrypted",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TotalAmountEncrypted",
                table: "Installments");

            migrationBuilder.DropColumn(
                name: "InstallmentAmountEncrypted",
                table: "Installments");

            migrationBuilder.DropColumn(
                name: "UsedLimitEncrypted",
                table: "CreditCards");

            migrationBuilder.DropColumn(
                name: "CreditLimitEncrypted",
                table: "CreditCards");

            migrationBuilder.DropColumn(
                name: "BalanceEncrypted",
                table: "BankAccounts");

            migrationBuilder.AlterColumn<string>(
                name: "Document",
                table: "Users",
                type: "character varying(14)",
                maxLength: 14,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Document",
                table: "Users",
                column: "Document",
                unique: true);
        }
    }
}
