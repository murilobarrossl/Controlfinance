using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public class PolpWebhookPayload
{
    [JsonPropertyName("event")] public string? Event { get; set; }
    [JsonPropertyName("resource")] public string? Resource { get; set; }
    [JsonPropertyName("resource_id")] public Guid? ResourceId { get; set; }
    [JsonPropertyName("query_parameters")] public string? QueryParameters { get; set; }
}

// O webhook da Polp é só um aviso ("algo mudou neste recurso"): nunca carrega dado financeiro e a
// doc não prevê assinatura. Por isso nada do payload é confiado: o resource_id só vale se já
// existir no nosso banco, a janela de datas é remontada campo a campo (PolpQueryWindow) e todo
// dado vem da API autenticada da Polp, não do corpo da requisição.
public class PolpWebhookProcessor(AppDbContext db, PolpSyncService sync)
{
    // Entregas repetidas do mesmo aviso (retentativa da Polp, ou alguém reenviando uma entrega
    // capturada) não devem virar uma rajada de chamadas à Polp, que tem teto mensal por CPF.
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromSeconds(5);
    private static readonly ConcurrentDictionary<string, DateTime> Recent = new();

    public static bool IsDuplicate(PolpWebhookPayload p)
    {
        var now = DateTime.UtcNow;
        foreach (var stale in Recent.Where(kv => now - kv.Value > DedupeWindow).Select(kv => kv.Key))
            Recent.TryRemove(stale, out _);

        var key = $"{p.Event}|{p.ResourceId}|{p.QueryParameters}";
        return !Recent.TryAdd(key, now);
    }

    public async Task ProcessAsync(PolpWebhookPayload payload, CancellationToken ct)
    {
        if (payload.Event is null || payload.ResourceId is not Guid resourceId) return;

        var window = PolpQueryWindow.TryParse(payload.QueryParameters);

        switch (payload.Event)
        {
            case "consents":
                await ConsentAsync(resourceId, ct);
                break;

            case "accounts":
            case "credit_cards":
                await AccountsAsync(resourceId, window, ct);
                break;

            case "bills":
                await ForCardAsync(resourceId, (local, card) => sync.SyncBillsAsync(card, window, ct), ct);
                break;

            case "accounts.transactions":
            case "credit_cards.transactions":
                await ForCardAsync(resourceId, (local, account) =>
                    sync.SyncTransactionsAsync(local.UserId, [account], window, ct), ct);
                break;

            // Empréstimos, financiamentos e investimentos estão fora do escopo desta integração.
        }
    }

    private async Task ConsentAsync(Guid consentId, CancellationToken ct)
    {
        var local = await FindIntegrationAsync(consentId, ct);
        if (local is null) return;

        await PolpSyncService.SyncOneWithLockAsync(async () =>
        {
            var authorised = await sync.RefreshConsentAsync(local, ct);
            // Backfill só uma vez: depois dele o sync é por webhook de recurso.
            if (authorised && !local.SyncedLocally)
                return await sync.SyncOneAsync(local, local.UserId, ct);
            return 0;
        }, local.Id, ct);
    }

    private async Task AccountsAsync(Guid consentId, PolpQueryWindow? window, CancellationToken ct)
    {
        var local = await FindIntegrationAsync(consentId, ct);
        if (local is null) return;

        await PolpSyncService.SyncOneWithLockAsync(async () =>
        {
            await db.Entry(local).ReloadAsync(ct);
            if (local.Status == PolpDisconnectService.PendingStatus) return 0;

            var touched = await sync.SyncAccountsAndCardsAsync(local, local.UserId, window, ct);
            local.LastSyncedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return touched.Count;
        }, local.Id, ct);
    }

    // bills e *.transactions apontam para a conta ou cartão (não para o consentimento).
    private async Task ForCardAsync(Guid accountRemoteId, Func<PolpIntegration, BankAccount, Task> action, CancellationToken ct)
    {
        var account = await db.BankAccounts
            .FirstOrDefaultAsync(b => b.PolpAccountId == accountRemoteId && b.IsActive, ct);
        if (account?.PolpIntegrationId is not Guid consentId) return;

        var local = await db.PolpIntegrations
            .FirstOrDefaultAsync(p => p.PolpIntegrationId == consentId && p.UserId == account.UserId, ct);
        if (local is null) return;

        await PolpSyncService.SyncOneWithLockAsync(async () =>
        {
            await db.Entry(local).ReloadAsync(ct);
            if (local.Status == PolpDisconnectService.PendingStatus) return 0;

            await action(local, account);
            local.LastSyncedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return 1;
        }, local.Id, ct);
    }

    private Task<PolpIntegration?> FindIntegrationAsync(Guid consentId, CancellationToken ct) =>
        db.PolpIntegrations.FirstOrDefaultAsync(
            p => p.PolpIntegrationId == consentId && p.Status != PolpDisconnectService.PendingStatus, ct);
}
