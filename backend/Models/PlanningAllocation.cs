namespace ControlFinance.API.Models;

// Liga um investimento ou caixinha importado a um único destino: a reserva de emergência (GoalId
// nulo) ou uma meta. O índice único por recurso garante que o mesmo dinheiro nunca conte duas vezes.
public class PlanningAllocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? GoalId { get; set; }
    public Guid? ImportedInvestmentId { get; set; }
    public Guid? ImportedReservedBalanceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
