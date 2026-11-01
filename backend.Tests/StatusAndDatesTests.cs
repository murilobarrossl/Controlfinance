using ControlFinance.API.Controllers;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Tests.Support;

namespace ControlFinance.API.Tests;

public class StatusAndDatesTests
{
    [Fact]
    public void Pendente_com_vencimento_ontem_vira_Overdue()
    {
        Assert.Equal("Overdue", TransactionDto.EffectiveStatus(TransactionStatus.Pending, Dates.Today.AddDays(-1)));
    }

    [Fact]
    public void Pendente_com_vencimento_hoje_a_meia_noite_utc_continua_Pending()
    {
        Assert.Equal("Pending", TransactionDto.EffectiveStatus(TransactionStatus.Pending, Dates.Today));
    }

    [Fact]
    public void Pendente_com_vencimento_hoje_as_23h59_continua_Pending()
    {
        Assert.Equal("Pending", TransactionDto.EffectiveStatus(TransactionStatus.Pending, Dates.Today.AddHours(23).AddMinutes(59)));
    }

    [Fact]
    public void Pendente_com_vencimento_amanha_continua_Pending()
    {
        Assert.Equal("Pending", TransactionDto.EffectiveStatus(TransactionStatus.Pending, Dates.Today.AddDays(1)));
    }

    [Fact]
    public void Pago_com_vencimento_no_passado_continua_Paid()
    {
        Assert.Equal("Paid", TransactionDto.EffectiveStatus(TransactionStatus.Paid, Dates.Today.AddDays(-400)));
    }

    [Fact]
    public void Overdue_marcado_a_mao_com_vencimento_futuro_continua_Overdue()
    {
        Assert.Equal("Overdue", TransactionDto.EffectiveStatus(TransactionStatus.Overdue, Dates.Today.AddDays(5)));
    }

    [Fact]
    public void FromEntity_aplica_o_status_efetivo()
    {
        var t = new Transaction { Name = "Luz", Status = TransactionStatus.Pending, Type = TransactionType.Expense, Amount = 10m, DueDate = Dates.Today.AddDays(-3) };

        Assert.Equal("Overdue", TransactionDto.FromEntity(t).Status);
    }

    [Fact]
    public async Task Criar_transacao_com_data_so_dia_grava_meia_noite_utc_e_nao_muda_o_dia_apos_ler()
    {
        using var t = new TestDb();
        var dto = new CreateTransactionDto("Aluguel", null, "Expense", "Pending", 1500.00m,
            DateTime.SpecifyKind(new DateTime(2026, 8, 20), DateTimeKind.Unspecified), null, null);

        await t.Controller(db => new TransactionsController(db)).Create(dto);

        await using var other = t.NewContext();
        var saved = other.Transactions.Single();
        Assert.Equal(new DateTime(2026, 8, 20, 0, 0, 0), saved.DueDate);
        Assert.Equal(1500.00m, saved.Amount);
    }

    [Fact]
    public async Task Listagem_filtra_status_Overdue_incluindo_pendentes_vencidas_e_exclui_a_que_vence_hoje()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 10m, Dates.Today.AddDays(-1), "Vencida", TransactionStatus.Pending);
        t.AddTransaction(account, TransactionType.Expense, 20m, Dates.Today, "Hoje", TransactionStatus.Pending);
        t.AddTransaction(account, TransactionType.Expense, 30m, Dates.Today.AddDays(-9), "Marcada", TransactionStatus.Overdue);
        t.AddTransaction(account, TransactionType.Expense, 40m, Dates.Today.AddDays(-9), "Paga", TransactionStatus.Paid);

        var result = await t.Controller(db => new TransactionsController(db)).GetAll("Overdue", null, null);

        var names = Results.OkValue<List<TransactionDto>>(result).Select(x => x.Name).Order().ToList();
        Assert.Equal(["Marcada", "Vencida"], names);
    }

    [Fact]
    public async Task Listagem_filtra_status_Pending_e_nao_inclui_pendente_ja_vencida()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 10m, Dates.Today.AddDays(-1), "Vencida", TransactionStatus.Pending);
        t.AddTransaction(account, TransactionType.Expense, 20m, Dates.Today, "Hoje", TransactionStatus.Pending);
        t.AddTransaction(account, TransactionType.Expense, 25m, Dates.Today.AddDays(3), "Futura", TransactionStatus.Pending);

        var result = await t.Controller(db => new TransactionsController(db)).GetAll("Pending", null, null);

        var names = Results.OkValue<List<TransactionDto>>(result).Select(x => x.Name).Order().ToList();
        Assert.Equal(["Futura", "Hoje"], names);
    }

    [Fact]
    public async Task Listagem_sem_filtro_rotula_pendente_vencida_como_Overdue()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 10m, Dates.Today.AddDays(-1), "Vencida", TransactionStatus.Pending);

        var result = await t.Controller(db => new TransactionsController(db)).GetAll(null, null, null);

        Assert.Equal("Overdue", Assert.Single(Results.OkValue<List<TransactionDto>>(result)).Status);
    }
}

