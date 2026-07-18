using System.ComponentModel.DataAnnotations;
using ControlFinance.API.Models;
using ControlFinance.API.Services;

namespace ControlFinance.API.DTOs;

// ── BANK ACCOUNT ──────────────────────────────────────
public record CreateBankAccountDto(
    [Required(ErrorMessage = "Nome é obrigatório."), MaxLength(100)] string Name,
    [MaxLength(20)] string? BankCode,
    [Range(-1_000_000_000, 1_000_000_000, ErrorMessage = "Saldo fora da faixa permitida.")] decimal Balance
);
public record BankAccountDto(Guid Id, string Name, string? BankCode, decimal Balance, bool IsActive);

// ── CATEGORY ──────────────────────────────────────────
public record CreateCategoryDto(
    [Required(ErrorMessage = "Nome é obrigatório."), MaxLength(80)] string Name,
    [MaxLength(20)] string? Color
);
public record CategoryDto(Guid Id, string Name, string? Color);

// ── TRANSACTION ───────────────────────────────────────
public record CreateTransactionDto(
    [Required(ErrorMessage = "Nome é obrigatório."), MaxLength(150)] string Name,
    [MaxLength(500)] string? Description,
    string Type,         // "Income" | "Expense"
    string Status,       // "Pending" | "Paid" | "Overdue"
    [Range(0.01, double.MaxValue, ErrorMessage = "Valor deve ser maior que zero.")] decimal Amount,
    DateTime DueDate,
    Guid? BankAccountId,
    Guid? CategoryId,
    bool IsFixed = false
);

public record SetTransactionFixedDto(bool IsFixed);

public record TransactionReportDto(
    IEnumerable<TransactionDto> Items,
    int TotalCount,
    decimal TotalIncome,
    decimal TotalExpense
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
    string? BankAccountName,
    bool IsFixed,
    bool IsTransfer = false
)
{
    // ownerName só é conhecido em quem lista várias transações (GetAll/GetReport/GetSummary), que
    // já busca o nome do usuário uma vez; nula aqui deixa IsTransfer em false (comportamento atual).
    public static TransactionDto FromEntity(Transaction t, string? ownerName = null) => new(
        t.Id, t.Name, t.Description,
        t.Type.ToString(), t.Status.ToString(),
        t.Amount, t.DueDate, t.PaidAt,
        t.Category?.Name,
        t.BankAccount?.Name,
        t.IsFixed,
        TransferDetection.IsSelfTransfer(t.Name, t.Category?.Name, ownerName)
    );
}

// ── CREDIT CARD ───────────────────────────────────────
public record CreateCreditCardDto(
    [Required(ErrorMessage = "Nome é obrigatório."), MaxLength(80)] string Name,
    [MaxLength(50)] string? Brand,
    [Range(0, double.MaxValue, ErrorMessage = "Limite não pode ser negativo.")] decimal CreditLimit,
    [Range(1, 31, ErrorMessage = "Dia de fechamento deve ser entre 1 e 31.")] int ClosingDay,
    [Range(1, 31, ErrorMessage = "Dia de vencimento deve ser entre 1 e 31.")] int DueDay
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
    Guid? CreditCardId,
    [Required(ErrorMessage = "Descrição é obrigatória."), MaxLength(150)] string Description,
    [Range(0.01, double.MaxValue, ErrorMessage = "Valor total deve ser maior que zero.")] decimal TotalAmount,
    [Range(1, int.MaxValue, ErrorMessage = "Total de parcelas deve ser maior que zero.")] int TotalInstallments,
    [Range(1, int.MaxValue, ErrorMessage = "Parcela atual deve ser maior que zero.")] int CurrentInstallment,
    [Range(0.01, double.MaxValue, ErrorMessage = "Valor da parcela deve ser maior que zero.")] decimal InstallmentAmount,
    DateTime NextDueDate
);

public record InstallmentDto(
    Guid Id,
    string Description,
    decimal InstallmentAmount,
    int CurrentInstallment,
    int TotalInstallments,
    DateTime NextDueDate,
    string? CreditCardName
)
{
    public static InstallmentDto FromEntity(Installment i) => new(
        i.Id, i.Description, i.InstallmentAmount,
        i.CurrentInstallment, i.TotalInstallments, i.NextDueDate,
        i.CreditCard?.Name
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
