using System.ComponentModel.DataAnnotations;
using ControlFinance.API.Models;

namespace ControlFinance.API.DTOs;

public record EmergencyReserveDto(decimal CurrentAmount, DateTime UpdatedAt)
{
    public static EmergencyReserveDto FromEntity(EmergencyReserve r) => new(r.CurrentAmount, r.UpdatedAt);
}

public record SavingsGoalDto(Guid Id, string Name, decimal TargetAmount, decimal CurrentAmount, DateOnly? Deadline, DateTime CreatedAt)
{
    public static SavingsGoalDto FromEntity(SavingsGoal g) => new(g.Id, g.Name, g.TargetAmount, g.CurrentAmount, g.Deadline, g.CreatedAt);
}

public record PlanningDto(EmergencyReserveDto? Reserve, List<SavingsGoalDto> Goals);

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
