using System.Globalization;
using ControlFinance.API.Controllers.Filters;
using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/cash-flow")]
[Authorize]
[BusinessHolderOnly]
public class CashFlowController(AppDbContext db) : ApiControllerBase
{
    private const int MaxRangeDays = 400;

    // A série usa apenas saldos disponíveis datados pelo banco. Lacunas não são
    // preenchidas com transações nem com o último saldo; não há fechamento presumido.
    [HttpGet("balance-history")]
    public async Task<IActionResult> BalanceHistory(
        [FromQuery] Guid? bankAccountId,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string granularity = "day",
        CancellationToken ct = default)
    {
        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
            || end <= start || end.DayNumber - start.DayNumber > MaxRangeDays)
            return BadRequest(new { message = "Intervalo de datas inválido." });
        if (granularity is not ("day" or "month"))
            return BadRequest(new { message = "Agrupamento inválido." });

        if (bankAccountId is null) return Ok(Unavailable("account"));

        var account = await db.BankAccounts.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == bankAccountId && b.UserId == UserId && b.IsActive, ct);
        if (account is null) return NotFound();
        if (CreditCardAccountDetector.LooksLikeCreditCard(account)) return Ok(Unavailable("creditCard", account.Name));
        var timeZoneId = await db.Users.Where(u => u.Id == UserId).Select(u => u.TimeZone).FirstAsync(ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(UserClock.Normalize(timeZoneId));
        var clock = HttpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
        var now = clock.GetUtcNow().UtcDateTime;
        // Filtrar por dia civil no fuso do titular evita deslocar observações próximas à meia-noite.
        var query = db.BankBalanceObservations.AsNoTracking()
            .Where(x => x.UserId == UserId && x.BankAccountId == account.Id && x.ObservedAt <= now);
        // Margem de um dia cobre os offsets IANA; o filtro civil abaixo decide a fronteira exata.
        var lower = (start > DateOnly.MinValue ? start.AddDays(-1) : start).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var upper = (end < DateOnly.MaxValue ? end.AddDays(1) : end).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var firstObservation = await query.OrderBy(x => x.ObservedAt).Select(x => (DateTime?)x.ObservedAt).FirstOrDefaultAsync(ct);
        var observations = await query.Where(x => x.ObservedAt >= lower && x.ObservedAt < upper)
            .OrderBy(x => x.ObservedAt).ToListAsync(ct);
        DateOnly CivilDate(DateTime instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(instant, DateTimeKind.Utc), zone));
        var points = observations.Where(x => CivilDate(x.ObservedAt) >= start && CivilDate(x.ObservedAt) < end)
            .GroupBy(x => granularity == "day" ? CivilDate(x.ObservedAt).ToString("yyyy-MM-dd") : CivilDate(x.ObservedAt).ToString("yyyy-MM"))
            .Select(g => g.Last())
            .Select(x => new BalancePointDto(CivilDate(x.ObservedAt), x.Balance, DateTime.SpecifyKind(x.ObservedAt, DateTimeKind.Utc)))
            .ToList();
        var historyStart = firstObservation is { } first ? CivilDate(first) : (DateOnly?)null;
        return Ok(new BalanceHistoryDto(points.Count > 0,
            points.Count > 0 ? null : firstObservation.HasValue ? "outsideHistory" : account.Balance is null ? "noBalance" : "unverifiedHistory",
            account.Name, account.BalanceUpdatedAt, historyStart, points));
    }

    private static BalanceHistoryDto Unavailable(string reason, string? accountName = null) =>
        new(false, reason, accountName, null, null, []);
}
