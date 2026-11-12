using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace ControlFinance.API.Migrations
{
    public partial class InvestmentContinuousSync : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "InvestmentSyncSchedules", columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                IntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceConsentId = table.Column<Guid>(type: "uuid", nullable: false),
                Resource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ExternalId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                NextSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ObservedRunId = table.Column<Guid>(type: "uuid", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_InvestmentSyncSchedules", x => x.Id);
                table.ForeignKey(name: "FK_InvSchedule_Integration", columns: x => new { x.UserId, x.IntegrationId },
                    principalTable: "PolpIntegrations", principalColumns: new[] { "UserId", "Id" }, onDelete: ReferentialAction.Cascade);
            });
            migrationBuilder.CreateIndex(name: "UX_InvSchedule_Resource", table: "InvestmentSyncSchedules",
                columns: new[] { "IntegrationId", "SourceConsentId", "Resource", "ExternalId" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_InvestmentSyncSchedules_UserId_IntegrationId", table: "InvestmentSyncSchedules",
                columns: new[] { "UserId", "IntegrationId" });
            migrationBuilder.CreateIndex(name: "IX_InvObservation_Reference", table: "InvestmentPositionObservations",
                columns: new[] { "InvestmentId", "ReferenceAt", "ObservedAt", "Id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Keep the additive schema when reverting code; don't erase collected evidence.
            throw new NotSupportedException("Preserve os cronogramas observados ao reverter o código. Remoção de schema requer planejamento explícito.");
        }
    }
}
