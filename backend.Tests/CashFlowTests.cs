using ControlFinance.API.Controllers;
using ControlFinance.API.Controllers.Filters;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Results = ControlFinance.API.Tests.Support.Results;

namespace ControlFinance.API.Tests;

public class CashFlowTests
{
    private static CashFlowController Controller(TestDb t) => t.Controller(db => new CashFlowController(db));

    private static string Iso(DateTime date) => date.ToString("yyyy-MM-dd");

    private static Transaction Imported(TestDb t, BankAccount account, TransactionType type, decimal amount, DateTime day,
        TransactionStatus status = TransactionStatus.Paid, PolpMovementKind kind = PolpMovementKind.None)
    {
        var tx = t.AddTransaction(account, type, amount, day, "Mov", status, movementKind: kind);
        tx.Description = Guid.NewGuid().ToString();
        t.Db.SaveChanges();
        return tx;
    }

    private static async Task<BalanceHistoryDto> History(TestDb t, Guid? accountId, DateTime from, DateTime to, string granularity = "day") =>
        Results.OkValue<BalanceHistoryDto>(await Controller(t).BalanceHistory(accountId, Iso(from), Iso(to), granularity, default));

    [Fact]
    public void Fluxo_de_caixa_exige_login_CNPJ() =>
        Assert.Contains(typeof(CashFlowController).GetCustomAttributes(true), a => a is BusinessHolderOnlyAttribute);

    [Fact]
    public async Task Saldo_atual_e_movimentacoes_pagas_nao_comprovam_saldos_historicos()
    {
        using var t = new TestDb();
        var today = Dates.Today;
        var account = t.AddAccount("Conta PJ", balance: 1000m, polpAccountId: Guid.NewGuid());
        var consentId = Guid.NewGuid();
        account.PolpIntegrationId = consentId;
        t.Db.PolpIntegrations.Add(new PolpIntegration
        {
            UserId = t.User.Id, PolpIntegrationId = consentId, LastSyncedAt = DateTime.UtcNow,
            SyncedLocally = true, Status = "AUTHORISED"
        });
        Imported(t, account, TransactionType.Income, 100m, today);
        Imported(t, account, TransactionType.Expense, 50m, today.AddDays(-1));
        Imported(t, account, TransactionType.Expense, 30m, today.AddDays(-1), kind: PolpMovementKind.OwnTransfer);
        Imported(t, account, TransactionType.Income, 200m, today.AddDays(-90));
        Imported(t, account, TransactionType.Expense, 999m, today.AddDays(5), TransactionStatus.Pending);
        t.AddTransaction(account, TransactionType.Expense, 777m, today.AddDays(-2), "Lançamento manual antigo");

        foreach (var granularity in new[] { "day", "month" })
        {
            var history = await History(t, account.Id, today.AddDays(-100), today.AddDays(1), granularity);
            Assert.False(history.Available);
            Assert.Equal("unverifiedHistory", history.Reason);
            Assert.Empty(history.Points);
            Assert.Null(history.BalanceSyncedAt);
            Assert.Null(history.HistoryStart);
        }
    }

    [Fact]
    public async Task Saldo_antigo_ou_negativo_sem_cobertura_comprovada_nao_gera_pontos()
    {
        using var t = new TestDb();
        var today = Dates.Today;
        var account = t.AddAccount("Conta PJ", balance: -500m);
        Imported(t, account, TransactionType.Income, 100m, today.AddDays(-60));
        Imported(t, account, TransactionType.Expense, 20m, today.AddDays(-2));
        var history = await History(t, account.Id, today.AddDays(-30), today, "month");
        Assert.False(history.Available);
        Assert.Equal("unverifiedHistory", history.Reason);
        Assert.Empty(history.Points);
    }

    [Fact]
    public async Task Sem_conta_unica_cartao_saldo_ou_historico_a_evolucao_fica_indisponivel_com_o_motivo()
    {
        using var t = new TestDb();
        var today = Dates.Today;
        var card = t.AddAccount("Cartão", balance: 10m, type: CreditCardAccountDetector.CardType);
        var noBalance = t.AddAccount("Sem saldo");
        noBalance.Balance = null;
        t.Db.SaveChanges();
        var empty = t.AddAccount("Sem lançamentos", balance: 10m);
        var recent = t.AddAccount("Recente", balance: 10m);
        Imported(t, recent, TransactionType.Income, 10m, today);

        Assert.Equal("account", (await History(t, null, today.AddDays(-7), today.AddDays(1))).Reason);
        Assert.Equal("creditCard", (await History(t, card.Id, today.AddDays(-7), today.AddDays(1))).Reason);
        Assert.Equal("noBalance", (await History(t, noBalance.Id, today.AddDays(-7), today.AddDays(1))).Reason);
        Assert.Equal("unverifiedHistory", (await History(t, empty.Id, today.AddDays(-7), today.AddDays(1))).Reason);
        var before = await History(t, recent.Id, today.AddDays(-30), today.AddDays(-20));
        Assert.False(before.Available);
        Assert.Equal("unverifiedHistory", before.Reason);
        Assert.Empty(before.Points);
    }

