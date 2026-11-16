using System.Reflection;
using ControlFinance.API.Controllers;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Tests;

public class TransactionEditingTests
{
    private static TransactionsController Controller(TestDb t) => t.Controller(db => new TransactionsController(db));

    private static Transaction AddImported(TestDb t, BankAccount account, decimal amount = 87.30m)
    {
        var tx = t.AddTransaction(account, TransactionType.Expense, amount, Dates.Today, "PADARIA CENTRAL");
        tx.Description = Guid.NewGuid().ToString();
        tx.BankDescription = "COMPRA CARTAO PADARIA CENTRAL";
        tx.CounterpartyName = "Padaria Central Ltda";
        t.Db.SaveChanges();
        return tx;
    }

    [Fact]
    public void Nenhuma_rota_cria_ou_sobrescreve_transacao()
    {
        // Só TransactionsController responde em api/transactions; qualquer POST ou PUT novo aqui
        // precisa ser uma decisão consciente, não um retorno silencioso do cadastro manual.
        var writes = typeof(TransactionsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .Select(a => (Method: a.HttpMethods.Single(), a.Template))
            .Where(r => r.Method != "GET")
            .Select(r => $"{r.Method} {r.Template}")
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["DELETE {id}", "PUT {id}/details", "PUT {id}/fixed"], writes);
    }

    [Fact]
    public async Task Personalizar_muda_so_nome_e_categoria_e_preserva_os_dados_do_banco()
    {
        using var t = new TestDb();
        var account = t.AddAccount("Conta PJ");
        var category = t.AddCategory("Insumos");
        var tx = AddImported(t, account);
        var polpId = tx.Description;

        Results.OkValue<TransactionDto>(await Controller(t).SetDetails(tx.Id, new SetTransactionDetailsDto("  Pão da loja  ", category.Id)));

        await using var db = t.NewContext();
        var saved = db.Transactions.Single(x => x.Id == tx.Id);
        Assert.Equal("Pão da loja", saved.Name);
        Assert.Equal(category.Id, saved.CategoryId);
        Assert.Equal(87.30m, saved.Amount);
        Assert.Equal(TransactionType.Expense, saved.Type);
        Assert.Equal(tx.DueDate, saved.DueDate);
        Assert.Equal(account.Id, saved.BankAccountId);
        Assert.Equal(polpId, saved.Description);
        Assert.Equal("COMPRA CARTAO PADARIA CENTRAL", saved.BankDescription);
        Assert.Equal("Padaria Central Ltda", saved.CounterpartyName);
        Assert.Equal(PolpMovementKind.None, saved.PolpMovementKind);
    }

    [Fact]
    public async Task Movimentacao_importada_nao_pode_ser_apagada()
    {
        using var t = new TestDb();
        var tx = AddImported(t, t.AddAccount());

        Assert.Equal(409, Http.Status(await Controller(t).Delete(tx.Id)));

        await using var db = t.NewContext();
        Assert.True(await db.Transactions.AnyAsync(x => x.Id == tx.Id));
    }

    [Fact]
    public async Task Lancamento_manual_antigo_ainda_pode_ser_apagado()
    {
        using var t = new TestDb();
        var legacy = t.AddTransaction(t.AddAccount(), TransactionType.Expense, 15m, Dates.Today, "Anotado à mão");

        Assert.Equal(204, Http.Status(await Controller(t).Delete(legacy.Id)));

        await using var db = t.NewContext();
        Assert.False(await db.Transactions.AnyAsync(x => x.Id == legacy.Id));
    }
}

public class DailyStatementPagingTests
{
    private static TransactionsController Controller(TestDb t) => t.Controller(db => new TransactionsController(db));

