using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Tests;

public class InstallmentProgressTests
{
    private static async Task<List<Installment>> Reconcile(TestDb t) =>
        await InstallmentProgress.ReconcileAsync(t.Db,
            await t.Db.Installments.Include(i => i.CreditCard).Include(i => i.BankAccount).ToListAsync(), Dates.Today);

    [Fact]
    public async Task Cartao12x100_criado_na_1a_parcela_apos_5_meses_avanca_para_6a_e_usado_cai_de_1200_para_700()
    {
        using var t = new TestDb();
        var card = t.AddCard(limit: 5000m, used: 1200m);
        var due = Dates.Day15MonthsAgo(5);
        var installment = t.AddInstallment(card, null, 100m, current: 1, total: 12, due);

        var active = await Reconcile(t);

        Assert.Single(active);
        Assert.Equal(6, installment.CurrentInstallment);
        Assert.Equal(due.AddMonths(5), installment.NextDueDate);
        Assert.Equal(700.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Avanco_e_persistido_e_relido_por_outro_contexto()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 1200m);
        var installment = t.AddInstallment(card, null, 100m, 1, 12, Dates.Day15MonthsAgo(5));

        await Reconcile(t);

        await using var other = t.NewContext();
        Assert.Equal(700.00m, (await other.CreditCards.SingleAsync()).UsedLimit);
        Assert.Equal(6, (await other.Installments.SingleAsync(i => i.Id == installment.Id)).CurrentInstallment);
    }

