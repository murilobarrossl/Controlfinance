namespace ControlFinance.API.DTOs;

// ── BANK ACCOUNT ──────────────────────────────────────
public record CreateBankAccountDto(string Name, string? BankCode, decimal Balance);
public record BankAccountDto(Guid Id, string Name, string? BankCode, decimal Balance, bool IsActive);

// ── CATEGORY ──────────────────────────────────────────
public record CreateCategoryDto(string Name, string? Color);
public record CategoryDto(Guid Id, string Name, string? Color);

// ── TRANSACTION ───────────────────────────────────────
public record CreateTransactionDto(
    string Name,
    string? Description,
    string Type,         // "Income" | "Expense"
    string Status,       // "Pending" | "Paid" | "Overdue"
    decimal Amount,
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
);

// ── CREDIT CARD ───────────────────────────────────────
public record CreateCreditCardDto(
    string Name,
    string? Brand,
    decimal CreditLimit,
    int ClosingDay,
    int DueDay
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
);

// ── INSTALLMENT ───────────────────────────────────────
public record CreateInstallmentDto(
    Guid CreditCardId,
    string Description,
    decimal TotalAmount,
    int TotalInstallments,
    int CurrentInstallment,
    decimal InstallmentAmount,
    DateTime NextDueDate
);

public record InstallmentDto(
    Guid Id,
    string Description,
    decimal InstallmentAmount,
    int CurrentInstallment,
    int TotalInstallments,
    DateTime NextDueDate
);

// ── DASHBOARD ─────────────────────────────────────────
public record DashboardSummaryDto(
    BankAccountDto ActiveAccount,
    decimal TotalIncome,
    decimal TotalExpense,
    IEnumerable<TransactionDto> PendingTransactions,
    IEnumerable<TransactionDto> PaidTransactions,
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
