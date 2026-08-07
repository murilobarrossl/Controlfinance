using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;

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
}
