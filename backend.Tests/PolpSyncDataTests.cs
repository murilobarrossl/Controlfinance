using ControlFinance.API.Controllers;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;

namespace ControlFinance.API.Tests;

public class PolpCurrentInvoiceTests
{
    private static DateTime Day(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private sealed class Scenario : IDisposable
    {
        public TestDb T { get; } = new();
        public ScriptedPolp Polp { get; } = new();
        public Guid CardId { get; } = Guid.NewGuid();
        public PolpIntegration Integration { get; }

        public Scenario(DateTimeOffset now, string zone = UserClock.DefaultTimeZoneId)
        {
            T.User.TimeZone = zone;
            T.Db.SaveChanges();
            T.Clock.UtcNow = now;
            Integration = SyncHarness.AddIntegration(T, synced: true);
            SyncHarness.AddLinked(T, Integration, CardId, card: true);
            Polp.Bills[CardId] = [];
            Polp.CardTransactions[CardId] = [];
        }

        public Task SyncCard() => SyncHarness.Processor(T, Polp).Process("credit_cards.transactions", CardId);

        public BankAccount Card()
        {
            using var db = T.NewContext();
            return db.BankAccounts.Single(b => b.PolpAccountId == CardId);
        }

        public void Dispose() => T.Dispose();
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Fechada_nao_vencida_e_nao_paga_e_a_fatura_atual()
    {
        using var s = new Scenario(Now);
        s.Polp.Bills[s.CardId] = [ScriptedPolp.Bill("2026-10-28T03:00:00.000000Z", "2026-11-10T03:00:00.000000Z", "500.00")];
        s.Polp.CardTransactions[s.CardId] =
        [
            ScriptedPolp.CardTx("2026-10-20T15:00:00.000Z", "500.00"),
            ScriptedPolp.CardTx("2026-11-01T15:00:00.000Z", "100.00"),
        ];

        await s.SyncCard();

        var card = s.Card();
        Assert.Equal(500m, card.CurrentInvoiceAmount);
        Assert.Equal(Day(2026, 11, 10), card.InvoiceDueDate);
        Assert.Equal(28, card.ClosingDay);
        Assert.Equal(10, card.DueDay);
    }

    [Fact]
    public async Task Fechada_paga_depois_do_fechamento_da_lugar_a_fatura_em_aberto()
    {
        using var s = new Scenario(Now);
        s.Polp.Bills[s.CardId] = [ScriptedPolp.Bill("2026-10-28", "2026-11-10", "500.00")];
        s.Polp.CardTransactions[s.CardId] =
        [
            ScriptedPolp.CardTx("2026-10-20T15:00:00.000Z", "500.00"),
            ScriptedPolp.CardTx("2026-11-01T15:00:00.000Z", "100.00"),
            ScriptedPolp.CardTx("2026-11-02T15:00:00.000Z", "500.00", credit: true, type: "PAGAMENTO_FATURA"),
        ];

        await s.SyncCard();

        var card = s.Card();
        Assert.Equal(100m, card.CurrentInvoiceAmount);
        Assert.Equal(Day(2026, 12, 10), card.InvoiceDueDate);
    }

    [Fact]
    public async Task Pagamento_parcial_nao_quita_a_fatura()
    {
        using var s = new Scenario(Now);
        s.Polp.Bills[s.CardId] = [ScriptedPolp.Bill("2026-10-28", "2026-11-10", "500.00")];
        s.Polp.CardTransactions[s.CardId] =
        [
            ScriptedPolp.CardTx("2026-11-02T15:00:00.000Z", "200.00", credit: true, type: "PAGAMENTO_FATURA"),
        ];

        await s.SyncCard();

        Assert.Equal(500m, s.Card().CurrentInvoiceAmount);
    }

    [Fact]
    public async Task So_faturas_vencidas_usa_a_fatura_em_aberto_com_vencimento_do_mes_seguinte()
    {
        using var s = new Scenario(Now);
        s.Polp.Bills[s.CardId] =
        [
            ScriptedPolp.Bill("2026-08-28", "2026-09-10", "350.00"),
            ScriptedPolp.Bill("2026-09-28", "2026-10-10", "400.00"),
        ];
        s.Polp.CardTransactions[s.CardId] =
        [
            ScriptedPolp.CardTx("2026-09-20T15:00:00.000Z", "400.00"),
            ScriptedPolp.CardTx("2026-10-05T15:00:00.000Z", "50.00"),
            ScriptedPolp.CardTx("2026-10-30T15:00:00.000Z", "70.00"),
            ScriptedPolp.CardTx("2026-11-01T15:00:00.000Z", "20.00", credit: true, type: "ESTORNO"),
            ScriptedPolp.CardTx("2026-10-08T15:00:00.000Z", "400.00", credit: true, type: "PAGAMENTO_FATURA"),
            ScriptedPolp.CardTx("2026-11-01T15:00:00.000Z", "999.00", billPostDate: "0001-01-01", forecast: "2027-01"),
        ];

        await s.SyncCard();

        var card = s.Card();
        Assert.Equal(100m, card.CurrentInvoiceAmount);
        Assert.Equal(Day(2026, 11, 10), card.InvoiceDueDate);
    }

    [Fact]
    public async Task Nunca_grava_vencimento_anterior_a_hoje_mesmo_com_faturas_antigas()
    {
        using var s = new Scenario(Now);
        s.Polp.Bills[s.CardId] = [ScriptedPolp.Bill("2026-07-31", "2026-08-31", "100.00")];

        await s.SyncCard();

        var card = s.Card();
        Assert.Equal(Day(2026, 11, 30), card.InvoiceDueDate);
        Assert.True(card.InvoiceDueDate >= Day(2026, 11, 4));
    }

    [Fact]
    public async Task Sem_fatura_fechada_soma_as_nao_faturadas_e_fica_sem_vencimento()
    {
        using var s = new Scenario(Now);
        s.Polp.CardTransactions[s.CardId] =
        [
            ScriptedPolp.CardTx("2026-10-30T15:00:00.000Z", "80.00"),
            ScriptedPolp.CardTx("2026-11-01T15:00:00.000Z", "20.00"),
            ScriptedPolp.CardTx("2026-11-01T15:00:00.000Z", "300.00", billPostDate: "0001-01-01", forecast: "2026-12"),
        ];

        await s.SyncCard();

        var card = s.Card();
        Assert.Equal(100m, card.CurrentInvoiceAmount);
        Assert.Null(card.InvoiceDueDate);
    }

    [Fact]
    public async Task Cartao_que_ja_teve_fatura_e_recebe_lista_vazia_mantem_a_fatura_gravada()
    {
        using var s = new Scenario(Now);
        s.Polp.Bills[s.CardId] = [ScriptedPolp.Bill("2026-10-28", "2026-11-10", "500.00")];
        await s.SyncCard();

        s.Polp.Bills[s.CardId] = [];
        s.Polp.CardTransactions[s.CardId] = [ScriptedPolp.CardTx("2026-11-01T15:00:00.000Z", "9999.00")];
        await s.SyncCard();

        var card = s.Card();
        Assert.Equal(500m, card.CurrentInvoiceAmount);
        Assert.Equal(Day(2026, 11, 10), card.InvoiceDueDate);
    }

    [Theory]
    [InlineData("America/Sao_Paulo")]
    [InlineData("America/Manaus")]
    public async Task Vencimento_vira_na_meia_noite_do_fuso_do_usuario(string zone)
    {
        var offset = zone == "America/Manaus" ? 4 : 3;
        var lastMinute = new DateTimeOffset(2026, 11, 10, 23, 59, 0, TimeSpan.FromHours(-offset));
        using var s = new Scenario(lastMinute, zone);
        s.Polp.Bills[s.CardId] = [ScriptedPolp.Bill("2026-10-28T03:00:00.000000Z", "2026-11-10T03:00:00.000000Z", "500.00")];
        s.Polp.CardTransactions[s.CardId] = [ScriptedPolp.CardTx("2026-11-01T15:00:00.000Z", "40.00")];

        await s.SyncCard();
        Assert.Equal(500m, s.Card().CurrentInvoiceAmount);
        Assert.Equal(Day(2026, 11, 10), s.Card().InvoiceDueDate);

        s.T.Clock.UtcNow = lastMinute.AddMinutes(1);
        await s.SyncCard();
        Assert.Equal(500m, s.Card().CurrentInvoiceAmount);
        Assert.Equal(Day(2026, 11, 10), s.Card().InvoiceDueDate);

        await using var db = s.T.NewContext();
        var today = await UserClock.TodayForUserAsync(db, s.T.User.Id, s.T.Clock);
        var dto = (await CreditCardSummaryBuilder.BuildRecognizedListAsync(db, s.T.User.Id, today)).Single();
        Assert.Equal(nameof(TransactionStatus.Overdue), dto.InvoiceStatus);
    }

    [Fact]
    public async Task Vencida_sem_pagamento_continua_sendo_a_fatura_atual()
    {
        using var s = new Scenario(new DateTimeOffset(2026, 11, 15, 15, 0, 0, TimeSpan.Zero));
        s.Polp.Bills[s.CardId] = [ScriptedPolp.Bill("2026-10-28", "2026-11-10", "500.00")];
        s.Polp.CardTransactions[s.CardId] = [ScriptedPolp.CardTx("2026-11-01T15:00:00.000Z", "40.00")];

        await s.SyncCard();

        var card = s.Card();
        Assert.Equal(500m, card.CurrentInvoiceAmount);
        Assert.Equal(Day(2026, 11, 10), card.InvoiceDueDate);
    }

    [Fact]
    public async Task Vencida_com_pagamento_minimo_da_lugar_a_fatura_em_aberto()
    {
        using var s = new Scenario(new DateTimeOffset(2026, 11, 15, 15, 0, 0, TimeSpan.Zero));
        var bill = ScriptedPolp.Bill("2026-10-28", "2026-11-10", "500.00");
        bill.BillMinimumAmount = ScriptedPolp.Money("75.00");
        s.Polp.Bills[s.CardId] = [bill];
        s.Polp.CardTransactions[s.CardId] =
        [
            ScriptedPolp.CardTx("2026-11-01T15:00:00.000Z", "40.00"),
            ScriptedPolp.CardTx("2026-11-09T15:00:00.000Z", "75.00", credit: true, type: "PAGAMENTO_FATURA"),
        ];

        await s.SyncCard();

        var card = s.Card();
        Assert.Equal(40m, card.CurrentInvoiceAmount);
        Assert.Equal(Day(2026, 12, 10), card.InvoiceDueDate);
    }

    [Fact]
    public async Task Evento_bills_usa_as_transacoes_ja_gravadas_e_nao_busca_transacoes()
    {
        using var s = new Scenario(Now);
        s.Polp.CardTransactions[s.CardId] =
        [
            ScriptedPolp.CardTx("2026-11-01T15:00:00.000Z", "100.00"),
            ScriptedPolp.CardTx("2026-11-02T15:00:00.000Z", "500.00", credit: true, type: "PAGAMENTO_FATURA"),
        ];
        await s.SyncCard();

        s.Polp.Calls.Clear();
        s.Polp.Bills[s.CardId] = [ScriptedPolp.Bill("2026-10-28", "2026-11-10", "500.00")];
        await SyncHarness.Processor(s.T, s.Polp).Process("bills", s.CardId, "fromUpdatedAt=2026-11-04T10:00:00");

        Assert.Equal([("bills", s.CardId, (string?)null)], s.Polp.Calls);
        var card = s.Card();
        Assert.Equal(100m, card.CurrentInvoiceAmount);
        Assert.Equal(Day(2026, 12, 10), card.InvoiceDueDate);
    }
}

public class PolpTransactionUpsertTests
{
    private static DateTime Day(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static (TestDb T, ScriptedPolp Polp, PolpIntegration Integration, BankAccount Account) Setup(bool card)
    {
        var t = new TestDb();
        var integration = SyncHarness.AddIntegration(t, synced: true);
        var account = SyncHarness.AddLinked(t, integration, Guid.NewGuid(), card);
        var polp = new ScriptedPolp();
        if (card) polp.Bills[account.PolpAccountId!.Value] = [];
        return (t, polp, integration, account);
    }

    private static Task Sync(TestDb t, ScriptedPolp polp, BankAccount account, bool card) =>
        SyncHarness.Processor(t, polp).Process(card ? "credit_cards.transactions" : "accounts.transactions", account.PolpAccountId!.Value);

    [Fact]
    public async Task Transacao_as_23h30_de_Brasilia_do_dia_31_fica_no_dia_31()
    {
        var (t, polp, _, account) = Setup(card: false);
        using var _t = t;
        polp.AccountTransactions[account.PolpAccountId!.Value] =
        [
            ScriptedPolp.AccountTx("2026-11-01T02:30:00.000Z", "50.00"),
            ScriptedPolp.AccountTx("2026-10-31T23:30:00", "60.00"),
            ScriptedPolp.AccountTx("2026-10-31T23:30:00-03:00", "70.00"),
        ];

        await Sync(t, polp, account, card: false);

        await using var db = t.NewContext();
        Assert.All(db.Transactions.ToList(), tx =>
        {
            Assert.Equal(Day(2026, 10, 31), tx.DueDate);
            Assert.Equal(Day(2026, 10, 31), tx.PaidAt);
        });
    }

    [Fact]
    public async Task Data_e_o_dia_civil_no_fuso_do_usuario()
    {
        var (t, polp, _, account) = Setup(card: false);
        using var _t = t;
        t.User.TimeZone = "America/Manaus";
        t.Db.SaveChanges();
        polp.AccountTransactions[account.PolpAccountId!.Value] = [ScriptedPolp.AccountTx("2026-11-01T03:30:00.000Z", "50.00")];

        await Sync(t, polp, account, card: false);

        await using var db = t.NewContext();
        Assert.Equal(Day(2026, 10, 31), db.Transactions.Single().DueDate);
    }

    [Fact]
    public async Task Data_invalida_usa_o_lancamento_na_fatura_depois_a_data_gravada_e_senao_e_erro_permanente()
    {
        var (t, polp, _, card) = Setup(card: true);
        using var _t = t;
        var remoteId = card.PolpAccountId!.Value;
        var known = Guid.NewGuid();
        t.Db.Transactions.Add(new Transaction
        {
            UserId = t.User.Id, BankAccountId = card.Id, Name = "COMPRA", Description = known.ToString(),
            Type = TransactionType.Expense, Status = TransactionStatus.Paid, Amount = 1m, DueDate = Day(2026, 9, 9)
        });
        t.Db.SaveChanges();

        var posted = ScriptedPolp.CardTx("2026-10-05T12:00:00.000Z", "10.00");
        posted.TransactionDateTime = "data-quebrada";
        var keepsSaved = ScriptedPolp.CardTx("2026-10-05T12:00:00.000Z", "20.00", billPostDate: "0001-01-01", id: known);
        keepsSaved.TransactionDateTime = null;
        polp.CardTransactions[remoteId] = [posted, keepsSaved];

        await Sync(t, polp, card, card: true);

        await using (var db = t.NewContext())
        {
            Assert.Equal(Day(2026, 10, 5), db.Transactions.Single(x => x.Description == posted.Id.ToString()).DueDate);
            Assert.Equal(Day(2026, 9, 9), db.Transactions.Single(x => x.Description == known.ToString()).DueDate);
        }

        var orphan = ScriptedPolp.CardTx("2026-10-05T12:00:00.000Z", "30.00", billPostDate: "0001-01-01");
        orphan.TransactionDateTime = "";
        polp.CardTransactions[remoteId] = [orphan];

        var ex = await Assert.ThrowsAsync<PolpPermanentException>(() => Sync(t, polp, card, card: true));
        Assert.Contains(orphan.Id.ToString(), ex.Message);
        await using (var db = t.NewContext())
            Assert.DoesNotContain(db.Transactions, x => x.Description == orphan.Id.ToString());
    }

    [Fact]
    public async Task Parcela_futura_fica_pendente_no_mes_previsto_no_dia_de_vencimento()
    {
        var (t, polp, _, card) = Setup(card: true);
        using var _t = t;
        var remoteId = card.PolpAccountId!.Value;
        polp.Bills[remoteId] = [ScriptedPolp.Bill("2026-10-03", "2026-10-31", "10.00")];
        polp.CardTransactions[remoteId] =
        [
            ScriptedPolp.CardTx("2026-09-15T15:00:00.000Z", "99.90", billPostDate: "0001-01-01", forecast: "2027-02"),
            ScriptedPolp.CardTx("2026-09-15T15:00:00.000Z", "99.90", billPostDate: "0001-01-01"),
        ];

        await Sync(t, polp, card, card: true);

        await using var db = t.NewContext();
        var future = db.Transactions.Where(x => x.Status == TransactionStatus.Pending).OrderBy(x => x.DueDate).ToList();
        Assert.Equal(2, future.Count);
        Assert.Equal(Day(2026, 9, 15), future[0].DueDate);
        Assert.Equal(Day(2027, 2, 28), future[1].DueDate);
        Assert.All(future, x => Assert.Null(x.PaidAt));
    }

    [Fact]
    public async Task Duplicatas_existentes_nao_quebram_e_lancamento_manual_nao_e_tocado()
    {
        var (t, polp, _, account) = Setup(card: false);
        using var _t = t;
        var polpId = Guid.NewGuid();
        Transaction Row(string? description, decimal amount, DateTime created) => new()
        {
            UserId = t.User.Id, BankAccountId = account.Id, Name = "X", Description = description, CreatedAt = created,
            Type = TransactionType.Expense, Status = TransactionStatus.Paid, Amount = amount, DueDate = Day(2026, 9, 1)
        };
        var oldest = Row(polpId.ToString(), 1m, Day(2026, 9, 1));
        var duplicate = Row(polpId.ToString(), 2m, Day(2026, 9, 2));
        var manualNull = Row(null, 3m, Day(2026, 9, 3));
        var manualNull2 = Row(null, 4m, Day(2026, 9, 3));
        var manualText = Row("aluguel", 5m, Day(2026, 9, 3));
        t.Db.Transactions.AddRange(oldest, duplicate, manualNull, manualNull2, manualText);
        t.Db.SaveChanges();
        polp.AccountTransactions[account.PolpAccountId!.Value] = [ScriptedPolp.AccountTx("2026-10-01T12:00:00.000Z", "42.00", id: polpId)];

        await Sync(t, polp, account, card: false);

        await using var db = t.NewContext();
        Assert.Equal(42m, db.Transactions.Single(x => x.Id == oldest.Id).Amount);
        Assert.Equal(2m, db.Transactions.Single(x => x.Id == duplicate.Id).Amount);
        Assert.Equal(3m, db.Transactions.Single(x => x.Id == manualNull.Id).Amount);
        Assert.Equal(4m, db.Transactions.Single(x => x.Id == manualNull2.Id).Amount);
        Assert.Equal(5m, db.Transactions.Single(x => x.Id == manualText.Id).Amount);
        Assert.Equal(5, db.Transactions.Count());
    }

    [Fact]
    public async Task Pagamento_de_fatura_no_cartao_fica_fora_dos_totais_e_as_compras_continuam_contando()
    {
        var (t, polp, _, card) = Setup(card: true);
        using var _t = t;
        var remoteId = card.PolpAccountId!.Value;
        var now = Dates.BrazilNow();
        var day = $"{now:yyyy-MM}-01T15:00:00.000Z";
        polp.CardTransactions[remoteId] =
        [
            ScriptedPolp.CardTx(day, "300.00"),
            ScriptedPolp.CardTx(day, "450.00", credit: true, type: "PAGAMENTO_FATURA"),
        ];

        await Sync(t, polp, card, card: true);

        await using (var db = t.NewContext())
        {
            var payment = db.Transactions.Single(x => x.Type == TransactionType.Income);
            Assert.Equal(PolpMovementKind.CardBillPayment, payment.PolpMovementKind);
            Assert.Equal(TransferDetection.CardBillPaymentCategoryName, db.Categories.Single(c => c.Id == payment.CategoryId).Name);
        }

        var monthStart = new DateTime(now.Year, now.Month, 1);
        var aggregates = Results.OkValue<TransactionAggregatesDto>(await t.Controller(db => new TransactionsController(db))
            .GetAggregates(card.Id, monthStart.ToString("yyyy-MM-dd"), monthStart.AddMonths(1).ToString("yyyy-MM-dd"), default));
        Assert.Equal(300m, aggregates.TotalExpense);
        Assert.Equal(0m, aggregates.TotalIncome);

        var summary = Results.OkValue<DashboardSummaryDto>(await t.Controller(db => new DashboardController(db)).GetSummary(card.Id, default));
        Assert.Equal(0m, summary.TotalIncome);
        Assert.Equal(300m, summary.TotalExpense);

        // Enriquecimento posterior com outro category_ref não rebaixa o pagamento.
        polp.CardTransactions[remoteId][1].CategoryRef = "TRANSFER_IN_DEPOSIT";
        await Sync(t, polp, card, card: true);
        await using (var db = t.NewContext())
            Assert.Equal(PolpMovementKind.CardBillPayment, db.Transactions.Single(x => x.Type == TransactionType.Income).PolpMovementKind);
    }

    [Fact]
    public async Task Enriquecimento_tardio_completa_nome_e_categoria_sem_sobrescrever_edicao()
    {
        var (t, polp, _, account) = Setup(card: false);
        using var _t = t;
        var remoteId = account.PolpAccountId!.Value;
        var edited = ScriptedPolp.AccountTx("2026-10-01T12:00:00.000Z", "10.00");
        var untouched = ScriptedPolp.AccountTx("2026-10-01T12:00:00.000Z", "20.00");
        polp.AccountTransactions[remoteId] = [edited, untouched];
        await Sync(t, polp, account, card: false);

        await using (var db = t.NewContext())
        {
            var row = db.Transactions.Single(x => x.Description == edited.Id.ToString());
            row.Name = "Meu nome";
            await db.SaveChangesAsync();
        }

        foreach (var tx in polp.AccountTransactions[remoteId])
        {
            tx.Counterparty = new PolpCounterpartyDto { Name = "Mercado Bom" };
            tx.CategoryRef = "FOOD_AND_DRINK_GROCERIES";
        }
        await Sync(t, polp, account, card: false);

        await using var check = t.NewContext();
        var food = check.Categories.Single(c => c.UserId == t.User.Id && c.Name == "Alimentação").Id;
        var e = check.Transactions.Single(x => x.Description == edited.Id.ToString());
        var u = check.Transactions.Single(x => x.Description == untouched.Id.ToString());
        Assert.Equal("Meu nome", e.Name);
        Assert.Equal("Mercado Bom", u.Name);
        Assert.Equal(food, u.CategoryId);
        Assert.Equal(food, e.CategoryId);
    }
}
