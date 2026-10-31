using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public enum RemoteDeletionOutcome
{
    /// <summary>A Polp aceitou a revogação do consentimento. O apagamento dos dados do lado dela
    /// segue as regras do fornecedor: "aceito" não é o mesmo que "concluído", e a tela diz isso.</summary>
    Requested,

    /// <summary>Não deu pra falar com a Polp agora (sem credenciais, fora do ar, erro). O dado
    /// local já foi apagado; o pedido fica marcado e é refeito por PendingDisconnectRetryService.</summary>
    Pending
}

// Desconectar um banco = parar de sincronizar + apagar tudo que veio daquela conexão + pedir à
// Polp que exclua a integração. Antes, "Sair" só marcava a conta como inativa: as transações
// ficavam guardadas e o sync automático continuava buscando dados dessa conexão na Polp.
public class PolpDisconnectService(AppDbContext db, IPolpService polp, ILogger<PolpDisconnectService> logger)
{
    /// <summary>Status local de uma integração cuja exclusão na Polp ainda não foi aceita. Toda
    /// rota de sincronização ignora integrações nesse status.</summary>
    public const string PendingStatus = "DISCONNECT_PENDING";

    public async Task<RemoteDeletionOutcome> DisconnectAsync(PolpIntegration local, CancellationToken ct)
    {
        // Mesmo lock do sync: espera um sync em andamento dessa conexão terminar antes de apagar,
        // senão ele regravaria contas/transações logo depois da exclusão.
        await PolpSyncService.SyncOneWithLockAsync(async () =>
        {
            // Marca primeiro: a partir daqui nenhum sync novo pega essa integração, mesmo que a
            // exclusão local abaixo falhe no meio.
            local.Status = PendingStatus;
            local.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await DeleteConnectionDataAsync(local.UserId, local.PolpIntegrationId, ct);
            return 0;
        }, local.Id, ct);

        return await TryRemoteDeleteAsync(local, ct);
    }

    // Refaz pedidos de exclusão que não chegaram na Polp (ex.: integração desativada por falta de
    // credenciais na hora em que o usuário desconectou ou excluiu a conta).
    public async Task RetryPendingAsync(CancellationToken ct)
    {
        var pending = await db.PolpIntegrations.Where(p => p.Status == PendingStatus).ToListAsync(ct);
        foreach (var local in pending)
        {
            await DeleteConnectionDataAsync(local.UserId, local.PolpIntegrationId, ct);
            await TryRemoteDeleteAsync(local, ct);
        }

        var orphaned = await db.PendingProviderDeletions.ToListAsync(ct);
        foreach (var item in orphaned)
        {
            try
            {
                await polp.DeleteConsentAsync(item.PolpIntegrationId, ct);
                db.PendingProviderDeletions.Remove(item);
            }
            catch (HttpRequestException)
            {
                item.LastAttemptAt = DateTime.UtcNow;
            }
        }
        if (orphaned.Count > 0) await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Usado na exclusão da conta: tenta pedir a exclusão de todas as integrações na Polp na hora;
    /// as que falharem viram PendingProviderDeletion (só o id técnico, sem vínculo com o usuário)
    /// pra serem refeitas depois. Devolve quantas ficaram pendentes.
    /// </summary>
    public async Task<int> RequestProviderDeletionForAccountAsync(Guid userId, CancellationToken ct)
    {
        var integrations = await db.PolpIntegrations.Where(p => p.UserId == userId).ToListAsync(ct);
        var pendingCount = 0;

        foreach (var local in integrations)
        {
            try
            {
                await polp.DeleteConsentAsync(local.PolpIntegrationId, ct);
            }
            catch (HttpRequestException)
            {
                db.PendingProviderDeletions.Add(new PendingProviderDeletion { PolpIntegrationId = local.PolpIntegrationId });
                pendingCount++;
            }
        }

        if (pendingCount > 0) await db.SaveChangesAsync(ct);
        return pendingCount;
    }

    private async Task<RemoteDeletionOutcome> TryRemoteDeleteAsync(PolpIntegration local, CancellationToken ct)
    {
        try
        {
            await polp.DeleteConsentAsync(local.PolpIntegrationId, ct);
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("Exclusão da integração {IntegrationId} na Polp ficou pendente; será tentada de novo.", local.Id);
            return RemoteDeletionOutcome.Pending;
        }

        // A Polp aceitou: não há mais nada a guardar desta conexão do nosso lado.
        db.PolpIntegrations.Remove(local);
        await db.SaveChangesAsync(ct);
        return RemoteDeletionOutcome.Requested;
    }

    private async Task DeleteConnectionDataAsync(Guid userId, Guid polpIntegrationId, CancellationToken ct)
    {
        var accountIds = await db.BankAccounts
            .Where(b => b.UserId == userId && b.PolpIntegrationId == polpIntegrationId)
            .Select(b => b.Id)
            .ToListAsync(ct);

        await DeleteAccountsDataAsync(db, userId, accountIds, ct);
    }

    /// <summary>
    /// Apaga contas e tudo que depende delas. Transaction e Installment têm FK com SetNull pra
    /// BankAccount (não cascade), então apagar só a conta deixaria o extrato inteiro órfão no
    /// banco — por isso cada tabela é apagada explicitamente, numa transação só.
    /// </summary>
    public static async Task DeleteAccountsDataAsync(AppDbContext db, Guid userId, List<Guid> accountIds, CancellationToken ct)
    {
        if (accountIds.Count == 0) return;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.RecurrenceDecisions
            .Where(r => r.UserId == userId && accountIds.Contains(r.BankAccountId))
            .ExecuteDeleteAsync(ct);
        await db.Installments
            .Where(i => i.UserId == userId && i.BankAccountId != null && accountIds.Contains(i.BankAccountId.Value))
            .ExecuteDeleteAsync(ct);
        await db.Transactions
            .Where(t => t.UserId == userId && t.BankAccountId != null && accountIds.Contains(t.BankAccountId.Value))
            .ExecuteDeleteAsync(ct);
        await db.BankAccounts
            .Where(b => b.UserId == userId && accountIds.Contains(b.Id))
            .ExecuteDeleteAsync(ct);

        await tx.CommitAsync(ct);
    }
}

// Tenta de novo, de hora em hora, as exclusões na Polp que ficaram pendentes.
public class PendingDisconnectRetryService(IServiceScopeFactory scopeFactory, ILogger<PendingDisconnectRetryService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<PolpDisconnectService>();
                await service.RetryPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao reprocessar desconexões pendentes da Polp.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
