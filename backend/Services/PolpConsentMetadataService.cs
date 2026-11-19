using System.Net;
using System.Text.Json;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using ControlFinance.API.Services.Investments;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public record PolpConsentResourceAvailability(string Type, string Status, bool HasResource);

// Authenticated observations of existing bank consents. This service never creates or
// recreates consent and exposes no authorization flow. Historical source metadata is kept
// for imported-resource ownership, delayed-event protection and provider deletion cleanup.
public class PolpConsentMetadataService(AppDbContext db, IPolpService polp,
    IIntegrationLock integrationLock, TimeProvider clock, IPolpInbox? inbox = null)
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly HashSet<string> InvestmentTypes =
        ["BANK_FIXED_INCOME", "CREDIT_FIXED_INCOME", "FUND", "TREASURE_TITLE", "VARIABLE_INCOME"];
    private static readonly HashSet<string> InvestmentProducts =
        ["INVESTMENTS", "BANK_FIXED_INCOME", "CREDIT_FIXED_INCOME", "FUND", "TREASURE_TITLE", "VARIABLE_INCOME"];

    public static bool HasInvestments(IEnumerable<string>? products) => products?.Any(InvestmentProducts.Contains) == true;
    public static string? SerializeProducts(IEnumerable<string>? products) => products is null ? null : JsonSerializer.Serialize(products);
    public static bool HasInvestmentsJson(string? products) => products is not null && HasInvestments(JsonSerializer.Deserialize<string[]>(products));

    // Called by the inbox, not on every browser poll. Persisted throttle works across nodes.
    public async Task RefreshAsync(PolpIntegration local, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var cutoff = now - RefreshInterval;
        var snapshot = await integrationLock.RunAsync(db, local.Id, async () =>
        {
            var parent = await db.PolpIntegrations.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == local.Id && p.UserId == local.UserId && p.PolpIntegrationId == local.PolpIntegrationId, ct);
            if (parent is null || parent.Status != "AUTHORISED" || parent.ConsentCheckedAt > cutoff) return null;
            await db.PolpIntegrations.Where(p => p.Id == parent.Id && p.Status == "AUTHORISED")
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ConsentCheckedAt, now), ct);
            var a = await db.PolpInvestmentAuthorizations.AsNoTracking().SingleOrDefaultAsync(a => a.IntegrationId == parent.Id, ct);
            return new RefreshSnapshot(parent, a);
        }, ct);
        if (snapshot is null) return;
        try
        {
            await RefreshSnapshotAsync(local, snapshot, now, ct);
            await PolpAuthorizationEnd.SuspendAsync(db, now, ct, local.Id);
        }
        catch
        {
            // The timestamp is also the cross-node lease. A failed provider read must not
            // make the inbox retry look like a successful refresh for the entire interval.
            db.ChangeTracker.Clear();
            if (!ct.IsCancellationRequested)
                await db.PolpIntegrations.Where(p => p.Id == local.Id && p.UserId == local.UserId
                    && p.PolpIntegrationId == snapshot.Parent.PolpIntegrationId
                    && p.Status == "AUTHORISED" && p.ConsentCheckedAt == now)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.ConsentCheckedAt, snapshot.Parent.ConsentCheckedAt), ct);
            throw;
        }
    }

    private async Task RefreshSnapshotAsync(PolpIntegration local, RefreshSnapshot snapshot, DateTime now, CancellationToken ct)
    {
        var a = snapshot.Authorization;
        if (a?.State == "Cancelled") return;
        if (a?.IsSupplemental == true)
        {
            // Historical source metadata is usable only while its owning bank connection
            // remains valid. A lost revocation event cannot leave that source enabled.
            PolpConsentDto primary;
            try { primary = await polp.GetConsentAsync(snapshot.Parent.PolpIntegrationId, ct); }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                await db.PolpIntegrations.Where(p => p.Id == local.Id && p.Status == "AUTHORISED" && p.UpdatedAt == snapshot.Parent.UpdatedAt)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "REVOKED"), ct);
                return;
            }
            ValidatePrimary(snapshot.Parent, primary);
            if (primary.Status != "AUTHORISED")
            {
                if (primary.Status != "AWAITING_AUTHORIZATION")
                    await db.PolpIntegrations.Where(p => p.Id == local.Id && p.Status == "AUTHORISED" && p.UpdatedAt == snapshot.Parent.UpdatedAt)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, primary.Status).SetProperty(p => p.UpdatedAt, now), ct);
                return;
            }
        }
        if (a?.IsSupplemental == true && a.PolpConsentId is null)
        {
            if (a.State is "Creating" or "Uncertain") await RecoverAsync(a, ct);
            return;
        }
        var remoteId = a?.PolpConsentId ?? snapshot.Parent.PolpIntegrationId;
        PolpConsentDto remote;
        try { remote = await polp.GetConsentAsync(remoteId, ct); }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // A removed provider consent can never be resurrected by a delayed read.
            if (a is not null)
                await db.PolpInvestmentAuthorizations.Where(x => x.Id == a.Id && x.OperationId == a.OperationId && x.State == "Ready")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProviderStatus, "REVOKED")
                        .SetProperty(x => x.ResourcesJson, (string?)null), ct);
            if (a is null || !a.IsSupplemental)
                await db.PolpIntegrations.Where(p => p.Id == local.Id && p.Status == "AUTHORISED")
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "REVOKED"), ct);
            return;
        }
        if (a is null || !a.IsSupplemental)
        {
            ValidatePrimary(snapshot.Parent, remote);
            await integrationLock.RunAsync(db, local.Id, async () =>
            {
                var parent = await db.PolpIntegrations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == local.Id && p.Status == "AUTHORISED", ct);
                if (parent is null || parent.UpdatedAt != snapshot.Parent.UpdatedAt || remote.Status == "AWAITING_AUTHORIZATION") return false;
                await db.PolpIntegrations.Where(p => p.Id == parent.Id && p.Status == "AUTHORISED" && p.UpdatedAt == snapshot.Parent.UpdatedAt)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.ConsentProductsJson, SerializeProducts(remote.Products))
                        .SetProperty(p => p.Status, remote.Status)
                        .SetProperty(p => p.UpdatedAt, now), ct);
                if (a is null && remote.Status == "AUTHORISED" && HasInvestments(remote.Products))
                {
                    var exists = await db.PolpInvestmentAuthorizations.AnyAsync(x => x.IntegrationId == parent.Id, ct);
                    if (!exists)
                    {
                        a = new PolpInvestmentAuthorization
                        {
                            IntegrationId = parent.Id, InstitutionId = parent.InstitutionId,
                            IsSupplemental = false, PolpConsentId = parent.PolpIntegrationId, State = "Ready"
                        };
                        db.PolpInvestmentAuthorizations.Add(a);
                        await db.SaveChangesAsync(ct);
                    }
                }
                return true;
            }, ct);
            db.ChangeTracker.Clear();
        }
        if (a is null) return;
        List<PolpConsentResourceDto>? resources = null;
        if (remote.Status == "AUTHORISED" && HasInvestments(remote.Products))
            resources = await polp.GetConsentResourcesAsync(remote.Id, ct);
        await ApplyAsync(new(a.Id, a.OperationId, local.Id, local.InstitutionId, remoteId), remote, resources, ct);
    }

    public static void ValidatePrimary(PolpIntegration parent, PolpConsentDto remote)
    {
        if (remote.Id != parent.PolpIntegrationId || remote.InstitutionId != parent.InstitutionId ||
            (remote.ClientUserId is not null && remote.ClientUserId != parent.UserId.ToString()))
            throw new PolpPermanentException("Identidade do consentimento não confere com a conexão.");
        if (remote.Status is not ("AUTHORISED" or "AWAITING_AUTHORIZATION" or "REJECTED" or "EXPIRED" or "REVOKED"))
            throw new PolpPermanentException("Estado do consentimento não reconhecido.");
    }

    private async Task ApplyAsync(Observation command, PolpConsentDto remote,
        List<PolpConsentResourceDto>? resources, CancellationToken ct)
    {
        remote.UpdatedAt = UtcDate(remote.UpdatedAt);
        remote.UrlToAuthenticateExpiresAt = UtcDate(remote.UrlToAuthenticateExpiresAt);
        var applied = await integrationLock.RunAsync(db, command.ParentId, async () =>
        {
            var a = await db.PolpInvestmentAuthorizations.SingleOrDefaultAsync(x => x.Id == command.Id && x.OperationId == command.OperationId, ct);
            if (a is null) return false;
            if (remote.Id == Guid.Empty || remote.InstitutionId != a.InstitutionId ||
                (a.IsSupplemental && remote.ClientUserId != a.Id.ToString()) ||
                (command.ConsentId.HasValue && remote.Id != command.ConsentId.Value))
                throw new PolpPermanentException("Identidade do consentimento bancário inválida.");
            if (remote.Status is not ("AUTHORISED" or "AWAITING_AUTHORIZATION" or "REJECTED" or "EXPIRED" or "REVOKED"))
                throw new PolpPermanentException("Estado da autorização não reconhecido.");
            var parent = await db.PolpIntegrations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == command.ParentId, ct);
            // A historical observation may arrive after disconnect. Keep only its id for cleanup.
            if (a.State == "Cancelled" || a.IntegrationId != command.ParentId || parent is null || parent.Status == PolpDisconnectService.PendingStatus)
            {
                a.State = "Cancelled";
                a.IntegrationId = null;
                a.PolpConsentId ??= remote.Id;
                await db.SaveChangesAsync(ct);
                return false;
            }
            if (parent.Status != "AUTHORISED") return false;
            if (parent.InstitutionId != a.InstitutionId) throw new PolpPermanentException("Instituição da autorização inválida.");
            // On recreation the provider id/correlation is unchanged. Require evidence newer
            // than the previous provider version, without comparing clocks on two servers.
            if ((a.State is "Creating" or "Uncertain") && a.PolpConsentId.HasValue
                && (remote.UpdatedAt is null || a.ProviderUpdatedAt is null || remote.UpdatedAt <= a.ProviderUpdatedAt)) return false;
            if (a.ProviderUpdatedAt > remote.UpdatedAt || (a.State == "Ready" && a.ProviderStatus == "REVOKED")) return false;
            if (a.State == "Ready" && a.ProviderStatus == "AUTHORISED" && remote.Status == "AWAITING_AUTHORIZATION") return false;
            if (a.PolpConsentId is { } previous && previous != remote.Id) throw new PolpPermanentException("Consentimento divergente.");
            // Provider correlation/institution checks also protect recovery of a lost POST.
            if (a.IsSupplemental && await db.PolpIntegrations.AnyAsync(p => p.PolpIntegrationId == remote.Id, ct))
                throw new PolpPermanentException("Consentimento já pertence a uma conexão bancária.");
            a.PolpConsentId = remote.Id;
            a.State = "Ready";
            a.ProviderStatus = remote.Status;
            a.ExecutionStatus = remote.ExecutionStatus;
            a.ProductsJson = SerializeProducts(remote.Products);
            a.ProviderUpdatedAt = UtcDate(remote.UpdatedAt);
            a.AuthorizationUrlExpiresAt = UtcDate(remote.UrlToAuthenticateExpiresAt);
            a.ResourcesJson = resources is null ? null : JsonSerializer.Serialize(resources
                .Where(r => InvestmentTypes.Contains(r.Type))
                .Select(r => new PolpConsentResourceAvailability(r.Type, r.Status, !string.IsNullOrWhiteSpace(r.ResourceId))));
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
        db.ChangeTracker.Clear();
        if (applied && inbox is not null && remote.Status == "AUTHORISED" && HasInvestments(remote.Products)
            && await InvestmentHolder.AllowsIntegrationAsync(db, command.ParentId, ct))
            await inbox.EnqueueAsync(new(PolpInternalEvents.InvestmentDiscovery, "integrations", command.ParentId.ToString(), null, PolpInboxPriority.Low), ct);
    }

    // One page per turn: no unbounded scan or repeated POST after an ambiguous timeout.
    // The cursor survives restarts; neither other users' results nor provider errors are exposed.
    public async Task RecoverAsync(PolpInvestmentAuthorization a, CancellationToken ct)
    {
        if (!a.IsSupplemental || a.PolpConsentId.HasValue) return;
        var page = await polp.GetConsentPageAsync(a.RecoveryCursor, ct);
        var matches = page.Data.Where(p => p.ClientUserId == a.Id.ToString() && p.InstitutionId == a.InstitutionId).ToList();
        if (matches.Count > 1) throw new PolpPermanentException("Mais de um consentimento para a mesma tentativa de autorização.");
        if (matches.Count == 1)
            await ApplyAsync(new(a.Id, a.OperationId, a.IntegrationId ?? a.Id, a.InstitutionId, null), matches[0], null, ct);
        else
            await db.PolpInvestmentAuthorizations.Where(x => x.Id == a.Id && x.OperationId == a.OperationId && x.PolpConsentId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RecoveryCursor, page.Meta == null ? null : page.Meta.NextCursor), ct);
    }

    // Existing webhook authentication stays in place. The event is a hint only: identity,
    // state, and products are read from the authenticated API, never from webhook payloads.
    public async Task<bool> HandleConsentEventAsync(Guid remoteId, CancellationToken ct)
    {
        var a = await db.PolpInvestmentAuthorizations.AsNoTracking().SingleOrDefaultAsync(x => x.PolpConsentId == remoteId, ct);
        if (a is null)
        {
            // A new consent webhook can arrive before POST returns or after its response is lost.
            var remote = await polp.GetConsentAsync(remoteId, ct);
            if (!Guid.TryParse(remote.ClientUserId, out var correlation)) return false;
            a = await db.PolpInvestmentAuthorizations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == correlation && x.IsSupplemental, ct);
            if (a is null) return false;
            await ApplyAsync(new(a.Id, a.OperationId, a.IntegrationId ?? a.Id, a.InstitutionId, a.PolpConsentId), remote, null, ct);
            a = await db.PolpInvestmentAuthorizations.AsNoTracking().SingleAsync(x => x.Id == a.Id, ct);
        }
        if (a.State == "Cancelled" || a.IntegrationId is null) return true;
        var parent = await db.PolpIntegrations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == a.IntegrationId, ct);
        if (parent is not null) await RefreshAsync(parent, ct);
        return a.IsSupplemental;
    }

    private static DateTime? UtcDate(DateTime? value) => value is null ? null
        : value.Value.Kind == DateTimeKind.Unspecified ? throw new JsonException("Data de autorização sem fuso horário.")
        : value.Value.ToUniversalTime();
    private sealed record Observation(Guid Id, Guid OperationId, Guid ParentId, Guid InstitutionId, Guid? ConsentId);
    private sealed record RefreshSnapshot(PolpIntegration Parent, PolpInvestmentAuthorization? Authorization);
}
