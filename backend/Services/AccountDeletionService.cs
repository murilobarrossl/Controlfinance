using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

// Único caminho de exclusão definitiva de conta: pedido do usuário (AccountController) e inatividade
// (InactiveAccountService). Idempotente: refeito sobre uma exclusão interrompida, termina o que faltou.
public class AccountDeletionService(AppDbContext db, PolpDisconnectService disconnectService, SessionValidator sessionValidator)
{
    /// <summary>Exclui a conta e devolve quantas revogações na Polp ficaram na fila.</summary>
    public async Task<int> DeleteAsync(Guid userId)
    {
        // Começada, a exclusão vai até o fim: parar no meio deixaria consentimentos revogados com a conta
        // ainda existindo.
        var work = CancellationToken.None;
        var revocation = await disconnectService.RequestProviderDeletionForAccountAsync(userId, work);
        int providerPending;

        await using (var tx = await db.Database.BeginTransactionAsync(work))
        {
            providerPending = revocation.SupplementalPending
                + await disconnectService.QueueUnconfirmedProviderDeletionsAsync(userId, revocation.Confirmed, work);
            await disconnectService.MarkAccountResourcesRemovedAsync(userId, work);
            // Reserva e metas também têm cascade de Users, mas saem explicitamente: são dados que o
            // usuário digitou, e a exclusão deles não pode depender só da FK existir no banco.
            await db.PlanningAllocations.Where(a => a.UserId == userId).ExecuteDeleteAsync(work);
            await db.SavingsGoals.Where(g => g.UserId == userId).ExecuteDeleteAsync(work);
            await db.Employees.Where(e => e.UserId == userId).ExecuteDeleteAsync(work);
            await db.Suppliers.Where(x => x.UserId == userId).ExecuteDeleteAsync(work);
            await db.PayeePaymentMarkEvents.Where(x => x.UserId == userId).ExecuteDeleteAsync(work);
            await db.PayrollObligations.Where(x => x.UserId == userId).ExecuteDeleteAsync(work);
            await db.PayrollObligationEvents.Where(x => x.UserId == userId).ExecuteDeleteAsync(work);
            await db.CashClosings.Where(c => c.UserId == userId).ExecuteDeleteAsync(work);
            await db.InvestmentRateProfiles.Where(p => p.UserId == userId).ExecuteDeleteAsync(work);
            await db.EmergencyReserves.Where(r => r.UserId == userId).ExecuteDeleteAsync(work);
            // O resto (contas, transações, categorias, cartões, decisões de recorrência,
            // integrações e registros de aceite) sai pelo cascade de Users no próprio Postgres.
            await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(work);
            await tx.CommitAsync(work);
        }

        sessionValidator.Invalidate(userId);
        return providerPending;
    }
}
