using System.ComponentModel.DataAnnotations;
using ControlFinance.API.Models;
using ControlFinance.API.Services;

namespace ControlFinance.API.DTOs;

public record EmergencyReserveDto(decimal CurrentAmount, Guid? BankAccountId, DateTime UpdatedAt,
    Guid? ImportedInvestmentId = null, Guid? ImportedReservedBalanceId = null, List<Guid>? ExcludedAutoIds = null)
{
    public static EmergencyReserveDto FromEntity(EmergencyReserve r) => new(r.CurrentAmount, r.BankAccountId, r.UpdatedAt,
        r.ImportedInvestmentId, r.ImportedReservedBalanceId, EmergencyReserveNames.Excluded(r));
}

public record SetImportedReserveDto(Guid? InvestmentId, Guid? ReservedBalanceId, bool Enabled);

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

public record SavingsGoalDto(Guid Id, string Name, decimal TargetAmount, decimal CurrentAmount, DateOnly? Deadline, DateTime CreatedAt)
{
    public static SavingsGoalDto FromEntity(SavingsGoal g) => new(g.Id, g.Name, g.TargetAmount, g.CurrentAmount, g.Deadline, g.CreatedAt);
}

public record PlanningDto(EmergencyReserveDto? Reserve, List<SavingsGoalDto> Goals, List<InvestmentRateProfileDto> RateProfiles);

public record ReserveAmountDto(
    [Range(0, 999_999_999_999.99, ErrorMessage = "Valor inválido.")] decimal Amount
);

public record CreateSavingsGoalDto(
    [Required(ErrorMessage = "Nome da meta é obrigatório."), MaxLength(100, ErrorMessage = "Nome da meta pode ter até 100 caracteres.")] string Name,
    [Range(0.01, 999_999_999_999.99, ErrorMessage = "Valor alvo inválido.")] decimal TargetAmount,
    [Range(0, 999_999_999_999.99, ErrorMessage = "Valor guardado inválido.")] decimal CurrentAmount,
    DateOnly? Deadline
);

public record UpdateGoalProgressDto(
    [Range(0, 999_999_999_999.99, ErrorMessage = "Valor guardado inválido.")] decimal CurrentAmount
);

public record LegacyGoalDto(Guid Id, string? Name, decimal TargetAmount, decimal CurrentAmount, DateOnly? Deadline, DateTime? CreatedAt);

public record ImportLegacyPlanningDto(decimal? ReserveAmount, List<LegacyGoalDto>? Goals);

public record ImportLegacyPlanningResultDto(PlanningDto Planning, bool ReserveImported, List<Guid> StoredGoalIds);
