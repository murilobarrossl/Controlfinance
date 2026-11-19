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
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);
        var cardAccounts = accounts.Where(CreditCardAccountDetector.LooksLikeCreditCard).ToList();

        var connections = (await db.PolpIntegrations
                .Where(p => p.UserId == userId)
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
