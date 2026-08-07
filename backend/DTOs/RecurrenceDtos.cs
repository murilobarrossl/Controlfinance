using System.ComponentModel.DataAnnotations;

namespace ControlFinance.API.DTOs;

// AnnualCost é o ponto central do Radar de Recorrências: o valor mensal sozinho ("R$29,90/mês")
// não impressiona ninguém, mas o custo anual ("R$358,80/ano") é o que faz a pessoa decidir. Vem
// calculado do backend (CurrentAmount * 12 se mensal, ou o próprio valor se já é anual) pra não
// duplicar essa conta em cada consumidor (tela, e no futuro o e-mail semanal).
public record RecurrenceDto(
    string NormalizedName,
    string DisplayName,
    Guid BankAccountId,
    string BankAccountName,
    string AccountOwnership, // "Personal" | "Business" | "Mixed"
    string Frequency,        // "Monthly" | "Yearly"
    decimal CurrentAmount,
    decimal AnnualCost,
    DateTime NextExpectedDate,
    int DaysUntilNextCharge,
    decimal TotalSinceFirst,
    DateTime FirstOccurrence,
    int OccurrenceCount,
    bool SilentIncrease,
    bool IsNew,
    bool AnnualFeeUpcoming,
    bool IsManuallyConfirmed,
    bool ReminderRequested
);

// Name vem cru (ex.: o DisplayName que a tela mostrou). O backend normaliza pra achar/gravar a
// mesma chave que a detecção usa, a tela não precisa saber como a normalização funciona.
//
// Status e ReminderRequested são independentes e opcionais: omitir um deles não mexe no valor
// atual gravado (update parcial). Isso existe pra "quero lembrete" não ser obrigado a também
// confirmar a recorrência como assinatura. São perguntas diferentes (ver RecurrencesController).
// AssumedFrequency só é exigida quando Status=Confirmed pra uma recorrência que a detecção
// automática ainda não pegou sozinha.
public record SetRecurrenceDecisionDto(
    Guid BankAccountId,
    [Required(ErrorMessage = "Nome é obrigatório.")] string Name,
    string? Status,
    string? AssumedFrequency,
    bool? ReminderRequested
);
