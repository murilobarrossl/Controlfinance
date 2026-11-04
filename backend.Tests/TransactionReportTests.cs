using ControlFinance.API.Controllers;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Tests.Support;

namespace ControlFinance.API.Tests;

public class TransactionReportTests
{
    private static readonly DateTime Base = new(2025, 3, 1);

    private static async Task<TransactionReportDto> Report(TestDb t, string? status = null, string? type = null,
        string? search = null, Guid? bankAccountId = null, int? year = null, int? month = null,
        string sortBy = "dueDate", string sortDir = "desc", int page = 1, int pageSize = 25) =>
        Results.OkValue<TransactionReportDto>(await t.Controller(db => new TransactionsController(db))
            .GetReport(status, type, search, bankAccountId, year, month, sortBy, sortDir, page, pageSize));

    private static void BulkAdd(TestDb t, BankAccount account, int count, TransactionType type, decimal amount,
        string name, DateTime firstDay, int dayStep = 0)
    {
        for (var i = 0; i < count; i++)
        {
            t.Db.Transactions.Add(new Transaction
            {
                UserId = t.User.Id, BankAccountId = account.Id, Name = $"{name} {i}", Type = type,
                Status = TransactionStatus.Paid, Amount = amount,
                DueDate = DateTime.SpecifyKind(firstDay.AddMinutes(i * (dayStep == 0 ? 1 : dayStep)), DateTimeKind.Utc)
            });
        }
        t.Db.SaveChanges();
    }

    [Fact]
    public async Task Total_soma_todas_as_2500_linhas_do_filtro_mesmo_acima_do_teto_de_2000_B2()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        BulkAdd(t, account, 2500, TransactionType.Expense, 1.10m, "Compra", Base);
        BulkAdd(t, account, 2100, TransactionType.Income, 3.25m, "Venda", Base);

        var r = await Report(t);

