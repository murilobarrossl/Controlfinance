using ControlFinance.API.Data;
using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

// Não existe endpoint pra avançar CurrentInstallment mês a mês nem job de fundo pra isso: sem essa
// reconciliação, "3/12" ficava preso em "3/12" pra sempre (mesmo meses depois), e um parcelamento
// já quitado continuava contando indefinidamente em UsedLimit/"Fatura atual" do cartão, a não ser
// que o usuário lembrasse de excluir manualmente. Roda sob demanda (chamado de
// InstallmentsController e DashboardController a cada leitura) em vez de precisar de um cron.
public static class InstallmentProgress
{
    public static async Task<List<Installment>> ReconcileAsync(
        AppDbContext db, List<Installment> installments, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var stillActive = new List<Installment>();
        var changed = false;

        foreach (var installment in installments)
        {
            var elapsed = MonthsPassed(installment.NextDueDate, now);
            if (elapsed == 0)
            {
                stillActive.Add(installment);
                continue;
            }

            var effectiveCurrent = installment.CurrentInstallment + elapsed;
            if (effectiveCurrent > installment.TotalInstallments)
            {
                // Todas as parcelas restantes já venceram: quitado. Libera o limite do cartão
                // (mesma conta usada em InstallmentsController.Delete) e remove a linha.
                if (installment.CreditCard is not null)
                {
                    var remaining = installment.TotalInstallments - installment.CurrentInstallment + 1;
                    var committed = installment.InstallmentAmount * remaining;
                    installment.CreditCard.UsedLimit = Math.Max(0, installment.CreditCard.UsedLimit - committed);
                }
                db.Installments.Remove(installment);
                changed = true;
                continue;
            }

            installment.CurrentInstallment = effectiveCurrent;
            installment.NextDueDate = installment.NextDueDate.AddMonths(elapsed);
            changed = true;
            stillActive.Add(installment);
        }

        if (changed) await db.SaveChangesAsync(ct);
        return stillActive;
    }

    // Quantas datas de vencimento da sequência (from, from+1mês, from+2meses, ...) já passaram do
    // dia de hoje — comparação por dia, não pelo instante exato: uma parcela que vence hoje ainda
    // não avança, só a partir de amanhã. Iterativo em vez de aritmética de calendário direta
    // porque AddMonths já resolve os casos de borda (dia 31 virando fim de mês mais curto) sozinho.
    private static int MonthsPassed(DateTime from, DateTime now)
    {
        var today = now.Date;
        var count = 0;
        while (from.AddMonths(count) < today) count++;
        return count;
    }
}
