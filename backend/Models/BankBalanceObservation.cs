namespace ControlFinance.API.Models;

// Observação do saldo disponível BRL com a data fornecida pelo banco. Não representa
// fechamento diário, saldo contábil nem um saldo reconstruído a partir de transações.
public class BankBalanceObservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid BankAccountId { get; set; }
    public DateTime ObservedAt { get; set; }
    public decimal Balance { get; set; }
}