public class BrazilDayBoundaryTests
{
    [Fact]
    public void Pendente_que_vence_hoje_em_Brasilia_nunca_e_Overdue_mesmo_com_UTC_ja_no_dia_seguinte()
    {
        Assert.Equal("Pending", ControlFinance.API.DTOs.TransactionDto.EffectiveStatus(
            ControlFinance.API.Models.TransactionStatus.Pending, Dates.Today));
    }

    [Fact]
    public void Pendente_que_venceu_ontem_em_Brasilia_e_Overdue_mesmo_quando_UTC_ainda_e_o_mesmo_dia()
    {
        Assert.Equal("Overdue", ControlFinance.API.DTOs.TransactionDto.EffectiveStatus(
            ControlFinance.API.Models.TransactionStatus.Pending, Dates.Today.AddDays(-1)));
    }

    [Fact]
    public async Task Filtro_Overdue_e_Pending_da_listagem_usam_o_dia_de_Brasilia()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        t.AddTransaction(account, ControlFinance.API.Models.TransactionType.Expense, 10m, Dates.Today.AddDays(-1), "ontem", ControlFinance.API.Models.TransactionStatus.Pending);
        t.AddTransaction(account, ControlFinance.API.Models.TransactionType.Expense, 20m, Dates.Today, "hoje", ControlFinance.API.Models.TransactionStatus.Pending);

        var controller = t.Controller(db => new TransactionsController(db));
        var overdue = Results.OkValue<List<TransactionDto>>(await controller.GetAll("Overdue", null, null));
        var pending = Results.OkValue<List<TransactionDto>>(await controller.GetAll("Pending", null, null));

        Assert.Equal(["ontem"], overdue.Select(x => x.Name).ToArray());
        Assert.Equal(["hoje"], pending.Select(x => x.Name).ToArray());
    }

    [Fact]
    public async Task Parcela_que_vence_hoje_em_Brasilia_nao_avanca_mesmo_com_UTC_no_dia_seguinte()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 1200m);
        var installment = t.AddInstallment(card, null, 100m, 1, 12, Dates.Today);

        await ControlFinance.API.Services.InstallmentProgress.ReconcileAsync(t.Db, [installment]);

        Assert.Equal(1, installment.CurrentInstallment);
        Assert.Equal(1200.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Vencimento_do_cartao_manual_com_dia_igual_ao_dia_de_Brasilia_e_hoje_em_Brasilia()
    {
        using var t = new TestDb();
        var today = Dates.Today;
        t.AddCard(5000m, 0m, dueDay: today.Day);

        var dto = Assert.Single(await ControlFinance.API.Services.CreditCardSummaryBuilder.BuildRecognizedListAsync(t.Db, t.User.Id));

        Assert.Equal(new DateTime(today.Year, today.Month, today.Day), dto.InvoiceDueDate);
    }

    [Fact]
    public async Task Vencimento_do_cartao_manual_um_dia_antes_de_hoje_em_Brasilia_vai_para_o_mes_seguinte()
    {
        var today = Dates.Today;
        if (today.Day == 1) return;
        using var t = new TestDb();
        t.AddCard(5000m, 0m, dueDay: today.Day - 1);

        var dto = Assert.Single(await ControlFinance.API.Services.CreditCardSummaryBuilder.BuildRecognizedListAsync(t.Db, t.User.Id));

        Assert.Equal(new DateTime(today.Year, today.Month, today.Day - 1).AddMonths(1), dto.InvoiceDueDate);
    }

    [Fact]
    public async Task Parcela_dia_31_gravada_como_fim_de_fevereiro_volta_a_ser_fim_de_mes_nos_meses_de_31()
    {
        using var t = new TestDb();
        var card = t.AddCard(1_000_000m, 200_000m);
        var installment = t.AddInstallment(card, null, 100m, current: 2, total: 2000, new DateTime(2024, 2, 29), dueDay: 31);

        await ControlFinance.API.Services.InstallmentProgress.ReconcileAsync(t.Db, [installment]);

        var d = installment.NextDueDate;
        Assert.Equal(DateTime.DaysInMonth(d.Year, d.Month), d.Day);
    }
}
