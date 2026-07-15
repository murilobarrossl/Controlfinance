using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyPlaintextColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // As colunas antigas em texto puro já foram zeradas pelo backfill da migration
            // anterior (EncryptionBackfill); os valores reais só existem nas colunas
            // *Encrypted a partir daqui. Sem isso, INSERTs novos falham (NOT NULL órfão).
            migrationBuilder.DropColumn(name: "Balance", table: "BankAccounts");
            migrationBuilder.DropColumn(name: "Amount", table: "Transactions");
            migrationBuilder.DropColumn(name: "CreditLimit", table: "CreditCards");
            migrationBuilder.DropColumn(name: "UsedLimit", table: "CreditCards");
            migrationBuilder.DropColumn(name: "TotalAmount", table: "Installments");
            migrationBuilder.DropColumn(name: "InstallmentAmount", table: "Installments");

            migrationBuilder.AlterColumn<string>(
                name: "DocumentHash",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Balance", table: "BankAccounts", type: "numeric(18,2)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(
                name: "Amount", table: "Transactions", type: "numeric(18,2)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(
                name: "CreditLimit", table: "CreditCards", type: "numeric(18,2)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(
                name: "UsedLimit", table: "CreditCards", type: "numeric(18,2)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(
                name: "TotalAmount", table: "Installments", type: "numeric(18,2)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(
                name: "InstallmentAmount", table: "Installments", type: "numeric(18,2)", nullable: false, defaultValue: 0m);

            migrationBuilder.AlterColumn<string>(
                name: "DocumentHash",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }
    }
}
