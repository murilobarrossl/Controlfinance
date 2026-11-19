using System.Globalization;
using ControlFinance.API.Controllers.Filters;
using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/cash-flow/closings")]
[Authorize]
[BusinessHolderOnly]
public class CashClosingsController(AppDbContext db, TimeProvider clock) : ApiControllerBase
{
    private const int MaxAdjustedClosings = 60;

    [HttpGet("day")]
    public async Task<IActionResult> GetDay([FromQuery] string? date, [FromQuery] Guid? bankAccountId, CancellationToken ct)
    {
        if (!TryParseDay(date, out var day)) return BadRequest(new { message = "Data inválida." });
        if (!await ScopeIsValidAsync(bankAccountId, ct)) return NotFound();
        return Ok(await BuildDayAsync(day, bankAccountId ?? Guid.Empty, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Close([FromBody] CloseCashDayDto dto, CancellationToken ct)
    {
        if (!TryParseDay(dto.Date, out var day)) return BadRequest(new { message = "Data inválida." });
        if (!await ScopeIsValidAsync(dto.BankAccountId, ct)) return NotFound();
        var scope = dto.BankAccountId ?? Guid.Empty;

        // Só o dia corrente no fuso da empresa: dias anteriores ficam para consulta e as diferenças
        // que chegarem depois aparecem como ajustes, nunca como um novo fechamento retroativo.
        if (day != DateOnly.FromDateTime(await UserTodayAsync(db, ct)))
            return BadRequest(new { message = "Só é possível fechar o caixa do dia atual." });

        if (await db.CashClosings.AnyAsync(c => c.UserId == UserId && c.Day == day && c.ScopeAccountId == scope && c.ReopenedAt == null, ct))
            return Conflict(new { message = "Este dia já está fechado." });

        var rows = await CashClosingLedger.LoadRowsAsync(db, UserId, scope, day, day.AddDays(1), ct);
        var totals = CashClosingLedger.Sum(rows);
        // Fecha exatamente o que a tela mostrou: se um sync mudou o dia nesse meio-tempo, o usuário
        // confere os novos valores antes, em vez de gravar números que nunca viu.
        if (totals.Income != dto.ExpectedIncome || totals.Expense != dto.ExpectedExpense || totals.Count != dto.ExpectedCount)
            return Conflict(new { message = "As movimentações do dia mudaram. Confira os valores e feche novamente.", stale = true });

        var balances = await BalancesAsync(scope, day, null, ct);
        db.CashClosings.Add(new CashClosing
        {
            UserId = UserId,
            Day = day,
            ScopeAccountId = scope,
            OpeningBalance = balances.Opening,
            Income = totals.Income,
            Expense = totals.Expense,
            ClosingBalance = balances.Closing,
            TransactionCount = totals.Count,
            ClosedAt = clock.GetUtcNow().UtcDateTime,
            ClosedByUserId = UserId,
            Items = rows.Select(r => new CashClosingItem
            {
                TransactionId = r.Id, Type = r.Type, MovementKind = r.Kind, Amount = r.Amount
            }).ToList()
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { message = "Este dia já está fechado." });
        }

        return Ok(await BuildDayAsync(day, scope, ct));
    }

    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid id, [FromBody] ReopenCashDayDto dto, CancellationToken ct)
    {
        var closing = await db.CashClosings.FirstOrDefaultAsync(c => c.Id == id && c.UserId == UserId, ct);
        if (closing is null) return NotFound();
        if (closing.Day != DateOnly.FromDateTime(await UserTodayAsync(db, ct)))
            return BadRequest(new { message = "Só é possível reabrir o caixa do dia atual." });
        if (closing.ReopenedAt is not null || closing.Version != dto.Version)
            return Conflict(new { message = "Este fechamento foi alterado. Atualize a página." });

        closing.ReopenedAt = clock.GetUtcNow().UtcDateTime;
        closing.ReopenedByUserId = UserId;
        closing.Version++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Este fechamento foi alterado. Atualize a página." });
        }

        return Ok(await BuildDayAsync(closing.Day, closing.ScopeAccountId, ct));
    }

    [HttpGet("adjusted")]
    public async Task<IActionResult> GetAdjustedDays([FromQuery] Guid? bankAccountId, CancellationToken ct)
    {
        if (!await ScopeIsValidAsync(bankAccountId, ct)) return NotFound();
        var scope = bankAccountId ?? Guid.Empty;

        var closings = await db.CashClosings.AsNoTracking()
            .Where(c => c.UserId == UserId && c.ScopeAccountId == scope && c.ReopenedAt == null)
            .OrderByDescending(c => c.Day)
            .Take(MaxAdjustedClosings)
            .Include(c => c.Items)
            .AsSplitQuery()
            .ToListAsync(ct);
        if (closings.Count == 0) return Ok(Array.Empty<CashAdjustedDayDto>());

        // Só os dias fechados: o intervalo entre o mais antigo e o mais recente pode cobrir anos de
        // transações. Dias próximos (até 7 de distância) viram uma leitura só.
        var rows = new List<CashClosingLedger.Row>();
        var days = closings.Select(c => c.Day).Distinct().Order().ToList();
        var runStart = days[0];
        var runEnd = days[0];
        foreach (var day in days.Skip(1).Append(DateOnly.MaxValue))
        {
            if (day != DateOnly.MaxValue && day.DayNumber - runEnd.DayNumber <= 7)
            {
                runEnd = day;
                continue;
            }
            rows.AddRange(await CashClosingLedger.LoadRowsAsync(db, UserId, scope, runStart, runEnd.AddDays(1), ct));
            runStart = runEnd = day;
        }
        var byDay = rows.ToLookup(r => r.Day);

        var result = closings
            .Select(c => (c.Day, Adjustments: CashClosingLedger.Adjustments(c.Items, byDay[c.Day])))
            .Where(x => x.Adjustments.Count > 0)
            .Select(x => new CashAdjustedDayDto(x.Day, x.Adjustments.Sum(a => a.IncomeDelta),
                x.Adjustments.Sum(a => a.ExpenseDelta), x.Adjustments.Count))
            .ToList();
        return Ok(result);
    }

    private async Task<CashDayDto> BuildDayAsync(DateOnly day, Guid scope, CancellationToken ct)
    {
        // Fuso e nome do titular numa leitura só: "hoje", saldos e autor do fechamento vêm da mesma linha.
        var owner = await db.Users.AsNoTracking().Where(u => u.Id == UserId)
            .Select(u => new { u.TimeZone, u.Name }).FirstAsync(ct);
        var today = DateOnly.FromDateTime(UserClock.TodayIn(owner.TimeZone, Clock.GetUtcNow()));
        var rows = await CashClosingLedger.LoadRowsAsync(db, UserId, scope, day, day.AddDays(1), ct);
        var totals = CashClosingLedger.Sum(rows);
        var balances = await BalancesAsync(scope, day, owner.TimeZone, ct);
        var movements = await CashClosingLedger.CashMovementsAsync(db, UserId, scope, day, ct);

        var history = await db.CashClosings.AsNoTracking()
            .Where(c => c.UserId == UserId && c.Day == day && c.ScopeAccountId == scope)
            .OrderByDescending(c => c.ClosedAt)
            .ToListAsync(ct);
        var active = history.FirstOrDefault(c => c.ReopenedAt == null);

        var adjustments = new List<CashAdjustmentDto>();
        if (active is not null)
        {
            var items = await db.CashClosingItems.AsNoTracking().Where(i => i.ClosingId == active.Id).ToListAsync(ct);
            adjustments = CashClosingLedger.Adjustments(items, rows);
            var ids = adjustments.Select(a => a.TransactionId).ToList();
            var names = await db.Transactions.AsNoTracking()
                .Where(t => t.UserId == UserId && ids.Contains(t.Id))
                .Select(t => new { t.Id, t.Name })
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
            adjustments = adjustments
                .Select(a => a with { Name = names.GetValueOrDefault(a.TransactionId) })
                .OrderBy(a => a.Kind).ThenByDescending(a => a.ImportedAt)
                .ToList();
        }

        // Um login = um titular: o único autor possível é o próprio usuário.
        var actors = new Dictionary<Guid, string> { [UserId] = owner.Name };

        CashClosingDto ToDto(CashClosing c) => new(c.Id, c.Income, c.Expense, c.TransactionCount, c.OpeningBalance,
            c.ClosingBalance, DateTime.SpecifyKind(c.ClosedAt, DateTimeKind.Utc), actors.GetValueOrDefault(c.ClosedByUserId),
            c.ReopenedAt is { } r ? DateTime.SpecifyKind(r, DateTimeKind.Utc) : null, null, c.Version);

        // Confere a variação dos saldos exibidos (os gravados, se o dia estiver fechado) com o que se
        // moveu nas contas no dia. A sobra não é atribuída a nada: vai para a tela como diferença.
        var openingShown = active?.OpeningBalance ?? (active is null ? balances.Opening : null);
        var closingShown = active?.ClosingBalance ?? (active is null ? balances.Closing : null);
        var change = closingShown - openingShown;
        var reconciliation = new CashReconciliationDto(change, movements.CountedNet, movements.ExcludedNet,
            change - movements.CountedNet - movements.ExcludedNet, movements.PendingCount, movements.OutsideBalanceCount,
            active is null
                ? balances.Readings.Select(r => new CashBalanceReadingDto(r.AccountName, r.OpeningReadAt, r.ClosingReadAt)).ToList()
                : []);

        var isToday = day == today;
        return new CashDayDto(day, isToday, isToday && active is null, isToday && active is not null,
            new CashDayTotalsDto(totals.Income, totals.Expense, totals.Count, balances.Opening, balances.Closing),
            active is null ? null : ToDto(active), adjustments, reconciliation);
    }

    private async Task<CashClosingLedger.BalanceView> BalancesAsync(Guid scope, DateOnly day, string? knownTimeZoneId, CancellationToken ct)
    {
        var timeZoneId = knownTimeZoneId ?? await db.Users.Where(u => u.Id == UserId).Select(u => u.TimeZone).FirstAsync(ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(UserClock.Normalize(timeZoneId));
        return await CashClosingLedger.BalancesAsync(db, UserId, scope, day, zone, clock.GetUtcNow().UtcDateTime, ct);
    }

    private async Task<bool> ScopeIsValidAsync(Guid? bankAccountId, CancellationToken ct) =>
        bankAccountId is null
        || await db.BankAccounts.AnyAsync(b => b.Id == bankAccountId && b.UserId == UserId && b.IsActive, ct);

    private static bool TryParseDay(string? value, out DateOnly day) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)
        && day.Year >= 2000;
}
