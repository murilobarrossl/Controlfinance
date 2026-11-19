using ControlFinance.API.Controllers.Filters;
using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/taxes")]
[Authorize]
[BusinessHolderOnly]
public class TaxesController(AppDbContext db) : ApiControllerBase
{
    public const string BankRecipientEvidence = "bankRecipient";
    public const string CollectionEvidence = "collectionClassified";

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int year, [FromQuery] int month, [FromQuery] Guid? bankAccountId, CancellationToken ct)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            return BadRequest(new { message = "Mês inválido." });

        var start = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = await db.Transactions.AsNoTracking()
            .Where(t => t.UserId == UserId && t.DueDate >= start && t.DueDate < start.AddYears(1)
                && t.BankAccount!.IsActive && t.BankAccount.PolpAccountId != null
                && (bankAccountId == null || t.BankAccountId == bankAccountId))
            .IdentifiedTaxPayments()
            .Select(t => new
            {
                t.Id, t.DueDate, t.Amount, t.TaxAuthority, t.Description,
                AccountName = t.BankAccount!.Name, AccountNumber = t.BankAccount.Number, t.BankAccount.PolpIntegrationId,
            })
            .ToListAsync(ct);
        rows = rows.Where(r => Guid.TryParse(r.Description, out _)).ToList();

        var consentIds = rows.Where(r => r.DueDate.Month == month && r.PolpIntegrationId != null)
            .Select(r => r.PolpIntegrationId!.Value).Distinct().ToList();
        var institutions = consentIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : await db.PolpIntegrations.AsNoTracking()
                .Where(p => p.UserId == UserId && consentIds.Contains(p.PolpIntegrationId))
                .ToDictionaryAsync(p => p.PolpIntegrationId, p => p.InstitutionName, ct);

        var payments = rows.Where(r => r.DueDate.Month == month)
            .OrderByDescending(r => r.DueDate)
            .Select(r => new TaxPaymentDto(
                r.Id, r.DueDate, r.Amount, r.TaxAuthority,
                r.TaxAuthority is null ? CollectionEvidence : BankRecipientEvidence,
                r.AccountName, r.AccountNumber,
                r.PolpIntegrationId is { } id ? institutions.GetValueOrDefault(id) : null))
            .ToList();

        var months = Enumerable.Range(1, 12)
            .Select(m => rows.Where(r => r.DueDate.Month == m).ToList())
            .Select((list, i) => new TaxMonthTotalDto(i + 1, list.Sum(r => r.Amount), list.Count))
            .ToList();

        return Ok(new TaxOverviewDto(year, month, payments.Sum(p => p.Amount), payments.Count, payments, months));
    }
}
