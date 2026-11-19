using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

public sealed record RecurringAccount(
    Guid Id, Guid ConsentId, Guid RemoteId, string Name, string? Number, bool IsCard,
    DateTime CreatedAt, DateTime? RecurringsSyncedAt, bool HasRecurrings);

public sealed record RecurringChargeRow(
    Guid Id, Guid BankAccountId, Guid RemoteId, DateOnly Date, decimal Amount, string? OperationType,
    string? BankDescription, string? CounterpartyCnpj, bool CnpjFromBank, string? CounterpartyName);

public record RecurringExpenseGroup(RecurringExpenseDto Expense, IReadOnlyList<RecurringChargeRow> Charges);

// Leitura independente do radar legado: não usa decisões manuais, IsFixed ou lançamentos futuros.
// Uma transação importada pertence a no máximo um grupo por conta/cartão.
public static class RecurringExpenseDetection
{
    public const string BankFeePackage = "PACOTE_TARIFA_SERVICOS";
    public const string Convenio = "CONVENIO_ARRECADACAO";
    // Só tipos em que o destinatário é identificado pelo CNPJ enviado pelo banco. CPF fica de fora:
    // não há como separar pagamento a terceiro de transferência para o próprio titular ou sócio.
    public static readonly string[] PayeeTypes = ["BOLETO", "PIX", "TED", "DOC", "TRANSFERENCIA_MESMA_INSTITUICAO", Convenio];

    public static Dictionary<Guid, List<PolpCardRecurringDto>> ParseSnapshots(IReadOnlyDictionary<Guid, string?> snapshots)
    {
        var result = new Dictionary<Guid, List<PolpCardRecurringDto>>();
        foreach (var (cardId, json) in snapshots)
        {
            if (json is null) continue;
            try { result[cardId] = JsonSerializer.Deserialize<List<PolpCardRecurringDto>>(json) ?? []; }
            catch (JsonException) { }
        }
        return result;
    }

