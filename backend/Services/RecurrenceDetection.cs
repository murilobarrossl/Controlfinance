using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

public record RecurrenceGroup(
    string NormalizedName,
    string DisplayName,
    Guid BankAccountId,
    RecurrenceFrequency Frequency,
    decimal CurrentAmount,
    DateTime NextExpectedDate,
    decimal TotalSinceFirst,
    DateTime FirstOccurrence,
    int OccurrenceCount,
    bool SilentIncrease,
    bool IsNew,
    bool AnnualFeeUpcoming,
    int DaysUntilNextCharge,
    bool IsManuallyConfirmed = false,
    bool ReminderRequested = false
);

// Detecta recorrências (assinaturas, mensalidades) a partir do histórico de despesas: agrupa por
// (nome normalizado, conta) — conta entra na chave pra não misturar a mesma assinatura cobrada de
// uma conta pessoal e de uma conta da empresa numa soma só — exige pelo menos 2 ocorrências pra
// virar candidato, e classifica a frequência pela mediana do intervalo entre cobranças.
//
// Pura: nenhum método toca DbContext, só recebem as transações já buscadas (e, em ApplyDecisions,
// as decisões do usuário já buscadas) — testável com listas montadas à mão, sem Postgres. Isso é
// o que permite reaproveitar essa mesma lógica depois na previsão de fim de mês e no resumo
// semanal por e-mail, sem reescrever nada: quem chama é que muda, a conta continua igual.
public static class RecurrenceDetection
{
    private const int MinOccurrences = 2;
    private const int MonthlyMinDays = 25;
    private const int MonthlyMaxDays = 35;
    private const int YearlyMinDays = 330;
    private const int YearlyMaxDays = 395;
    private const decimal SilentIncreaseThreshold = 0.10m; // 10%
    private const int NewChargeWindowDays = 60;
    private const int AnnualFeeWarningDays = 30;

    public static List<RecurrenceGroup> DetectGroups(IEnumerable<Transaction> transactions, DateTime referenceDate)
    {
        var result = new List<RecurrenceGroup>();

        var candidateGroups = transactions
            .Where(t => t.Type == TransactionType.Expense && t.BankAccountId.HasValue)
            .GroupBy(t => (NormalizedName: Normalize(t.Name), BankAccountId: t.BankAccountId!.Value));

        foreach (var group in candidateGroups)
        {
            var occurrences = group.OrderBy(t => t.DueDate).ToList();
            if (occurrences.Count < MinOccurrences) continue;

            var frequency = ClassifyFrequency(occurrences);
            if (frequency is null) continue;

            result.Add(BuildGroup(
                group.Key.NormalizedName, group.Key.BankAccountId, occurrences,
                frequency.Value, MedianIntervalDays(occurrences), referenceDate));
        }

        return result;
    }

    // Aplica as decisões do usuário sobre o resultado "cru" da detecção: dispensado some de vez
    // (mesmo que o algoritmo continue achando que é recorrente); confirmado manualmente entra
    // mesmo que o algoritmo sozinho não teria batido o critério (poucas ocorrências, ou intervalo
    // fora da janela mensal/anual) — nesse caso usa a frequência que o usuário assumiu ao confirmar,
    // já que não dá pra calcular a partir de pouco histórico.
    public static List<RecurrenceGroup> ApplyDecisions(
        List<RecurrenceGroup> detected,
        IEnumerable<Transaction> allTransactions,
        IReadOnlyDictionary<(Guid BankAccountId, string NormalizedName), RecurrenceDecision> decisions,
        DateTime referenceDate)
    {
        var result = new List<RecurrenceGroup>();
        var detectedKeys = new HashSet<(Guid BankAccountId, string NormalizedName)>();

        foreach (var group in detected)
        {
            var key = (group.BankAccountId, group.NormalizedName);
            detectedKeys.Add(key);

            decisions.TryGetValue(key, out var decision);
            if (decision?.Status == RecurrenceDecisionStatus.Dismissed) continue;

            result.Add(group with
            {
                IsManuallyConfirmed = decision?.Status == RecurrenceDecisionStatus.Confirmed,
                ReminderRequested = decision?.ReminderRequested ?? false
            });
        }

        foreach (var (key, decision) in decisions)
        {
            if (decision.Status != RecurrenceDecisionStatus.Confirmed) continue;
            if (detectedKeys.Contains(key)) continue;
            if (decision.AssumedFrequency is null) continue; // RecurrencesController exige isso ao confirmar sem detecção prévia

            var occurrences = allTransactions
                .Where(t => t.Type == TransactionType.Expense
                         && t.BankAccountId == key.BankAccountId
                         && Normalize(t.Name) == key.NormalizedName)
                .OrderBy(t => t.DueDate)
                .ToList();

            if (occurrences.Count == 0) continue; // decisão órfã (transações somem do histórico) — nada pra mostrar

            var frequency = decision.AssumedFrequency.Value;
            var intervalDays = frequency == RecurrenceFrequency.Monthly ? 30 : 365;

            result.Add(BuildGroup(key.NormalizedName, key.BankAccountId, occurrences, frequency, intervalDays, referenceDate)
                with
            { IsManuallyConfirmed = true, ReminderRequested = decision.ReminderRequested });
        }

        return result;
    }

    private static RecurrenceGroup BuildGroup(
        string normalizedName, Guid bankAccountId, List<Transaction> occurrences,
        RecurrenceFrequency frequency, double intervalDays, DateTime referenceDate)
    {
        var latest = occurrences[^1];
        var first = occurrences[0];
        var nextExpected = latest.DueDate.AddDays(intervalDays);

        var silentIncrease = occurrences.Count >= 2
            && latest.Amount > occurrences[^2].Amount * (1 + SilentIncreaseThreshold);
        var isNew = (referenceDate - first.DueDate).TotalDays <= NewChargeWindowDays;
        // Mesma régua pro sinalizador e pro número mostrado na tela: AnnualFeeUpcoming não
        // recalcula esse valor por conta própria, só olha se ele cai dentro da janela de aviso.
        var daysUntilNextCharge = (int)Math.Round((nextExpected - referenceDate).TotalDays);
        var annualFeeUpcoming = frequency == RecurrenceFrequency.Yearly
            && daysUntilNextCharge is >= 0 and <= AnnualFeeWarningDays;

        return new RecurrenceGroup(
            normalizedName, latest.Name, bankAccountId, frequency, latest.Amount,
            nextExpected, occurrences.Sum(t => t.Amount), first.DueDate, occurrences.Count,
            silentIncrease, isNew, annualFeeUpcoming, daysUntilNextCharge);
    }

    private static RecurrenceFrequency? ClassifyFrequency(List<Transaction> occurrences)
    {
        var medianDays = MedianIntervalDays(occurrences);
        return medianDays switch
        {
            >= MonthlyMinDays and <= MonthlyMaxDays => RecurrenceFrequency.Monthly,
            >= YearlyMinDays and <= YearlyMaxDays => RecurrenceFrequency.Yearly,
            _ => null
        };
    }

    private static double MedianIntervalDays(List<Transaction> occurrences)
    {
        var intervals = new List<double>();
        for (var i = 1; i < occurrences.Count; i++)
            intervals.Add((occurrences[i].DueDate - occurrences[i - 1].DueDate).TotalDays);

        intervals.Sort();
        var mid = intervals.Count / 2;
        return intervals.Count % 2 == 0
            ? (intervals[mid - 1] + intervals[mid]) / 2
            : intervals[mid];
    }

    public static string Normalize(string name) =>
        string.Join(' ', name.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
