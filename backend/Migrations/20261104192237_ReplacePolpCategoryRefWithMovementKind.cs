using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class ReplacePolpCategoryRefWithMovementKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PolpMovementKind",
                table: "Transactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.Sql(
                """UPDATE "Transactions" SET "PolpMovementKind" = 'CardBillPayment' WHERE "PolpCategoryRef" = 'LOAN_PAYMENTS_CREDIT_CARD_PAYMENT';""");
            migrationBuilder.Sql(
                """UPDATE "Transactions" SET "PolpMovementKind" = 'OwnTransfer' WHERE "PolpCategoryRef" IN ('TRANSFER_IN_ACCOUNT_TRANSFER', 'TRANSFER_OUT_ACCOUNT_TRANSFER');""");

            migrationBuilder.DropColumn(
                name: "PolpCategoryRef",
                table: "Transactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PolpCategoryRef",
                table: "Transactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // A distinção IN/OUT se perde; a regra dos totais só precisa saber que é transferência própria.
            migrationBuilder.Sql(
                """UPDATE "Transactions" SET "PolpCategoryRef" = 'LOAN_PAYMENTS_CREDIT_CARD_PAYMENT' WHERE "PolpMovementKind" = 'CardBillPayment';""");
            migrationBuilder.Sql(
                """UPDATE "Transactions" SET "PolpCategoryRef" = 'TRANSFER_OUT_ACCOUNT_TRANSFER' WHERE "PolpMovementKind" = 'OwnTransfer';""");

            migrationBuilder.DropColumn(
                name: "PolpMovementKind",
                table: "Transactions");
        }
    }
}
