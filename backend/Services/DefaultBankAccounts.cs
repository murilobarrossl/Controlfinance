using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public static class DefaultBankAccounts
{
    private static readonly string[] KindOrder = ["CONTA_DEPOSITO_A_VISTA", "CONTA_PAGAMENTO_PRE_PAGA", "CONTA_POUPANCA"];

    // Toda escrita da conta padrão (manual ou automática) trava a linha do usuário: as escritas
    // se serializam e o índice único parcial nunca é disputado.
    public static async Task LockUserAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE", ct);
    }

    // Depois de existir uma conta padrão, nenhuma conexão volta a ser candidata, nem se a padrão
    // for desconectada depois.
    public static Task ClearPendingAsync(AppDbContext db, Guid userId, CancellationToken ct) =>
        db.PolpIntegrations.Where(p => p.UserId == userId && p.AutoDefaultPending)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AutoDefaultPending, false), ct);

    // A marca AutoDefaultPending só sai quando a empresa passa a ter conta padrão: se esta
    // tentativa falhar ou a conta elegível ainda não tiver chegado, o próximo sync da mesma
    // conexão tenta de novo. Com dois bancos iniciados juntos, vence o primeiro que tiver conta
    // elegível sob a trava; o outro encontra a padrão e só limpa a marca.
    public static async Task AssignPendingAsync(AppDbContext db, Guid userId, Guid localIntegrationId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockUserAsync(db, userId, ct);

        var integration = await db.PolpIntegrations.AsNoTracking()
            .Where(p => p.Id == localIntegrationId && p.UserId == userId && p.AutoDefaultPending)
            .Select(p => new { p.PolpIntegrationId, p.Status })
            .FirstOrDefaultAsync(ct);
        if (integration is null) return;

        if (await db.BankAccounts.AnyAsync(b => b.UserId == userId && b.IsDefault, ct))
        {
            await ClearPendingAsync(db, userId, ct);
            await tx.CommitAsync(ct);
            return;
        }
        if (integration.Status != "AUTHORISED") return;

        var candidates = await db.BankAccounts.AsNoTracking()
            .Where(b => b.UserId == userId && b.IsActive && b.PolpIntegrationId == integration.PolpIntegrationId && b.PolpAccountId != null)
            .Select(b => new { b.Id, b.PolpAccountType, b.PolpAccountSubtype, b.CreatedAt })
            .ToListAsync(ct);
        var chosen = candidates
            .Where(b => !CreditCardAccountDetector.LooksLikeCreditCard(b.PolpAccountType, b.PolpAccountSubtype))
            .OrderBy(b => KindOrder.Contains(b.PolpAccountType) ? Array.IndexOf(KindOrder, b.PolpAccountType) : KindOrder.Length)
            .ThenBy(b => b.CreatedAt)
            .ThenBy(b => b.Id)
            .FirstOrDefault();
        if (chosen is null) return;

        await db.BankAccounts.Where(b => b.Id == chosen.Id && b.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsDefault, true), ct);
        await ClearPendingAsync(db, userId, ct);
        await tx.CommitAsync(ct);
    }
}
