using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public static class CashClosingLedger
{
    public sealed record Row(Guid Id, DateOnly Day, TransactionType Type, PolpMovementKind Kind, decimal Amount, DateTime CreatedAt)
    {
        public bool Counted => !TransferDetection.IsSelfTransfer(Kind);
    }

    public sealed record Totals(decimal Income, decimal Expense, int Count);

    // Só movimentações importadas da Polp (Description = id da Polp) em contas ativas: a mesma base
    // dos agregados, sem lançamentos manuais legados.
    public static async Task<List<Row>> LoadRowsAsync(
        AppDbContext db, Guid userId, Guid scopeAccountId, DateOnly from, DateOnly toExclusive, CancellationToken ct)
    {
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = toExclusive.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var query = db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && t.BankAccountId != null && t.BankAccount!.IsActive
                && t.DueDate >= start && t.DueDate < end);
        if (scopeAccountId != Guid.Empty)
            query = query.Where(t => t.BankAccountId == scopeAccountId);

        var rows = await query
            .Select(t => new { t.Id, t.DueDate, t.Type, t.PolpMovementKind, t.Amount, t.CreatedAt, t.Description })
            .ToListAsync(ct);
        return rows
            .Where(r => Guid.TryParse(r.Description, out _))
            .Select(r => new Row(r.Id, DateOnly.FromDateTime(r.DueDate), r.Type, r.PolpMovementKind, r.Amount, r.CreatedAt))
            .ToList();
    }

    public static Totals Sum(IEnumerable<Row> rows)
    {
        decimal income = 0, expense = 0;
        var count = 0;
        foreach (var r in rows)
        {
            if (!r.Counted) continue;
            if (r.Type == TransactionType.Income) income += r.Amount; else expense += r.Amount;
            count++;
        }
        return new Totals(income, expense, count);
    }

    private static (decimal Income, decimal Expense) Contribution(TransactionType type, PolpMovementKind kind, decimal amount) =>
        TransferDetection.IsSelfTransfer(kind) ? (0m, 0m)
        : type == TransactionType.Income ? (amount, 0m) : (0m, amount);

    // Compara o retrato gravado no fechamento com o estado atual, sem tocar em nenhum dos dois.
    // Cada diferença carrega o próprio efeito nos totais, então somar os ajustes ao fechamento
    // dá exatamente o total atual, sem contar nada duas vezes.
    public static List<CashAdjustmentDto> Adjustments(IEnumerable<CashClosingItem> snapshot, IEnumerable<Row> current)
    {
        var before = snapshot.ToDictionary(i => i.TransactionId);
        var now = current.ToDictionary(r => r.Id);
        var result = new List<CashAdjustmentDto>();

        foreach (var row in now.Values)
        {
            var (ci, ce) = Contribution(row.Type, row.Kind, row.Amount);
            if (!before.TryGetValue(row.Id, out var old))
            {
                result.Add(new CashAdjustmentDto(row.Id, "added", null, null, null, false,
                    row.Type.ToString(), row.Amount, row.Counted, row.CreatedAt, ci, ce));
                continue;
            }
            if (old.Type == row.Type && old.MovementKind == row.Kind && old.Amount == row.Amount) continue;
            var (oi, oe) = Contribution(old.Type, old.MovementKind, old.Amount);
            result.Add(new CashAdjustmentDto(row.Id, "changed", null, old.Type.ToString(), old.Amount,
                !TransferDetection.IsSelfTransfer(old.MovementKind), row.Type.ToString(), row.Amount, row.Counted,
                row.CreatedAt, ci - oi, ce - oe));
        }

        foreach (var old in before.Values.Where(o => !now.ContainsKey(o.TransactionId)))
        {
            var (oi, oe) = Contribution(old.Type, old.MovementKind, old.Amount);
            result.Add(new CashAdjustmentDto(old.TransactionId, "removed", null, old.Type.ToString(), old.Amount,
                !TransferDetection.IsSelfTransfer(old.MovementKind), null, null, false, null, -oi, -oe));
        }

        return result;
    }

    public sealed record Reading(Guid AccountId, string AccountName, DateTime? OpeningReadAt, DateTime? ClosingReadAt);

    public sealed record BalanceView(decimal? Opening, decimal? Closing, IReadOnlyList<Reading> Readings);

    // Saldos só de observações datadas pelo banco (BankBalanceObservation), nunca reconstruídos a
    // partir de transações: inicial = última leitura do dia anterior, final = última leitura do dia.
    // São leituras pontuais, não saldos de 00:00/23:59: o horário de cada uma volta junto para a tela
    // não sugerir que inicial + entradas − saídas = final. Falta de leitura em qualquer conta do
    // escopo deixa o saldo indisponível em vez de parcial.
    public static async Task<BalanceView> BalancesAsync(
        AppDbContext db, Guid userId, Guid scopeAccountId, DateOnly day, TimeZoneInfo zone, DateTime nowUtc, CancellationToken ct)
    {
        var accounts = await CashAccountsAsync(db, userId, scopeAccountId, ct);
        if (accounts.Count == 0) return new BalanceView(null, null, []);
        var ids = accounts.Select(a => a.Id).ToList();

        var lower = day.AddDays(-2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var upper = day.AddDays(2).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var observations = await db.BankBalanceObservations.AsNoTracking()
            .Where(x => x.UserId == userId && ids.Contains(x.BankAccountId)
                && x.ObservedAt >= lower && x.ObservedAt < upper && x.ObservedAt <= nowUtc)
            .OrderBy(x => x.ObservedAt)
            .ToListAsync(ct);

        DateOnly Civil(DateTime instant) => DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(instant, DateTimeKind.Utc), zone));
        BankBalanceObservation? LastOn(Guid account, DateOnly date) =>
            observations.LastOrDefault(o => o.BankAccountId == account && Civil(o.ObservedAt) == date);

        var readings = new List<Reading>();
        decimal? opening = 0, closing = 0;
        foreach (var account in accounts)
        {
            var before = LastOn(account.Id, day.AddDays(-1));
            var during = LastOn(account.Id, day);
            opening = before is null ? null : opening + before.Balance;
            closing = during is null ? null : closing + during.Balance;
            readings.Add(new Reading(account.Id, account.Name,
                before is null ? null : DateTime.SpecifyKind(before.ObservedAt, DateTimeKind.Utc),
                during is null ? null : DateTime.SpecifyKind(during.ObservedAt, DateTimeKind.Utc)));
        }
        return new BalanceView(opening, closing, readings);
    }

    public sealed record CashMovements(decimal CountedNet, decimal ExcludedNet, int PendingCount, int OutsideBalanceCount);

    // O saldo do banco só se move com lançamentos efetivados nas contas correntes do escopo, inclusive
    // transferências entre contas próprias, faturas e empréstimos (fora de Entradas/Saídas). Compras de
    // cartão e lançamentos pendentes entram nos totais, mas não no saldo: ficam só contados à parte.
    public static async Task<CashMovements> CashMovementsAsync(
        AppDbContext db, Guid userId, Guid scopeAccountId, DateOnly day, CancellationToken ct)
    {
        var cashIds = (await CashAccountsAsync(db, userId, scopeAccountId, ct)).Select(a => a.Id).ToHashSet();
        var start = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var query = db.Transactions.AsNoTracking()
            .Where(t => t.UserId == userId && t.BankAccountId != null && t.BankAccount!.IsActive
                && t.DueDate >= start && t.DueDate < end);
        if (scopeAccountId != Guid.Empty)
            query = query.Where(t => t.BankAccountId == scopeAccountId);
        var rows = (await query
                .Select(t => new { t.BankAccountId, t.Type, t.Status, t.PolpMovementKind, t.Amount, t.Description })
                .ToListAsync(ct))
            .Where(r => Guid.TryParse(r.Description, out _));

        decimal counted = 0, excluded = 0;
        int pending = 0, outside = 0;
        foreach (var r in rows)
        {
            var inBalance = cashIds.Contains(r.BankAccountId!.Value) && r.Status == TransactionStatus.Paid;
            if (r.Status == TransactionStatus.Pending) pending++;
            if (!inBalance)
            {
                if (!TransferDetection.IsSelfTransfer(r.PolpMovementKind)) outside++;
                continue;
            }
            var signed = r.Type == TransactionType.Income ? r.Amount : -r.Amount;
            if (TransferDetection.IsSelfTransfer(r.PolpMovementKind)) excluded += signed; else counted += signed;
        }
        return new CashMovements(counted, excluded, pending, outside);
    }

    private static async Task<List<BankAccount>> CashAccountsAsync(
        AppDbContext db, Guid userId, Guid scopeAccountId, CancellationToken ct)
    {
        var query = db.BankAccounts.AsNoTracking().Where(b => b.UserId == userId && b.IsActive);
        if (scopeAccountId != Guid.Empty)
            query = query.Where(b => b.Id == scopeAccountId);
        return (await query.OrderBy(b => b.Name).ToListAsync(ct))
            .Where(a => !CreditCardAccountDetector.LooksLikeCreditCard(a))
            .ToList();
    }
}
