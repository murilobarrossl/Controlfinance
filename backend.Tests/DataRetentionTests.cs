using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Tests;

// A retenção só pode alcançar o histórico que veio do banco. Lançamento digitado pelo usuário é
// dado dele e não tem prazo de validade, mesmo quando vinculado a uma conta sincronizada.
public class DataRetentionTests
{
    private static readonly DateTime Cutoff = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Old = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static BankAccount AddAccount(TestDb t, bool synced)
    {
        var account = new BankAccount
        {
            UserId = t.User.Id,
            Name = synced ? "Conta sincronizada" : "Carteira",
            PolpAccountId = synced ? Guid.NewGuid() : null,
        };
        t.Db.BankAccounts.Add(account);
        t.Db.SaveChanges();
        return account;
    }

    private static Transaction AddTransaction(TestDb t, BankAccount account, string? description)
    {
        var transaction = new Transaction
        {
            UserId = t.User.Id,
            BankAccountId = account.Id,
            Name = "Lançamento",
            Description = description,
            Type = TransactionType.Expense,
            Status = TransactionStatus.Paid,
            Amount = 10m,
            DueDate = Old,
        };
        t.Db.Transactions.Add(transaction);
        t.Db.SaveChanges();
        return transaction;
    }

    private static List<Guid> Purgeable(TestDb t) =>
        t.Db.Transactions.Where(DataRetentionPolicy.PurgeableSyncedHistory(Cutoff)).Select(x => x.Id).ToList();

    [Fact]
    public void Historico_importado_antigo_de_conta_sincronizada_pode_ser_apagado()
    {
        using var t = new TestDb();
        var imported = AddTransaction(t, AddAccount(t, synced: true), Guid.NewGuid().ToString());

        Assert.Equal([imported.Id], Purgeable(t));
    }

    [Fact]
    public void Lancamento_manual_em_conta_sincronizada_nao_e_apagado()
    {
        using var t = new TestDb();
        AddTransaction(t, AddAccount(t, synced: true), description: null);

        Assert.Empty(Purgeable(t));
    }

    [Fact]
    public void Lancamento_de_conta_manual_nao_e_apagado()
    {
        using var t = new TestDb();
        AddTransaction(t, AddAccount(t, synced: false), description: null);

        Assert.Empty(Purgeable(t));
    }

    [Fact]
    public void Transacao_pendente_e_recente_nao_sao_apagadas()
    {
        using var t = new TestDb();
        var account = AddAccount(t, synced: true);

        var pending = AddTransaction(t, account, Guid.NewGuid().ToString());
        pending.Status = TransactionStatus.Pending;
        var recent = AddTransaction(t, account, Guid.NewGuid().ToString());
        recent.DueDate = Cutoff.AddDays(1);
        t.Db.SaveChanges();

        Assert.Empty(Purgeable(t));
    }
}
