using ControlFinance.API.Controllers;
using ControlFinance.API.DTOs;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;

namespace ControlFinance.API.Tests;

public class DashboardAccountTests
{
    private static async Task<DashboardAccountDto> Account(TestDb t, Guid id) =>
        Results.OkValue<DashboardSummaryDto>(await t.Controller(db => new DashboardController(db)).GetSummary(id, default)).Account;

    [Theory]
    [InlineData("CONTA_DEPOSITO_A_VISTA", "Checking")]
    [InlineData("CONTA_PAGAMENTO_PRE_PAGA", "Payment")]
    [InlineData("CONTA_POUPANCA", "Savings")]
    [InlineData(CreditCardAccountDetector.CardType, "CreditCard")]
    [InlineData(null, "Checking")]
    public async Task Tipo_da_conta_vem_do_tipo_da_Polp(string? type, string kind)
    {
        using var t = new TestDb();
        var account = t.AddAccount(type: type);

        Assert.Equal(kind, (await Account(t, account.Id)).Kind);
    }

    [Fact]
    public async Task Conta_sincronizada_sem_saldo_marca_saldo_incompleto()
    {
        using var t = new TestDb();
        var account = t.AddAccount(type: "CONTA_DEPOSITO_A_VISTA", polpAccountId: Guid.NewGuid());
        account.Balance = null;
        t.Db.SaveChanges();

        var dto = await Account(t, account.Id);
        Assert.Null(dto.Balance);
        Assert.True(dto.BalanceIncomplete);
    }

    [Fact]
    public async Task Cartao_sincronizado_e_conta_manual_nunca_marcam_saldo_incompleto()
    {
        using var t = new TestDb();
        var card = t.AddAccount(type: CreditCardAccountDetector.CardType, polpAccountId: Guid.NewGuid());
        var manual = t.AddAccount(balance: 10m);

        Assert.False((await Account(t, card.Id)).BalanceIncomplete);
        Assert.False((await Account(t, manual.Id)).BalanceIncomplete);
    }
}
