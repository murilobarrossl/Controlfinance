namespace ControlFinance.API.DTOs;

public record RecurringChargeDto(Guid Id, DateOnly Date, decimal Amount, string? CardNumber);

public record RecurringExpenseDto(
    string Id, string Name, Guid BankAccountId, string AccountName, string? AccountNumber,
    string? InstitutionName, bool IsCard, string Source, int? PeriodMonths,
    decimal LastAmount, DateOnly LastChargeDate, DateOnly? NextEstimatedDate,
    string Activity, int ChargeCount, DateTime? DataUpdatedAt, string? PaymentMethod);

public record RecurringExpenseOverviewDto(
    int Year, int Month, decimal ChargedTotal, int ServiceCount,
    int CardsWithoutDetection, IReadOnlyList<RecurringExpenseDto> Items, int TotalCount, int Page, int PageSize);

public record RecurringExpenseDetailDto(RecurringExpenseDto Expense, IReadOnlyList<RecurringChargeDto> Charges);
