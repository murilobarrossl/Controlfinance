using ControlFinance.API.Controllers;
using ControlFinance.API.DTOs;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;

namespace ControlFinance.API.Tests;

public class CreditCardSummaryTests
{
    private static async Task<List<RecognizedCardDto>> Build(TestDb t, Guid? primary = null)
    {
        t.User.PrimaryCardId = primary;
        await t.Db.SaveChangesAsync();
        return await CreditCardSummaryBuilder.BuildRecognizedListAsync(t.Db, t.User.Id, Dates.Today);
    }

    [Fact]
    public async Task Fatura_atual_do_cartao_manual_e_a_soma_das_parcelas_ativas_100_mais_33_33_igual_133_33()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 1200m);
        t.AddInstallment(card, null, 100m, 1, 12, Dates.Today.AddDays(10));
        t.AddInstallment(card, null, 33.33m, 1, 3, Dates.Today.AddDays(10));

        var dto = Assert.Single(await Build(t));

        Assert.Equal(133.33m, dto.CurrentInvoice);
    }

    [Fact]
    public async Task Fatura_atual_ignora_parcelamento_quitado_e_usa_a_ativa_100()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 800m);
        t.AddInstallment(card, null, 100m, 1, 12, Dates.Today.AddDays(10));
        t.AddInstallment(card, null, 50m, 1, 2, Dates.Day15MonthsAgo(3));

        var dto = Assert.Single(await Build(t));

        Assert.Equal(100.00m, dto.CurrentInvoice);
        Assert.Single(dto.Installments);
    }

    [Fact]
    public async Task Cartao_manual_sem_parcelas_tem_fatura_zero()
    {
        using var t = new TestDb();
        t.AddCard(5000m, 0m);

        var dto = Assert.Single(await Build(t));

        Assert.Equal(0.00m, dto.CurrentInvoice);
    }

    [Fact]
    public async Task Resumo_reconcilia_antes_de_ler_o_usado_1200_vira_700_apos_5_meses()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 1200m);
        t.AddInstallment(card, null, 100m, 1, 12, Dates.Day15MonthsAgo(5));

        var dto = Assert.Single(await Build(t));

        Assert.Equal(700.00m, dto.UsedLimit);
        Assert.Equal(5000.00m, dto.CreditLimit);
    }

    [Fact]
    public async Task Vencimento_no_proprio_dia_de_hoje_continua_sendo_hoje_e_nao_pula_para_o_mes_seguinte()
    {
        using var t = new TestDb();
        var today = Dates.Today;
        t.AddCard(5000m, 0m, dueDay: today.Day);

        var dto = Assert.Single(await Build(t));

        Assert.Equal(new DateTime(today.Year, today.Month, today.Day), dto.InvoiceDueDate);
    }

    [Fact]
    public async Task Vencimento_com_dia_ja_passado_neste_mes_vai_para_o_dia_1_do_mes_seguinte()
    {
        var today = Dates.Today;
        if (today.Day == 1) return;
        using var t = new TestDb();
        t.AddCard(5000m, 0m, dueDay: 1);

        var dto = Assert.Single(await Build(t));

        Assert.Equal(new DateTime(today.Year, today.Month, 1).AddMonths(1), dto.InvoiceDueDate);
    }

    [Fact]
    public async Task Vencimento_dia_31_cai_no_ultimo_dia_do_mes_corrente_sem_estourar()
    {
        using var t = new TestDb();
        var today = Dates.Today;
        t.AddCard(5000m, 0m, dueDay: 31);

        var dto = Assert.Single(await Build(t));

        Assert.Equal(new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)), dto.InvoiceDueDate);
    }

    [Fact]
    public async Task Vencimento_gravado_e_so_data_meia_noite_utc()
    {
        using var t = new TestDb();
        t.AddCard(5000m, 0m, dueDay: 31);

        var dto = Assert.Single(await Build(t));

        Assert.Equal(TimeSpan.Zero, dto.InvoiceDueDate!.Value.TimeOfDay);
        Assert.Equal(DateTimeKind.Utc, dto.InvoiceDueDate.Value.Kind);
    }

    [Fact]
    public async Task Cartao_sincronizado_repassa_limite_usado_fatura_e_vencimento_gravados()
    {
        using var t = new TestDb();
        var account = t.AddAccount("Cartao Banco", type: "CREDIT_CARD", creditLimit: 8000.00m, usedLimit: 2345.67m);
        account.CurrentInvoiceAmount = 987.65m;
        account.InvoiceDueDate = new DateTime(2099, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        account.ClosingDay = 3;
        account.DueDay = 10;
        await t.Db.SaveChangesAsync();

        var dto = Assert.Single(await Build(t));

        Assert.True(dto.IsSynced);
        Assert.Equal(8000.00m, dto.CreditLimit);
        Assert.Equal(2345.67m, dto.UsedLimit);
        Assert.Equal(987.65m, dto.CurrentInvoice);
        Assert.Equal(new DateTime(2099, 3, 10), dto.InvoiceDueDate);
        Assert.True(dto.HasFullDetails);
    }

    [Fact]
    public async Task Cartao_sincronizado_sem_limite_nao_tem_detalhes_completos_e_nao_inventa_numeros()
    {
        using var t = new TestDb();
        t.AddAccount("Cartao Banco", type: "CREDIT_CARD");

        var dto = Assert.Single(await Build(t));

        Assert.False(dto.HasFullDetails);
        Assert.Null(dto.CreditLimit);
        Assert.Null(dto.UsedLimit);
        Assert.Null(dto.CurrentInvoice);
    }

    [Fact]
    public async Task Conta_corrente_nao_aparece_como_cartao()
    {
        using var t = new TestDb();
        t.AddAccount("Corrente", type: "BANK");

        Assert.Empty(await Build(t));
    }

    [Fact]
    public async Task Cartao_principal_escolhido_pelo_usuario_e_marcado_e_so_ele()
    {
        using var t = new TestDb();
        var first = t.AddCard(1000m, 0m);
        var second = t.AddCard(2000m, 0m);

        var list = await Build(t, second.Id);

        Assert.Equal(second.Id, Assert.Single(list, c => c.IsPrimary).Id);
        Assert.False(list.Single(c => c.Id == first.Id).IsPrimary);
    }

    [Fact]
    public async Task Sem_principal_valido_o_primeiro_da_lista_e_o_principal()
    {
        using var t = new TestDb();
        var first = t.AddCard(1000m, 0m);
        t.AddCard(2000m, 0m);

        var list = await Build(t, Guid.NewGuid());

        Assert.Equal(first.Id, list.Single(c => c.IsPrimary).Id);
    }

    [Fact]
    public async Task Cartao_inativo_nao_entra_no_resumo()
    {
        using var t = new TestDb();
        var card = t.AddCard(1000m, 0m);
        card.IsActive = false;
        await t.Db.SaveChangesAsync();

        Assert.Empty(await Build(t));
    }

    [Fact]
    public async Task Endpoint_de_resumo_devolve_os_mesmos_numeros_do_builder()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 1200m);
        t.AddInstallment(card, null, 100m, 1, 12, Dates.Day15MonthsAgo(5));

        var result = await t.Controller(db => new CreditCardsController(db)).GetSummary(default);

        var dto = Assert.Single(Results.OkValue<List<RecognizedCardDto>>(result));
        Assert.Equal(700.00m, dto.UsedLimit);
        Assert.Equal(100.00m, dto.CurrentInvoice);
    }
}
