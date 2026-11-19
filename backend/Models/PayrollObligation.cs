namespace ControlFinance.API.Models;

public enum PayrollObligationKind { EmployeeCost = 4 }

// Valor apurado pela empresa ou pela contabilidade; o Control Finance não calcula nada aqui.
// EmployeeCost = gasto adicional do funcionário (nome livre em Description, valor opcional).
public class PayrollObligation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? EmployeeId { get; set; }
    public PayrollObligationKind Kind { get; set; }
    public string? Description { get; set; }
    /// <summary>Nulo = valor ainda não informado (só gastos do funcionário).</summary>
    public decimal? Amount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// Auditoria só de acréscimo, sem FK para a obrigação: o histórico sobrevive à exclusão dela.
public class PayrollObligationEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid ObligationId { get; set; }
    public PayrollObligationKind Kind { get; set; }
    public Guid? EmployeeId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal? Amount { get; set; }
    // Campos históricos compartilhados com os antigos vínculos de custos dos funcionários.
    public DateOnly? DueDate { get; set; }
    public Guid? TransactionId { get; set; }
    public decimal? PaymentAmount { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