    [Fact]
    public async Task Segunda_reconciliacao_seguida_nao_altera_nada()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 1200m);
        t.AddInstallment(card, null, 100m, 1, 12, Dates.Day15MonthsAgo(5));

        await Reconcile(t);
        await Reconcile(t);

        Assert.Equal(700.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Quitacao_a_partir_da_6a_parcela_libera_so_os_700_restantes_e_usado_fica_0()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 700m);
        t.AddInstallment(card, null, 100m, current: 6, total: 12, Dates.Day15MonthsAgo(7));

        var active = await Reconcile(t);

        Assert.Empty(active);
        Assert.Equal(0.00m, card.UsedLimit);
        Assert.Empty(await t.Db.Installments.ToListAsync());
    }

    [Fact]
    public async Task Quitacao_nao_libera_alem_do_restante_quando_ha_outra_compra_no_mesmo_cartao()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 1000m);
        t.AddInstallment(card, null, 100m, current: 6, total: 12, Dates.Day15MonthsAgo(7));

        await Reconcile(t);

        Assert.Equal(300.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Ultima_parcela_ainda_a_vencer_nao_quita_e_libera_so_a_parcela_paga()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 200m);
        var installment = t.AddInstallment(card, null, 100m, current: 11, total: 12, Dates.Day15MonthsAgo(1));

        var active = await Reconcile(t);

        Assert.Single(active);
        Assert.Equal(12, installment.CurrentInstallment);
        Assert.Equal(100.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Avancar_exatamente_ate_a_ultima_parcela_6_de_12_com_6_meses_mantem_100_usado()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 700m);
        var installment = t.AddInstallment(card, null, 100m, current: 6, total: 12, Dates.Day15MonthsAgo(6));

        await Reconcile(t);

        Assert.Equal(12, installment.CurrentInstallment);
        Assert.Equal(100.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Limite_usado_nunca_fica_negativo_piso_zero_ao_avancar()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 250m);
        t.AddInstallment(card, null, 100m, current: 1, total: 12, Dates.Day15MonthsAgo(5));

        await Reconcile(t);

        Assert.Equal(0.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Limite_usado_nunca_fica_negativo_piso_zero_na_quitacao()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 50m);
        t.AddInstallment(card, null, 100m, current: 1, total: 3, Dates.Day15MonthsAgo(5));

        await Reconcile(t);

        Assert.Equal(0.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Parcela_que_vence_hoje_ainda_nao_avanca()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 1200m);
        var installment = t.AddInstallment(card, null, 100m, 1, 12, Dates.Today);

        await Reconcile(t);

        Assert.Equal(1, installment.CurrentInstallment);
        Assert.Equal(1200.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Parcela_que_venceu_ontem_avanca_uma_parcela_e_libera_100()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 1200m);
        var installment = t.AddInstallment(card, null, 100m, 1, 12, Dates.Today.AddDays(-1));

        await Reconcile(t);

        Assert.Equal(2, installment.CurrentInstallment);
        Assert.Equal(1100.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Parcela_futura_nao_altera_nada()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 1200m);
        var installment = t.AddInstallment(card, null, 100m, 1, 12, Dates.Today.AddDays(20));

        await Reconcile(t);

        Assert.Equal(1, installment.CurrentInstallment);
        Assert.Equal(1200.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Duas_compras_no_mesmo_cartao_liberam_soma_de_cada_uma_100x5_mais_50x5_igual_750()
    {
        using var t = new TestDb();
        var card = t.AddCard(10000m, 1800m);
        t.AddInstallment(card, null, 100m, 1, 12, Dates.Day15MonthsAgo(5));
        t.AddInstallment(card, null, 50m, 1, 12, Dates.Day15MonthsAgo(5));

        await Reconcile(t);

        Assert.Equal(1050.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Parcela_com_centavos_33_33_em_3x_avanca_1_mes_e_usado_cai_de_99_99_para_66_66()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 99.99m);
        var installment = t.AddInstallment(card, null, 33.33m, 1, 3, Dates.Day15MonthsAgo(1), totalAmount: 100.00m);

        await Reconcile(t);

        Assert.Equal(2, installment.CurrentInstallment);
        Assert.Equal(66.66m, card.UsedLimit);
    }

    [Fact]
    public async Task Parcela_com_centavos_33_33_em_3x_quitada_zera_o_usado_exatamente()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 99.99m);
        t.AddInstallment(card, null, 33.33m, 1, 3, Dates.Day15MonthsAgo(3), totalAmount: 100.00m);

        await Reconcile(t);

        Assert.Equal(0.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Parcela_unica_vencida_quita_e_libera_o_valor_inteiro()
    {
        using var t = new TestDb();
        var card = t.AddCard(5000m, 250.50m);
        t.AddInstallment(card, null, 250.50m, 1, 1, Dates.Day15MonthsAgo(1));

        var active = await Reconcile(t);

        Assert.Empty(active);
        Assert.Equal(0.00m, card.UsedLimit);
    }

    [Fact]
    public async Task Cartao_sincronizado_Polp_avanca_a_parcela_mas_nao_altera_o_usado_1200()
    {
        using var t = new TestDb();
        var account = t.AddAccount("Cartao Banco", type: "CREDIT_CARD", creditLimit: 5000m, usedLimit: 1200m, polpAccountId: Guid.NewGuid());
        var installment = t.AddInstallment(null, account, 100m, 1, 12, Dates.Day15MonthsAgo(5));

        await Reconcile(t);

        Assert.Equal(6, installment.CurrentInstallment);
        Assert.Equal(1200.00m, account.UsedLimit);
    }

    [Fact]
    public async Task Cartao_sincronizado_Polp_quitado_remove_a_parcela_e_nao_altera_o_usado_700()
    {
        using var t = new TestDb();
        var account = t.AddAccount("Cartao Banco", type: "CREDIT_CARD", creditLimit: 5000m, usedLimit: 700m, polpAccountId: Guid.NewGuid());
        t.AddInstallment(null, account, 100m, 6, 12, Dates.Day15MonthsAgo(7));

        var active = await Reconcile(t);

        Assert.Empty(active);
        Assert.Equal(700.00m, account.UsedLimit);
    }

    [Fact]
    public async Task Lado_a_lado_manual_e_conta_sem_PolpAccountId_aplicam_B1_e_sincronizado_nao()
    {
        using var t = new TestDb();
        var manual = t.AddCard(5000m, 1200m);
        var plainAccount = t.AddAccount("Conta cartao", type: "CREDIT_CARD", creditLimit: 5000m, usedLimit: 1200m);
        var synced = t.AddAccount("Cartao Polp", type: "CREDIT_CARD", creditLimit: 5000m, usedLimit: 1200m, polpAccountId: Guid.NewGuid());
        t.AddInstallment(manual, null, 100m, 1, 12, Dates.Day15MonthsAgo(5));
        t.AddInstallment(null, plainAccount, 100m, 1, 12, Dates.Day15MonthsAgo(5));
        t.AddInstallment(null, synced, 100m, 1, 12, Dates.Day15MonthsAgo(5));

        await Reconcile(t);

        Assert.Equal(700.00m, manual.UsedLimit);
        Assert.Equal(700.00m, plainAccount.UsedLimit);
        Assert.Equal(1200.00m, synced.UsedLimit);
    }

    [Fact]
    public async Task Conta_sincronizada_sem_limite_usado_conhecido_nao_e_alterada_nem_quebra()
    {
        using var t = new TestDb();
        var account = t.AddAccount("Cartao Banco", type: "CREDIT_CARD", creditLimit: 5000m, usedLimit: null);
        var installment = t.AddInstallment(null, account, 100m, 1, 12, Dates.Day15MonthsAgo(5));

        await Reconcile(t);

        Assert.Null(account.UsedLimit);
        Assert.Equal(6, installment.CurrentInstallment);
    }

    [Fact]
    public async Task Parcelamento_sem_cartao_avanca_sem_mexer_em_limite_algum()
    {
        using var t = new TestDb();
        var installment = t.AddInstallment(null, null, 100m, 1, 12, Dates.Day15MonthsAgo(5));

        await Reconcile(t);

        Assert.Equal(6, installment.CurrentInstallment);
    }

    [Fact]
    public async Task Dia31_e_virada_de_ano_proxima_data_usa_fim_de_mes_correto_a_partir_da_data_original()
    {
        using var t = new TestDb();
        var card = t.AddCard(1_000_000m, 200_000m);
        var installment = t.AddInstallment(card, null, 100m, current: 1, total: 2000, new DateTime(2020, 1, 31));

        await Reconcile(t);

        var advanced = installment.CurrentInstallment - 1;
        Assert.True(advanced > 0);
        Assert.True(Expected(advanced) >= Dates.Today);
        Assert.True(Expected(advanced - 1) < Dates.Today);
        Assert.Equal(Expected(advanced), installment.NextDueDate);
        Assert.Equal(200_000m - 100m * advanced, card.UsedLimit);

        static DateTime Expected(int monthsAfterJan2020)
        {
            var year = 2020 + monthsAfterJan2020 / 12;
            var month = 1 + monthsAfterJan2020 % 12;
            return new DateTime(year, month, Math.Min(31, DateTime.DaysInMonth(year, month)));
        }
    }

    private static DateTime AnchoredDate(int startYear, int startMonth, int dueDay, int monthsAfter)
    {
        var index = startYear * 12 + (startMonth - 1) + monthsAfter;
        var year = index / 12;
        var month = index % 12 + 1;
        return new DateTime(year, month, Math.Min(dueDay, DateTime.DaysInMonth(year, month)));
    }

    [Theory]
    [InlineData(2024, 1, 31, 31)]
    [InlineData(2024, 1, 30, 30)]
    [InlineData(2025, 10, 31, 31)]
    [InlineData(2023, 11, 29, 29)]
    public async Task DueDay_avanca_para_min_do_dia_original_e_dias_do_mes_voltando_ao_dia_original(int year, int month, int dueDay, int startDay)
    {
        using var t = new TestDb();
        var card = t.AddCard(1_000_000m, 200_000m);
        var installment = t.AddInstallment(card, null, 100m, current: 1, total: 2000,
            new DateTime(year, month, startDay), dueDay: dueDay);

        await Reconcile(t);

        var advanced = installment.CurrentInstallment - 1;
        Assert.True(advanced > 0);
        Assert.True(AnchoredDate(year, month, dueDay, advanced) >= Dates.Today);
        Assert.True(AnchoredDate(year, month, dueDay, advanced - 1) < Dates.Today);
        Assert.Equal(AnchoredDate(year, month, dueDay, advanced), installment.NextDueDate);
        Assert.Equal(200_000m - 100m * advanced, card.UsedLimit);
    }

    [Theory]
    [InlineData(2024, 2, 29)]
    [InlineData(2024, 4, 30)]
    [InlineData(2025, 2, 28)]
    public async Task DueDay_31_com_data_gravada_clampada_volta_ao_fim_de_mes_correto(int year, int month, int day)
    {
        using var t = new TestDb();
        var card = t.AddCard(1_000_000m, 200_000m);
        var installment = t.AddInstallment(card, null, 100m, current: 2, total: 2000, new DateTime(year, month, day), dueDay: 31);

        await Reconcile(t);

        var d = installment.NextDueDate;
        Assert.Equal(Math.Min(31, DateTime.DaysInMonth(d.Year, d.Month)), d.Day);
    }

    [Fact]
    public async Task DueDay_30_em_fevereiro_cai_no_ultimo_dia_e_volta_a_30_em_marco()
    {
        Assert.Equal(new DateTime(2024, 2, 29), AnchoredDate(2024, 1, 30, 1));
        Assert.Equal(new DateTime(2025, 2, 28), AnchoredDate(2025, 1, 30, 1));
        Assert.Equal(new DateTime(2024, 3, 30), AnchoredDate(2024, 1, 30, 2));

        using var t = new TestDb();
        var card = t.AddCard(1_000_000m, 200_000m);
        var installment = t.AddInstallment(card, null, 100m, current: 1, total: 2000, new DateTime(2024, 1, 30), dueDay: 30);

        await Reconcile(t);

        var d = installment.NextDueDate;
        Assert.Equal(Math.Min(30, DateTime.DaysInMonth(d.Year, d.Month)), d.Day);
    }

    [Fact]
    public void Sequencia_31_jan_29_fev_31_mar_30_abr_31_mai_de_2024_calculada_a_mao()
    {
        Assert.Equal(new DateTime(2024, 1, 31), AnchoredDate(2024, 1, 31, 0));
        Assert.Equal(new DateTime(2024, 2, 29), AnchoredDate(2024, 1, 31, 1));
        Assert.Equal(new DateTime(2024, 3, 31), AnchoredDate(2024, 1, 31, 2));
        Assert.Equal(new DateTime(2024, 4, 30), AnchoredDate(2024, 1, 31, 3));
        Assert.Equal(new DateTime(2024, 5, 31), AnchoredDate(2024, 1, 31, 4));
    }

    [Fact]
    public async Task DueDay_nulo_mantem_o_comportamento_antigo_sem_recuperar_o_dia_31()
    {
        using var t = new TestDb();
        var card = t.AddCard(1_000_000m, 200_000m);
        var installment = t.AddInstallment(card, null, 100m, current: 2, total: 2000, new DateTime(2024, 2, 29), dueDay: null);

        await Reconcile(t);

        var advanced = installment.CurrentInstallment - 2;
        Assert.True(advanced > 0);
        Assert.Equal(new DateTime(2024, 2, 29).AddMonths(advanced), installment.NextDueDate);
    }
}
