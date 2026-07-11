using System.ComponentModel.DataAnnotations;
using ControlFinance.API.Models;

namespace ControlFinance.API.DTOs;

// ── BANK ACCOUNT ──────────────────────────────────────
public record CreateBankAccountDto(
    [property: Required(ErrorMessage = "Nome é obrigatório."), MaxLength(100)] string Name,
    string? BankCode,
    decimal Balance
);
public record BankAccountDto(Guid Id, string Name, string? BankCode, decimal Balance, bool IsActive);

// ── CATEGORY ──────────────────────────────────────────
public record CreateCategoryDto(
    [property: Required(ErrorMessage = "Nome é obrigatório."), MaxLength(80)] string Name,
    string? Color
);
public record CategoryDto(Guid Id, string Name, string? Color);

// ── TRANSACTION ───────────────────────────────────────
public record CreateTransactionDto(
    [property: Required(ErrorMessage = "Nome é obrigatório."), MaxLength(150)] string Name,
    string? Description,
    string Type,         // "Income" | "Expense"
    string Status,       // "Pending" | "Paid" | "Overdue"
    [property: Range(0.01, double.MaxValue, ErrorMessage = "Valor deve ser maior que zero.")] decimal Amount,
    DateTime DueDate,
    Guid? BankAccountId,
    Guid? CategoryId
);

public record TransactionDto(
    Guid Id,
    string Name,
    string? Description,
    string Type,
    string Status,
    decimal Amount,
    DateTime DueDate,
    DateTime? PaidAt,
    string? CategoryName,
    string? BankAccountName
)
{
    public static TransactionDto FromEntity(Transaction t) => new(
        t.Id, t.Name, t.Description,
        t.Type.ToString(), t.Status.ToString(),
        t.Amount, t.DueDate, t.PaidAt,
        t.Category?.Name,
        t.BankAccount?.Name
    );
}

// ── CREDIT CARD ───────────────────────────────────────
public record CreateCreditCardDto(
    [property: Required(ErrorMessage = "Nome é obrigatório."), MaxLength(80)] string Name,
    string? Brand,
    [property: Range(0, double.MaxValue, ErrorMessage = "Limite não pode ser negativo.")] decimal CreditLimit,
    [property: Range(1, 31, ErrorMessage = "Dia de fechamento deve ser entre 1 e 31.")] int ClosingDay,
    [property: Range(1, 31, ErrorMessage = "Dia de vencimento deve ser entre 1 e 31.")] int DueDay
);

public record CreditCardDto(
    Guid Id,
    string Name,
    string? Brand,
    decimal CreditLimit,
    decimal UsedLimit,
    decimal AvailableLimit,
    int ClosingDay,
    int DueDay
)
{
    public static CreditCardDto FromEntity(CreditCard c) => new(
        c.Id, c.Name, c.Brand,
        c.CreditLimit, c.UsedLimit,
        c.CreditLimit - c.UsedLimit,
        c.ClosingDay, c.DueDay
    );
}

// ── INSTALLMENT ───────────────────────────────────────
public record CreateInstallmentDto(
    Guid CreditCardId,
    [property: Required(ErrorMessage = "Descrição é obrigatória."), MaxLength(150)] string Description,
    [property: Range(0.01, double.MaxValue, ErrorMessage = "Valor total deve ser maior que zero.")] decimal TotalAmount,
    [property: Range(1, int.MaxValue, ErrorMessage = "Total de parcelas deve ser maior que zero.")] int TotalInstallments,
    [property: Range(1, int.MaxValue, ErrorMessage = "Parcela atual deve ser maior que zero.")] int CurrentInstallment,
    [property: Range(0.01, double.MaxValue, ErrorMessage = "Valor da parcela deve ser maior que zero.")] decimal InstallmentAmount,
    DateTime NextDueDate
);

public record InstallmentDto(
    Guid Id,
    string Description,
    decimal InstallmentAmount,
    int CurrentInstallment,
    int TotalInstallments,
    DateTime NextDueDate
)
{
    public static InstallmentDto FromEntity(Installment i) => new(
        i.Id, i.Description, i.InstallmentAmount,
        i.CurrentInstallment, i.TotalInstallments, i.NextDueDate
    );
}

// ── DASHBOARD ─────────────────────────────────────────
public record DashboardSummaryDto(
    BankAccountDto ActiveAccount,
    decimal TotalIncome,
    decimal TotalExpense,
    IEnumerable<TransactionDto> PendingTransactions,
    IEnumerable<CategoryExpenseDto> CategoryExpenses,
    CreditCardDashboardDto? ActiveCard
);

public record CategoryExpenseDto(string CategoryName, decimal Amount, decimal Percentage);

public record CreditCardDashboardDto(
    CreditCardDto Card,
    decimal CurrentInvoice,
    DateTime InvoiceDueDate,
    IEnumerable<InstallmentDto> Installments
);
