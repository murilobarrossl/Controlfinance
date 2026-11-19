namespace ControlFinance.API.DTOs;

public record TaxPaymentDto(
    Guid Id, DateTime Date, decimal Amount, string? Authority, string Evidence,
    string AccountName, string? AccountNumber, string? InstitutionName);

public record TaxMonthTotalDto(int Month, decimal Total, int Count);

public record TaxOverviewDto(
    int Year, int Month, decimal Total, int Count,
    List<TaxPaymentDto> Payments, List<TaxMonthTotalDto> Months);
