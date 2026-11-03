using ControlFinance.API.Controllers;
using ControlFinance.API.DTOs;
using ControlFinance.API.Migrations;
using ControlFinance.API.Models;
using ControlFinance.API.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace ControlFinance.API.Tests;

public class LatestFixesTests
{
    [Fact]
    public void Down_dos_indices_nao_remove_indices_que_ja_existiam_antes_da_migracao()
    {
        var migration = new AddPerformanceIndexes();
        var down = string.Join("\n", migration.DownOperations.OfType<SqlOperation>().Select(o => o.Sql));
        var up = string.Join("\n", migration.UpOperations.OfType<SqlOperation>().Select(o => o.Sql));

        Assert.DoesNotContain("IX_Installments_BankAccountId", down);
        Assert.DoesNotContain("IX_Transactions_UserId_BankAccountId_DueDate", down);
        Assert.Contains("DROP INDEX IF EXISTS \"IX_BankAccounts_UserId_IsActive\"", down);
        Assert.Contains("DROP INDEX IF EXISTS \"IX_Installments_UserId\"", down);
        Assert.Equal(4, migration.UpOperations.OfType<SqlOperation>().Count(o => o.Sql.StartsWith("CREATE INDEX IF NOT EXISTS")));
        Assert.Contains("IX_Installments_BankAccountId", up);
    }

    [Fact]
    public async Task Detalhes_trazem_todos_os_2300_lancamentos_do_estabelecimento_no_ano_sem_teto()
    {
        using var t = new TestDb("Igor Luis Lima");
        var account = t.AddAccount("A");
        var mercado = t.AddCategory("Mercado");
        var lazer = t.AddCategory("Lazer");
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 2300; i++)
            t.Db.Transactions.Add(Expense(t, account, mercado, $"Supermercado {(i % 12 + 1):00}/12", 2.50m, start.AddMinutes(i * 200)));
        t.Db.Transactions.Add(Expense(t, account, mercado, "Padaria", 9.99m, start.AddDays(3)));
        t.Db.Transactions.Add(Expense(t, account, lazer, "Supermercado 01/12", 7.00m, start.AddDays(4)));
        t.Db.Transactions.Add(Expense(t, account, mercado, "Supermercado 01/12", 5.00m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        t.Db.Transactions.Add(Expense(t, account, null, "Loja sem categoria", 50.00m, start.AddDays(5)));
        await t.Db.SaveChangesAsync();

        var controller = t.Controller(db => new TransactionsController(db));
        var result = await controller.GetDetails(account.Id, "2025-01-01", "2026-01-01", "Expense", "Mercado", "Supermercado", CancellationToken.None);
        var items = Assert.IsAssignableFrom<IEnumerable<TransactionDto>>(Assert.IsType<OkObjectResult>(result).Value).ToList();

        Assert.Equal(2300, items.Count);
        Assert.Equal(5750.00m, items.Sum(i => i.Amount));
        Assert.All(items, i => Assert.Equal("Mercado", i.CategoryName));
        Assert.True(items.Zip(items.Skip(1)).All(p => p.First.DueDate >= p.Second.DueDate));

        var uncategorized = await controller.GetDetails(account.Id, "2025-01-01", "2026-01-01", "Expense", "Sem categoria", null, CancellationToken.None);
        var semCategoria = Assert.Single(Assert.IsAssignableFrom<IEnumerable<TransactionDto>>(Assert.IsType<OkObjectResult>(uncategorized).Value));
        Assert.Equal(50.00m, semCategoria.Amount);
    }

    [Fact]
    public async Task Saldo_nao_informado_pelo_banco_fica_nulo_e_saldo_zero_informado_continua_zero()
    {
        using var t = new TestDb();
        var semSaldo = new BankAccount { UserId = t.User.Id, Name = "Corrente Polp", PolpAccountId = Guid.NewGuid(), PolpAccountType = "BANK", IsActive = true };
        t.Db.BankAccounts.Add(semSaldo);
        var zero = t.AddAccount("Manual", balance: 0m);
        await t.Db.SaveChangesAsync();

        using var reread = t.NewContext();
        var stored = await reread.BankAccounts.SingleAsync(b => b.Id == semSaldo.Id);
        var storedZero = await reread.BankAccounts.SingleAsync(b => b.Id == zero.Id);

        Assert.Null(stored.Balance);
        Assert.Null(BankAccountDto.FromEntity(stored).Balance);
        Assert.Equal(0m, BankAccountDto.FromEntity(storedZero).Balance);
    }

    private static Transaction Expense(TestDb t, BankAccount account, Category? category, string name, decimal amount, DateTime dueDate) => new()
    {
        UserId = t.User.Id, BankAccountId = account.Id, CategoryId = category?.Id, Name = name,
        Type = TransactionType.Expense, Status = TransactionStatus.Paid, Amount = amount,
        DueDate = DateTime.SpecifyKind(dueDate, DateTimeKind.Utc)
    };
}
