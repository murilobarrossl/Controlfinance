namespace ControlFinance.API.DTOs;

public record BalancePointDto(DateOnly Date, decimal Balance, DateTime ObservedAt);

// Pontos são observações reais de saldo disponível na data do banco, nunca fechamentos
// calculados. HistoryStart indica a primeira observação guardada, não cobertura de extrato.
public record BalanceHistoryDto(
    bool Available,
    string? Reason,
    string? AccountName,
    DateTime? BalanceSyncedAt,
    DateOnly? HistoryStart,
    IReadOnlyList<BalancePointDto> Points);