        Assert.Equal(2750.00m, r.TotalExpense);
        Assert.Equal(6825.00m, r.TotalIncome);
        Assert.Equal(4600, r.TotalCount);
    }

    [Fact]
    public async Task Total_com_busca_soma_todas_as_2300_linhas_que_casam_mesmo_acima_do_teto_B2()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        BulkAdd(t, account, 2300, TransactionType.Expense, 2.50m, "Mercado", Base);
        BulkAdd(t, account, 200, TransactionType.Expense, 1m, "Outro", Base);

        var r = await Report(t, search: "mercado");

        Assert.Equal(5750.00m, r.TotalExpense);
        Assert.Equal(0m, r.TotalIncome);
    }

    [Fact]
    public async Task Quantidade_total_com_busca_conta_todas_as_2300_linhas_que_casam_acima_do_teto()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        BulkAdd(t, account, 2300, TransactionType.Expense, 2.50m, "Mercado", Base);
        BulkAdd(t, account, 200, TransactionType.Expense, 1m, "Outro", Base);

        var r = await Report(t, search: "mercado");

        Assert.Equal(2300, r.TotalCount);
    }

    [Fact]
    public async Task Total_com_filtro_de_mes_soma_todas_as_2100_linhas_do_mes_e_ignora_outros_meses()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        BulkAdd(t, account, 2100, TransactionType.Expense, 1.50m, "Marco", new DateTime(2025, 3, 1));
        BulkAdd(t, account, 300, TransactionType.Expense, 100m, "Abril", new DateTime(2025, 4, 1));

        var r = await Report(t, year: 2025, month: 3);

        Assert.Equal(3150.00m, r.TotalExpense);
        Assert.Equal(2100, r.TotalCount);
    }

    [Fact]
    public async Task Total_ordenando_por_valor_tambem_soma_todas_as_linhas_acima_do_teto()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        BulkAdd(t, account, 2500, TransactionType.Expense, 1.10m, "Compra", Base);

        var r = await Report(t, sortBy: "amount", sortDir: "asc");

        Assert.Equal(2750.00m, r.TotalExpense);
    }

    [Fact]
    public async Task Total_exclui_transferencias_entre_contas_proprias_inclusive_com_busca()
    {
        using var t = new TestDb("Igor Luis Lima");
        var account = t.AddAccount();
        var day = new DateTime(2025, 3, 10);
        t.AddTransaction(account, TransactionType.Expense, 40m, day, "Pix enviado mercado");
        t.AddTransaction(account, TransactionType.Expense, 5000m, day, "Pix enviado Transferência enviada|IGOR L LIMA");

        var all = await Report(t);
        var searched = await Report(t, search: "pix");

        Assert.Equal(40m, all.TotalExpense);
        Assert.Equal(40m, searched.TotalExpense);
    }

    [Fact]
    public async Task Filtro_de_mes_inclui_dia_1_as_00h_e_ultimo_instante_e_exclui_dia_1_do_mes_seguinte()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 1m, new DateTime(2025, 3, 1));
        t.AddTransaction(account, TransactionType.Expense, 2m, new DateTime(2025, 4, 1).AddTicks(-1));
        t.AddTransaction(account, TransactionType.Expense, 400m, new DateTime(2025, 4, 1));
        t.AddTransaction(account, TransactionType.Expense, 800m, new DateTime(2025, 3, 1).AddTicks(-1));

        var r = await Report(t, year: 2025, month: 3);

        Assert.Equal(3m, r.TotalExpense);
    }

    [Fact]
    public async Task Filtro_de_dezembro_nao_vaza_para_janeiro_do_ano_seguinte()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 10m, new DateTime(2025, 12, 31, 23, 59, 59));
        t.AddTransaction(account, TransactionType.Expense, 500m, new DateTime(2026, 1, 1));

        var r = await Report(t, year: 2025, month: 12);

        Assert.Equal(10m, r.TotalExpense);
        Assert.Equal(1, r.TotalCount);
    }

    [Fact]
    public async Task Mes_invalido_retorna_bad_request()
    {
        using var t = new TestDb();

        var result = await t.Controller(db => new TransactionsController(db)).GetReport(null, null, null, null, 2025, 13);

        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Filtro_de_tipo_Expense_zera_a_receita_do_total()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Income, 100m, Base);
        t.AddTransaction(account, TransactionType.Expense, 30m, Base);

        var r = await Report(t, type: "Expense");

        Assert.Equal(0m, r.TotalIncome);
        Assert.Equal(30m, r.TotalExpense);
        Assert.Equal(1, r.TotalCount);
    }

    [Fact]
    public async Task Filtro_de_status_Overdue_soma_pendentes_vencidas_e_nao_a_que_vence_hoje()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 10m, Dates.Today.AddDays(-1), "a", TransactionStatus.Pending);
        t.AddTransaction(account, TransactionType.Expense, 20m, Dates.Today, "b", TransactionStatus.Pending);
        t.AddTransaction(account, TransactionType.Expense, 40m, Dates.Today.AddDays(-30), "c", TransactionStatus.Overdue);

        var r = await Report(t, status: "Overdue");

        Assert.Equal(50m, r.TotalExpense);
        Assert.Equal(2, r.TotalCount);
    }

    [Fact]
    public async Task Total_ignora_conta_desativada_e_transacoes_de_outro_usuario()
    {
        using var t = new TestDb();
        var active = t.AddAccount("Ativa");
        var inactive = t.AddAccount("Inativa", active: false);
        t.AddTransaction(active, TransactionType.Expense, 10m, Base);
        t.AddTransaction(inactive, TransactionType.Expense, 999m, Base);
        var other = new User { Name = "Outro", Email = "o@x.com", DocumentHash = "h2", PasswordHash = "x" };
        t.Db.Users.Add(other);
        t.Db.Transactions.Add(new Transaction { UserId = other.Id, Name = "x", Type = TransactionType.Expense, Status = TransactionStatus.Paid, Amount = 888m, DueDate = DateTime.SpecifyKind(Base, DateTimeKind.Utc) });
        await t.Db.SaveChangesAsync();

        var r = await Report(t);

        Assert.Equal(10m, r.TotalExpense);
        Assert.Equal(1, r.TotalCount);
    }

    [Fact]
    public async Task Transacao_sem_conta_entra_no_total()
    {
        using var t = new TestDb();
        t.AddTransaction(null, TransactionType.Expense, 12.34m, Base);

        Assert.Equal(12.34m, (await Report(t)).TotalExpense);
    }

    [Fact]
    public async Task Busca_ignora_maiusculas_e_acentos_pao_acha_Pao()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 8.75m, Base, "Pão de queijo");
        t.AddTransaction(account, TransactionType.Expense, 100m, Base, "Gasolina");

        var r = await Report(t, search: "PAO");

        Assert.Equal(8.75m, r.TotalExpense);
        Assert.Equal(1, r.TotalCount);
    }

    [Fact]
    public async Task Paginacao_pagina_2_de_60_linhas_com_25_por_pagina_traz_25_e_contagem_60()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        BulkAdd(t, account, 60, TransactionType.Expense, 1m, "L", Base);

        var r = await Report(t, page: 2, pageSize: 25);

        Assert.Equal(25, r.Items.Count());
        Assert.Equal(60, r.TotalCount);
        Assert.Equal(60m, r.TotalExpense);
    }

    [Fact]
    public async Task Ultima_pagina_parcial_traz_10_itens_e_contagem_60()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        BulkAdd(t, account, 60, TransactionType.Expense, 1m, "L", Base);

        var r = await Report(t, page: 3, pageSize: 25);

        Assert.Equal(10, r.Items.Count());
        Assert.Equal(60, r.TotalCount);
    }

    [Fact]
    public async Task Paginas_nao_repetem_nem_perdem_linhas_com_a_mesma_data()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        for (var i = 0; i < 30; i++)
            t.AddTransaction(account, TransactionType.Expense, 1m, Base, $"Mesmo dia {i}");

        var p1 = (await Report(t, page: 1, pageSize: 10)).Items.Select(i => i.Id);
        var p2 = (await Report(t, page: 2, pageSize: 10)).Items.Select(i => i.Id);
        var p3 = (await Report(t, page: 3, pageSize: 10)).Items.Select(i => i.Id);

        Assert.Equal(30, p1.Concat(p2).Concat(p3).Distinct().Count());
    }

    [Fact]
    public async Task Ordenar_por_valor_e_numerico_9_10_100_e_nao_textual()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 100m, Base, "c");
        t.AddTransaction(account, TransactionType.Expense, 9m, Base, "a");
        t.AddTransaction(account, TransactionType.Expense, 10m, Base, "b");

        var r = await Report(t, sortBy: "amount", sortDir: "asc");

        Assert.Equal([9m, 10m, 100m], r.Items.Select(i => i.Amount).ToArray());
    }

    [Fact]
    public async Task Valor_negativo_e_zero_nao_quebram_ordenacao_nem_soma()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, -5.5m, Base, "neg");
        t.AddTransaction(account, TransactionType.Expense, 0m, Base, "zero");
        t.AddTransaction(account, TransactionType.Expense, 2m, Base, "pos");

        var r = await Report(t, sortBy: "amount", sortDir: "asc");

        Assert.Equal([-5.5m, 0m, 2m], r.Items.Select(i => i.Amount).ToArray());
        Assert.Equal(-3.5m, r.TotalExpense);
    }

    private static void BulkAddVaryingAmounts(TestDb t, BankAccount account, int count, string name)
    {
        for (var i = 0; i < count; i++)
        {
            t.Db.Transactions.Add(new Transaction
            {
                UserId = t.User.Id, BankAccountId = account.Id, Name = $"{name} {i}", Type = TransactionType.Expense,
                Status = TransactionStatus.Paid, Amount = 1m + i * 0.01m,
                DueDate = DateTime.SpecifyKind(Base.AddMinutes(i), DateTimeKind.Utc)
            });
        }
        t.Db.SaveChanges();
    }

    [Fact]
    public async Task Busca_com_2300_linhas_pagina_21_alem_de_2000_retorna_100_itens()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        BulkAdd(t, account, 2300, TransactionType.Expense, 2.50m, "Mercado", Base);

        var r = await Report(t, search: "mercado", page: 21, pageSize: 100);

        Assert.Equal(100, r.Items.Count());
        Assert.Equal(2300, r.TotalCount);
        Assert.Equal(5750.00m, r.TotalExpense);
    }

    [Fact]
    public async Task Busca_com_2300_linhas_ultima_pagina_23_tem_100_e_pagina_24_vazia_sem_repetir_ids()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        BulkAdd(t, account, 2300, TransactionType.Expense, 2.50m, "Mercado", Base);

        var ids = new List<Guid>();
        for (var page = 1; page <= 23; page++)
            ids.AddRange((await Report(t, search: "mercado", page: page, pageSize: 100)).Items.Select(i => i.Id));
        var beyond = await Report(t, search: "mercado", page: 24, pageSize: 100);

        Assert.Equal(2300, ids.Count);
        Assert.Equal(2300, ids.Distinct().Count());
        Assert.Empty(beyond.Items);
    }

    [Fact]
    public async Task Ordenar_por_valor_com_2300_linhas_fica_crescente_entre_todas_as_paginas_e_total_28738_50()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        BulkAddVaryingAmounts(t, account, 2300, "Item");

        var amounts = new List<decimal>();
        TransactionReportDto last = null!;
        for (var page = 1; page <= 23; page++)
        {
            last = await Report(t, sortBy: "amount", sortDir: "asc", page: page, pageSize: 100);
            amounts.AddRange(last.Items.Select(i => i.Amount));
        }

        Assert.Equal(2300, amounts.Count);
        Assert.Equal(amounts.Order().ToList(), amounts);
        Assert.Equal(1.00m, amounts[0]);
        Assert.Equal(23.99m, amounts[^1]);
        Assert.Equal(28738.50m, last.TotalExpense);
        Assert.Equal(2300, last.TotalCount);
    }

    [Fact]
    public async Task Ordenar_por_valor_decrescente_com_2300_linhas_a_pagina_1_traz_os_100_maiores()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        BulkAddVaryingAmounts(t, account, 2300, "Item");

        var r = await Report(t, sortBy: "amount", sortDir: "desc", page: 1, pageSize: 100);

        Assert.Equal(23.99m, r.Items.First().Amount);
        Assert.Equal(23.00m, r.Items.Last().Amount);
    }
}
