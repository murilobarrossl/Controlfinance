using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Transactions_UserId_BankAccountId_DueDate\" ON \"Transactions\" (\"UserId\", \"BankAccountId\", \"DueDate\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_BankAccounts_UserId_IsActive\" ON \"BankAccounts\" (\"UserId\", \"IsActive\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Installments_UserId\" ON \"Installments\" (\"UserId\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Installments_BankAccountId\" ON \"Installments\" (\"BankAccountId\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_BankAccounts_UserId_IsActive\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Installments_UserId\";");
        }
    }
}
