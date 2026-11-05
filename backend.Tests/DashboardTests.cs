using ControlFinance.API.Controllers;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Tests.Support;

namespace ControlFinance.API.Tests;

public class DashboardTests
{
    private static DateTime MonthStart(int offset = 0)
    {
        var now = Dates.BrazilNow();
        return new DateTime(now.Year, now.Month, 1).AddMonths(offset);
    }

    private static async Task<DashboardSummaryDto> Summary(TestDb t, Guid? accountId = null) =>
        Results.OkValue<DashboardSummaryDto>(await t.Controller(db => new DashboardController(db)).GetSummary(accountId, default));

    [Fact]
    public async Task Sem_conta_cadastrada_devolve_mensagem_e_nao_numeros()
    {
        using var t = new TestDb();

        var result = await t.Controller(db => new DashboardController(db)).GetSummary(null, default);

        var ok = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        Assert.IsNotType<DashboardSummaryDto>(ok.Value);
    }

    [Fact]
    public async Task Receita_do_mes_3000_mais_500_50_e_despesa_1200_mais_99_99()
    {
        using var t = new TestDb();
        var account = t.AddAccount(balance: 4321.09m);
        var day = MonthStart().AddDays(9);
        t.AddTransaction(account, TransactionType.Income, 3000m, day);
        t.AddTransaction(account, TransactionType.Income, 500.50m, day);
        t.AddTransaction(account, TransactionType.Expense, 1200m, day);
        t.AddTransaction(account, TransactionType.Expense, 99.99m, day);

        var s = await Summary(t);

        Assert.Equal(3500.50m, s.TotalIncome);
        Assert.Equal(1299.99m, s.TotalExpense);
        Assert.Equal(4321.09m, s.ActiveAccount.Balance);
    }

    [Fact]
    public async Task Saldo_da_conta_negativo_e_exibido_com_sinal_e_centavos()
    {
        using var t = new TestDb();
        t.AddAccount(balance: -1234.56m);

        var s = await Summary(t);

        Assert.Equal(-1234.56m, s.ActiveAccount.Balance);
    }

    [Fact]
    public async Task Tres_centavos_somam_exatamente_3_centavos_sem_erro_de_ponto_flutuante()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        var day = MonthStart().AddDays(3);
        t.AddTransaction(account, TransactionType.Income, 0.01m, day);
        t.AddTransaction(account, TransactionType.Income, 0.01m, day);
        t.AddTransaction(account, TransactionType.Income, 0.01m, day);
        t.AddTransaction(account, TransactionType.Expense, 0.10m, day);
        t.AddTransaction(account, TransactionType.Expense, 0.20m, day);

        var s = await Summary(t);

