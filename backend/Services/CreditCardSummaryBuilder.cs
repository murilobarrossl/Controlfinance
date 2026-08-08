using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

// Lista unificada de "cartões reconhecidos" de um usuário: BankAccount sincronizadas da Polp
// identificadas como cartão (CreditCardAccountDetector) — a própria conta é o cartão, sem entidade
// separada — seguidas dos CreditCard 100% manuais (nunca conectados por nenhum banco). Reaproveitada
// por CreditCardsController.GetSummary (lista completa) e DashboardController (pega o primeiro).
public static class CreditCardSummaryBuilder
{
    public static async Task<List<RecognizedCardDto>> BuildRecognizedListAsync(
        AppDbContext db, Guid userId, CancellationToken ct = default)
    {
        var accounts = await db.BankAccounts
            .Where(a => a.UserId == userId && a.IsActive)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);
        var cardAccounts = accounts.Where(CreditCardAccountDetector.LooksLikeCreditCard).ToList();

        var manualCards = await db.CreditCards
            .Include(c => c.Installments)
            .Where(c => c.UserId == userId && c.IsActive)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        var result = new List<RecognizedCardDto>();

        foreach (var account in cardAccounts)
        {
            var installments = await db.Installments
                .Include(i => i.BankAccount)
                .Where(i => i.BankAccountId == account.Id)
                .ToListAsync(ct);
            var activeInstallments = await InstallmentProgress.ReconcileAsync(db, installments, ct);
            var installmentDtos = activeInstallments.OrderBy(i => i.NextDueDate).Select(InstallmentDto.FromEntity).ToList();

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
                installmentDtos));
        }

        foreach (var card in manualCards)
        {
            // Reconcilia antes de ler CreditLimit/UsedLimit: parcelamento já quitado (todas as
            // parcelas restantes já venceram) libera limite aqui, senão "Fatura atual"/"Limite
            // usado" ficavam inflados pra sempre por um parcelamento que devia ter sumido sozinho.
            var activeInstallments = await InstallmentProgress.ReconcileAsync(db, card.Installments.ToList(), ct);
            var installmentDtos = activeInstallments.OrderBy(i => i.NextDueDate).Select(InstallmentDto.FromEntity).ToList();
            var currentInvoice = activeInstallments.Sum(i => i.InstallmentAmount);

            // próximo vencimento da fatura: clampa pro último dia válido do mês (ex.: DueDay=31
            // em fevereiro derrubaria o dashboard inteiro com ArgumentOutOfRangeException).
            var today = DateTime.UtcNow;
            var safeDueDay = Math.Min(card.DueDay, DateTime.DaysInMonth(today.Year, today.Month));
            var dueDate = new DateTime(today.Year, today.Month, safeDueDay, 0, 0, 0, DateTimeKind.Utc);
            if (dueDate < today) dueDate = dueDate.AddMonths(1);

            result.Add(new RecognizedCardDto(
                card.Id,
                IsSynced: false,
                card.Name,
                card.Brand,
                Number: null,
                card.CreditLimit,
                card.UsedLimit,
                card.ClosingDay,
                card.DueDay,
                currentInvoice,
                dueDate,
                HasFullDetails: true,
                installmentDtos));
        }

        return result;
    }
}
