using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;
using ControlFinance.API.Services.Investments;

namespace ControlFinance.API.Services;

// Processa um evento da inbox (aviso da Polp ou evento interno). O aviso nunca carrega dado
// financeiro e nada nele é confiado: o resource_id só vale se já existir no nosso banco, a janela
// é remontada campo a campo (PolpQueryWindow) e todo dado vem da API autenticada da Polp. Sem
// estado em memória: dedupe e retentativa são da inbox. Recurso ainda desconhecido nunca é
// descartado (PolpResourceNotReadyException ⇒ nova tentativa); só o que o usuário desconectou ou
// apagou (RemovedPolpResources) termina sem fazer nada.
public class PolpWebhookProcessor(
    AppDbContext db, PolpSyncService sync, IPolpService polp, IPolpInbox inbox, ILogger<PolpWebhookProcessor> logger,
    PolpConsentMetadataService? consentMetadata = null, PolpInvestmentImportService? investmentImports = null)
{
    public async Task ProcessAsync(PolpInboxEvent evt, CancellationToken ct)
    {
        var window = PolpQueryWindow.TryParse(evt.QueryParameters);

        var investmentFamily = PolpInvestmentContracts.EventFamily(evt.Event);
        if (investmentFamily is not null && investmentImports is not null)
        {
            EnsureEnabled();
            if (Guid.TryParse(evt.ResourceId, out var removedId) && await IsRemovedAsync(evt, removedId, ct)) return;
            await investmentImports.HandleEventAsync(investmentFamily, evt.ResourceId ?? "", evt.Event.EndsWith(".transactions"), window, ct);
            return;
        }

        switch (evt.Event)
        {
            case "consents":
            {
                var remoteId = ResourceId(evt);
                if (await IsRemovedAsync(evt, remoteId, ct)) return;
                var isPrimary = await db.PolpIntegrations.AnyAsync(p => p.PolpIntegrationId == remoteId, ct);
                if (!isPrimary && consentMetadata is not null && await consentMetadata.HandleConsentEventAsync(remoteId, ct)) return;
                var local = await IntegrationAsync(evt, ct);
                if (local is not null && await sync.RefreshConsentAsync(local, ct))
                    await inbox.EnqueueAsync(new PolpInboxEvent(PolpInternalEvents.Backfill, "consents", local.PolpIntegrationId.ToString(), null), ct);
                if (local is not null && consentMetadata is not null)
                    await inbox.EnqueueAsync(new(PolpInternalEvents.ConsentMetadata, "consents", local.PolpIntegrationId.ToString(), null), ct);
                if (local is not null && investmentImports is not null)
                    await inbox.EnqueueAsync(new(PolpInternalEvents.InvestmentDiscovery, "integrations", local.Id.ToString(), null), ct);
                break;
            }

            case "accounts":
            case "credit_cards":
            {
                var local = await IntegrationAsync(evt, ct);
                if (local is not null) await sync.SyncAccountsAndCardsAsync(local, window, ct);
                if (local is not null && investmentImports is not null)
                    await inbox.EnqueueAsync(new(PolpInternalEvents.InvestmentDiscovery, "integrations", local.Id.ToString(), null), ct);
                break;
            }

            case "bills":
                await ForResourceAsync(evt, transactions: false, window, ct);
                break;

            case "accounts.transactions":
            case "credit_cards.transactions":
                await ForResourceAsync(evt, transactions: true, window, ct);
                break;

            case PolpInternalEvents.Backfill:
            {
                var local = await IntegrationAsync(evt, ct);
                if (local is not null && consentMetadata is not null)
                    await inbox.EnqueueAsync(new(PolpInternalEvents.ConsentMetadata, "consents", local.PolpIntegrationId.ToString(), null), ct);
                if (local is not null && investmentImports is not null)
                    await inbox.EnqueueAsync(new(PolpInternalEvents.InvestmentDiscovery, "integrations", local.Id.ToString(), null), ct);
                if (local is not null) await sync.BackfillAsync(local, ct);
                // Discover newly imported accounts even when an earlier catalog scan completed.
                if (local is not null && investmentImports is not null)
                    await inbox.EnqueueAsync(new(PolpInternalEvents.InvestmentDiscovery, "integrations", local.Id.ToString(), null), ct);
                break;
            }

            case PolpInternalEvents.TransactionDetails:
            {
                var local = await IntegrationAsync(evt, ct);
                if (local is not null) await sync.RefreshTransactionDetailsAsync(local, ct);
                break;
            }

            case PolpInternalEvents.Reconcile:
            {
                var local = await IntegrationAsync(evt, ct);
                if (local is not null) await sync.ReconcileAsync(local, ct);
                if (local is not null && consentMetadata is not null)
                    await inbox.EnqueueAsync(new(PolpInternalEvents.ConsentMetadata, "consents", local.PolpIntegrationId.ToString(), null), ct);
                break;
            }

            case "internal.investment-authorization": // Previously queued metadata reads only.
            case PolpInternalEvents.ConsentMetadata:
            {
                var local = await IntegrationAsync(evt, ct);
                if (local is not null && consentMetadata is not null)
                    await consentMetadata.RefreshAsync(local, ct);
                break;
            }

            case PolpInternalEvents.InvestmentDiscovery:
                if (investmentImports is not null) { EnsureEnabled(); await investmentImports.DiscoverAsync(ResourceId(evt), ct); }
                break;
            case PolpInternalEvents.InvestmentPage:
                if (investmentImports is not null)
                {
                    EnsureEnabled();
                    var parts = (evt.QueryParameters ?? "").Split('&');
                    if (parts.Length != 2 || !parts[0].StartsWith("run=", StringComparison.Ordinal)
                        || !Guid.TryParseExact(parts[0][4..], "D", out var run)
                        || !parts[1].StartsWith("page=", StringComparison.Ordinal)
                        || !int.TryParse(parts[1][5..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var page) || page < 0)
                        throw new PolpPermanentException("Geração da página de investimentos inválida.");
                    await investmentImports.ProcessPageAsync(ResourceId(evt), ct, run, page);
                }
                break;
            case PolpInternalEvents.ReservedBalances:
                if (investmentImports is not null)
                {
                    EnsureEnabled();
                    if (await IsRemovedAsync(evt, ResourceId(evt), ct)) return;
                    await investmentImports.HandleReservedAsync(evt.ResourceId ?? "", window, ct);
                }
                break;

            // Empréstimos, financiamentos e eventos desconhecidos: fora do escopo.
        }
    }

    private static Guid ResourceId(PolpInboxEvent evt) =>
        Guid.TryParse(evt.ResourceId, out var id)
            ? id
            : throw new PolpPermanentException($"Evento {evt.Event} sem resource_id válido.");

    // Sem credenciais da Polp, as leituras devolvem listas vazias: gravar a partir delas apagaria
    // fatura e marcaria sync que não aconteceu. O evento espera a integração voltar.
    private void EnsureEnabled()
    {
        if (polp is NullPolpService)
            throw new PolpResourceNotReadyException("Integração com a Polp desativada.");
    }

    // Null = integração em desconexão ou já removida: o evento termina sem fazer nada.
    private async Task<PolpIntegration?> IntegrationAsync(PolpInboxEvent evt, CancellationToken ct)
    {
        var consentId = ResourceId(evt);
        EnsureEnabled();

        var local = await db.PolpIntegrations.AsNoTracking().FirstOrDefaultAsync(p => p.PolpIntegrationId == consentId, ct);
        if (local is null)
        {
            if (await IsRemovedAsync(evt, consentId, ct)) return null;
            // Consentimento desconhecido: corrida com o Connect, que ainda não gravou a integração.
            throw new PolpResourceNotReadyException("Consentimento ainda não existe localmente.");
        }

        return local.Status == PolpDisconnectService.PendingStatus ? null : local;
    }

    private async Task<bool> IsRemovedAsync(PolpInboxEvent evt, Guid remoteId, CancellationToken ct)
    {
        if (!await RemovedPolpResources.IsRemovedAsync(db, remoteId, ct)) return false;
        logger.LogInformation("Evento {Event} da Polp ignorado: recurso {ResourceId} removido localmente.", evt.Event, remoteId);
        return true;
    }

    // bills e *.transactions apontam para a conta ou o cartão, não para o consentimento.
    private async Task ForResourceAsync(PolpInboxEvent evt, bool transactions, PolpQueryWindow? window, CancellationToken ct)
    {
        var remoteId = ResourceId(evt);
        EnsureEnabled();

        var accounts = await db.BankAccounts.AsNoTracking()
            .Where(b => b.PolpAccountId == remoteId && b.PolpIntegrationId != null)
            .ToListAsync(ct);
        if (accounts.Count == 0)
        {
            if (await IsRemovedAsync(evt, remoteId, ct)) return;
            throw new PolpResourceNotReadyException("Conta ou cartão ainda não existe localmente.");
        }

        foreach (var account in accounts)
        {
            var local = await db.PolpIntegrations.AsNoTracking()
                .FirstOrDefaultAsync(p => p.PolpIntegrationId == account.PolpIntegrationId && p.UserId == account.UserId, ct);
            if (local is null || local.Status == PolpDisconnectService.PendingStatus) continue;

            await sync.SyncResourceAsync(local, account, transactions, window, ct);
        }
    }
}
