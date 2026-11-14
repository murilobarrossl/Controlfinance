namespace ControlFinance.API.Models;

public enum TransactionType { Income, Expense }
public enum TransactionStatus { Pending, Paid, Overdue }
public enum PolpMovementKind { None, CardBillPayment, OwnTransfer }

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

    /// <summary>Marca um gasto recorrente (assinatura, mensalidade). Não se propaga sozinho pra
    /// novas transações sincronizadas do banco: precisa ser marcado de novo a cada mês.</summary>
    public bool IsFixed { get; set; }

    public PolpMovementKind PolpMovementKind { get; set; }

    /// <summary>Quando o lembrete de vencimento por e-mail foi enviado (null = ainda não enviado).</summary>
    public DateTime? ReminderSentAt { get; set; }

    // Dados do banco, gravados só pelo sync (nunca editáveis). CPF e agência da contraparte,
    // MCC e textos livres da Polp não são guardados: nenhuma tela precisa deles.
    /// <summary>Nulo quando o banco não manda horário (só data ou "T00:00:00").</summary>
    public DateTime? BankDateTime { get; set; }
    public string? BankOperationType { get; set; }
    public string? BankDescription { get; set; }
    public string? CounterpartyName { get; set; }
    public string? CounterpartyAlias { get; set; }
    public string? CounterpartyCnpj { get; set; }
    public string? CounterpartyPersonType { get; set; }
    public string? CounterpartyBankCode { get; set; }
    public string? CounterpartyAccount { get; set; }
    public string? CardNumber { get; set; }
    public int? InstallmentNumber { get; set; }
    public int? InstallmentTotal { get; set; }
    public decimal? OriginalAmount { get; set; }
    public string? OriginalCurrency { get; set; }
    public string? BillMonth { get; set; }
}
