using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

// Lista de "cartões reconhecidos" de um usuário: BankAccount sincronizadas da Polp identificadas
// como cartão (CreditCardAccountDetector) — a própria conta é o cartão, sem entidade separada.
// Reaproveitada por CreditCardsController.GetSummary e DashboardController.
public static class CreditCardSummaryBuilder
{
    public static async Task<List<RecognizedCardDto>> BuildRecognizedListAsync(
        AppDbContext db, Guid userId, DateTime today, CancellationToken ct = default)
    {
        var accounts = await db.BankAccounts
            .Where(a => a.UserId == userId && a.IsActive)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.PolpAccountType, a.PolpAccountSubtype, a.PolpIntegrationId,
                a.Name, a.Brand, a.Number, a.CreditLimit, a.UsedLimit, a.ClosingDay, a.DueDay,
                a.CurrentInvoiceAmount, a.InvoiceDueDate, a.ProductType, a.PolpOriginalName, a.AvailableLimit })
            .ToListAsync(ct);
        var cardAccounts = accounts.Where(a => CreditCardAccountDetector.LooksLikeCreditCard(a.PolpAccountType, a.PolpAccountSubtype)).ToList();
        if (cardAccounts.Count == 0) return [];
        var consents = cardAccounts.Where(a => a.PolpIntegrationId.HasValue).Select(a => a.PolpIntegrationId!.Value).Distinct().ToArray();

        var connections = (await db.PolpIntegrations
                .Where(p => p.UserId == userId && consents.Contains(p.PolpIntegrationId))
                .Select(p => new { p.Id, p.PolpIntegrationId, p.InstitutionId, p.InstitutionName, p.LastSyncedAt })
                .ToListAsync(ct))
            .GroupBy(p => p.PolpIntegrationId)
            .ToDictionary(g => g.Key, g => g.First());

        var result = new List<RecognizedCardDto>();

        foreach (var account in cardAccounts)
        {
            var connection = account.PolpIntegrationId is Guid consentId ? connections.GetValueOrDefault(consentId) : null;

            result.Add(new RecognizedCardDto(
                account.Id,
                IsSynced: true,
                account.Name,
                account.Brand,
                account.Number,
                account.CreditLimit,
                account.UsedLimit,
                account.ClosingDay,
                account.DueDay,
                account.CurrentInvoiceAmount,
                account.InvoiceDueDate,
                HasFullDetails: account.CreditLimit.HasValue && account.ClosingDay.HasValue && account.DueDay.HasValue,
                ProductType: account.ProductType,
                InstitutionColor: BankBrandColors.Find(connection?.InstitutionName) ?? BankBrandColors.Find(account.Name),
                ConnectionId: connection?.Id,
                BankName: connection?.InstitutionName,
                ConnectionLastSyncedAt: connection?.LastSyncedAt)
            {
                IssuerInstitutionId = connection?.InstitutionId,
                OriginalProductName = account.PolpOriginalName,
                AvailableLimit = account.AvailableLimit,
            });
        }

        return result.Select(c => c with
        {
            InvoiceStatus = c.InvoiceDueDate is not DateTime due ? null
                : due.Date < today.Date ? nameof(TransactionStatus.Overdue) : nameof(TransactionStatus.Pending),
        }).ToList();
    }
}
