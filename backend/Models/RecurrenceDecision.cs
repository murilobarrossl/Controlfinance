namespace ControlFinance.API.Models;

public enum RecurrenceDecisionStatus { Confirmed, Dismissed }
public enum RecurrenceFrequency { Monthly, Yearly }

// Guarda o veredito do usuário sobre uma recorrência detectada (ou não detectada, no caso de
// confirmação manual) do Radar de Recorrências. Fica no Postgres, não no localStorage: o e-mail
// semanal (ainda não construído) roda em background sem navegador, e precisa enxergar o mesmo
// "dispensei isso" que a tela gravou, senão manda alerta de algo que o usuário já descartou.
public class RecurrenceDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid BankAccountId { get; set; }
    public BankAccount BankAccount { get; set; } = null!;

    /// <summary>Mesma chave de agrupamento usada pela detecção (nome normalizado: trim + minúsculo
    /// + espaços colapsados).</summary>
    public string NormalizedName { get; set; } = string.Empty;

    // Nullable: uma linha pode existir só pra guardar ReminderRequested, sem o usuário ter dado
    // opinião sobre confirmar/dispensar — os dois campos são independentes (ver SetDecision em
    // RecurrencesController: pedir lembrete não força Confirmed, senão maquiaria como "confirmado
    // manualmente" uma recorrência que a detecção automática já achou sozinha).
    public RecurrenceDecisionStatus? Status { get; set; }

    /// <summary>Só usado quando Status=Confirmed e o histórico ainda não tem ocorrências
    /// suficientes pra classificar a frequência sozinho — vem da escolha do usuário na tela.</summary>
    public RecurrenceFrequency? AssumedFrequency { get; set; }

    /// <summary>Usuário pediu lembrete antes da próxima cobrança esperada.</summary>
    public bool ReminderRequested { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
