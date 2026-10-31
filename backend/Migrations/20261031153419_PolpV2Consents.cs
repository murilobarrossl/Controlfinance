using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    // ATENÇÃO, IRREVERSÍVEL: apaga em definitivo tudo que veio da Polp v1 (contas, cartões,
    // transações, parcelamentos, decisões do Radar e as próprias integrações). A API v1 deixa de
    // existir em 28/11/2026, ids inteiros da v1 não correspondem a nenhum recurso da v2 (que usa
    // UUID) e um item da v1 não vira consentimento da v2: o usuário precisa autorizar de novo no
    // banco. Só havia conexões de teste, e o dono do projeto autorizou descartá-las. Conta
    // cadastrada à mão (sem id da Polp) não é tocada. Revisar antes de aplicar em produção.
    //
    // As colunas de id mudam de integer para uuid por DROP + ADD, não ALTER: o Postgres não converte
    // integer em uuid, e depois do DELETE acima nenhuma linha tem valor a preservar.
    //
    // Integração da v1 que o usuário ainda não desconectou fica órfã do lado da Polp; a v1 inteira
    // é desligada pelo fornecedor em 28/11/2026, então não há pedido de exclusão a refazer
    // (PendingProviderDeletion antigo também é descartado: o id v1 não existe na v2).
    public partial class PolpV2Consents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "RecurrenceDecisions"
                WHERE "BankAccountId" IN (
                    SELECT "Id" FROM "BankAccounts"
                    WHERE "PolpAccountId" IS NOT NULL OR "PolpIntegrationId" IS NOT NULL);
                """);

            migrationBuilder.Sql("""
                DELETE FROM "Installments"
                WHERE "BankAccountId" IN (
                    SELECT "Id" FROM "BankAccounts"
                    WHERE "PolpAccountId" IS NOT NULL OR "PolpIntegrationId" IS NOT NULL);
                """);

            migrationBuilder.Sql("""
                DELETE FROM "Transactions"
                WHERE "BankAccountId" IN (
                    SELECT "Id" FROM "BankAccounts"
                    WHERE "PolpAccountId" IS NOT NULL OR "PolpIntegrationId" IS NOT NULL);
                """);

            migrationBuilder.Sql("""
                DELETE FROM "BankAccounts"
                WHERE "PolpAccountId" IS NOT NULL OR "PolpIntegrationId" IS NOT NULL;
                """);

            migrationBuilder.Sql("""DELETE FROM "PolpIntegrations";""");
            migrationBuilder.Sql("""DELETE FROM "PendingProviderDeletions";""");

            migrationBuilder.DropColumn(name: "PolpIntegrationId", table: "PolpIntegrations");
            migrationBuilder.AddColumn<Guid>(
                name: "PolpIntegrationId",
                table: "PolpIntegrations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.DropColumn(name: "InstitutionId", table: "PolpIntegrations");
            migrationBuilder.AddColumn<Guid>(
                name: "InstitutionId",
                table: "PolpIntegrations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "ExecutionStatus",
                table: "PolpIntegrations",
                type: "text",
                nullable: true);

            migrationBuilder.DropColumn(name: "PolpIntegrationId", table: "PendingProviderDeletions");
            migrationBuilder.AddColumn<Guid>(
                name: "PolpIntegrationId",
                table: "PendingProviderDeletions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.DropColumn(name: "PolpIntegrationId", table: "BankAccounts");
            migrationBuilder.AddColumn<Guid>(
                name: "PolpIntegrationId",
                table: "BankAccounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.DropColumn(name: "PolpAccountId", table: "BankAccounts");
            migrationBuilder.AddColumn<Guid>(
                name: "PolpAccountId",
                table: "BankAccounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolpIntegrations_PolpIntegrationId",
                table: "PolpIntegrations",
                column: "PolpIntegrationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_PolpAccountId",
                table: "BankAccounts",
                column: "PolpAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Não reversível: dado da v1 apagado em definitivo.
        }
    }
}
