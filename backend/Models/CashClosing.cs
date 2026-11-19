namespace ControlFinance.API.Models;

// Fechamento é registro imutável: reabrir só carimba ReopenedAt, e fechar de novo cria outra linha.
// Items é o retrato das transações no momento do fechamento; sem FK para Transactions, porque o
// sync corrige e a desconexão apaga linhas, e é justamente essa diferença que vira ajuste.
public class CashClosing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateOnly Day { get; set; }
    /// <summary>Guid.Empty = todas as contas.</summary>
    public Guid ScopeAccountId { get; set; }
    public decimal? OpeningBalance { get; set; }
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal? ClosingBalance { get; set; }
    public int TransactionCount { get; set; }
    public DateTime ClosedAt { get; set; } = DateTime.UtcNow;
    public Guid ClosedByUserId { get; set; }
    public DateTime? ReopenedAt { get; set; }
    public Guid? ReopenedByUserId { get; set; }
    public int Version { get; set; }
    public List<CashClosingItem> Items { get; set; } = [];
}

public class CashClosingItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClosingId { get; set; }
    public Guid TransactionId { get; set; }
    public TransactionType Type { get; set; }
    public PolpMovementKind MovementKind { get; set; }
    public decimal Amount { get; set; }
}