    private static DateTime MonthStart() => new(Dates.Today.Year, Dates.Today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<TransactionReportDto> Page(TestDb t, int page, int pageSize = 25, bool excludeTransfers = true) =>
        Results.OkValue<TransactionReportDto>(await Controller(t).GetReport(
            null, null, null, null, Dates.Today.Year, Dates.Today.Month, "dueDate", "desc", page, pageSize, excludeTransfers));

    [Fact]
    public async Task Paginas_nao_repetem_nem_perdem_movimentacoes_e_o_total_do_dia_e_do_dia_inteiro()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        var busyDay = MonthStart().AddDays(1);
        var otherDay = MonthStart();
        // 30 lançamentos no mesmo dia: a página 1 corta o dia no meio.
        for (var i = 0; i < 30; i++)
            t.AddTransaction(account, i % 3 == 0 ? TransactionType.Income : TransactionType.Expense, 10.10m + i, busyDay, $"Mov {i}");
        t.AddTransaction(account, TransactionType.Expense, 5.05m, otherDay, "Outro dia");

        var first = await Page(t, 1);
        var second = await Page(t, 2);

        Assert.Equal(31, first.TotalCount);
        Assert.Equal(25, first.Items.Count());
        Assert.Equal(6, second.Items.Count());
        var ids = first.Items.Concat(second.Items).Select(i => i.Id).ToList();
        Assert.Equal(31, ids.Distinct().Count());

        await using var db = t.NewContext();
        var busy = db.Transactions.AsEnumerable().Where(x => x.DueDate == busyDay).ToList();
        var busyIncome = busy.Where(x => x.Type == TransactionType.Income).Sum(x => x.Amount);
        var busyExpense = busy.Where(x => x.Type == TransactionType.Expense).Sum(x => x.Amount);
        foreach (var page in new[] { first, second })
        {
            var day = Assert.Single(page.DayTotals, d => d.Date == busyDay);
            Assert.Equal(busyIncome, day.Income);
            Assert.Equal(busyExpense, day.Expense);
            Assert.Equal(busyIncome, page.TotalIncome);
            Assert.Equal(busyExpense + 5.05m, page.TotalExpense);
        }
    }

    [Fact]
    public async Task Transferencias_entre_contas_proprias_saem_da_lista_e_da_contagem_sem_mudar_os_totais()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        var day = MonthStart();
        t.AddTransaction(account, TransactionType.Income, 100m, day, "Venda");
        t.AddTransaction(account, TransactionType.Expense, 400m, day, "Para a poupança", movementKind: PolpMovementKind.OwnTransfer);
        t.AddTransaction(account, TransactionType.Expense, 900m, day, "Fatura", movementKind: PolpMovementKind.CardBillPayment);

        var statement = await Page(t, 1);
        var report = await Page(t, 1, excludeTransfers: false);

        Assert.Equal(1, statement.TotalCount);
        Assert.Equal("Venda", Assert.Single(statement.Items).Name);
        Assert.Equal(3, report.TotalCount);
        Assert.Equal(report.TotalIncome, statement.TotalIncome);
        Assert.Equal(report.TotalExpense, statement.TotalExpense);
        Assert.Equal(0m, statement.TotalExpense);
        Assert.Equal(0m, Assert.Single(statement.DayTotals).Expense);
    }

    [Fact]
    public async Task Tamanho_de_pagina_e_limitado_no_servidor()
    {
        using var t = new TestDb();
        var account = t.AddAccount();
        for (var i = 0; i < 120; i++)
            t.AddTransaction(account, TransactionType.Expense, 1m, MonthStart(), $"Mov {i}");

        Assert.Equal(100, (await Page(t, 1, pageSize: 10_000)).Items.Count());
        Assert.Single((await Page(t, 1, pageSize: 0)).Items);
        Assert.Equal(25, (await Page(t, -5)).Items.Count());
    }

    [Theory]
    [InlineData(PolpMovementKind.None)]
    [InlineData(PolpMovementKind.CardBillPayment)]
    [InlineData(PolpMovementKind.OwnTransfer)]
    public async Task Filtro_em_SQL_segue_a_mesma_regra_de_IsSelfTransfer(PolpMovementKind kind)
    {
        using var t = new TestDb();
        t.AddTransaction(t.AddAccount(), TransactionType.Expense, 1m, MonthStart(), "Mov", movementKind: kind);

        var kept = await t.NewContext().Transactions.WithoutSelfTransfers().AnyAsync();

        Assert.Equal(!TransferDetection.IsSelfTransfer(kind), kept);
    }
}
