using System;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace ControlFinance.API.Migrations
{
    public partial class ImportedInvestmentResources : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "PolpApiCooldowns", columns: table => new
            {
                ClientKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                RetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            }, constraints: table => table.PrimaryKey("PK_PolpApiCooldowns", x => x.ClientKey));
            migrationBuilder.AddUniqueConstraint(name: "AK_PolpIntegrations_UserId_Id", table: "PolpIntegrations", columns: new[] { "UserId", "Id" });
            migrationBuilder.AddColumn<bool>(name: "HasReservedBalance", table: "BankAccounts", type: "boolean", nullable: true);
            migrationBuilder.AlterColumn<string>(name: "ResourceId", table: "PolpWebhookEvents", type: "character varying(160)", maxLength: 160, nullable: true, oldClrType: typeof(string), oldType: "character varying(64)", oldMaxLength: 64, oldNullable: true);
            migrationBuilder.CreateTable(name: "ImportedInvestments", columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                IntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceConsentId = table.Column<Guid>(type: "uuid", nullable: false),
                Family = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ExternalId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                NameEncrypted = table.Column<string>(type: "text", nullable: true),
                ProductType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                IdentityEncrypted = table.Column<string>(type: "text", nullable: false),
                IdentityAvailabilityEncrypted = table.Column<string>(type: "text", nullable: false),
                ProviderUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                FirstObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_ImportedInvestments", x => x.Id);
                table.ForeignKey(name: "FK_Inv_Integration", columns: x => new { x.UserId, x.IntegrationId }, principalTable: "PolpIntegrations", principalColumns: new[] { "UserId", "Id" }, onDelete: ReferentialAction.Cascade);
            });
            migrationBuilder.CreateIndex(name: "IX_ImportedInvestments_UserId_IntegrationId_Id", table: "ImportedInvestments", columns: new[] { "UserId", "IntegrationId", "Id" }, unique: false);
            migrationBuilder.CreateIndex(name: "IX_ImportedInvestments_UserId_FirstObservedAt_Id", table: "ImportedInvestments", columns: new[] { "UserId", "FirstObservedAt", "Id" }, unique: false);
            migrationBuilder.CreateIndex(name: "IX_ImportedInvestments_Family_ExternalId", table: "ImportedInvestments", columns: new[] { "Family", "ExternalId" }, unique: true);
            migrationBuilder.CreateTable(name: "InvestmentContracts", columns: table => new
            {
                InvestmentId = table.Column<Guid>(type: "uuid", nullable: false),
                ValuesEncrypted = table.Column<string>(type: "text", nullable: false),
                AvailabilityEncrypted = table.Column<string>(type: "text", nullable: false),
                ProvenanceEncrypted = table.Column<string>(type: "text", nullable: false),
                ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_InvestmentContracts", x => x.InvestmentId);
                table.ForeignKey("FK_InvContract_Investment", x => x.InvestmentId, "ImportedInvestments", "Id", onDelete: ReferentialAction.Cascade);
            });
            migrationBuilder.CreateTable(name: "InvestmentPositions", columns: table => new
            {
                InvestmentId = table.Column<Guid>(type: "uuid", nullable: false),
                ValuesEncrypted = table.Column<string>(type: "text", nullable: false),
                AvailabilityEncrypted = table.Column<string>(type: "text", nullable: false),
                ProvenanceEncrypted = table.Column<string>(type: "text", nullable: false),
                ReferenceAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_InvestmentPositions", x => x.InvestmentId);
                table.ForeignKey("FK_InvPosition_Investment", x => x.InvestmentId, "ImportedInvestments", "Id", onDelete: ReferentialAction.Cascade);
            });
            migrationBuilder.CreateTable(name: "InvestmentPositionObservations", columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                InvestmentId = table.Column<Guid>(type: "uuid", nullable: false),
                ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PayloadEncrypted = table.Column<string>(type: "text", nullable: false),
                ReferenceAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_InvestmentPositionObservations", x => x.Id);
                table.ForeignKey("FK_InvObservation_Investment", x => x.InvestmentId, "ImportedInvestments", "Id", onDelete: ReferentialAction.Cascade);
            });
            migrationBuilder.CreateIndex(name: "IX_InvestmentPositionObservations_InvestmentId_ContentHash", table: "InvestmentPositionObservations", columns: new[] { "InvestmentId", "ContentHash" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_InvestmentPositionObservations_InvestmentId_ObservedAt_Id", table: "InvestmentPositionObservations", columns: new[] { "InvestmentId", "ObservedAt", "Id" }, unique: false);
            migrationBuilder.CreateTable(name: "InvestmentMovements", columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                InvestmentId = table.Column<Guid>(type: "uuid", nullable: false),
                ExternalId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Direction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                MovementType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                TransactionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ProviderUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ValuesEncrypted = table.Column<string>(type: "text", nullable: false),
                AvailabilityEncrypted = table.Column<string>(type: "text", nullable: false),
                ProvenanceEncrypted = table.Column<string>(type: "text", nullable: false),
                FirstObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_InvestmentMovements", x => x.Id);
                table.ForeignKey("FK_InvMovement_Investment", x => x.InvestmentId, "ImportedInvestments", "Id", onDelete: ReferentialAction.Cascade);
            });
            migrationBuilder.CreateIndex(name: "IX_InvestmentMovements_InvestmentId_ExternalId", table: "InvestmentMovements", columns: new[] { "InvestmentId", "ExternalId" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_InvestmentMovements_InvestmentId_TransactionDate_Id", table: "InvestmentMovements", columns: new[] { "InvestmentId", "TransactionDate", "Id" }, unique: false);
            migrationBuilder.CreateIndex(name: "IX_InvestmentMovements_InvestmentId_FirstObservedAt_Id", table: "InvestmentMovements", columns: new[] { "InvestmentId", "FirstObservedAt", "Id" }, unique: false);
            migrationBuilder.CreateTable(name: "ImportedReservedBalances", columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                IntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                BankAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceConsentId = table.Column<Guid>(type: "uuid", nullable: false),
                ExternalId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                ReservedIdentification = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                NameEncrypted = table.Column<string>(type: "text", nullable: true),
                ValuesEncrypted = table.Column<string>(type: "text", nullable: false),
                AvailabilityEncrypted = table.Column<string>(type: "text", nullable: false),
                ProvenanceEncrypted = table.Column<string>(type: "text", nullable: false),
                ProviderUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                FirstObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_ImportedReservedBalances", x => x.Id);
                table.ForeignKey(name: "FK_Reserved_Integration", columns: x => new { x.UserId, x.IntegrationId }, principalTable: "PolpIntegrations", principalColumns: new[] { "UserId", "Id" }, onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_Reserved_Account", x => x.BankAccountId, "BankAccounts", "Id", onDelete: ReferentialAction.Cascade);
            });
            migrationBuilder.CreateIndex(name: "IX_ImportedReservedBalances_BankAccountId_ExternalId", table: "ImportedReservedBalances", columns: new[] { "BankAccountId", "ExternalId" }, unique: true);
            migrationBuilder.CreateIndex(name: "UX_Reserved_OriginalIdentity", table: "ImportedReservedBalances", columns: new[] { "BankAccountId", "ReservedIdentification" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_ImportedReservedBalances_UserId_IntegrationId_Id", table: "ImportedReservedBalances", columns: new[] { "UserId", "IntegrationId", "Id" }, unique: false);
            migrationBuilder.CreateIndex(name: "IX_ImportedReservedBalances_UserId_FirstObservedAt_Id", table: "ImportedReservedBalances", columns: new[] { "UserId", "FirstObservedAt", "Id" }, unique: false);
            migrationBuilder.CreateTable(name: "ReservedBalanceObservations", columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ReservedBalanceId = table.Column<Guid>(type: "uuid", nullable: false),
                ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PayloadEncrypted = table.Column<string>(type: "text", nullable: false),
                ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_ReservedBalanceObservations", x => x.Id);
                table.ForeignKey("FK_ReservedObservation_Reserved", x => x.ReservedBalanceId, "ImportedReservedBalances", "Id", onDelete: ReferentialAction.Cascade);
            });
            migrationBuilder.CreateIndex(name: "IX_ReservedBalanceObservations_ReservedBalanceId_ContentHash", table: "ReservedBalanceObservations", columns: new[] { "ReservedBalanceId", "ContentHash" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_ReservedBalanceObservations_ReservedBalanceId_ObservedAt_Id", table: "ReservedBalanceObservations", columns: new[] { "ReservedBalanceId", "ObservedAt", "Id" }, unique: false);
            migrationBuilder.CreateTable(name: "InvestmentSyncStates", columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                IntegrationId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceConsentId = table.Column<Guid>(type: "uuid", nullable: false),
                AuthorizationId = table.Column<Guid>(type: "uuid", nullable: true),
                AuthorizationOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                Family = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                ParentExternalId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                RunId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CursorEncrypted = table.Column<string>(type: "text", nullable: true),
                WindowEncrypted = table.Column<string>(type: "text", nullable: true),
                SeenCursorsEncrypted = table.Column<string>(type: "text", nullable: false),
                Availability = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                PageNumber = table.Column<int>(type: "integer", nullable: false),
                ReplayRequested = table.Column<bool>(type: "boolean", nullable: false),
                StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastSuccessAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastFullSuccessAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            }, constraints: table =>
            {
                table.PrimaryKey("PK_InvestmentSyncStates", x => x.Id);
                table.ForeignKey(name: "FK_InvSync_Integration", columns: x => new { x.UserId, x.IntegrationId }, principalTable: "PolpIntegrations", principalColumns: new[] { "UserId", "Id" }, onDelete: ReferentialAction.Cascade);
            });
            migrationBuilder.CreateIndex(name: "UX_InvSync_Stream", table: "InvestmentSyncStates", columns: new[] { "IntegrationId", "SourceConsentId", "Family", "Kind", "ParentExternalId" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_InvestmentSyncStates_Status_LastAttemptAt", table: "InvestmentSyncStates", columns: new[] { "Status", "LastAttemptAt" }, unique: false);
            migrationBuilder.CreateIndex(name: "IX_InvestmentSyncStates_UserId_IntegrationId", table: "InvestmentSyncStates", columns: new[] { "UserId", "IntegrationId" }, unique: false);
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Reversão automática desabilitada para preservar investimentos importados. Mantenha o schema aditivo ao reverter o código e planeje a preservação dos dados antes de removê-lo.");
        }
    }
}
