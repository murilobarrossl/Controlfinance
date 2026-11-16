namespace ControlFinance.API.DTOs;

public record PayeePixDto(string? Type, string? Key);

public record PayeeBankDto(string? BankCode, string? Branch, string? Account, string? AccountType);

public record PayeePixView(string Type, string KeyMasked);

public record PayeeBankView(string BankCode, string Branch, string AccountMasked, string AccountType);