    [Fact]
    public async Task Conta_de_outra_empresa_responde_404_e_intervalo_invalido_400()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        var today = Dates.Today;

        Assert.Equal(404, Http.Status(await Controller(t).BalanceHistory(other.Account.Id, Iso(today.AddDays(-7)), Iso(today), "day", default)));
        Assert.Equal(400, Http.Status(await Controller(t).BalanceHistory(null, Iso(today), Iso(today.AddDays(-1)), "day", default)));
        Assert.Equal(400, Http.Status(await Controller(t).BalanceHistory(null, Iso(today.AddDays(-800)), Iso(today), "day", default)));
        Assert.Equal(400, Http.Status(await Controller(t).BalanceHistory(null, Iso(today.AddDays(-7)), Iso(today), "hour", default)));
    }

    [Fact]
    public async Task Agregados_trazem_entradas_e_saidas_por_dia_so_em_intervalos_curtos_e_sem_transferencias_proprias()
    {
        using var t = new TestDb();
        var today = Dates.Today;
        var account = t.AddAccount("Conta PJ", balance: 0m);
        Imported(t, account, TransactionType.Income, 300m, today);
        Imported(t, account, TransactionType.Expense, 40m, today);
        Imported(t, account, TransactionType.Expense, 500m, today, kind: PolpMovementKind.OwnTransfer);
        var transactions = t.Controller(db => new TransactionsController(db));

        var week = Results.OkValue<TransactionAggregatesDto>(
            await transactions.GetAggregates(null, Iso(today.AddDays(-6)), Iso(today.AddDays(1)), default));
        var year = Results.OkValue<TransactionAggregatesDto>(
            await transactions.GetAggregates(null, Iso(today.AddDays(-300)), Iso(today.AddDays(1)), default));

        Assert.Equal(7, week.Days.Count);
        var todayBucket = Assert.Single(week.Days, d => d.Date == DateOnly.FromDateTime(today));
        Assert.Equal(300m, todayBucket.Income);
        Assert.Equal(40m, todayBucket.Expense);
        Assert.Empty(year.Days);
    }

    [Fact]
    public async Task Totais_diarios_mensais_categorias_e_detalhes_usam_a_mesma_base_isolada()
    {
        using var t = new TestDb();
        var day = new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc);
        var account = t.AddAccount("Conta PJ");
        var category = t.AddCategory("Operação");
        t.AddTransaction(account, TransactionType.Income, 300m, day, category: category);
        t.AddTransaction(account, TransactionType.Expense, 40m, day, category: category);
        t.AddTransaction(account, TransactionType.Expense, 10m, day, status: TransactionStatus.Pending, category: category);
        foreach (var kind in new[] { PolpMovementKind.OwnTransfer, PolpMovementKind.CardBillPayment })
        {
            t.AddTransaction(account, TransactionType.Income, 500m, day, category: category, movementKind: kind);
            t.AddTransaction(account, TransactionType.Expense, 500m, day, category: category, movementKind: kind);
        }
        t.AddTransaction(account, TransactionType.Income, 999m, day.AddDays(-1));
        t.AddTransaction(account, TransactionType.Income, 999m, day.AddDays(1));
        var inactive = t.AddAccount("Inativa", active: false);
        t.AddTransaction(inactive, TransactionType.Income, 999m, day);
        var other = OtherUser.Add(t);
        var foreign = t.AddTransaction(other.Account, TransactionType.Income, 999m, day);
        foreign.UserId = other.Account.UserId;
        t.Db.SaveChanges();

        var controller = t.Controller(db => new TransactionsController(db));
        var totals = Results.OkValue<TransactionAggregatesDto>(await controller.GetAggregates(null, Iso(day), Iso(day.AddDays(1)), default));
        Assert.Equal(300m, totals.TotalIncome);
        Assert.Equal(50m, totals.TotalExpense);
        Assert.Equal(3, totals.Count);
        Assert.Equal(totals.TotalIncome, Assert.Single(totals.Days).Income);
        Assert.Equal(totals.TotalExpense, Assert.Single(totals.Days).Expense);
        Assert.Equal(totals.TotalIncome, Assert.Single(totals.Months).Income);
        Assert.Equal(totals.TotalExpense, Assert.Single(totals.Months).Expense);
        Assert.Equal(totals.TotalIncome, totals.Categories.Where(c => c.Type == "Income").Sum(c => c.Amount));
        Assert.Equal(totals.TotalExpense, totals.Categories.Where(c => c.Type == "Expense").Sum(c => c.Amount));
        var details = Results.OkValue<List<TransactionDto>>(await controller.GetDetails(null, Iso(day), Iso(day.AddDays(1)), "Expense", category.Name, null, default));
        Assert.Equal(2, details.Count);
        Assert.Equal(totals.TotalExpense, details.Sum(d => d.Amount));
    }
}
