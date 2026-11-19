using ControlFinance.API.Data;
using ControlFinance.API.Services;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/credit-cards")]
[Authorize]
public class CreditCardsController(AppDbContext db) : ApiControllerBase
{
    // Cartões reconhecidos do usuário: contas da Polp identificadas como cartão. Ver
    // CreditCardSummaryBuilder.BuildRecognizedListAsync.
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct) =>
        Ok(await CreditCardSummaryBuilder.BuildRecognizedListAsync(db, UserId, await UserTodayAsync(db, ct), ct));

    private sealed record DueItem(Guid Id, DateTime DueDate, bool IsInvoice, string? Name = null,
        decimal? Amount = null, string? CategoryName = null, string? BankAccountName = null, string? Status = null);

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(CancellationToken ct)
    {
        const int limit = 8;
        var today = await UserTodayAsync(db, ct);
        await using var snapshot = db.Database.IsNpgsql()
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct) : null;
        var cards = await CreditCardSummaryBuilder.BuildRecognizedListAsync(db, UserId, today, ct);
        var expenses = await db.Transactions.AsNoTracking().WithoutSelfTransfers()
            .Where(t => t.UserId == UserId && (t.BankAccount == null || t.BankAccount.IsActive)
                && t.Type == TransactionType.Expense && (t.Status == TransactionStatus.Pending || t.Status == TransactionStatus.Overdue))
            .OrderBy(t => t.DueDate).ThenBy(t => t.Id).Take(limit)
            .Select(t => new DueItem(t.Id, t.DueDate, false, t.Name, t.Amount,
                t.Category == null ? null : t.Category.Name, t.BankAccount == null ? null : t.BankAccount.Name,
                TransactionDto.EffectiveStatus(t.Status, t.DueDate, today))).ToListAsync(ct);
        if (snapshot != null) await snapshot.CommitAsync(ct);

        // Uma despesa depois das oito primeiras já tem oito despesas à frente, mesmo antes de juntar as faturas.
        var invoices = cards.Where(c => c.InvoiceDueDate.HasValue)
            .Select(c => new DueItem(c.Id, c.InvoiceDueDate!.Value, true));
        var upcoming = invoices.Concat(expenses).OrderBy(d => d.DueDate)
            .ThenByDescending(d => d.IsInvoice).ThenBy(d => d.Id).Take(limit).ToList();
        return Ok(new { cards, upcoming });
    }

    [HttpGet("category-expenses")]
    public async Task<IActionResult> GetCategoryExpenses([FromQuery] DateOnly month, [FromQuery] Guid? cardId,
        [FromQuery] string? category, [FromQuery] int page = 1, CancellationToken ct = default, [FromQuery] bool annual = false)
    {
        const int pageSize = 25;
        if (month.Day != 1 || month.Year is < 2000 or > 9998 || page < 1)
            return BadRequest(new { message = "Mês ou página inválidos." });

        var accounts = await db.BankAccounts.AsNoTracking()
            .Where(a => a.UserId == UserId && a.IsActive && (cardId == null || a.Id == cardId))
            .Select(a => new { a.Id, a.PolpAccountType, a.PolpAccountSubtype })
            .ToListAsync(ct);
        var ids = accounts.Where(a => CreditCardAccountDetector.LooksLikeCreditCard(a.PolpAccountType, a.PolpAccountSubtype))
            .Select(a => a.Id).ToArray();
        var first = annual ? new DateOnly(month.Year, 1, 1) : month;
        var start = first.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = annual ? start.AddYears(1) : start.AddMonths(1);
        var expenses = db.Transactions.AsNoTracking().WithoutSelfTransfers()
            .Where(t => t.UserId == UserId && t.BankAccountId.HasValue && ids.Contains(t.BankAccountId.Value)
                && t.BankAccount!.IsActive && t.Type == TransactionType.Expense && t.DueDate >= start && t.DueDate < end)
            .Select(t => new { Row = t, Name = t.Category == null || t.Category.Name == "" ? "Sem categoria"
                : TransferDetection.LegacyLoanCategoryNames.Contains(t.Category.Name) ? TransferDetection.LoanCategoryName : t.Category.Name });

        if (category is null)
        {
            // Amount é cifrado. Só o resumo precisa decifrar os valores do período inteiro.
            var totals = new Dictionary<string, (decimal Value, int Count)>();
            await foreach (var row in expenses.Select(x => new { x.Name, x.Row.Amount }).AsAsyncEnumerable().WithCancellation(ct))
            {
                totals.TryGetValue(row.Name, out var previous);
                totals[row.Name] = (previous.Value + row.Amount, previous.Count + 1);
            }
            return Ok(totals.OrderByDescending(x => x.Value.Value).ThenBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new { Name = x.Key, x.Value.Value, x.Value.Count }));
        }

        var filtered = expenses.Where(x => x.Name == category).Select(x => x.Row);
        var totalCount = await filtered.CountAsync(ct);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (decimal)pageSize));
        page = Math.Min(page, totalPages);
        var today = await UserTodayAsync(db, ct);
        var items = await filtered.OrderByDescending(t => t.DueDate).ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => new { t.Id, t.BankAccountId, t.Name, t.DueDate, t.Amount, Type = t.Type.ToString(),
                Status = TransactionDto.EffectiveStatus(t.Status, t.DueDate, today) }).ToListAsync(ct);
        return Ok(new { items, page, totalPages, totalCount });
    }
}
