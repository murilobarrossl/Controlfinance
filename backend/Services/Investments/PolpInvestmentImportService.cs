using System.Net;
using System.Text.Json;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services.Investments;

public sealed record InvestmentSource(Guid UserId, Guid IntegrationId, Guid InstitutionId, Guid ConsentId,
    Guid? AuthorizationId, Guid? OperationId, bool Reserved = false);

public sealed class InvestmentSyncDeferredException(string availability, DateTime retryAt)
    : Exception("Aguardando disponibilidade do recurso de investimento.")
{
    public string Availability { get; } = availability;
    public DateTime RetryAt { get; } = retryAt;
}

public class PolpInvestmentImportService(AppDbContext db, IPolpService polp, IIntegrationLock integrationLock,
    IPolpInbox inbox, InvestmentImportStore store, TimeProvider clock, IConfiguration config)
{
    private const int MaxPagesPerRun = 10000;
    private static readonly TimeSpan ReconcileOverlap = TimeSpan.FromDays(7);

    public async Task DiscoverAsync(Guid integrationId, CancellationToken ct)
    {
        var parent = await db.PolpIntegrations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == integrationId && p.Status == "AUTHORISED", ct);
        if (parent is null) return;
        var source = await SourceAsync(parent, reserved: false, ct);
        var discoverySource = source ?? await SourceAsync(parent, true, ct);
        if (discoverySource is null) return;
        var discoveryRun = await ClaimDiscoveryAsync(discoverySource, ct);
        if (discoveryRun is null) return;
        if (source is not null)
        {
            await RequestAsync(source, "sync-schedules", "schedules", "", null, false, ct);
            foreach (var family in PolpInvestmentContracts.Families.Keys)
                await RequestAsync(source, family, "list", "", null, force: false, ct);
        }
        // Reconciliation recovers a crash between a catalog commit and movement enqueue.
        if (source is not null)
        {
            Guid? after = null;
            var recent = clock.GetUtcNow().UtcDateTime.AddHours(-6);
            while (true)
            {
                var query = db.ImportedInvestments.AsNoTracking()
                    .Where(x => x.UserId == source.UserId && x.IntegrationId == integrationId && x.SourceConsentId == source.ConsentId
                        && !db.InvestmentSyncStates.Any(s => s.IntegrationId == integrationId && s.SourceConsentId == source.ConsentId
                            && s.Family == x.Family && s.Kind == "movements" && s.ParentExternalId == x.ExternalId
                            && s.AuthorizationOperationId == source.OperationId && s.Status == "Complete" && s.LastSuccessAt >= recent));
                if (after is { } lastId) query = query.Where(x => x.Id.CompareTo(lastId) > 0);
                var assets = await query
                    .OrderBy(x => x.Id).Select(x => new { x.Id, x.Family, x.ExternalId }).Take(100).ToListAsync(ct);
                if (assets.Count == 0) break;
                foreach (var asset in assets) await RequestAsync(source, asset.Family, "movements", asset.ExternalId, null, false, ct);
                after = assets[^1].Id;
            }
        }
        // Reserved balances belong to the original ACCOUNT consent, not INVESTMENTS.
        var accountSource = await SourceAsync(parent, reserved: true, ct);
        if (accountSource is not null)
        {
            if (accountSource.ConsentId != source?.ConsentId)
                await RequestAsync(accountSource, "account-schedules", "schedules", "", null, false, ct);
            var accounts = await db.BankAccounts.AsNoTracking().Where(x => x.UserId == parent.UserId && x.PolpIntegrationId == parent.PolpIntegrationId
                && x.IsActive && x.HasReservedBalance == true && x.PolpAccountId != null).Select(x => x.PolpAccountId!.Value).ToListAsync(ct);
            foreach (var id in accounts) await RequestAsync(accountSource, "reserved-balances", "reserved", id.ToString(), null, false, ct);
        }
        await db.InvestmentSyncStates.Where(s => s.IntegrationId == discoverySource.IntegrationId && s.RunId == discoveryRun.Value
            && s.Kind == "discovery").ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Complete"), ct);
    }

    private async Task<Guid?> ClaimDiscoveryAsync(InvestmentSource source, CancellationToken ct)
    {
        var claimed = await integrationLock.RunAsync<Guid?>(db, source.IntegrationId, async () =>
        {
            if (!await StillAuthorizedAsync(source, ct)) return null;
            var now = clock.GetUtcNow().UtcDateTime;
            var marker = await db.InvestmentSyncStates.SingleOrDefaultAsync(s => s.IntegrationId == source.IntegrationId
                && s.SourceConsentId == source.ConsentId && s.Family == "all" && s.Kind == "discovery" && s.ParentExternalId == "", ct);
            if (marker is not null && marker.AuthorizationOperationId == source.OperationId && marker.Status == "Running"
                && now - marker.StartedAt < TimeSpan.FromMinutes(5))
            {
                throw new PolpResourceNotReadyException("Descoberta de investimentos em processamento.");
            }
            // Completed discovery is not a cooldown for newly imported accounts. RequestAsync
            // already coalesces each stream and throttles successful provider reads.
            if (marker is null)
            {
                marker = new InvestmentSyncState { UserId = source.UserId, IntegrationId = source.IntegrationId,
                    SourceConsentId = source.ConsentId, Family = "all", Kind = "discovery" };
                db.InvestmentSyncStates.Add(marker);
            }
            marker.AuthorizationId = source.AuthorizationId; marker.AuthorizationOperationId = source.OperationId;
            marker.Status = "Running"; marker.StartedAt = now; marker.RunId = Guid.NewGuid();
            await db.SaveChangesAsync(ct); return marker.RunId;
        }, ct);
        db.ChangeTracker.Clear(); return claimed;
    }

    public async Task HandleReservedAsync(string accountId, PolpQueryWindow? window, CancellationToken ct)
    {
        if (!Guid.TryParse(accountId, out var remote)) throw new PolpPermanentException("Conta de reservas inválida.");
        var account = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.PolpAccountId == remote && x.IsActive, ct);
        if (account is null) throw new PolpResourceNotReadyException("Conta de reservas ainda não importada.");
        var parent = await db.PolpIntegrations.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == account.UserId
            && p.PolpIntegrationId == account.PolpIntegrationId && p.Status == "AUTHORISED", ct);
        if (parent is null) return;
        var source = await SourceAsync(parent, true, ct);
        if (source is not null) await RequestAsync(source, "reserved-balances", "reserved", remote.ToString(), window, true, ct);
    }

    public async Task HandleEventAsync(string family, string resourceId, bool movements, PolpQueryWindow? window, CancellationToken ct)
    {
        PolpInvestmentContracts.Family(family);
        resourceId = PolpInvestmentContracts.ValidateIdentifier(resourceId);
        if (!movements)
        {
            if (!Guid.TryParse(resourceId, out var consentId)) throw new PolpPermanentException("Consentimento de investimentos inválido.");
            var source = await SourceForConsentAsync(consentId, ct);
            if (source is not null) await RequestAsync(source, family, "list", "", window, true, ct);
            return;
        }
        var asset = await db.ImportedInvestments.AsNoTracking().SingleOrDefaultAsync(x => x.Family == family && x.ExternalId == resourceId, ct);
        if (asset is null)
        {
            // Transactions may arrive before the list. The show response supplies consent_id;
            // bind only after resolving that consent to a currently owned authorized source.
            var detail = await polp.GetInvestmentDetailAsync(family, resourceId, ct);
            if (PolpInvestmentContracts.Identifier(detail, "id") != resourceId ||
                !Guid.TryParse(PolpInvestmentContracts.Identifier(detail, "consent_id"), out var consentId))
                throw new PolpPermanentException("Identidade de investimento divergente.");
            var source = await SourceForConsentAsync(consentId, ct);
            if (source is not null) await RequestAsync(source, family, "list", "", null, true, ct);
            throw new PolpResourceNotReadyException("Aguardando importação da identidade do investimento.");
        }
        var ownedSource = await SourceForConsentAsync(asset.SourceConsentId, ct);
        if (ownedSource is null) return;
        if (ownedSource.UserId != asset.UserId || ownedSource.IntegrationId != asset.IntegrationId)
            throw new PolpPermanentException("Vínculo do investimento divergente.");
        await RequestAsync(ownedSource, family, "movements", resourceId, window, true, ct);
    }

    private async Task<InvestmentSource?> SourceForConsentAsync(Guid consentId, CancellationToken ct)
    {
        if (await RemovedPolpResources.IsRemovedAsync(db, consentId, ct)) return null;
        var parent = await db.PolpIntegrations.AsNoTracking().SingleOrDefaultAsync(p => p.PolpIntegrationId == consentId && p.Status == "AUTHORISED", ct);
        if (parent is not null)
        {
            var source = await SourceAsync(parent, false, ct);
            return source?.ConsentId == consentId ? source : null;
        }
        var auth = await db.PolpInvestmentAuthorizations.AsNoTracking().SingleOrDefaultAsync(a => a.PolpConsentId == consentId && a.IntegrationId != null, ct);
        if (auth is null) throw new PolpResourceNotReadyException("Consentimento de investimentos ainda não confirmado.");
        parent = await db.PolpIntegrations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == auth.IntegrationId && p.Status == "AUTHORISED", ct);
        if (parent is null) return null;
        var resolved = await SourceAsync(parent, false, ct);
        return resolved?.ConsentId == consentId ? resolved : null;
    }

    private async Task<InvestmentSource?> SourceAsync(PolpIntegration parent, bool reserved, CancellationToken ct)
    {
        if (parent.Status != "AUTHORISED") return null;
        if (reserved || PolpConsentMetadataService.HasInvestmentsJson(parent.ConsentProductsJson))
            return new(parent.UserId, parent.Id, parent.InstitutionId, parent.PolpIntegrationId, null, null, reserved);
        var auth = await db.PolpInvestmentAuthorizations.AsNoTracking().SingleOrDefaultAsync(a => a.IntegrationId == parent.Id && a.IsSupplemental, ct);
        if (auth?.PolpConsentId is not { } consent || auth.State != "Ready" || auth.ProviderStatus != "AUTHORISED"
            || !PolpConsentMetadataService.HasInvestmentsJson(auth.ProductsJson)) return null;
        return new(parent.UserId, parent.Id, parent.InstitutionId, consent, auth.Id, auth.OperationId);
    }

    private async Task RequestAsync(InvestmentSource source, string family, string kind, string parentExternalId,
        PolpQueryWindow? window, bool force, CancellationToken ct)
    {
        var wake = false;
        var job = await integrationLock.RunAsync(db, source.IntegrationId, async () =>
        {
            if (!await StillAuthorizedAsync(source, ct)) return null;
            var scope = await db.InvestmentSyncStates.SingleOrDefaultAsync(s => s.IntegrationId == source.IntegrationId && s.SourceConsentId == source.ConsentId
                && s.Family == family && s.Kind == kind && s.ParentExternalId == parentExternalId, ct);
            var sameAuthorization = scope is not null && scope.AuthorizationId == source.AuthorizationId && scope.AuthorizationOperationId == source.OperationId;
            var now = clock.GetUtcNow().UtcDateTime;
            if (scope is null)
            {
                scope = new InvestmentSyncState { UserId = source.UserId, IntegrationId = source.IntegrationId, SourceConsentId = source.ConsentId,
                    Family = family, Kind = kind, ParentExternalId = parentExternalId };
                db.InvestmentSyncStates.Add(scope);
            }
            else if (scope.AuthorizationId == source.AuthorizationId && scope.AuthorizationOperationId == source.OperationId
                && (scope.Status is "Pending" or "Running" or "Reading" or "Error"))
            {
                // Identical windows can describe a later provider batch. Once a page was
                // read, coalesce a new notification into one replay instead of losing it.
                if (force && (scope.Status != "Pending" || scope.PageNumber > 0 || window?.Query != scope.WindowQuery)) scope.ReplayRequested = true;
                wake = scope.Status == "Error" && scope.ErrorCode == "provider_resource_pending"
                    && (force || scope.LastAttemptAt is null || scope.LastAttemptAt < now.AddMinutes(-5));
                await db.SaveChangesAsync(ct);
                return scope;
            }
            else if (!force && scope.AuthorizationId == source.AuthorizationId && scope.AuthorizationOperationId == source.OperationId
                && scope.Status == "Complete" && now - scope.LastSuccessAt < TimeSpan.FromHours(6)) return null;
            if (!force && scope.Status == "Complete" && kind == "movements" && scope.AuthorizationOperationId == source.OperationId
                && scope.LastSuccessAt > now.AddHours(-24))
            {
                var resource = PolpInvestmentContracts.ScheduleResource(family) + ".transactions";
                var schedule = await db.InvestmentSyncSchedules.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == source.UserId
                    && s.IntegrationId == source.IntegrationId && s.SourceConsentId == source.ConsentId && s.Resource == resource && s.ExternalId == parentExternalId, ct);
                if (schedule is { LastSyncAt: not null, NextSyncAt: not null } && schedule.LastSyncAt <= scope.LastSuccessAt
                    && schedule.NextSyncAt > now && schedule.ObservedAt > now.AddHours(-24)) return null;
            }
            scope.AuthorizationId = source.AuthorizationId; scope.AuthorizationOperationId = source.OperationId;
            if (!sameAuthorization) { scope.LastSuccessAt = null; scope.LastFullSuccessAt = null; }
            scope.Status = "Pending"; scope.RunId = Guid.NewGuid(); scope.Cursor = null; scope.SeenCursorsJson = "[]";
            scope.PageNumber = 0; scope.StartedAt = now; scope.ErrorCode = null; scope.ReplayRequested = false;
            scope.Availability = "awaiting_sync";
            // Full initial/monthly reconciliation; incremental movement scans overlap seven days.
            var useDelta = scope.LastSuccessAt.HasValue && scope.LastFullSuccessAt > now.AddDays(-30);
            scope.WindowQuery = window?.Query ?? (kind == "movements" && useDelta
                ? PolpQueryWindow.ChangedSince(new DateTimeOffset(DateTime.SpecifyKind(scope.LastSuccessAt!.Value, DateTimeKind.Utc)) - ReconcileOverlap).Query : null);
            await db.SaveChangesAsync(ct);
            return scope;
        }, ct);
        db.ChangeTracker.Clear();
        if (job is null || await QueueAsync(job, ct) || !wake) return;
        var resourceId = job.Id.ToString();
        var query = $"run={job.RunId:D}&page={job.PageNumber}";
        var due = PolpInboxTime.Now(clock);
        await db.PolpWebhookEvents.Where(e => e.Event == PolpInternalEvents.InvestmentPage && e.ResourceId == resourceId
                && e.QueryParameters == query && e.Status == PolpWebhookEventStatus.Pending && e.NextAttemptAt > due)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.NextAttemptAt, due), ct);
    }

    public async Task ProcessPageAsync(Guid scopeId, CancellationToken ct, Guid? expectedRun = null, int? expectedPage = null)
    {
        var scope = await db.InvestmentSyncStates.AsNoTracking().SingleOrDefaultAsync(s => s.Id == scopeId, ct);
        if (scope is null || scope.Status is "Complete" or "Blocked") return;
        if ((expectedRun.HasValue && scope.RunId != expectedRun) || (expectedPage.HasValue && scope.PageNumber != expectedPage)) return;
        var source = await SourceFromScopeAsync(scope, ct);
        if (source is null) { await BlockAsync(scope, "authorization_changed", ct); return; }
        // Several notifications may address the same stream. Claim a short persistent read
        // lease before network calls, so multiple nodes do not fetch the same page at once.
        var readAt = PolpInboxTime.Now(clock);
        var claimed = await integrationLock.RunAsync(db, scope.IntegrationId, async () =>
        {
            var current = await db.InvestmentSyncStates.SingleOrDefaultAsync(s => s.Id == scope.Id && s.RunId == scope.RunId && s.PageNumber == scope.PageNumber, ct);
            if (current is null || current.Status is "Complete" or "Blocked"
                || (current.Status == "Reading" && current.LastAttemptAt > readAt.AddMinutes(-5))) return false;
            current.Status = "Reading"; current.LastAttemptAt = readAt;
            await db.SaveChangesAsync(ct); return true;
        }, ct);
        db.ChangeTracker.Clear();
        if (!claimed) throw new PolpResourceNotReadyException("Página de investimentos em processamento ou alterada.");
        scope.LastAttemptAt = readAt;
        try
        {
            // One bounded page per inbox item. The provider consent is read via authenticated
            // API, never inferred from a callback status or from the frontend.
            var remote = await polp.GetConsentAsync(source.ConsentId, ct);
            if (!await RemoteAuthorizedAsync(source, remote, ct)) { await BlockAsync(scope, "not_authorized", ct); return; }
            var partial = false;
            if (scope.Kind is "list" or "movements" or "reserved")
            {
                if (remote.ExecutionStatus is null or "AWAITING_RESOURCES") throw await DeferredAsync(scope, "awaiting_sync", ct);
                var resources = (await polp.GetConsentResourcesAsync(source.ConsentId, ct))
                    .Where(r => r.Type == (source.Reserved ? "ACCOUNT" : PolpInvestmentContracts.ResourceType(scope.Family))).ToList();
                if (scope.Kind == "reserved")
                    resources = resources.Where(r => r.ResourceId is not null && PolpInvestmentContracts.ValidateIdentifier(r.ResourceId) == scope.ParentExternalId).ToList();
                var ready = resources.Any(r => r.Status == "AVAILABLE" && !string.IsNullOrWhiteSpace(r.ResourceId));
                partial = ready && resources.Any(r => r.Status != "AVAILABLE" || string.IsNullOrWhiteSpace(r.ResourceId));
                if (!ready && resources.Count > 0)
                {
                    var state = resources.Any(r => r.Status == "PENDING_AUTHORISATION") ? "pending_authorization"
                        : resources.Any(r => r.Status == "TEMPORARILY_UNAVAILABLE") ? "temporarily_unavailable"
                        : resources.Any(r => r.Status == "AVAILABLE") ? "awaiting_sync"
                        : resources.All(r => r.Status == "UNAVAILABLE") ? "resource_unavailable" : "unrecognized_resource_status";
                    if (state is "temporarily_unavailable" or "awaiting_sync" or "unrecognized_resource_status") throw await DeferredAsync(scope, state, ct);
                    await BlockAsync(scope, state, ct); return;
                }
                if (!ready && (scope.Kind is "movements" or "reserved")) throw await DeferredAsync(scope, "awaiting_resources", ct);
            }
            var parentId = scope.Kind == "list" ? source.ConsentId.ToString() : scope.ParentExternalId;
            var page = scope.Kind == "schedules"
                ? await polp.GetInvestmentSchedulePageAsync(source.ConsentId, scope.Cursor, ct)
                : await polp.GetInvestmentPageAsync(scope.Family, parentId, scope.Kind == "movements", scope.Cursor,
                    PolpQueryWindow.TryParse(scope.WindowQuery), ct);
            var imported = new List<ImportedInvestment>();
            InvestmentSyncState? continuation = null;
            await integrationLock.RunAsync(db, scope.IntegrationId, async () =>
            {
                if (!await StillAuthorizedAsync(source, ct)) throw new PermissionChangedException();
                var current = await db.InvestmentSyncStates.SingleOrDefaultAsync(s => s.Id == scope.Id && s.RunId == scope.RunId
                    && s.PageNumber == scope.PageNumber && s.LastAttemptAt == readAt, ct);
                if (current is null || current.Status is "Complete" or "Blocked") return false;
                if (current.PageNumber >= MaxPagesPerRun) throw new PolpPermanentException("Paginação de investimentos excedeu o limite de segurança.");
                var seen = JsonSerializer.Deserialize<HashSet<string>>(current.SeenCursorsJson) ?? [];
                if (page.NextCursor is { } next && (next == current.Cursor || !seen.Add(PolpInvestmentContracts.Hash(System.Text.Json.Nodes.JsonValue.Create(next)!))))
                    throw new PolpPermanentException("Ciclo na paginação de investimentos.");
                var now = clock.GetUtcNow().UtcDateTime;
                if (scope.Kind == "list") imported.AddRange(await store.CatalogAsync(scope, page.Data, now, ct));
                else if (scope.Kind == "movements") await store.MovementsAsync(scope, page.Data, now, DataRetentionPolicy.SyncedHistoryCutoff(config, now), ct);
                else if (scope.Kind == "schedules") await store.SchedulesAsync(scope, page.Data, now, ct);
                else await store.ReservedAsync(scope, page.Data, now, ct);
                current.Cursor = page.NextCursor; current.PageNumber++; current.SeenCursorsJson = JsonSerializer.Serialize(seen);
                current.LastAttemptAt = now; current.ErrorCode = null;
                if (partial) current.Availability = "partially_available";
                else if (page.Data.Count > 0 && current.Availability != "partially_available") current.Availability = "available";
                else if (current.PageNumber == 1) current.Availability = current.WindowQuery is null ? "none_reported" : "no_changes";
                current.Status = page.NextCursor is null ? "Complete" : "Running";
                if (page.NextCursor is null)
                {
                    current.LastSuccessAt = current.StartedAt;
                    if (current.WindowQuery is null && current.Availability != "partially_available") current.LastFullSuccessAt = current.StartedAt;
                    if (scope.Kind == "schedules")
                        await db.InvestmentSyncSchedules.Where(s => s.UserId == source.UserId && s.IntegrationId == source.IntegrationId
                            && s.SourceConsentId == source.ConsentId && s.ObservedRunId != scope.RunId).ExecuteDeleteAsync(ct);
                    if (current.ReplayRequested)
                    {
                        current.RunId = Guid.NewGuid(); current.Cursor = null; current.WindowQuery = null;
                        current.SeenCursorsJson = "[]"; current.PageNumber = 0; current.StartedAt = now;
                        current.Status = "Pending"; current.ReplayRequested = false;
                        current.Availability = "awaiting_sync";
                    }
                }
                await db.SaveChangesAsync(ct);
                // Disconnect marks the parent before acquiring this lock. A late marking rolls
                // back the entire page, including snapshots and movement upserts.
                var parentQuery = db.PolpIntegrations.Where(p => p.Id == source.IntegrationId && p.UserId == source.UserId && p.Status == "AUTHORISED");
                var touched = scope.Kind == "schedules"
                    ? await parentQuery.ExecuteUpdateAsync(s => s.SetProperty(p => p.LastSyncedAt, p => p.LastSyncedAt), ct)
                    : await parentQuery.ExecuteUpdateAsync(s => s.SetProperty(p => p.LastSyncedAt, now), ct);
                if (touched != 1) throw new PermissionChangedException();
                continuation = current.Status == "Complete" ? null : current;
                return true;
            }, ct);
            db.ChangeTracker.Clear();
            foreach (var asset in imported) await RequestAsync(source, asset.Family, "movements", asset.ExternalId, null, false, ct);
            if (continuation is not null) await QueueAsync(continuation, ct);
        }
        catch (PermissionChangedException)
        {
            db.ChangeTracker.Clear(); await BlockAsync(scope, "authorization_changed", ct);
        }
        catch (InvestmentSyncDeferredException ex)
        {
            db.ChangeTracker.Clear();
            await db.InvestmentSyncStates.Where(s => s.Id == scope.Id && s.RunId == scope.RunId && s.PageNumber == scope.PageNumber && s.LastAttemptAt == readAt)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Error").SetProperty(x => x.Availability, ex.Availability)
                    .SetProperty(x => x.ErrorCode, "provider_resource_pending"), ct);
            throw;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            db.ChangeTracker.Clear();
            var code = ex is HttpRequestException http ? $"http_{(int?)http.StatusCode}" : ex is PolpPermanentException or JsonException ? "invalid_contract"
                : ex is PolpResourceNotReadyException ? "resource_pending" : "import_interrupted";
            var availability = ex is HttpRequestException { StatusCode: HttpStatusCode.Forbidden } ? "access_unavailable"
                : ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound } ? "resource_unavailable" : "unconfirmed";
            if (!ct.IsCancellationRequested)
                await db.InvestmentSyncStates.Where(s => s.Id == scope.Id && s.RunId == scope.RunId && s.PageNumber == scope.PageNumber && s.LastAttemptAt == readAt)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Error").SetProperty(x => x.ErrorCode, code)
                        .SetProperty(x => x.Availability, availability).SetProperty(x => x.LastAttemptAt, clock.GetUtcNow().UtcDateTime), ct);
            throw;
        }
    }

    private async Task<InvestmentSyncDeferredException> DeferredAsync(InvestmentSyncState scope, string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        // Carga inicial usa só o backoff da inbox, sem esperar o cronograma periódico.
        if (scope.LastSuccessAt is null) return new(reason, now);
        var prefix = scope.Kind == "reserved" ? "account.reserved_balances" : PolpInvestmentContracts.ScheduleResource(scope.Family);
        var next = await db.InvestmentSyncSchedules.AsNoTracking().Where(s => s.UserId == scope.UserId && s.IntegrationId == scope.IntegrationId
            && s.SourceConsentId == scope.SourceConsentId && (s.Resource == prefix || s.Resource == prefix + ".balances" || s.Resource == prefix + ".transactions")
            && (scope.Kind == "list" || s.ExternalId == scope.ParentExternalId) && s.ObservedAt >= now.AddHours(-24) && s.NextSyncAt > now)
            .Select(s => s.NextSyncAt).MinAsync(ct);
        // This is an application retry delay, never an invented provider sync schedule.
        return new(reason, next > now.AddMinutes(15) ? next.Value : now.AddMinutes(15));
    }

    private async Task<InvestmentSource?> SourceFromScopeAsync(InvestmentSyncState scope, CancellationToken ct)
    {
        var parent = await db.PolpIntegrations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == scope.IntegrationId && p.UserId == scope.UserId && p.Status == "AUTHORISED", ct);
        if (parent is null) return null;
        var source = await SourceAsync(parent, scope.Kind == "reserved" || scope.Family == "account-schedules", ct);
        return source?.ConsentId == scope.SourceConsentId && source.AuthorizationId == scope.AuthorizationId && source.OperationId == scope.AuthorizationOperationId ? source : null;
    }
    private async Task<bool> StillAuthorizedAsync(InvestmentSource source, CancellationToken ct)
    {
        var parent = await db.PolpIntegrations.AsNoTracking().SingleOrDefaultAsync(p => p.Id == source.IntegrationId && p.UserId == source.UserId
            && p.InstitutionId == source.InstitutionId && p.Status == "AUTHORISED", ct);
        if (parent is null || await RemovedPolpResources.IsRemovedAsync(db, source.ConsentId, ct)) return false;
        var current = await SourceAsync(parent, source.Reserved, ct);
        return current?.ConsentId == source.ConsentId && current.AuthorizationId == source.AuthorizationId && current.OperationId == source.OperationId;
    }
    private async Task<bool> RemoteAuthorizedAsync(InvestmentSource source, PolpConsentDto remote, CancellationToken ct)
    {
        if (remote.Id != source.ConsentId || remote.InstitutionId != source.InstitutionId)
            throw new PolpPermanentException("Identidade do consentimento de investimentos divergente.");
        if (source.AuthorizationId.HasValue)
        {
            if (remote.ClientUserId != source.AuthorizationId.Value.ToString()) throw new PolpPermanentException("Titularidade da autorização divergente.");
        }
        else if (remote.ClientUserId is not null && remote.ClientUserId != source.UserId.ToString()) throw new PolpPermanentException("Titularidade do consentimento divergente.");
        return remote.Status == "AUTHORISED" && (source.Reserved ? remote.Products?.Contains("ACCOUNT") == true : PolpConsentMetadataService.HasInvestments(remote.Products))
            && await StillAuthorizedAsync(source, ct);
    }
    private Task BlockAsync(InvestmentSyncState scope, string reason, CancellationToken ct) => db.InvestmentSyncStates
        .Where(s => s.Id == scope.Id && s.RunId == scope.RunId && s.PageNumber == scope.PageNumber && s.LastAttemptAt == scope.LastAttemptAt)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Blocked").SetProperty(x => x.Availability, reason).SetProperty(x => x.ErrorCode, reason), ct);
    private Task<bool> QueueAsync(InvestmentSyncState state, CancellationToken ct) => inbox.EnqueueAsync(new(
        PolpInternalEvents.InvestmentPage, "investment-sync", state.Id.ToString(), $"run={state.RunId:D}&page={state.PageNumber}",
        state.LastSuccessAt is null ? PolpInboxPriority.High : PolpInboxPriority.Low), ct);
    private sealed class PermissionChangedException : Exception;
}