    public static List<RecurringExpenseGroup> Detect(
        Guid userId, IReadOnlyList<RecurringAccount> accounts, IReadOnlyList<RecurringChargeRow> rows,
        IReadOnlyDictionary<Guid, List<PolpCardRecurringDto>> cardSnapshots,
        IReadOnlyDictionary<Guid, PolpIntegration> integrations, DateOnly today, string? holderDocumentHash,
        IEncryptionService encryption)
    {
        var result = new List<RecurringExpenseGroup>();
        var holderCache = new Dictionary<string, bool>();
        bool IsHolder(string? document)
        {
            var digits = Digits(document);
            if (digits.Length == 0 || string.IsNullOrEmpty(holderDocumentHash)) return false;
            if (!holderCache.TryGetValue(digits, out var own))
                holderCache[digits] = own = encryption.ComputeLookupHashCandidates(digits).Contains(holderDocumentHash);
            return own;
        }

        // rows chega ordenado por CreatedAt/Id: Description é a chave remota do importador e
        // duplicatas legadas contam só uma vez.
        var byAccount = rows.ToLookup(r => r.BankAccountId);
        foreach (var account in accounts.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).DistinctBy(a => (a.ConsentId, a.RemoteId)))
        {
            if (!integrations.TryGetValue(account.ConsentId, out var integration) || integration.UserId != userId
                || integration.Status != PolpSyncService.AuthorisedStatus) continue;

            var charges = byAccount[account.Id].DistinctBy(r => r.RemoteId)
                .Where(r => r.Amount > 0 && r.Date <= today && !IsHolder(r.CounterpartyCnpj))
                .OrderBy(r => r.Date).ThenBy(r => r.Id).ToList();

            if (account.IsCard)
            {
                // Não inferimos assinaturas do cartão sem o recurso oficial: os campos locais
                // não preservam payment_type, necessário para excluir A_PRAZO com segurança.
                if (!cardSnapshots.TryGetValue(account.Id, out var detected)) continue;
                var byRemote = charges.ToDictionary(t => t.RemoteId);
                var used = new HashSet<Guid>();
                foreach (var recurring in MergeDetections(detected))
                {
                    var remoteIds = (recurring.Occurrences ?? []).Distinct().ToList();
                    if (remoteIds.Count < 3 || string.IsNullOrWhiteSpace(recurring.Description)) continue;
                    var matched = remoteIds.Where(byRemote.ContainsKey).Select(id => byRemote[id])
                        .Where(t => !used.Contains(t.Id)).OrderBy(t => t.Date).ThenBy(t => t.Id).ToList();
                    if (matched.Count < 3) continue;
                    var period = recurring.PeriodMonths is 1 or 2 or 3 or 6 or 12 ? recurring.PeriodMonths : null;
                    var key = matched[0].RemoteId.ToString();
                    result.Add(Build(account, integration, matched, recurring.Description.Trim(), "Polp", key,
                        period, recurring.NextExpectedAt, today, encryption, account.RecurringsSyncedAt, null));
                    foreach (var t in matched) used.Add(t.Id);
                }
                continue;
            }

            var candidates = charges.Where(t => !HasInstallmentDescription(t.BankDescription))
                .Select(t => (Key: AccountKey(t), Charge: t)).Where(x => x.Key is not null);
            foreach (var group in candidates.GroupBy(x => x.Key!, x => x.Charge))
            {
                var history = group.ToList();
                var found = Recurrence(history);
                var subgroups = new List<(string Key, List<RecurringChargeRow> Charges, int Period)>();
                if (found is { } whole) subgroups.Add((group.Key, whole.Charges, whole.Period));
                else if (group.Key.StartsWith("cnpj:", StringComparison.Ordinal))
                {
                    // Mesmo CNPJ com mais de uma cobrança por mês: separa pela descrição bancária exata.
                    foreach (var byName in history.Where(t => Normalize(t.BankDescription).Length >= 3).GroupBy(t => Normalize(t.BankDescription)))
                        if (Recurrence(byName.ToList()) is { } part) subgroups.Add(($"{group.Key}|{byName.Key}", part.Charges, part.Period));
                }

                foreach (var (key, run, period) in subgroups)
                {
                    var latest = run[^1];
                    var name = key == "fee" ? latest.BankDescription ?? "Pacote de serviços"
                        : latest.CounterpartyName ?? latest.BankDescription ?? FormatCnpj(Digits(latest.CounterpartyCnpj));
                    var method = run.Select(t => t.OperationType).Distinct().Count() == 1 ? latest.OperationType : null;
                    result.Add(Build(account, integration, run, name, "ControlFinance", key, period, null, today,
                        encryption, integration.LastSyncedAt, method));
                }
            }
        }
        return result.OrderByDescending(g => g.Expense.LastChargeDate).ThenBy(g => g.Expense.Name).ThenBy(g => g.Expense.Id, StringComparer.Ordinal).ToList();
    }

    private static string? AccountKey(RecurringChargeRow t)
    {
        if (t.OperationType == BankFeePackage) return "fee";
        var cnpj = Digits(t.CounterpartyCnpj);
        return t.CnpjFromBank && cnpj.Length == 14 && PayeeTypes.Contains(t.OperationType) ? $"cnpj:{cnpj}" : null;
    }

    // Última sequência mensal ou anual ininterrupta com três ou mais cobranças e valores estáveis.
    // Cobranças fora dela (avulsas, anteriores a uma interrupção) não entram no grupo.
    private static (List<RecurringChargeRow> Charges, int Period)? Recurrence(List<RecurringChargeRow> history)
    {
        if (history.Count < 3) return null;
        history = history.OrderBy(t => t.Date).ThenBy(t => t.Id).ToList();
        foreach (var period in new[] { 1, 12 })
        {
            var end = history.Count;
            while (end >= 3)
            {
                var start = end - 1;
                while (start > 0 && Follows(history[start - 1].Date, history[start].Date, period)) start--;
                if (end - start >= 3)
                {
                    var run = history.GetRange(start, end - start);
                    if (StableAmounts(run)) return (run, period);
                }
                end = start;
            }
        }
        return null;
    }

    private static bool Follows(DateOnly previous, DateOnly current, int period) =>
        Math.Abs(current.DayNumber - previous.AddMonths(period).DayNumber) <= (period == 12 ? 10 : 5);

    // Conta de consumo varia com o uso; as demais exigem faixa de até 20%.
    private static bool StableAmounts(List<RecurringChargeRow> run)
    {
        var min = run.Min(t => t.Amount);
        var tolerance = run.All(t => t.OperationType == Convenio) ? 2m : 1.20m;
        return run.Max(t => t.Amount) <= min * tolerance;
    }

    // Respostas sobrepostas são a mesma evidência, mesmo que o fornecedor mude a descrição.
    // Unimos por IDs de transação, nunca apenas pelo nome do serviço.
    private static List<PolpCardRecurringDto> MergeDetections(List<PolpCardRecurringDto> rows)
    {
        var merged = new List<PolpCardRecurringDto>();
        foreach (var source in rows.Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Description)))
        {
            var row = new PolpCardRecurringDto
            {
                Description = source.Description, Currency = source.Currency, PeriodMonths = source.PeriodMonths,
                ExpectedDay = source.ExpectedDay, NextExpectedAt = source.NextExpectedAt,
            };
            var ids = (source.Occurrences ?? []).ToHashSet();
            if (ids.Count < 3) continue;
            var overlaps = merged.Where(r => r.Occurrences.Any(ids.Contains)).ToList();
            foreach (var previous in overlaps)
            {
                ids.UnionWith(previous.Occurrences);
                if (row.PeriodMonths != previous.PeriodMonths) row.PeriodMonths = null;
                if (row.NextExpectedAt != previous.NextExpectedAt) row.NextExpectedAt = null;
                merged.Remove(previous);
            }
            row.Occurrences = ids.Order().ToList();
            merged.Add(row);
        }
        return merged;
    }

    private static bool HasInstallmentDescription(string? text) => text is not null
        && (text.Contains("PARCEL", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(text, @"\b\d{1,2}\s*[/\-]\s*\d{1,2}\b"));

    private static RecurringExpenseGroup Build(RecurringAccount account, PolpIntegration integration, List<RecurringChargeRow> history,
        string name, string source, string key, int? period, DateOnly? providerNext, DateOnly today,
        IEncryptionService encryption, DateTime? updatedAt, string? paymentMethod)
    {
        var last = history[^1];
        var lastDate = last.Date;
        var stale = updatedAt is null || DateOnly.FromDateTime(updatedAt.Value) < today.AddDays(-7);
        DateOnly? next = period is { } months && lastDate.Year < 9998 ? lastDate.AddMonths(months) : null;
        if (providerNext is { } remote && next is { } expected && remote > lastDate && Math.Abs(remote.DayNumber - expected.DayNumber) <= 10)
            next = remote;
        var interrupted = next is { } due && due.AddDays(period == 12 ? 30 : 10) < today
            || period is null && lastDate < today.AddDays(-45);
        var activity = stale ? "OutdatedData" : interrupted ? "NoRecentCharge" : "RecentCharges";
        // Não projeta cobranças sucessivas quando a esperada passou. A atividade da assinatura é desconhecida.
        if (stale || interrupted || next < today) next = null;
        var hash = encryption.ComputeLookupHash($"recurring-expense|{integration.UserId}|{account.ConsentId}|{account.RemoteId}|{source}|{key}");
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash))).ToLowerInvariant();
        var dto = new RecurringExpenseDto(id, name, account.Id, account.Name, account.Number, integration.InstitutionName,
            account.IsCard, source, period, last.Amount, lastDate, next, activity, history.Count, updatedAt, paymentMethod);
        return new(dto, history);
    }

    private static string FormatCnpj(string d) => d.Length == 14
        ? $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}" : "Cobrança recorrente";

    private static string Normalize(string? name) => string.Join(' ', (name ?? "").Trim().ToLower(CultureInfo.InvariantCulture)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static string Digits(string? text) => new((text ?? "").Where(char.IsAsciiDigit).ToArray());
}
