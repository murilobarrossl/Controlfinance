using System.Text;
using System.Text.Json;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Services.Investments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

[ApiController]
[EnableRateLimiting(InvestmentReadRateLimit.Policy)]
[InvestmentReadMetrics]
[Route("api/investments")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class InvestmentsController(AppDbContext db, TimeProvider clock) : ApiControllerBase
{
    private IQueryable<ImportedInvestment> Owned => db.ImportedInvestments.AsNoTracking().Where(x => x.UserId == UserId
        && db.PolpIntegrations.Any(p => p.Id == x.IntegrationId && p.UserId == UserId && p.Status != PolpDisconnectService.PendingStatus && !PolpAuthorizationEnd.EndedStatuses.Contains(p.Status)));
    private IQueryable<ImportedReservedBalance> OwnedReserves => db.ImportedReservedBalances.AsNoTracking().Where(x => x.UserId == UserId
        && db.PolpIntegrations.Any(p => p.Id == x.IntegrationId && p.UserId == UserId && p.Status != PolpDisconnectService.PendingStatus && !PolpAuthorizationEnd.EndedStatuses.Contains(p.Status)));

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] Guid? integrationId, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Id == UserId && u.DocumentType == DocumentTypes.Cnpj, ct)) return Forbid();
        const int limit = 3;
        var assets = Owned.Where(a => integrationId == null || a.IntegrationId == integrationId);
        var institutions = db.PolpIntegrations.AsNoTracking().Where(p => p.UserId == UserId);
        var rows = await (from a in assets
            join bank in institutions on a.IntegrationId equals bank.Id
            join pos in db.InvestmentPositions on a.Id equals pos.InvestmentId into positions
            from pos in positions.DefaultIfEmpty()
            orderby a.LastObservedAt descending, a.Id
            select new { a.Id, a.Name, a.Family, a.ProductType, a.IdentityJson, bank.InstitutionName,
                Values = pos == null ? null : pos.ValuesJson, Availability = pos == null ? null : pos.AvailabilityJson,
                ReferenceAt = pos == null ? null : pos.ReferenceAt })
            .Take(limit).ToListAsync(ct);
        var products = new List<object>(limit);
        products.AddRange(rows.Select(a => new { a.Id, a.Name, a.Family, a.ProductType, a.InstitutionName,
            reserved = false, identity = new { values = Json(a.IdentityJson) },
            position = a.Values == null ? null : new { a.ReferenceAt, document = Document(a.Values, a.Availability!) } }));
        if (products.Count < limit)
        {
            var reserves = await (from r in OwnedReserves
                join bank in institutions on r.IntegrationId equals bank.Id
                where integrationId == null || r.IntegrationId == integrationId
                orderby r.LastObservedAt descending, r.Id
                select new { r.Id, r.Name, r.ValuesJson, r.AvailabilityJson, bank.InstitutionName })
                .Take(limit - products.Count).ToListAsync(ct);
            products.AddRange(reserves.Select(r => new { r.Id, r.Name, r.InstitutionName, reserved = true,
                document = Document(r.ValuesJson, r.AvailabilityJson) }));
        }
        return Ok(products);
    }

    [HttpGet("connections")]
    public async Task<IActionResult> Connections([FromQuery] string? cursor, CancellationToken ct)
    {
        if (!TryCursor(cursor, out var after)) return BadRequest();
        var query = db.PolpIntegrations.AsNoTracking().Where(p => p.UserId == UserId && p.Status != PolpDisconnectService.PendingStatus);
        if (after is not null) query = query.Where(p => p.CreatedAt > after.At || (p.CreatedAt == after.At && p.Id.CompareTo(after.Id) > 0));
        var roots = await query.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).Take(101).ToListAsync(ct);
        if (roots.Count == 0) return Ok(new { data = Array.Empty<object>(), nextCursor = (string?)null });
        var page = roots.Take(100).ToList();
        var ids = page.Select(p => p.Id).ToArray();
        var remoteIds = page.Select(p => p.PolpIntegrationId).ToArray();
        var auths = await db.PolpInvestmentAuthorizations.AsNoTracking().Where(a => a.IntegrationId != null && ids.Contains(a.IntegrationId.Value))
            .Select(a => new { a.IntegrationId, a.PolpConsentId, a.State, a.ProviderStatus, a.ProductsJson, a.AuthorizationUrlExpiresAt }).ToListAsync(ct);
        var streams = await db.InvestmentSyncStates.AsNoTracking().Where(s => s.UserId == UserId && ids.Contains(s.IntegrationId) && (s.Kind == "list" || s.Kind == "reserved" || s.Kind == "discovery"))
            .Select(s => new { s.IntegrationId, s.SourceConsentId, s.Kind, s.Status, s.Availability, s.LastFullSuccessAt,
                s.StartedAt, s.LastAttemptAt, s.ErrorCode }).ToListAsync(ct);
        var withInvestments = (await db.ImportedInvestments.AsNoTracking().Where(x => x.UserId == UserId && ids.Contains(x.IntegrationId))
            .Select(x => x.IntegrationId).Distinct().ToListAsync(ct)).ToHashSet();
        var withReserves = (await db.ImportedReservedBalances.AsNoTracking().Where(x => x.UserId == UserId && ids.Contains(x.IntegrationId))
            .Select(x => x.IntegrationId).Distinct().ToListAsync(ct)).ToHashSet();
        static bool StillConfirmed(DateTime? lastFullSuccessAt, string availability) =>
            lastFullSuccessAt != null && availability is "awaiting_sync" or "no_changes";
        var rootKeys = ids.Select(id => id.ToString("D")).ToArray();
        var consentKeys = remoteIds.Select(id => id.ToString("D")).ToArray();
        // Only control jobs belonging to this user's selected connections, never error bodies.
        var jobs = await db.PolpWebhookEvents.AsNoTracking().Where(e =>
                (e.Event == PolpInternalEvents.InvestmentDiscovery && rootKeys.Contains(e.ResourceId!))
                || ((e.Event == PolpInternalEvents.ConsentMetadata || e.Event == PolpInternalEvents.Backfill)
                    && consentKeys.Contains(e.ResourceId!)))
            .GroupBy(e => new { e.Event, e.ResourceId })
            .Select(g => g.OrderByDescending(e => e.ReceivedAt).ThenByDescending(e => e.Id)
                .Select(e => new { e.Event, e.ResourceId, e.Status }).First()).ToListAsync(ct);
        var staleBefore = clock.GetUtcNow().UtcDateTime.AddMinutes(-15);
        var flags = await db.BankAccounts.AsNoTracking().Where(a => a.UserId == UserId && a.IsActive && a.PolpAccountType != CreditCardAccountDetector.CardType
            && a.PolpIntegrationId != null && remoteIds.Contains(a.PolpIntegrationId.Value))
            .Select(a => new { a.PolpIntegrationId, a.HasReservedBalance }).ToListAsync(ct);
        return Ok(new { data = page.Select(p =>
        {
            var auth = auths.SingleOrDefault(a => a.IntegrationId == p.Id);
            var discoveryFailed = jobs.Any(e => e.Event == PolpInternalEvents.InvestmentDiscovery
                && e.ResourceId == p.Id.ToString("D") && e.Status == PolpWebhookEventStatus.Dead);
            var metadataFailed = jobs.Any(e => e.Event == PolpInternalEvents.ConsentMetadata
                && e.ResourceId == p.PolpIntegrationId.ToString("D") && e.Status == PolpWebhookEventStatus.Dead);
            var accountsFailed = jobs.Any(e => e.Event == PolpInternalEvents.Backfill
                && e.ResourceId == p.PolpIntegrationId.ToString("D") && e.Status == PolpWebhookEventStatus.Dead);
            var primary = PolpConsentMetadataService.HasInvestmentsJson(p.ConsentProductsJson);
            var permitted = primary || (auth?.State == "Ready" && auth.ProviderStatus == "AUTHORISED" && PolpConsentMetadataService.HasInvestmentsJson(auth.ProductsJson));
            var source = primary ? (Guid?)p.PolpIntegrationId : auth?.PolpConsentId;
            var catalogs = streams.Where(s => s.IntegrationId == p.Id && s.SourceConsentId == source && s.Kind == "list").ToList();
            string state;
            if (p.Status == "EXPIRED") state = "expired";
            else if (p.Status == "REVOKED") state = "revoked";
            else if (p.Status == "REJECTED") state = "rejected";
            else if (p.Status != "AUTHORISED") state = "pending_authorization";
            else if (p.ConsentProductsJson is null && auth is null)
                state = metadataFailed ? "temporary_error" : p.CreatedAt < staleBefore ? "sync_delayed" : "checking";
            else if (auth is { State: "Ready", ProviderStatus: "EXPIRED" } && auth.PolpConsentId == source) state = "expired";
            else if (auth is { State: "Ready", ProviderStatus: "REVOKED" } && auth.PolpConsentId == source) state = "revoked";
            else if (!permitted)
            {
                if (auth?.State is "Creating" or "Uncertain") state = "pending_authorization";
                else if (auth?.State == "Deferred") state = "temporary_error";
                else if (auth?.ProviderStatus == "EXPIRED") state = "expired";
                else if (auth?.ProviderStatus == "REVOKED" || auth?.State == "Cancelled") state = "revoked";
                else if (auth?.ProviderStatus == "REJECTED") state = "rejected";
                else if (auth?.ProviderStatus == "AWAITING_AUTHORIZATION" && auth.AuthorizationUrlExpiresAt <= clock.GetUtcNow().UtcDateTime) state = "expired";
                else if (auth?.ProviderStatus == "AWAITING_AUTHORIZATION") state = "pending_authorization";
                else if (auth?.ProviderStatus == "AUTHORISED") state = "not_authorized";
                else state = "authorization_required";
            }
            else if (catalogs.Any(s => s.Availability == "pending_authorization")) state = "pending_authorization";
            else if (catalogs.Any(s => s.Availability is "not_authorized" or "authorization_changed" or "access_unavailable")) state = "resource_access_unconfirmed";
            else if (catalogs.Any(s => s.Status == "Error" && s.ErrorCode != "provider_resource_pending")) state = "temporary_error";
            else if (catalogs.Any(s => s.Availability == "temporarily_unavailable")) state = "temporarily_unavailable";
            else if (catalogs.Any(s => s.Availability == "resource_unavailable")) state = "resource_unavailable";
            else if (catalogs.Count == PolpInvestmentContracts.Families.Count && catalogs.All(s => s.Status == "Complete" && s.LastFullSuccessAt.HasValue && s.Availability == "none_reported")) state = "none_reported";
            else if (catalogs.Any(s => s.Availability is "available" or "partially_available")
                || (withInvestments.Contains(p.Id) && catalogs.Any(s => StillConfirmed(s.LastFullSuccessAt, s.Availability)))) state = "available";
            else if (discoveryFailed) state = "temporary_error";
            else if ((catalogs.Count == 0 && p.CreatedAt < staleBefore)
                || catalogs.Any(s => s.Status != "Complete" && s.ErrorCode != "provider_resource_pending"
                    && (s.LastAttemptAt ?? s.StartedAt) < staleBefore)) state = "sync_delayed";
            else state = "awaiting_sync";
            var accountFlags = flags.Where(a => a.PolpIntegrationId == p.PolpIntegrationId).ToList();
            var reserves = streams.Where(s => s.IntegrationId == p.Id && s.SourceConsentId == p.PolpIntegrationId && s.Kind == "reserved").ToList();
            string reservedState;
            if (p.Status != "AUTHORISED") reservedState = state;
            else if (p.ConsentProductsJson is not null
                && JsonSerializer.Deserialize<string[]>(p.ConsentProductsJson)?.Contains("ACCOUNT") != true) reservedState = "not_authorized";
            else if (reserves.Any(s => s.Availability is "not_authorized" or "authorization_changed" or "access_unavailable" or "pending_authorization")) reservedState = "not_authorized";
            else if (reserves.Any(s => s.Status == "Error" && s.ErrorCode != "provider_resource_pending")) reservedState = "temporary_error";
            else if (reserves.Any(s => s.Availability == "temporarily_unavailable")) reservedState = "temporarily_unavailable";
            else if (reserves.Any(s => s.Availability == "resource_unavailable")) reservedState = "resource_unavailable";
            else if (reserves.Any(s => s.Availability == "available")
                || (withReserves.Contains(p.Id) && reserves.Any(s => StillConfirmed(s.LastFullSuccessAt, s.Availability)))) reservedState = "available";
            else if ((p.SyncedLocally && accountFlags.Count > 0 && accountFlags.All(a => a.HasReservedBalance == false))
                || (reserves.Count > 0 && reserves.All(s => s.Status == "Complete" && s.Availability == "none_reported")
                    && accountFlags.Count > 0 && accountFlags.All(a => a.HasReservedBalance.HasValue))) reservedState = "none_reported";
            else if (discoveryFailed || (accountsFailed && !p.SyncedLocally)) reservedState = "temporary_error";
            else if (p.SyncedLocally && (accountFlags.Count == 0 || accountFlags.Any(a => a.HasReservedBalance is null))) reservedState = "information_not_provided";
            else if ((reserves.Count == 0 && p.CreatedAt < staleBefore)
                || reserves.Any(s => s.Status != "Complete" && s.ErrorCode != "provider_resource_pending"
                    && (s.LastAttemptAt ?? s.StartedAt) < staleBefore)) reservedState = "sync_delayed";
            else reservedState = "awaiting_sync";
            return new { p.Id, p.InstitutionId, p.InstitutionName, connectionStatus = p.Status, status = state, reservedStatus = reservedState,
                investmentConsentId = source, accountConsentId = p.PolpIntegrationId, p.LastSyncedAt };
        }).ToArray(), nextCursor = roots.Count > 100 ? Cursor(page[^1].CreatedAt, page[^1].Id) : null });
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? cursor, [FromQuery] int limit = 50, CancellationToken ct = default,
        [FromQuery] Guid? integrationId = null)
    {
        if (!TryCursor(cursor, out var after) || limit is < 1 or > 100) return BadRequest();
        var query = Owned;
        if (integrationId.HasValue)
        {
            if (!await db.PolpIntegrations.AnyAsync(p => p.Id == integrationId.Value && p.UserId == UserId
                && p.Status != PolpDisconnectService.PendingStatus, ct)) return NotFound();
            query = query.Where(x => x.IntegrationId == integrationId.Value);
        }
        if (after is not null) query = query.Where(x => x.FirstObservedAt > after.At || (x.FirstObservedAt == after.At && x.Id.CompareTo(after.Id) > 0));
        var rows = await query.OrderBy(x => x.FirstObservedAt).ThenBy(x => x.Id).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToList();
        return Ok(new
        {
            data = await CatalogItemsAsync(page, ct),
            nextCursor = rows.Count > limit ? Cursor(page[^1].FirstObservedAt, page[^1].Id) : null,
            numericEncoding = "exact-base10-strings"
        });
    }

    private async Task<object[]> CatalogItemsAsync(List<ImportedInvestment> page, CancellationToken ct)
    {
        if (page.Count == 0) return [];
        var ids = page.Select(x => x.Id).ToArray();
        var positions = await db.InvestmentPositions.AsNoTracking().Where(x => ids.Contains(x.InvestmentId)).ToDictionaryAsync(x => x.InvestmentId, ct);
        var integrations = page.Select(x => x.IntegrationId).Distinct().ToArray();
        var catalogs = await db.InvestmentSyncStates.AsNoTracking().Where(x => x.UserId == UserId
            && integrations.Contains(x.IntegrationId) && x.Kind == "list")
            .Select(x => new { x.IntegrationId, x.SourceConsentId, x.Family, x.LastFullSuccessAt }).ToListAsync(ct);
        var lastScans = catalogs.ToDictionary(x => (x.IntegrationId, x.SourceConsentId, x.Family), x => x.LastFullSuccessAt);
        return page.Select(x => new { x.Id, x.IntegrationId, x.SourceConsentId, x.Family, x.ExternalId, x.Name, x.ProductType,
            identity = Document(x.IdentityJson, x.IdentityAvailabilityJson),
            catalogPresence = Presence(x.LastObservedAt, lastScans.GetValueOrDefault((x.IntegrationId, x.SourceConsentId, x.Family))),
            position = positions.TryGetValue(x.Id, out var p) ? Position(p) : null,
            x.FirstObservedAt, x.LastObservedAt }).ToArray();
    }

    public sealed record ResourceIds(Guid[] Investments, Guid[] Reserves);

    [HttpPost("linked-resources")]
    public async Task<IActionResult> LinkedResources([FromBody] ResourceIds requested, CancellationToken ct)
    {
        if (requested.Investments == null || requested.Reserves == null
            || (long)requested.Investments.Length + requested.Reserves.Length > 100) return BadRequest();
        if (!await InvestmentHolder.AllowsAsync(db, UserId, ct)) return Forbid();
        var assets = requested.Investments.Length == 0 ? new List<ImportedInvestment>()
            : await Owned.Where(x => requested.Investments.Contains(x.Id)).ToListAsync(ct);
        var reserves = requested.Reserves.Length == 0 ? new List<ImportedReservedBalance>()
            : await OwnedReserves.Where(x => requested.Reserves.Contains(x.Id)).ToListAsync(ct);
        return Ok(new { investments = await CatalogItemsAsync(assets, ct), reserves = reserves.Select(ReserveItem) });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct)
    {
        var asset = await Owned.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (asset is null) return NotFound();
        var parent = await db.PolpIntegrations.AsNoTracking().SingleAsync(p => p.Id == asset.IntegrationId && p.UserId == UserId, ct);
        var position = await db.InvestmentPositions.AsNoTracking().SingleOrDefaultAsync(x => x.InvestmentId == id, ct);
        var contract = await db.InvestmentContracts.AsNoTracking().SingleOrDefaultAsync(x => x.InvestmentId == id, ct);
        var streams = await db.InvestmentSyncStates.AsNoTracking().Where(x => x.UserId == UserId && x.IntegrationId == parent.Id
            && x.SourceConsentId == asset.SourceConsentId && x.Family == asset.Family && (x.Kind == "list" || x.ParentExternalId == asset.ExternalId))
            .Select(x => new { x.Kind, x.Status, x.Availability, x.LastAttemptAt, x.LastSuccessAt, x.ErrorCode, x.PageNumber }).ToListAsync(ct);
        var authorization = await db.PolpInvestmentAuthorizations.AsNoTracking().Where(a => a.IntegrationId == parent.Id
            && a.PolpConsentId == asset.SourceConsentId).Select(a => new { a.State, a.ProviderStatus }).SingleOrDefaultAsync(ct);
        var lastFullScan = await db.InvestmentSyncStates.AsNoTracking().Where(x => x.UserId == UserId
            && x.IntegrationId == parent.Id && x.SourceConsentId == asset.SourceConsentId && x.Family == asset.Family && x.Kind == "list")
            .Select(x => x.LastFullSuccessAt).SingleOrDefaultAsync(ct);
        return Ok(new
        {
            asset.Id, asset.IntegrationId, asset.SourceConsentId, asset.Family, asset.ExternalId, asset.Name, asset.ProductType,
            institution = new { parent.InstitutionId, parent.InstitutionName },
            sourceConnectionStatus = parent.Status,
            sourceAuthorization = authorization,
            catalogPresence = Presence(asset.LastObservedAt, lastFullScan),
            identity = Document(asset.IdentityJson, asset.IdentityAvailabilityJson),
            contract = contract is null ? null : Document(contract.ValuesJson, contract.AvailabilityJson, contract.ProvenanceJson),
            position = position is null ? null : Position(position), synchronization = streams,
            projection = InvestmentFinancialRules.Projection(asset, contract),
            asset.FirstObservedAt, asset.LastObservedAt,
            positionTaxMeaning = asset.Family == "funds" ? "provisions_reported_in_position" : "taxes_reported_in_position_not_proof_of_withholding",
            numericEncoding = "exact-base10-strings"
        });
    }

    [HttpGet("{id:guid}/positions")]
    public async Task<IActionResult> Positions(Guid id, [FromQuery] string? cursor, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        if (!await Owned.AnyAsync(x => x.Id == id, ct)) return NotFound();
        if (!TryCursor(cursor, out var after) || limit is < 1 or > 100) return BadRequest();
        var query = db.InvestmentPositionObservations.AsNoTracking().Where(x => x.InvestmentId == id);
        if (after is not null) query = query.Where(x => x.ObservedAt > after.At || (x.ObservedAt == after.At && x.Id.CompareTo(after.Id) > 0));
        var rows = await query.OrderBy(x => x.ObservedAt).ThenBy(x => x.Id).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToList();
        return Ok(new { data = page.Select(x => new { x.Id, x.ReferenceAt, x.ObservedAt, payload = Json(x.PayloadJson) }),
            nextCursor = rows.Count > limit ? Cursor(page[^1].ObservedAt, page[^1].Id) : null, historyKind = "observed_positions" });
    }

    [HttpGet("{id:guid}/movements")]
    public async Task<IActionResult> Movements(Guid id, [FromQuery] string? cursor, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        if (!await Owned.AnyAsync(x => x.Id == id, ct)) return NotFound();
        if (!TryCursor(cursor, out var after) || limit is < 1 or > 100) return BadRequest();
        var query = db.InvestmentMovements.AsNoTracking().Where(x => x.InvestmentId == id);
        if (after is not null) query = query.Where(x => x.FirstObservedAt > after.At || (x.FirstObservedAt == after.At && x.Id.CompareTo(after.Id) > 0));
        var rows = await query.OrderBy(x => x.FirstObservedAt).ThenBy(x => x.Id).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToList();
        return Ok(new { data = page.Select(x => new { x.Id, x.ExternalId, x.Direction, x.MovementType, x.TransactionDate,
                x.ProviderUpdatedAt, x.FirstObservedAt, x.LastObservedAt, document = Document(x.ValuesJson, x.AvailabilityJson, x.ProvenanceJson) }),
            nextCursor = rows.Count > limit ? Cursor(page[^1].FirstObservedAt, page[^1].Id) : null,
            taxMeaning = "reported_for_this_operation", numericEncoding = "exact-base10-strings" });
    }

    [HttpGet("reserved-balances")]
    public async Task<IActionResult> Reserves([FromQuery] string? cursor, [FromQuery] int limit = 50, CancellationToken ct = default,
        [FromQuery] Guid? integrationId = null)
    {
        if (!TryCursor(cursor, out var after) || limit is < 1 or > 100) return BadRequest();
        var query = OwnedReserves;
        if (integrationId.HasValue)
        {
            if (!await db.PolpIntegrations.AnyAsync(p => p.Id == integrationId.Value && p.UserId == UserId
                && p.Status != PolpDisconnectService.PendingStatus, ct)) return NotFound();
            query = query.Where(x => x.IntegrationId == integrationId.Value);
        }
        if (after is not null) query = query.Where(x => x.FirstObservedAt > after.At || (x.FirstObservedAt == after.At && x.Id.CompareTo(after.Id) > 0));
        var rows = await query.OrderBy(x => x.FirstObservedAt).ThenBy(x => x.Id).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToList();
        return Ok(new { data = page.Select(ReserveItem),
            nextCursor = rows.Count > limit ? Cursor(page[^1].FirstObservedAt, page[^1].Id) : null, numericEncoding = "exact-base10-strings" });
    }

    private static object ReserveItem(ImportedReservedBalance x) => new { x.Id, x.IntegrationId, x.BankAccountId, x.SourceConsentId, x.ExternalId,
                x.ReservedIdentification, x.Name, x.ProviderUpdatedAt, x.FirstObservedAt, x.LastObservedAt,
                document = Document(x.ValuesJson, x.AvailabilityJson, x.ProvenanceJson) };

    [HttpGet("reserved-balances/{id:guid}/positions")]
    public async Task<IActionResult> ReservePositions(Guid id, [FromQuery] string? cursor, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        if (!await OwnedReserves.AnyAsync(x => x.Id == id, ct)) return NotFound();
        if (!TryCursor(cursor, out var after) || limit is < 1 or > 100) return BadRequest();
        var query = db.ReservedBalanceObservations.AsNoTracking().Where(x => x.ReservedBalanceId == id);
        if (after is not null) query = query.Where(x => x.ObservedAt > after.At || (x.ObservedAt == after.At && x.Id.CompareTo(after.Id) > 0));
        var rows = await query.OrderBy(x => x.ObservedAt).ThenBy(x => x.Id).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).ToList();
        return Ok(new { data = page.Select(x => new { x.Id, x.ObservedAt, payload = Json(x.PayloadJson) }),
            nextCursor = rows.Count > limit ? Cursor(page[^1].ObservedAt, page[^1].Id) : null, historyKind = "observed_reserved_balances" });
    }

    [HttpGet("reserved-balances/{id:guid}")]
    public async Task<IActionResult> ReserveDetail(Guid id, CancellationToken ct)
    {
        var reserve = await OwnedReserves.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (reserve is null) return NotFound();
        var parent = await db.PolpIntegrations.AsNoTracking().SingleAsync(p => p.Id == reserve.IntegrationId && p.UserId == UserId, ct);
        var account = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == reserve.BankAccountId && a.UserId == UserId, ct);
        var remoteAccount = account?.PolpAccountId?.ToString("D");
        var streams = await db.InvestmentSyncStates.AsNoTracking().Where(s => s.UserId == UserId && s.IntegrationId == parent.Id
            && s.SourceConsentId == reserve.SourceConsentId && s.Kind == "reserved" && s.ParentExternalId == remoteAccount)
            .Select(s => new { s.Status, s.Availability, s.LastSuccessAt }).ToListAsync(ct);
        return Ok(new { reserve.Id, reserve.IntegrationId, reserve.BankAccountId, reserve.SourceConsentId, reserve.Name,
            document = Document(reserve.ValuesJson, reserve.AvailabilityJson, reserve.ProvenanceJson),
            institution = new { parent.InstitutionId, parent.InstitutionName }, sourceConnectionStatus = parent.Status,
            synchronization = streams, reserve.FirstObservedAt, reserve.LastObservedAt,
            projection = new { status = "unavailable", reason = "reserved_balance_contract_not_sufficient", source = "imported_contract" },
            numericEncoding = "exact-base10-strings" });
    }

    [HttpGet("synchronization")]
    public async Task<IActionResult> Synchronization([FromQuery] Guid integrationId, CancellationToken ct)
    {
        if (!await db.PolpIntegrations.AnyAsync(p => p.Id == integrationId && p.UserId == UserId && p.Status != PolpDisconnectService.PendingStatus, ct)) return NotFound();
        // Bounded list: list-level state plus counts, not every transaction stream in a large portfolio.
        var query = db.InvestmentSyncStates.AsNoTracking().Where(x => x.UserId == UserId && x.IntegrationId == integrationId && x.Kind != "discovery");
        return Ok(new { catalogs = await query.Where(x => x.Kind == "list").Select(x => new { x.Family, x.SourceConsentId,
            x.Status, x.Availability, x.LastAttemptAt, x.LastSuccessAt, x.ErrorCode }).Take(100).ToListAsync(ct),
            streams = await query.GroupBy(x => x.Status).Select(g => new { status = g.Key, count = g.Count() }).ToListAsync(ct) });
    }

    [HttpGet("{id:guid}/sync-schedules")]
    public async Task<IActionResult> Schedules(Guid id, CancellationToken ct)
    {
        var asset = await Owned.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (asset is null) return NotFound();
        var root = PolpInvestmentContracts.ScheduleResource(asset.Family);
        var names = new[] { root, root + ".balances", root + ".product", root + ".transactions", root + ".broker_notes" };
        var rows = await db.InvestmentSyncSchedules.AsNoTracking().Where(x => x.UserId == UserId
            && x.IntegrationId == asset.IntegrationId && x.SourceConsentId == asset.SourceConsentId && names.Contains(x.Resource)
            && (x.ExternalId == "" || x.ExternalId == asset.ExternalId))
            .Select(x => new { x.Resource, x.ExternalId, x.LastSyncAt, x.NextSyncAt, x.ObservedAt }).Take(20).ToListAsync(ct);
        return Ok(new { data = rows, status = rows.Count == 0 ? "not_reported" : "provider_schedule_observed",
            realTime = false, scheduleIsGuarantee = false });
    }
    private static object Position(InvestmentPosition position) => new { position.ReferenceAt, position.ObservedAt,
        document = Document(position.ValuesJson, position.AvailabilityJson, position.ProvenanceJson) };
    // Absence from a completed full scan is not evidence of liquidation or a zero balance.
    private static object Presence(DateTime lastObserved, DateTime? lastFullScan) => new
    {
        lastFullScanAt = lastFullScan,
        status = lastFullScan is null ? "full_scan_not_confirmed"
            : lastObserved >= lastFullScan.Value ? "reported" : "not_reported_in_last_full_scan"
    };
    private static object Document(string values, string availability, string? provenance = null) => new
    { values = Json(values), availability = Json(availability), provenance = provenance is null ? (JsonElement?)null : Json(provenance) };
    private static JsonElement Json(string value) { using var doc = JsonDocument.Parse(value); return doc.RootElement.Clone(); }
    private sealed record PageCursor(DateTime At, Guid Id);
    private static string Cursor(DateTime at, Guid id) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new PageCursor(at, id))));
    private static bool TryCursor(string? raw, out PageCursor? cursor)
    {
        cursor = null;
        if (raw is null) return true;
        if (raw.Length > 256) return false;
        try { cursor = JsonSerializer.Deserialize<PageCursor>(Convert.FromBase64String(raw)); return cursor is not null && cursor.At.Kind == DateTimeKind.Utc && cursor.Id != Guid.Empty; }
        catch (Exception ex) when (ex is FormatException or JsonException) { return false; }
    }
}
