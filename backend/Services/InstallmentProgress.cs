using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

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
        var now = BrazilClock.Today;
        var stillActive = new List<Installment>();
        var changed = false;

        foreach (var installment in installments)
        {
            var elapsed = MonthsPassed(installment.NextDueDate, installment.DueDay, now);
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
                if (installment.CreditCard is not null || installment.BankAccount is { UsedLimit: not null, PolpAccountId: null })
                {
                    var remaining = installment.TotalInstallments - installment.CurrentInstallment + 1;
                    var committed = installment.InstallmentAmount * remaining;
                    if (installment.CreditCard is not null)
                        installment.CreditCard.UsedLimit = Math.Max(0, installment.CreditCard.UsedLimit - committed);
                    if (installment.BankAccount is { UsedLimit: not null, PolpAccountId: null } account)
                        account.UsedLimit = Math.Max(0, account.UsedLimit.Value - committed);
                }
                db.Installments.Remove(installment);
                changed = true;
                continue;
            }

            var paid = installment.InstallmentAmount * elapsed;
            if (installment.CreditCard is not null)
                installment.CreditCard.UsedLimit = Math.Max(0, installment.CreditCard.UsedLimit - paid);
            if (installment.BankAccount is { UsedLimit: not null, PolpAccountId: null } paidAccount)
                paidAccount.UsedLimit = Math.Max(0, paidAccount.UsedLimit.Value - paid);

            installment.CurrentInstallment = effectiveCurrent;
            installment.NextDueDate = DueDateAfter(installment.NextDueDate, installment.DueDay, elapsed);
            changed = true;
            stillActive.Add(installment);
        }

        if (changed)
        {
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { }
        }
        return stillActive;
    }

    // Quantas datas de vencimento da sequência (from, from+1mês, from+2meses, ...) já passaram do
    // dia de hoje. Comparação por dia, não pelo instante exato: uma parcela que vence hoje ainda
    // não avança, só a partir de amanhã. Iterativo em vez de aritmética de calendário direta
    // porque AddMonths já resolve os casos de borda (dia 31 virando fim de mês mais curto) sozinho.
    private static int MonthsPassed(DateTime from, int? dueDay, DateTime now)
    {
        var today = now.Date;
        var count = 0;
        while (DueDateAfter(from, dueDay, count) < today) count++;
        return count;
    }

    private static DateTime DueDateAfter(DateTime from, int? dueDay, int months)
    {
        var shifted = from.AddMonths(months);
        if (dueDay is not int day || months == 0) return shifted;
        var safeDay = Math.Min(day, DateTime.DaysInMonth(shifted.Year, shifted.Month));
        return new DateTime(shifted.Year, shifted.Month, safeDay, shifted.Hour, shifted.Minute, shifted.Second, shifted.Kind);
    }
}
