namespace ControlFinance.API.Models;

// Contrato de empréstimo lido da Polp (produto CREDIT_OPERATIONS, só CPF). É informativo: nunca
// gera transação. Parcelas pagas e o valor liberado continuam vindo do extrato da conta.
public class LoanContract
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid IntegrationId { get; set; }
    public Guid PolpLoanId { get; set; }
    public string ValuesJson { get; set; } = "{}";
    public DateTime? ProviderUpdatedAt { get; set; }
    public DateTime FirstObservedAt { get; set; }
    public DateTime LastObservedAt { get; set; }
}
