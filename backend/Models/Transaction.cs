namespace ControlFinance.API.Models;

public enum TransactionType { Income, Expense }
public enum TransactionStatus { Pending, Paid, Overdue }

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TransactionType Type { get; set; }
    public TransactionStatus Status { get; set; }
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Quando o lembrete de vencimento por e-mail foi enviado (null = ainda não enviado).</summary>
    public DateTime? ReminderSentAt { get; set; }
}
