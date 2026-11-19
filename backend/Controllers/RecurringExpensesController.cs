using System.Globalization;
using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/recurring-expenses")]
[Authorize]
public class RecurringExpensesController(AppDbContext db, IEncryptionService encryption, IMemoryCache cache) : ApiControllerBase
{
    private const int PageSize = 25;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);
    private static readonly CompareInfo SearchCompare = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;

    private sealed record CachedDetection(string Stamp, List<RecurringExpenseGroup> Groups);

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int year, [FromQuery] int month, [FromQuery] Guid? bankAccountId,
        [FromQuery] int page = 1, [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(await UserTodayAsync(db, ct));
        if (year is < 2000 or > 2100 || month is < 1 or > 12 || new DateOnly(year, month, 1) > today)
            return BadRequest(new { message = "Selecione um mês válido, até o mês atual." });
        var (scopedGroups, accounts) = await LoadScopeAsync(today, bankAccountId, ct);
        var charged = scopedGroups.SelectMany(g => g.Charges).DistinctBy(t => t.Id)
            .Where(t => t.Date.Year == year && t.Date.Month == month).ToList();
        var counted = new HashSet<Guid>();
        var serviceCount = scopedGroups.Count(g =>
        {
            var added = false;
            foreach (var t in g.Charges)
                if (t.Date.Year == year && t.Date.Month == month) added |= counted.Add(t.Id);
            return added;
        });
        var withoutDetection = accounts.Count(a => a.IsCard
            && (!a.HasRecurrings || a.RecurringsSyncedAt is null || DateOnly.FromDateTime(a.RecurringsSyncedAt.Value) < today.AddDays(-7)));

        var term = search?.Trim() is { Length: > 0 } text ? text[..Math.Min(text.Length, 100)] : null;
        var filtered = term is null ? scopedGroups : scopedGroups.Where(g => SearchCompare.IndexOf(
            string.Join(' ', new[] { g.Expense.Name, g.Expense.InstitutionName, g.Expense.AccountName, g.Expense.AccountNumber }.Where(s => s is not null)),
            term, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0).ToList();
        var totalPages = Math.Max(1, (filtered.Count + PageSize - 1) / PageSize);
        var current = Math.Clamp(page, 1, totalPages);
        var items = filtered.Skip((current - 1) * PageSize).Take(PageSize).Select(g => g.Expense).ToList();

        return Ok(new RecurringExpenseOverviewDto(year, month, charged.Sum(t => t.Amount), serviceCount,
            withoutDetection, items, filtered.Count, current, PageSize));
    }

    [HttpGet("upcoming")]
    public async Task<IActionResult> Upcoming([FromQuery] Guid? bankAccountId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(await UserTodayAsync(db, ct));
        var (groups, _) = await LoadScopeAsync(today, bankAccountId, ct);
        return Ok(groups.Select(g => g.Expense).Where(e => e.NextEstimatedDate >= today)
            .OrderBy(e => e.NextEstimatedDate).ThenBy(e => e.Id).Take(5)
            .Select(e => new { e.Id, e.Name, e.NextEstimatedDate, e.LastAmount, e.Activity }));
    }

    private async Task<(List<RecurringExpenseGroup>, List<RecurringAccount>)> LoadScopeAsync(
        DateOnly today, Guid? bankAccountId, CancellationToken ct)
    {
        var (groups, accounts) = await LoadAsync(today, ct);
        if (bankAccountId is { } scoped)
        {
            // O seletor só oferece contas; os cartões daquela conexão entram junto, senão nunca apareceriam.
            var consent = accounts.FirstOrDefault(a => a.Id == scoped)?.ConsentId;
            accounts = accounts.Where(a => a.Id == scoped || consent != null && a.ConsentId == consent && a.IsCard).ToList();
        }
        var scope = accounts.Select(a => a.Id).ToHashSet();
        var scopedGroups = groups.Where(g => scope.Contains(g.Expense.BankAccountId)).ToList();
        return (scopedGroups, accounts);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Detail(string id, CancellationToken ct)
    {
        if (id.Length != 64 || !id.All(char.IsAsciiHexDigit)) return NotFound();
        var today = DateOnly.FromDateTime(await UserTodayAsync(db, ct));
        var (groups, _) = await LoadAsync(today, ct);
        var group = groups.FirstOrDefault(g => g.Expense.Id == id);
        if (group is null) return NotFound();
        var cardNumbers = new Dictionary<Guid, string?>();
        if (group.Expense.IsCard)
        {
            var ids = group.Charges.Select(c => c.Id).ToList();
            cardNumbers = await db.Transactions.AsNoTracking().Where(t => t.UserId == UserId && ids.Contains(t.Id))
                .Select(t => new { t.Id, t.CardNumber }).ToDictionaryAsync(t => t.Id, t => t.CardNumber, ct);
        }
        return Ok(new RecurringExpenseDetailDto(group.Expense, group.Charges.OrderByDescending(c => c.Date).ThenByDescending(c => c.Id)
            .Select(c => new RecurringChargeDto(c.Id, c.Date, c.Amount, cardNumbers.GetValueOrDefault(c.Id))).ToList()));
    }

    private async Task<(List<RecurringExpenseGroup> Groups, List<RecurringAccount> Accounts)> LoadAsync(DateOnly today, CancellationToken ct)
    {
        var integrations = await db.PolpIntegrations.AsNoTracking()
            .Where(p => p.UserId == UserId && p.Status == PolpSyncService.AuthorisedStatus)
            .ToDictionaryAsync(p => p.PolpIntegrationId, ct);
        var consentIds = integrations.Keys.ToList();
        var accounts = (await db.BankAccounts.AsNoTracking()
                .Where(a => a.UserId == UserId && a.IsActive && a.PolpAccountId != null && a.PolpIntegrationId != null
                    && consentIds.Contains(a.PolpIntegrationId.Value))
                .Select(a => new
                {
                    a.Id, Consent = a.PolpIntegrationId!.Value, Remote = a.PolpAccountId!.Value, a.Name, a.Number,
                    a.PolpAccountType, a.PolpAccountSubtype, a.CreatedAt, a.PolpRecurringsSyncedAt,
                    HasRecurrings = a.PolpRecurringsJson != null,
                })
                .ToListAsync(ct))
            .Select(a => new RecurringAccount(a.Id, a.Consent, a.Remote, a.Name, a.Number,
                CreditCardAccountDetector.LooksLikeCreditCard(a.PolpAccountType, a.PolpAccountSubtype),
                a.CreatedAt, a.PolpRecurringsSyncedAt, a.HasRecurrings))
            .ToList();
        if (accounts.Count == 0) return ([], accounts);

        // Toda importação passa por PolpSyncService.ApplyAsync, que carimba UpdatedAt/LastSyncedAt da
        // integração; o snapshot de recorrências tem carimbo próprio. O TTL cobre o expurgo por retenção.
        var stamp = string.Join(';', new[] { today.DayNumber.ToString(CultureInfo.InvariantCulture) }
            .Concat(integrations.Values.OrderBy(p => p.Id).Select(p => $"{p.Id}:{p.UpdatedAt.Ticks}:{p.LastSyncedAt?.Ticks}"))
            .Concat(accounts.OrderBy(a => a.Id).Select(a => $"{a.Id}:{a.RecurringsSyncedAt?.Ticks}:{a.HasRecurrings}")));
        var cacheKey = $"recurring-expenses:{UserId}";
        if (cache.TryGetValue(cacheKey, out CachedDetection? hit) && hit!.Stamp == stamp) return (hit.Groups, accounts);

        var groups = await DetectAsync(accounts, integrations, today, ct);
        cache.Set(cacheKey, new CachedDetection(stamp, groups), CacheTtl);
        return (groups, accounts);
    }

    private async Task<List<RecurringExpenseGroup>> DetectAsync(List<RecurringAccount> accounts,
        Dictionary<Guid, PolpIntegration> integrations, DateOnly today, CancellationToken ct)
    {
        var withSnapshot = accounts.Where(a => a.IsCard && a.HasRecurrings).Select(a => a.Id).ToList();
        var snapshots = RecurringExpenseDetection.ParseSnapshots(withSnapshot.Count == 0 ? new Dictionary<Guid, string?>()
            : await db.BankAccounts.AsNoTracking().Where(a => a.UserId == UserId && withSnapshot.Contains(a.Id))
                .Select(a => new { a.Id, a.PolpRecurringsJson }).ToDictionaryAsync(a => a.Id, a => a.PolpRecurringsJson, ct));
        var cardIds = snapshots.Keys.ToList();
        var occurrences = snapshots.Values.SelectMany(l => l).SelectMany(r => r.Occurrences ?? [])
            .Distinct().Select(g => g.ToString()).ToList();
        var bankIds = accounts.Where(a => !a.IsCard).Select(a => a.Id).ToList();
        if (occurrences.Count == 0 && bankIds.Count == 0) return [];
        var payeeTypes = RecurringExpenseDetection.PayeeTypes;
        const string fee = RecurringExpenseDetection.BankFeePackage;

        // Cartão: só as ocorrências apontadas pela Polp. Conta: só tipos com destinatário identificável.
        // O histórico não tem corte de data para não perder cobranças anuais.
        var raw = await db.Transactions.AsNoTracking()
            .Where(t => t.UserId == UserId && t.BankAccountId != null
                && t.Type == TransactionType.Expense && t.Status == TransactionStatus.Paid
                && t.PolpMovementKind == PolpMovementKind.None && !t.PolpTaxPayment
                && (t.InstallmentTotal == null || t.InstallmentTotal <= 1) && (t.InstallmentNumber == null || t.InstallmentNumber <= 1)
                && (cardIds.Contains(t.BankAccountId.Value) && occurrences.Contains(t.Description!)
                    || bankIds.Contains(t.BankAccountId.Value)
                        && (t.BankOperationType == fee || t.CounterpartyCnpjFromBank && payeeTypes.Contains(t.BankOperationType!))))
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .Select(t => new
            {
                t.Id, BankAccountId = t.BankAccountId!.Value, t.Description, t.DueDate, t.Amount, t.BankOperationType,
                t.BankDescription, t.CounterpartyCnpj, t.CounterpartyCnpjFromBank, t.CounterpartyAlias, t.CounterpartyName,
            })
            .ToListAsync(ct);
        var rows = raw.Where(t => Guid.TryParse(t.Description, out _))
            .Select(t => new RecurringChargeRow(t.Id, t.BankAccountId, Guid.Parse(t.Description!), DateOnly.FromDateTime(t.DueDate),
                t.Amount, t.BankOperationType, t.BankDescription, t.CounterpartyCnpj, t.CounterpartyCnpjFromBank,
                t.CounterpartyAlias ?? t.CounterpartyName))
            .ToList();
        var holderHash = await db.Users.Where(u => u.Id == UserId).Select(u => u.DocumentHash).FirstAsync(ct);
        return RecurringExpenseDetection.Detect(UserId, accounts, rows, snapshots, integrations, today, holderHash, encryption);
    }
}
