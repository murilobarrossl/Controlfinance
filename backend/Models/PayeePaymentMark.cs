namespace ControlFinance.API.Models;

// Decisão manual de "pago neste mês", uma linha por cadastro e mês de referência (dia 1, no fuso do
// usuário). Fica separada da identificação bancária, que é sempre recalculada a partir das
// transações: desmarcar nunca apaga nem altera transação, e meses anteriores ficam como histórico.
// Paid = true é declaração da empresa, não confirmação da instituição financeira.
public abstract class PayeePaymentMark
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateOnly ReferenceMonth { get; set; }
    public bool Paid { get; set; }
    /// <summary>Incrementada a cada mudança; token de concorrência (ver PayeePaymentMarkEvent).</summary>
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class EmployeePaymentMark : PayeePaymentMark
{
    public Guid EmployeeId { get; set; }
}

public enum PayeeKind { Employee }

// Auditoria das marcações manuais: só acréscimo, nunca atualizada. Gravada no mesmo SaveChanges que
// a marcação (uma transação). Version repete a versão da marcação depois da mudança, que é token de
// concorrência: a ordem dos eventos de um cadastro/mês é a ordem efetiva das gravações. Só ids
// técnicos e estados; o valor bancário do momento fica cifrado. Sem FK para o cadastro: o histórico
// continua depois de um cadastro excluído e sai com a conta (cascade de Users).
public class PayeePaymentMarkEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid ActorUserId { get; set; }
    public PayeeKind PayeeKind { get; set; }
    public Guid PayeeId { get; set; }
    public DateOnly ReferenceMonth { get; set; }
    public int Version { get; set; }
    /// <summary>Estado final antes e depois: "Bank", "ManualPaid", "ManualUnpaid" ou "None".</summary>
    public string PreviousStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    /// <summary>Histórico: valores declarados quando a marcação ainda tinha valor manual (removido).
    /// Eventos novos não preenchem.</summary>
    public decimal? PreviousManualAmount { get; set; }
    public decimal? NewManualAmount { get; set; }
    public bool BankIdentified { get; set; }
    public decimal? BankPaidAmount { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
