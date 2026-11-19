namespace ControlFinance.API.DTOs;

public record CashDayTotalsDto(decimal Income, decimal Expense, int Count, decimal? OpeningBalance, decimal? ClosingBalance);

public record CashClosingDto(
    Guid Id,
    decimal Income,
    decimal Expense,
    int Count,
    decimal? OpeningBalance,
    decimal? ClosingBalance,
    DateTime ClosedAt,
    string? ClosedBy,
    DateTime? ReopenedAt,
    string? ReopenedBy,
    int Version);

public record CashAdjustmentDto(
    Guid TransactionId,
    string Kind,
    string? Name,
    string? OriginalType,
    decimal? OriginalAmount,
    bool OriginalCounted,
    string? CurrentType,
    decimal? CurrentAmount,
    bool CurrentCounted,
    DateTime? ImportedAt,
    decimal IncomeDelta,
    decimal ExpenseDelta);

public record CashBalanceReadingDto(string AccountName, DateTime? OpeningReadAt, DateTime? ClosingReadAt);

public record CashReconciliationDto(
    decimal? BalanceChange,
    decimal CountedNet,
    decimal ExcludedNet,
    decimal? Unexplained,
    int PendingCount,
    int OutsideBalanceCount,
    IReadOnlyList<CashBalanceReadingDto> Readings);

public record CashDayDto(
    DateOnly Day,
    bool IsToday,
    bool Closable,
    bool Reopenable,
    CashDayTotalsDto Current,
    CashClosingDto? Closing,
    IReadOnlyList<CashAdjustmentDto> Adjustments,
    CashReconciliationDto Reconciliation);

public record CloseCashDayDto(string? Date, Guid? BankAccountId, decimal ExpectedIncome, decimal ExpectedExpense, int ExpectedCount);

public record ReopenCashDayDto(int Version);

public record CashAdjustedDayDto(DateOnly Day, decimal IncomeDelta, decimal ExpenseDelta, int Count);
