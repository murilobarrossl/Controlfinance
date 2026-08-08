using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

// Cálculo de fatura/vencimento/parcelas ativas de um cartão, extraído do DashboardController
// (que só olhava pro primeiro cartão do usuário) pra ser reaproveitado também na listagem de
// todos os cartões (CreditCardsController), sem duplicar a lógica de reconciliação/vencimento.
public static class CreditCardSummaryBuilder
{
    public static async Task<CreditCardDashboardDto> BuildAsync(AppDbContext db, CreditCard card, CancellationToken ct = default)
    {
        // Reconcilia antes de ler CreditLimit/UsedLimit: parcelamento já quitado (todas as
        // parcelas restantes já venceram) libera limite aqui, senão "Fatura atual"/"Limite
        // usado" ficavam inflados pra sempre por um parcelamento que devia ter sumido sozinho.
        var activeInstallments = await InstallmentProgress.ReconcileAsync(db, card.Installments.ToList(), ct);
        var cardInfo = CreditCardDto.FromEntity(card);

        var currentInvoice = activeInstallments.Sum(i => i.InstallmentAmount);

        // próximo vencimento da fatura: clampa pro último dia válido do mês (ex.: DueDay=31
        // em fevereiro derrubaria o dashboard inteiro com ArgumentOutOfRangeException).
        var today = DateTime.UtcNow;
        var safeDueDay = Math.Min(card.DueDay, DateTime.DaysInMonth(today.Year, today.Month));
        var dueDate = new DateTime(today.Year, today.Month, safeDueDay, 0, 0, 0, DateTimeKind.Utc);
        if (dueDate < today) dueDate = dueDate.AddMonths(1);

        var installments = activeInstallments
            .OrderBy(i => i.NextDueDate)
            .Select(InstallmentDto.FromEntity)
            .ToList();

        return new CreditCardDashboardDto(cardInfo, currentInvoice, dueDate, installments);
    }

    // Lista unificada de "cartões reconhecidos" de um usuário: contas da Polp identificadas como
    // cartão (CreditCardAccountDetector), com ou sem CreditCard cadastrado ainda, seguidas dos
    // CreditCard 100% manuais (sem nenhuma conta vinculada). Reaproveitada por
    // CreditCardsController.GetSummary (lista completa) e DashboardController (pega o primeiro).
    public static async Task<List<RecognizedCardDto>> BuildRecognizedListAsync(
        AppDbContext db, Guid userId, CancellationToken ct = default)
    {
        var accounts = await db.BankAccounts
            .Where(a => a.UserId == userId && a.IsActive)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);
        var cardAccounts = accounts.Where(CreditCardAccountDetector.LooksLikeCreditCard).ToList();

        var creditCards = await db.CreditCards
            .Include(c => c.Installments)
            .Include(c => c.BankAccount)
            .Where(c => c.UserId == userId && c.IsActive)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        var result = new List<RecognizedCardDto>();

        foreach (var account in cardAccounts)
        {
            var linked = creditCards.FirstOrDefault(c => c.BankAccountId == account.Id);
            result.Add(new RecognizedCardDto(
                account.Id, account.Name, account.Number,
                HasDetails: linked is not null,
                Details: linked is not null ? await BuildAsync(db, linked, ct) : null));
        }

        var cardAccountIds = cardAccounts.Select(a => a.Id).ToHashSet();
        var manualCards = creditCards.Where(c => c.BankAccountId is null || !cardAccountIds.Contains(c.BankAccountId.Value));
        foreach (var manual in manualCards)
            result.Add(new RecognizedCardDto(null, null, null, true, await BuildAsync(db, manual, ct)));

        return result;
    }
}
