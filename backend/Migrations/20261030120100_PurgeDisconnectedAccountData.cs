using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    // ATENÇÃO, IRREVERSÍVEL: apaga em definitivo o que sobrou de contas que o usuário já tinha
    // "desconectado". Até esta versão, "Sair" de uma conta só marcava IsActive = false: a conta e
    // todas as transações continuavam no banco, e a sincronização automática seguia buscando
    // dados dela na Polp. Quem desconectou esperava o contrário, então o certo é cumprir a
    // vontade já manifestada em vez de manter o dado. Revisar antes de aplicar em produção.
    //
    // A integração na Polp de uma conexão que ficou sem nenhuma conta ativa não é apagada aqui
    // (SQL não chama API): é marcada como DISCONNECT_PENDING, o que já tira ela de toda
    // sincronização, e PolpDisconnectService pede a exclusão na Polp e remove a linha local
    // quando a Polp confirmar.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261030120100_PurgeDisconnectedAccountData")]
    public partial class PurgeDisconnectedAccountData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "PolpIntegrations" p
                SET "Status" = 'DISCONNECT_PENDING', "UpdatedAt" = now()
                WHERE EXISTS (
                        SELECT 1 FROM "BankAccounts" b
                        WHERE b."UserId" = p."UserId" AND b."PolpIntegrationId" = p."PolpIntegrationId")
                  AND NOT EXISTS (
                        SELECT 1 FROM "BankAccounts" b
                        WHERE b."UserId" = p."UserId" AND b."PolpIntegrationId" = p."PolpIntegrationId"
                          AND b."IsActive" = true);
                """);

            migrationBuilder.Sql("""
                DELETE FROM "RecurrenceDecisions"
                WHERE "BankAccountId" IN (SELECT "Id" FROM "BankAccounts" WHERE "IsActive" = false);
                """);

            migrationBuilder.Sql("""
                DELETE FROM "Installments"
                WHERE "BankAccountId" IN (SELECT "Id" FROM "BankAccounts" WHERE "IsActive" = false);
                """);

            migrationBuilder.Sql("""
                DELETE FROM "Transactions"
                WHERE "BankAccountId" IN (SELECT "Id" FROM "BankAccounts" WHERE "IsActive" = false);
                """);

            migrationBuilder.Sql("""
                DELETE FROM "BankAccounts" WHERE "IsActive" = false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Não reversível: dado apagado em definitivo.
        }
    }
}
