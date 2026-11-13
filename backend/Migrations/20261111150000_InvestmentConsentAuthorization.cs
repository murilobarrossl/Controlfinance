using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    public partial class InvestmentConsentAuthorization : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "ConsentProductsJson", table: "PolpIntegrations", type: "text", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "ConsentCheckedAt", table: "PolpIntegrations", type: "timestamp with time zone", nullable: true);
            migrationBuilder.CreateTable(
                name: "PolpInvestmentAuthorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IntegrationId = table.Column<Guid>(type: "uuid", nullable: true),
                    InstitutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsSupplemental = table.Column<bool>(type: "boolean", nullable: false),
                    PolpConsentId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ProviderStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ExecutionStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ProductsJson = table.Column<string>(type: "text", nullable: true),
                    ResourcesJson = table.Column<string>(type: "text", nullable: true),
                    AuthorizationUrlEncrypted = table.Column<string>(type: "text", nullable: true),
                    RecoveryCursorEncrypted = table.Column<string>(type: "text", nullable: true),
                    AuthorizationUrlExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OperationStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PolpInvestmentAuthorizations", x => x.Id);
                    // Keep technical recovery/revocation records after account deletion.
                    table.ForeignKey("FK_PolpInvestmentAuthorizations_PolpIntegrations_IntegrationId",
                        x => x.IntegrationId, "PolpIntegrations", "Id", onDelete: ReferentialAction.SetNull);
                });
            migrationBuilder.CreateIndex("IX_PolpInvestmentAuthorizations_IntegrationId", "PolpInvestmentAuthorizations", "IntegrationId", unique: true);
            migrationBuilder.CreateIndex("IX_PolpInvestmentAuthorizations_PolpConsentId", "PolpInvestmentAuthorizations", "PolpConsentId", unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("PolpInvestmentAuthorizations");
            migrationBuilder.DropColumn("ConsentProductsJson", "PolpIntegrations");
            migrationBuilder.DropColumn("ConsentCheckedAt", "PolpIntegrations");
        }
    }
}
