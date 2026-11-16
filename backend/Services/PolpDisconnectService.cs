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
public class PolpDisconnectService(AppDbContext db, IPolpService polp, IIntegrationLock integrationLock, ILogger<PolpDisconnectService> logger,
    PolpConsentMetadataService? consentMetadata = null)
{
    /// <summary>Status local de uma integração cuja exclusão na Polp ainda não foi aceita. Toda
    /// rota de sincronização ignora integrações nesse status.</summary>
    public const string PendingStatus = "DISCONNECT_PENDING";

    public async Task<RemoteDeletionOutcome> DisconnectAsync(PolpIntegration local, CancellationToken ct)
    {
        // Marca antes e fora do lock: um sync em andamento pode segurar o lock enquanto grava, e
        // a partir daqui ele (e qualquer sync novo) desiste em vez de gravar.
        await db.PolpIntegrations
            .Where(p => p.Id == local.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PendingStatus)
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct);
        await db.Entry(local).ReloadAsync(ct);

        // Sob o lock: um sync que já tinha passado da checagem termina de gravar antes, e o que
        // ele gravou é apagado aqui.
        var authorizationIds = await DeleteConnectionDataLockedAsync(local, ct);
        await CleanupStoredConsentMetadataAsync(ct, authorizationIds);
        var supplementalPending = await db.PolpInvestmentAuthorizations.AnyAsync(a => authorizationIds.Contains(a.Id) && a.IsSupplemental, ct);
        var outcome = await TryRemoteDeleteAsync(local, ct);
        return supplementalPending ? RemoteDeletionOutcome.Pending : outcome;
    }

    // Refaz pedidos de exclusão que não chegaram na Polp (ex.: integração desativada por falta de
    // credenciais na hora em que o usuário desconectou ou excluiu a conta).
    public async Task RetryPendingAsync(CancellationToken ct)
    {
        await CleanupStoredConsentMetadataAsync(ct);
        var pending = await db.PolpIntegrations.Where(p => p.Status == PendingStatus).ToListAsync(ct);
        foreach (var local in pending)
        {
            await DeleteConnectionDataLockedAsync(local, ct);
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
            catch (Exception ex) when (PolpFailure.IsProviderFailure(ex, ct))
            {
                // Fica na fila até a Polp confirmar: nunca é descartado por número de tentativas.
                item.LastAttemptAt = DateTime.UtcNow;
            }
        }
        if (orphaned.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            var stillPending = orphaned.Count(i => db.Entry(i).State != EntityState.Detached);
            if (stillPending > 0)
                logger.LogWarning("{Count} revogação(ões) de contas excluídas seguem pendentes na Polp.", stillPending);
        }
    }

    private Task<List<Guid>> DeleteConnectionDataLockedAsync(PolpIntegration local, CancellationToken ct) =>
        integrationLock.RunAsync(db, local.Id, async () =>
        {
            var authorizationIds = await CancelStoredConsentMetadataAsync(local.Id, ct);
            await db.InvestmentSyncStates.Where(x => x.UserId == local.UserId && x.IntegrationId == local.Id).ExecuteDeleteAsync(ct);
            await db.InvestmentSyncSchedules.Where(x => x.UserId == local.UserId && x.IntegrationId == local.Id).ExecuteDeleteAsync(ct);
            await db.ImportedInvestments.Where(x => x.UserId == local.UserId && x.IntegrationId == local.Id).ExecuteDeleteAsync(ct);
            await db.ImportedReservedBalances.Where(x => x.UserId == local.UserId && x.IntegrationId == local.Id).ExecuteDeleteAsync(ct);
            await DeleteConnectionDataAsync(local.UserId, local.PolpIntegrationId, ct);
            return authorizationIds;
        }, ct);

    private async Task<List<Guid>> CancelStoredConsentMetadataAsync(Guid integrationId, CancellationToken ct)
    {
        var ids = await db.PolpInvestmentAuthorizations.Where(a => a.IntegrationId == integrationId).Select(a => a.Id).ToListAsync(ct);
        await db.PolpInvestmentAuthorizations.Where(a => a.IntegrationId == integrationId && a.State == "Deferred" && a.PolpConsentId == null).ExecuteDeleteAsync(ct);
        await db.PolpInvestmentAuthorizations.Where(a => a.IntegrationId == integrationId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, "Cancelled")
                .SetProperty(a => a.ResourcesJson, (string?)null), ct);
        return ids;
    }

    /// <summary>
    /// Usado na exclusão da conta: pede à Polp a revogação de todas as integrações do usuário, em
    /// qualquer status. Só conta como revogada a que a Polp confirmou (2xx, ou 404 de consentimento
    /// que já não existe). As outras entram na fila em QueueUnconfirmedProviderDeletionsAsync, dentro
    /// da transação que apaga a conta.
    /// </summary>
    public async Task<AccountProviderDeletion> RequestProviderDeletionForAccountAsync(Guid userId, CancellationToken ct)
    {
        // Stop new authorization requests and in-flight sync before calling the provider.
        await db.PolpIntegrations.Where(p => p.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PendingStatus), ct);
        var parentIds = await db.PolpIntegrations.Where(p => p.UserId == userId).Select(p => p.Id).ToListAsync(ct);
        var authorizationIds = new List<Guid>();
        foreach (var parentId in parentIds)
            authorizationIds.AddRange(await integrationLock.RunAsync(db, parentId,
                () => CancelStoredConsentMetadataAsync(parentId, ct), ct));
        await CleanupStoredConsentMetadataAsync(ct, authorizationIds);
        var consentIds = await db.PolpIntegrations.Where(p => p.UserId == userId).Select(p => p.PolpIntegrationId).ToListAsync(ct);
        var confirmed = new HashSet<Guid>();

        foreach (var consentId in consentIds.Distinct())
        {
            try
            {
                await polp.DeleteConsentAsync(consentId, ct);
                confirmed.Add(consentId);
            }
            catch (Exception ex) when (PolpFailure.IsProviderFailure(ex, ct))
            {
                logger.LogWarning("Revogação de consentimento na exclusão de conta ficou pendente ({ErrorType})", ex.GetType().Name);
            }
        }

        // Complementares sem confirmação continuam em PolpInvestmentAuthorizations (sem vínculo com
        // o usuário, IntegrationId vira nulo) e são refeitas por CleanupStoredConsentMetadataAsync.
        var supplementalPending = await db.PolpInvestmentAuthorizations.CountAsync(a => a.IsSupplemental && authorizationIds.Contains(a.Id), ct);
        return new AccountProviderDeletion(confirmed, supplementalPending);
    }

    /// <summary>
    /// Chamar dentro da transação que apaga a conta, antes de apagar o usuário. Trava a linha do
    /// usuário (Postgres) e enfileira a revogação de toda integração dele que a Polp não confirmou,
    /// inclusive uma criada por um Connect concorrente depois de RequestProviderDeletionForAccountAsync.
    /// A fila guarda só o id técnico do consentimento. Devolve quantas revogações ficaram pendentes.
    /// </summary>
    public async Task<int> QueueUnconfirmedProviderDeletionsAsync(Guid userId, IReadOnlySet<Guid> confirmed, CancellationToken ct)
    {
        // Sem a trava, um Connect que grava a integração depois desta leitura e antes do commit teria
        // a linha apagada pelo cascade de Users sem revogação. Com ela, esse INSERT espera o commit,
        // falha pela FK e o próprio Connect enfileira a revogação (QueueOrphanConsentAsync).
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE", ct);

        var unconfirmed = (await db.PolpIntegrations.Where(p => p.UserId == userId).Select(p => p.PolpIntegrationId).ToListAsync(ct))
            .Where(id => !confirmed.Contains(id))
            .Distinct()
            .ToList();
        if (unconfirmed.Count == 0) return 0;

        var alreadyQueued = await db.PendingProviderDeletions.Where(x => unconfirmed.Contains(x.PolpIntegrationId))
            .Select(x => x.PolpIntegrationId).ToListAsync(ct);
        db.PendingProviderDeletions.AddRange(unconfirmed.Except(alreadyQueued)
            .Select(id => new PendingProviderDeletion { PolpIntegrationId = id }));
        await db.SaveChangesAsync(ct);
        return unconfirmed.Count;
    }

    /// <summary>
    /// Consentimento que a Polp criou mas que não ficou registrado localmente porque a conta foi
    /// excluída durante a conexão: ninguém mais vai reaproveitá-lo, então entra na fila de revogação
    /// e os avisos atrasados dele são descartados.
    /// </summary>
    public async Task QueueOrphanConsentAsync(Guid consentId, CancellationToken ct)
    {
        await RemovedPolpResources.MarkAsync(db, [consentId], DateTime.UtcNow, ct);
        if (await db.PendingProviderDeletions.AnyAsync(x => x.PolpIntegrationId == consentId, ct)) return;
        db.PendingProviderDeletions.Add(new PendingProviderDeletion { PolpIntegrationId = consentId });
        await db.SaveChangesAsync(ct);
        logger.LogWarning("Consentimento criado para uma conta excluída durante a conexão; revogação enfileirada.");
    }

    // Also handles technical orphan rows retained by ON DELETE SET NULL. An unknown POST
    // outcome is recovered by correlation before revocation; it is never silently discarded.
    public async Task CleanupStoredConsentMetadataAsync(CancellationToken ct, List<Guid>? authorizationIds = null)
    {
        var rows = await db.PolpInvestmentAuthorizations.AsNoTracking()
            .Where(a => (a.State == "Cancelled" || a.IntegrationId == null)
                && (authorizationIds == null || authorizationIds.Contains(a.Id)))
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            if (!row.IsSupplemental || (row.PolpConsentId is null && row.ProviderStatus == "REJECTED"))
            {
                await db.PolpInvestmentAuthorizations.Where(a => a.Id == row.Id).ExecuteDeleteAsync(ct);
                continue;
            }
            try
            {
                if (row.PolpConsentId is null && consentMetadata is not null)
                    await consentMetadata.RecoverAsync(row, ct);
                var remoteId = await db.PolpInvestmentAuthorizations.Where(a => a.Id == row.Id).Select(a => a.PolpConsentId).SingleOrDefaultAsync(ct);
                if (remoteId is null) continue;
                await RemovedPolpResources.MarkAsync(db, [remoteId.Value], DateTime.UtcNow, ct);
                await polp.DeleteConsentAsync(remoteId.Value, ct);
                await db.PolpInvestmentAuthorizations.Where(a => a.Id == row.Id).ExecuteDeleteAsync(ct);
            }
            catch (Exception ex) when (PolpFailure.IsProviderFailure(ex, ct))
            {
                logger.LogWarning("Revogação de autorização complementar pendente ({ErrorType})", ex.GetType().Name);
            }
        }
    }

    private async Task<RemoteDeletionOutcome> TryRemoteDeleteAsync(PolpIntegration local, CancellationToken ct)
    {
        try
        {
            await polp.DeleteConsentAsync(local.PolpIntegrationId, ct);
        }
        catch (Exception ex) when (PolpFailure.IsProviderFailure(ex, ct))
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
        var accounts = await db.BankAccounts
            .Where(b => b.UserId == userId && b.PolpIntegrationId == polpIntegrationId)
            .Select(b => new { b.Id, b.PolpAccountId })
            .ToListAsync(ct);

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;

        await RemovedPolpResources.MarkAsync(db,
            accounts.Where(a => a.PolpAccountId.HasValue).Select(a => a.PolpAccountId!.Value).Append(polpIntegrationId),
            DateTime.UtcNow, ct);
        await DeleteAccountsDataAsync(db, userId, accounts.Select(a => a.Id).ToList(), ct);

        if (tx is not null) await tx.CommitAsync(ct);
    }

    /// <summary>Exclusão da conta: marca como removidos os consentimentos e as contas/cartões da
    /// Polp do usuário. Chamar na mesma transação que apaga os dados.</summary>
    public async Task MarkAccountResourcesRemovedAsync(Guid userId, CancellationToken ct)
    {
        var consentIds = await db.PolpIntegrations
            .Where(p => p.UserId == userId)
            .Select(p => p.PolpIntegrationId)
            .ToListAsync(ct);
        var resourceIds = await db.BankAccounts
            .Where(b => b.UserId == userId && b.PolpIntegrationId != null && b.PolpAccountId != null)
            .Select(b => b.PolpAccountId!.Value)
            .ToListAsync(ct);

        await RemovedPolpResources.MarkAsync(db, consentIds.Concat(resourceIds), DateTime.UtcNow, ct);
    }

    /// <summary>
    /// Apaga contas e tudo que depende delas. Transaction tem FK com SetNull pra
    /// BankAccount (não cascade), então apagar só a conta deixaria o extrato inteiro órfão no
    /// banco — por isso cada tabela é apagada explicitamente, numa transação só.
    /// </summary>
    public static async Task DeleteAccountsDataAsync(AppDbContext db, Guid userId, List<Guid> accountIds, CancellationToken ct)
    {
        if (accountIds.Count == 0) return;

        // Dentro do IIntegrationLock a transação já existe; fora dele (exclusão de uma conta
        // avulsa) abre a própria.
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;

        var remoteAccounts = (await db.BankAccounts.Where(b => b.UserId == userId && accountIds.Contains(b.Id) && b.PolpAccountId != null)
            .Select(b => b.PolpAccountId!.Value).ToListAsync(ct)).Select(x => x.ToString()).ToList();
        await db.InvestmentSyncStates.Where(s => s.UserId == userId && s.Kind == "reserved" && remoteAccounts.Contains(s.ParentExternalId)).ExecuteDeleteAsync(ct);
        await db.InvestmentSyncSchedules.Where(s => s.UserId == userId && s.Resource == "account.reserved_balances" && remoteAccounts.Contains(s.ExternalId)).ExecuteDeleteAsync(ct);
        await db.RecurrenceDecisions
            .Where(r => r.UserId == userId && accountIds.Contains(r.BankAccountId))
            .ExecuteDeleteAsync(ct);
        await db.Transactions
            .Where(t => t.UserId == userId && t.BankAccountId != null && accountIds.Contains(t.BankAccountId.Value))
            .ExecuteDeleteAsync(ct);
        await db.InvestmentRateProfiles
            .Where(p => p.UserId == userId && accountIds.Contains(p.BankAccountId))
            .ExecuteDeleteAsync(ct);
        await db.EmergencyReserves
            .Where(r => r.UserId == userId && r.BankAccountId != null && accountIds.Contains(r.BankAccountId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.BankAccountId, (Guid?)null), ct);
        await db.BankAccounts
            .Where(b => b.UserId == userId && accountIds.Contains(b.Id))
            .ExecuteDeleteAsync(ct);

        if (tx is not null) await tx.CommitAsync(ct);
    }
}

// Tenta de novo, de hora em hora, as exclusões na Polp que ficaram pendentes.
public class PendingDisconnectRetryService(IServiceScopeFactory scopeFactory, IJobLock jobLock, ILogger<PendingDisconnectRetryService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await jobLock.RunExclusiveAsync(JobLockKeys.PendingDisconnectRetry, async () =>
                {
                    using var scope = scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<PolpDisconnectService>();
                    await service.RetryPendingAsync(stoppingToken);
                }, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao reprocessar desconexões pendentes da Polp.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}

/// <summary>Resultado da revogação na exclusão de conta: Confirmed são os consentimentos que a Polp
/// confirmou; SupplementalPending, autorizações complementares que seguem para nova tentativa.</summary>
public sealed record AccountProviderDeletion(IReadOnlySet<Guid> Confirmed, int SupplementalPending);