        Assert.Equal(0.03m, s.TotalIncome);
        Assert.Equal(0.30m, s.TotalExpense);
    }

    [Fact]
    public async Task Mes_sem_transacoes_tem_totais_zero()
    {
        using var t = new TestDb();
        t.AddAccount();

        var s = await Summary(t);

        Assert.Equal(0m, s.TotalIncome);
        Assert.Equal(0m, s.TotalExpense);
    }

    [Fact]
    public async Task Primeiro_instante_do_mes_entra_e_primeiro_instante_do_mes_seguinte_nao_entra()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 10m, MonthStart());
        t.AddTransaction(account, TransactionType.Expense, 20m, MonthStart(1).AddTicks(-1));
        t.AddTransaction(account, TransactionType.Expense, 400m, MonthStart(1));

        var s = await Summary(t);

        Assert.Equal(30m, s.TotalExpense);
    }

    [Fact]
    public async Task Ultimo_instante_do_mes_anterior_nao_entra()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Income, 777m, MonthStart().AddTicks(-1));
        t.AddTransaction(account, TransactionType.Income, 5m, MonthStart());

        var s = await Summary(t);

        Assert.Equal(5m, s.TotalIncome);
    }

    [Fact]
    public async Task Transacao_da_mesma_data_um_ano_antes_nao_entra_no_mes_corrente()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 900m, MonthStart().AddYears(-1).AddDays(9));
        t.AddTransaction(account, TransactionType.Expense, 1m, MonthStart().AddDays(9));

        var s = await Summary(t);

        Assert.Equal(1m, s.TotalExpense);
    }

    [Fact]
    public async Task Transferencia_entre_contas_proprias_nao_entra_em_receita_nem_em_despesa()
    {
        using var t = new TestDb("Igor Luis Lima");
        var account = t.AddAccount();
        var day = MonthStart().AddDays(9);
        t.AddTransaction(account, TransactionType.Income, 1000m, day, "Salario");
        t.AddTransaction(account, TransactionType.Expense, 300m, day, "Mercado");
        t.AddTransaction(account, TransactionType.Expense, 2000m, day, "Transferência enviada|IGOR L LIMA",
            movementKind: PolpMovementKind.OwnTransfer);
        t.AddTransaction(account, TransactionType.Income, 2000m, day, "Transferência recebida|Igor Luis Lima",
            movementKind: PolpMovementKind.OwnTransfer);

        var s = await Summary(t);

        Assert.Equal(1000m, s.TotalIncome);
        Assert.Equal(300m, s.TotalExpense);
    }

    [Fact]
    public async Task Transferencia_para_outra_pessoa_continua_sendo_despesa()
    {
        using var t = new TestDb("Igor Luis Lima");
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 250m, MonthStart().AddDays(9), "Transferência enviada|Joao Pedro Souza");

        var s = await Summary(t);

        Assert.Equal(250m, s.TotalExpense);
    }

    [Fact]
    public async Task Transferencia_excluida_dos_totais_continua_listada_nas_pendencias_marcada_como_transferencia()
    {
        using var t = new TestDb("Igor Luis Lima");
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 2000m, Dates.Today.AddDays(2), "Transferência enviada|IGOR L LIMA", TransactionStatus.Pending,
            movementKind: PolpMovementKind.OwnTransfer);

        var s = await Summary(t);

        var item = Assert.Single(s.PendingTransactions);
        Assert.True(item.IsTransfer);
    }

    [Fact]
    public async Task Totais_consideram_so_a_conta_selecionada()
    {
        using var t = new TestDb();
        var a = t.AddAccount("A");
        var b = t.AddAccount("B");
        var day = MonthStart().AddDays(9);
        t.AddTransaction(a, TransactionType.Expense, 100m, day);
        t.AddTransaction(b, TransactionType.Expense, 7m, day);

        Assert.Equal(100m, (await Summary(t, a.Id)).TotalExpense);
        Assert.Equal(7m, (await Summary(t, b.Id)).TotalExpense);
    }

    [Fact]
    public async Task Transacoes_de_outro_usuario_nao_entram_nos_totais()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        var other = new User { Name = "Outro", Email = "o@x.com", DocumentHash = "h2", PasswordHash = "x" };
        t.Db.Users.Add(other);
        t.Db.Transactions.Add(new Transaction
        {
            UserId = other.Id, BankAccountId = account.Id, Name = "Alheia", Type = TransactionType.Expense,
            Status = TransactionStatus.Paid, Amount = 999m, DueDate = DateTime.SpecifyKind(MonthStart().AddDays(9), DateTimeKind.Utc)
        });
        await t.Db.SaveChangesAsync();

        Assert.Equal(0m, (await Summary(t)).TotalExpense);
    }

    [Fact]
    public async Task Percentual_por_categoria_300_em_3_categorias_iguais_da_33_3_cada_e_valores_corretos()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        var day = MonthStart().AddDays(9);
        t.AddTransaction(account, TransactionType.Expense, 100m, day, "a", category: t.AddCategory("Casa"));
        t.AddTransaction(account, TransactionType.Expense, 100m, day, "b", category: t.AddCategory("Lazer"));
        t.AddTransaction(account, TransactionType.Expense, 100m, day, "c", category: t.AddCategory("Saude"));

        var s = await Summary(t);

        Assert.Equal(3, s.CategoryExpenses.Count());
        Assert.All(s.CategoryExpenses, c =>
        {
            Assert.Equal(100m, c.Amount);
            Assert.Equal(33.3m, c.Percentage);
        });
    }

    [Fact]
    public async Task Percentual_por_categoria_75_e_25()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        var day = MonthStart().AddDays(9);
        var casa = t.AddCategory("Casa");
        t.AddTransaction(account, TransactionType.Expense, 150m, day, "a", category: casa);
        t.AddTransaction(account, TransactionType.Expense, 150m, day, "b", category: casa);
        t.AddTransaction(account, TransactionType.Expense, 100m, day, "c", category: t.AddCategory("Lazer"));

        var s = await Summary(t);

        Assert.Equal(300m, s.CategoryExpenses.Single(c => c.CategoryName == "Casa").Amount);
        Assert.Equal(75.0m, s.CategoryExpenses.Single(c => c.CategoryName == "Casa").Percentage);
        Assert.Equal(25.0m, s.CategoryExpenses.Single(c => c.CategoryName == "Lazer").Percentage);
    }

    [Fact]
    public async Task Receita_nao_entra_nas_categorias_de_despesa()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Income, 5000m, MonthStart().AddDays(9), "Salario", category: t.AddCategory("Renda"));

        Assert.Empty((await Summary(t)).CategoryExpenses);
    }

    [Fact]
    public async Task Pendentes_vencidas_aparecem_como_Overdue_e_pagas_nao_aparecem()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 10m, Dates.Today.AddDays(-2), "Vencida", TransactionStatus.Pending);
        t.AddTransaction(account, TransactionType.Expense, 20m, Dates.Today, "Hoje", TransactionStatus.Pending);
        t.AddTransaction(account, TransactionType.Expense, 30m, Dates.Today.AddDays(-2), "Paga", TransactionStatus.Paid);

        var s = await Summary(t);

        Assert.Equal("Overdue", s.PendingTransactions.Single(x => x.Name == "Vencida").Status);
        Assert.Equal("Pending", s.PendingTransactions.Single(x => x.Name == "Hoje").Status);
        Assert.DoesNotContain(s.PendingTransactions, x => x.Name == "Paga");
    }

    [Fact]
    public async Task Cartao_do_dashboard_reconcilia_parcelas_e_mostra_usado_700()
    {
        using var t = new TestDb();
        t.AddAccount();
        var card = t.AddCard(5000m, 1200m);
        t.AddInstallment(card, null, 100m, 1, 12, Dates.Day15MonthsAgo(5));

        var s = await Summary(t);

        Assert.NotNull(s.ActiveCard);
        Assert.Equal(700.00m, s.ActiveCard!.UsedLimit);
        Assert.Equal(5000.00m, s.ActiveCard.CreditLimit);
        Assert.Equal(100.00m, s.ActiveCard.CurrentInvoice);
    }

    [Fact]
    public async Task Mes_corrente_e_o_de_Sao_Paulo_nao_o_do_servidor_ao_meio_do_mes()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 42m, MonthStart().AddDays(14));
        t.AddTransaction(account, TransactionType.Expense, 1000m, MonthStart(-1).AddDays(14));
        t.AddTransaction(account, TransactionType.Expense, 2000m, MonthStart(1).AddDays(14));

        Assert.Equal(42m, (await Summary(t)).TotalExpense);
    }

    [Fact]
    public async Task Saldo_de_conta_manual_com_zero_e_zero_e_nao_nulo()
    {
        using var t = new TestDb();
        t.AddAccount(balance: 0m);

        var s = await Summary(t);

        Assert.Equal(0m, s.ActiveAccount.Balance);
        Assert.NotNull(s.ActiveAccount.Balance);
    }

    [Fact]
    public async Task Saldo_de_conta_manual_com_valor_e_o_valor_real()
    {
        using var t = new TestDb();
        t.AddAccount(balance: 321.45m);

        Assert.Equal(321.45m, (await Summary(t)).ActiveAccount.Balance);
    }

    [Fact]
    public async Task Saldo_de_cartao_sincronizado_Polp_e_nulo_mesmo_gravado_como_zero()
    {
        using var t = new TestDb();
        t.AddAccount("Cartao Polp", balance: 0m, type: "CREDIT_CARD", creditLimit: 5000m, polpAccountId: Guid.NewGuid());

        Assert.Null((await Summary(t)).ActiveAccount.Balance);
    }

    [Fact]
    public void BankAccountDto_FromEntity_manual_devolve_saldo_e_sincronizado_devolve_nulo()
    {
        var manual = new BankAccount { Name = "M", Balance = -10.50m };
        var synced = new BankAccount { Name = "S", Balance = 99m, PolpAccountId = Guid.NewGuid(), PolpAccountType = "CREDIT_CARD" };
        var polpChecking = new BankAccount { Name = "C", Balance = 55m, PolpAccountId = Guid.NewGuid(), PolpAccountType = "BANK" };

        Assert.Equal(-10.50m, BankAccountDto.FromEntity(manual).Balance);
        Assert.Null(BankAccountDto.FromEntity(synced).Balance);
        Assert.Equal(55m, BankAccountDto.FromEntity(polpChecking).Balance);
    }
}
