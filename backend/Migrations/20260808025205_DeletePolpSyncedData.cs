using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFinance.API.Migrations
{
    /// <inheritdoc />
    public partial class DeletePolpSyncedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Plano com a Polp encerrado, renovação incerta: a pedido explícito do usuário, esta
            // migration apagou em definitivo (hard delete, não reversível) tudo que veio da
            // integração open finance de uma conta específica — contas sincronizadas, as
            // transações reais importadas por elas, e os registros de consentimento
            // (PolpIntegration). Cartões/contas 100% manuais não foram tocados.
            //
            // Já foi aplicada e confirmada em produção antes deste commit; o corpo abaixo é
            // deixado vazio de propósito (em vez de manter o DELETE original com o e-mail do
            // usuário cravado no SQL) porque reaplicar essa limpeza em qualquer outro ambiente
            // não faz sentido — um banco novo nunca teve esses dados pra começo de conversa.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Não reversível: dado real apagado em definitivo, não existe estado anterior pra
            // reconstruir (ao contrário das migrations de recoloração, que só trocavam um valor
            // por outro dentro de um conjunto conhecido).
        }
    }
}
