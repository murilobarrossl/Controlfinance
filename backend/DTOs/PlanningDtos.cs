using System.ComponentModel.DataAnnotations;
using ControlFinance.API.Models;

namespace ControlFinance.API.DTOs;

// Reserva e metas não guardam valor: o saldo vem sempre dos investimentos vinculados.
public record EmergencyReserveDto(Guid? BankAccountId, List<Guid> ExcludedAutoIds, List<Guid> InvestmentIds, List<Guid> ReservedBalanceIds);

public record SavingsGoalDto(Guid Id, string Name, decimal TargetAmount, DateOnly? Deadline, DateTime CreatedAt,
    List<Guid> InvestmentIds, List<Guid> ReservedBalanceIds);

public record AllocationChangeDto(Guid? InvestmentId, Guid? ReservedBalanceId, bool Enabled);

public record InvestmentRateProfileDto(Guid BankAccountId, string Kind, decimal Value, bool IncomeTaxExempt, DateTime UpdatedAt)
{
    public static InvestmentRateProfileDto FromEntity(InvestmentRateProfile p) =>
        new(p.BankAccountId, p.Kind, p.Value, p.IncomeTaxExempt, p.UpdatedAt);
}

public record SetReserveAccountDto(Guid? BankAccountId);

public record SetInvestmentRateDto(
    [Required(ErrorMessage = "Tipo de rendimento é obrigatório.")] string Kind,
    decimal Value,
    bool IncomeTaxExempt
);

public record PlanningDto(EmergencyReserveDto Reserve, List<SavingsGoalDto> Goals, List<InvestmentRateProfileDto> RateProfiles);

public record CreateSavingsGoalDto(
    [Required(ErrorMessage = "Nome da meta é obrigatório."), MaxLength(100, ErrorMessage = "Nome da meta pode ter até 100 caracteres.")] string Name,
    [Range(0.01, 999_999_999_999.99, ErrorMessage = "Valor alvo inválido.")] decimal TargetAmount,
    DateOnly? Deadline
);

public record LegacyGoalDto(Guid Id, string? Name, decimal TargetAmount, DateOnly? Deadline, DateTime? CreatedAt);

public record ImportLegacyPlanningDto(List<LegacyGoalDto>? Goals);

public record ImportLegacyPlanningResultDto(PlanningDto Planning, List<Guid> StoredGoalIds);
