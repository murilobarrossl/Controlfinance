using System;
using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261117200000_LoanContracts")]
    public partial class LoanContracts : Migration
    {
        // Só esquema: conexões, extratos e consentimentos existentes ficam intactos. Consentimento sem
        // CREDIT_OPERATIONS apenas não importa contratos, e a marcação de empréstimo recebido chega na
        // próxima reimportação do extrato.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "LoanContracts", columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                IntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                PolpLoanId = table.Column<Guid>(type: "uuid", nullable: false),
                ValuesEncrypted = table.Column<string>(type: "text", nullable: false),
                ProviderUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                FirstObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_LoanContracts", x => x.Id);
                table.ForeignKey(name: "FK_Loan_Integration", columns: x => new { x.UserId, x.IntegrationId }, principalTable: "PolpIntegrations", principalColumns: new[] { "UserId", "Id" }, onDelete: ReferentialAction.Cascade);
            });
            migrationBuilder.CreateIndex(name: "IX_LoanContracts_IntegrationId_PolpLoanId", table: "LoanContracts", columns: new[] { "IntegrationId", "PolpLoanId" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_LoanContracts_UserId_IntegrationId", table: "LoanContracts", columns: new[] { "UserId", "IntegrationId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LoanContracts");
        }
    }
}
