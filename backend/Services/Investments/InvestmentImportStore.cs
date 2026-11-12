using System.Text.Json;
using System.Text.Json.Nodes;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services.Investments;

// Called only inside the integration lock/transaction after source ownership and permission
// generation were checked. No financial aggregate or tax/rate interpretation happens here.
public class InvestmentImportStore(AppDbContext db)
{
    public async Task SchedulesAsync(InvestmentSyncState scope, IReadOnlyList<JsonElement> rows, DateTime observedAt, CancellationToken ct)
    {
        var relevant = new List<(string Resource, string ExternalId, DateTime? Last, DateTime? Next)>();
        foreach (var row in rows)
        {
            if (PolpInvestmentContracts.Identifier(row, "consent_id") != scope.SourceConsentId.ToString("D"))
                throw new PolpPermanentException("Cronograma de outro consentimento.");
            if (!row.TryGetProperty("resource", out var resource)) throw new PolpPermanentException("Recurso de cronograma inválido.");
            if (resource.ValueKind == JsonValueKind.Object)
            {
                if (resource.TryGetProperty("consent_id", out _)
                    && PolpInvestmentContracts.Identifier(resource, "consent_id") != scope.SourceConsentId.ToString("D"))
                    throw new PolpPermanentException("Cronograma de outro consentimento.");
                if (!resource.TryGetProperty("resource", out resource)) throw new PolpPermanentException("Recurso de cronograma inválido.");
            }
            if (resource.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(resource.GetString()))
                throw new PolpPermanentException("Recurso de cronograma inválido.");
            var name = resource.GetString()!;
            if (!PolpInvestmentContracts.IsInvestmentSchedule(name)) continue;
            var external = row.TryGetProperty("resource_id", out var id) && id.ValueKind != JsonValueKind.Null
                ? PolpInvestmentContracts.Identifier(row, "resource_id") : "";
            relevant.Add((name, external, PolpInvestmentContracts.Date(row, "last_sync_at"), PolpInvestmentContracts.Date(row, "next_sync_at")));
        }
        if (relevant.Select(x => (x.Resource, x.ExternalId)).Distinct().Count() != relevant.Count)
            throw new PolpPermanentException("Cronograma duplicado na mesma página.");
        var names = relevant.Select(x => x.Resource).Distinct().ToArray();
        var ids = relevant.Select(x => x.ExternalId).Distinct().ToArray();
        var existing = (await db.InvestmentSyncSchedules.Where(x => x.UserId == scope.UserId && x.IntegrationId == scope.IntegrationId
            && x.SourceConsentId == scope.SourceConsentId && names.Contains(x.Resource) && ids.Contains(x.ExternalId)).ToListAsync(ct))
            .ToDictionary(x => (x.Resource, x.ExternalId));
        foreach (var row in relevant)
        {
            if (!existing.TryGetValue((row.Resource, row.ExternalId), out var schedule))
            {
                schedule = new InvestmentSyncSchedule { UserId = scope.UserId, IntegrationId = scope.IntegrationId,
                    SourceConsentId = scope.SourceConsentId, Resource = row.Resource, ExternalId = row.ExternalId };
                db.InvestmentSyncSchedules.Add(schedule);
            }
            if (row.Last is null || schedule.LastSyncAt is null || row.Last >= schedule.LastSyncAt)
            {
                schedule.LastSyncAt = row.Last;
                schedule.NextSyncAt = row.Next;
            }
            schedule.ObservedAt = observedAt; schedule.ObservedRunId = scope.RunId;
        }
    }

    public async Task<IReadOnlyList<ImportedInvestment>> CatalogAsync(InvestmentSyncState scope, IReadOnlyList<JsonElement> rows,
        DateTime observedAt, CancellationToken ct)
    {
        var spec = PolpInvestmentContracts.Family(scope.Family);
        var ids = rows.Select(r => PolpInvestmentContracts.Identifier(r, "id")).ToArray();
        if (ids.Distinct().Count() != ids.Length) throw new PolpPermanentException("Investimento duplicado na página da Polp.");
        var existing = await db.ImportedInvestments.Where(x => x.Family == scope.Family && ids.Contains(x.ExternalId)).ToDictionaryAsync(x => x.ExternalId, ct);
        if (existing.Values.Any(x => x.UserId != scope.UserId || x.IntegrationId != scope.IntegrationId || x.SourceConsentId != scope.SourceConsentId))
            throw new PolpPermanentException("Vínculo de investimento divergente.");
        var localIds = existing.Values.Select(x => x.Id).ToArray();
        var contracts = await db.InvestmentContracts.Where(x => localIds.Contains(x.InvestmentId)).ToDictionaryAsync(x => x.InvestmentId, ct);
        var positions = await db.InvestmentPositions.Where(x => localIds.Contains(x.InvestmentId)).ToDictionaryAsync(x => x.InvestmentId, ct);
        var positionHashes = new List<(Guid Id, string Hash, string Payload, DateTime? Reference)>();
        var imported = new List<ImportedInvestment>();
        foreach (var row in rows)
        {
            if (!Guid.TryParse(PolpInvestmentContracts.Identifier(row, "consent_id"), out var consent) || consent != scope.SourceConsentId)
                throw new PolpPermanentException("Investimento de outro consentimento na resposta.");
            var externalId = PolpInvestmentContracts.Identifier(row, "id");
            var identity = PolpInvestmentContracts.Select(row, spec.Identity, legacyProduct: true);
            identity.Values["source_id"] = RawIdentifier(row, "id");
            identity.Values["consent_id"] = RawIdentifier(row, "consent_id");
            foreach (var field in new[] { "created_at", "updated_at" })
                if (row.TryGetProperty(field, out var stamp) && stamp.ValueKind == JsonValueKind.String) identity.Values[field] = stamp.GetString();
            var contract = PolpInvestmentContracts.Select(row, spec.Contract, legacyProduct: true);
            var providerUpdated = PolpInvestmentContracts.Date(row, "updated_at");
            if (!existing.TryGetValue(externalId, out var asset))
            {
                asset = new ImportedInvestment { UserId = scope.UserId, IntegrationId = scope.IntegrationId, SourceConsentId = scope.SourceConsentId,
                    Family = scope.Family, ExternalId = externalId, FirstObservedAt = observedAt };
                db.ImportedInvestments.Add(asset);
                existing.Add(externalId, asset);
            }
            var current = IsCurrent(providerUpdated, asset.ProviderUpdatedAt);
            if (current)
            {
                var replace = IsNewer(providerUpdated, asset.ProviderUpdatedAt);
                var merged = PolpInvestmentContracts.Merge(asset.IdentityJson, "{}", identity, observedAt, null, replace);
                asset.IdentityJson = merged.Values;
                asset.IdentityAvailabilityJson = merged.Availability;
                using var mergedIdentity = JsonDocument.Parse(merged.Values);
                if (spec.NameField.Length > 0 && mergedIdentity.RootElement.TryGetProperty(spec.NameField, out var name)) asset.Name = name.GetString();
                if (mergedIdentity.RootElement.TryGetProperty("investment_type", out var type)) asset.ProductType = type.GetString();
                asset.ProviderUpdatedAt = providerUpdated ?? asset.ProviderUpdatedAt;
                if (!contracts.TryGetValue(asset.Id, out var terms))
                {
                    terms = new InvestmentContract { InvestmentId = asset.Id };
                    contracts.Add(asset.Id, terms);
                    db.InvestmentContracts.Add(terms);
                }
                var mergedTerms = PolpInvestmentContracts.Merge(terms.ValuesJson, terms.ProvenanceJson, contract, observedAt, null, replace);
                terms.ValuesJson = mergedTerms.Values; terms.AvailabilityJson = mergedTerms.Availability;
                terms.ProvenanceJson = mergedTerms.Provenance; terms.ObservedAt = observedAt;
            }
            asset.LastObservedAt = observedAt;
            imported.Add(asset);
            row.TryGetProperty("balance", out var balance);
            if (balance.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined))
                throw new PolpPermanentException("Posição de investimento fora do contrato.");
            var position = PolpInvestmentContracts.Select(balance, spec.Position);
            var reference = PolpInvestmentContracts.Reference(position);
            if (position.Values.Any(x => x.Key is not ("reference_date" or "reference_date_time" or "updated_at") && x.Value is not null))
            {
                var payload = new JsonObject { ["values"] = position.Values.DeepClone(), ["availability"] = position.Availability.DeepClone() };
                positionHashes.Add((asset.Id, PolpInvestmentContracts.Hash(payload), payload.ToJsonString(), reference));
            }
            if (!positions.TryGetValue(asset.Id, out var latest))
            {
                latest = new InvestmentPosition { InvestmentId = asset.Id };
                db.InvestmentPositions.Add(latest); positions.Add(asset.Id, latest);
            }
            if ((balance.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) && current)
                latest.AvailabilityJson = position.Availability.ToJsonString();
            // An older reference is observed history, not a replacement for a newer position.
            // Product identification and balance timestamps are independent in Polp.
            using var previousPosition = JsonDocument.Parse(latest.ValuesJson);
            var previousBalanceUpdated = PolpInvestmentContracts.Date(previousPosition.RootElement, "updated_at");
            var balanceUpdated = balance.ValueKind == JsonValueKind.Object ? PolpInvestmentContracts.Date(balance, "updated_at") : null;
            var newerReference = reference.HasValue && (latest.ReferenceAt is null || reference > latest.ReferenceAt);
            var sameReference = reference == latest.ReferenceAt;
            if (newerReference || (sameReference && IsCurrent(balanceUpdated, previousBalanceUpdated)))
            {
                var merged = PolpInvestmentContracts.Merge(latest.ValuesJson, latest.ProvenanceJson, position, observedAt, reference,
                    newerReference || IsNewer(balanceUpdated, previousBalanceUpdated));
                latest.ValuesJson = merged.Values; latest.AvailabilityJson = merged.Availability; latest.ProvenanceJson = merged.Provenance;
                latest.ReferenceAt = reference ?? latest.ReferenceAt; latest.ObservedAt = observedAt;
            }
        }
        var hashes = positionHashes.Select(x => x.Hash).ToArray();
        var known = (await db.InvestmentPositionObservations.Where(x => localIds.Contains(x.InvestmentId) && hashes.Contains(x.ContentHash))
            .Select(x => new { x.InvestmentId, x.ContentHash }).ToListAsync(ct)).Select(x => (x.InvestmentId, x.ContentHash)).ToHashSet();
        foreach (var observation in positionHashes.Where(x => !known.Contains((x.Id, x.Hash))))
            db.InvestmentPositionObservations.Add(new() { InvestmentId = observation.Id, ContentHash = observation.Hash,
                PayloadJson = observation.Payload, ReferenceAt = observation.Reference, ObservedAt = observedAt });
        return imported;
    }

    public async Task MovementsAsync(InvestmentSyncState scope, IReadOnlyList<JsonElement> rows, DateTime observedAt,
        DateTime? historyCutoff, CancellationToken ct)
    {
        var spec = PolpInvestmentContracts.Family(scope.Family);
        var asset = await db.ImportedInvestments.SingleOrDefaultAsync(x => x.UserId == scope.UserId && x.IntegrationId == scope.IntegrationId
            && x.SourceConsentId == scope.SourceConsentId && x.Family == scope.Family && x.ExternalId == scope.ParentExternalId, ct)
            ?? throw new PolpResourceNotReadyException("Identidade do investimento ainda não importada.");
        var ids = rows.Select(r => PolpInvestmentContracts.Identifier(r, "id")).ToArray();
        if (ids.Distinct().Count() != ids.Length) throw new PolpPermanentException("Movimentação duplicada na página da Polp.");
        var existing = await db.InvestmentMovements.Where(x => x.InvestmentId == asset.Id && ids.Contains(x.ExternalId)).ToDictionaryAsync(x => x.ExternalId, ct);
        foreach (var row in rows)
        {
            if (PolpInvestmentContracts.Identifier(row, spec.ParentField) != asset.ExternalId)
                throw new PolpPermanentException("Movimentação de outro investimento na resposta.");
            var id = PolpInvestmentContracts.Identifier(row, "id");
            var document = PolpInvestmentContracts.Select(row, spec.Movement);
            document.Values["source_id"] = RawIdentifier(row, "id");
            document.Values[spec.ParentField] = RawIdentifier(row, spec.ParentField);
            var dateField = scope.Family == "funds" ? "transaction_conversion_date" : "transaction_date";
            var date = PolpInvestmentContracts.Date(row, dateField);
            if (historyCutoff.HasValue && date.HasValue && date < historyCutoff) continue;
            var updated = PolpInvestmentContracts.Date(row, "updated_at");
            if (!existing.TryGetValue(id, out var movement))
            {
                movement = new InvestmentMovement { InvestmentId = asset.Id, ExternalId = id, FirstObservedAt = observedAt };
                existing.Add(id, movement); db.InvestmentMovements.Add(movement);
            }
            if (IsCurrent(updated, movement.ProviderUpdatedAt))
            {
                var merged = PolpInvestmentContracts.Merge(movement.ValuesJson, movement.ProvenanceJson, document, observedAt, date, IsNewer(updated, movement.ProviderUpdatedAt));
                if (merged.Availability.Contains("conflicting_source_version", StringComparison.Ordinal))
                    throw new PolpPermanentException("Movimentação com valores conflitantes sem nova versão de origem.");
                movement.ValuesJson = merged.Values; movement.AvailabilityJson = merged.Availability; movement.ProvenanceJson = merged.Provenance;
                using var mergedMovement = JsonDocument.Parse(merged.Values);
                if (mergedMovement.RootElement.TryGetProperty("type", out var direction)) movement.Direction = direction.GetString();
                if (mergedMovement.RootElement.TryGetProperty("transaction_type", out var movementType)) movement.MovementType = movementType.GetString();
                movement.TransactionDate = PolpInvestmentContracts.Date(mergedMovement.RootElement, dateField) ?? movement.TransactionDate;
                movement.ProviderUpdatedAt = updated ?? movement.ProviderUpdatedAt;
            }
            movement.LastObservedAt = observedAt;
        }
    }

    public async Task ReservedAsync(InvestmentSyncState scope, IReadOnlyList<JsonElement> rows, DateTime observedAt, CancellationToken ct)
    {
        if (!Guid.TryParse(scope.ParentExternalId, out var accountId)) throw new PolpPermanentException("Conta de reservas inválida.");
        var account = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == scope.UserId && x.PolpAccountId == accountId
            && x.PolpIntegrationId == scope.SourceConsentId && x.IsActive, ct) ?? throw new PolpResourceNotReadyException("Conta de reservas não disponível.");
        var ids = rows.Select(r => PolpInvestmentContracts.Identifier(r, "id")).ToArray();
        if (ids.Distinct().Count() != ids.Length) throw new PolpPermanentException("Reserva duplicada na página da Polp.");
        var identifications = rows.Select(r => PolpInvestmentContracts.Identifier(r, "reserved_identification")).ToArray();
        if (identifications.Distinct().Count() != identifications.Length) throw new PolpPermanentException("Identidade de reserva duplicada na página.");
        var knownRows = await db.ImportedReservedBalances.Where(x => x.BankAccountId == account.Id
            && (ids.Contains(x.ExternalId) || identifications.Contains(x.ReservedIdentification))).ToListAsync(ct);
        var existing = knownRows.ToDictionary(x => x.ReservedIdentification);
        var observations = new List<(Guid Id, string Hash, string Json)>();
        foreach (var row in rows)
        {
            if (PolpInvestmentContracts.Identifier(row, "account_id") != scope.ParentExternalId)
                throw new PolpPermanentException("Reserva de outra conta na resposta.");
            var external = PolpInvestmentContracts.Identifier(row, "id");
            var identification = PolpInvestmentContracts.Identifier(row, "reserved_identification");
            var document = PolpInvestmentContracts.Select(row, PolpInvestmentContracts.ReservedFields);
            document.Values["source_id"] = RawIdentifier(row, "id");
            document.Values["account_id"] = RawIdentifier(row, "account_id");
            var updated = PolpInvestmentContracts.Date(row, "updated_at");
            if (!existing.TryGetValue(identification, out var reserve))
            {
                if (knownRows.Any(x => x.ExternalId == external)) throw new PolpPermanentException("Identificador de reserva foi reatribuído.");
                reserve = new ImportedReservedBalance { UserId = scope.UserId, IntegrationId = scope.IntegrationId,
                    BankAccountId = account.Id, SourceConsentId = scope.SourceConsentId, ExternalId = external,
                    ReservedIdentification = identification, FirstObservedAt = observedAt };
                db.ImportedReservedBalances.Add(reserve); existing.Add(identification, reserve);
            }
            if (reserve.UserId != scope.UserId || reserve.IntegrationId != scope.IntegrationId || reserve.ReservedIdentification != identification)
                throw new PolpPermanentException("Vínculo da reserva divergente.");
            if (IsCurrent(updated, reserve.ProviderUpdatedAt))
            {
                reserve.ExternalId = external;
                var merged = PolpInvestmentContracts.Merge(reserve.ValuesJson, reserve.ProvenanceJson, document, observedAt, null, IsNewer(updated, reserve.ProviderUpdatedAt));
                reserve.ValuesJson = merged.Values; reserve.AvailabilityJson = merged.Availability; reserve.ProvenanceJson = merged.Provenance;
                using var mergedReserve = JsonDocument.Parse(merged.Values);
                if (mergedReserve.RootElement.TryGetProperty("reserved_name", out var reserveName)) reserve.Name = reserveName.GetString();
                reserve.ProviderUpdatedAt = updated ?? reserve.ProviderUpdatedAt;
            }
            reserve.LastObservedAt = observedAt;
            if (document.Values["available_amount"] is not null)
            {
                var payload = new JsonObject { ["values"] = document.Values.DeepClone(), ["availability"] = document.Availability.DeepClone() };
                observations.Add((reserve.Id, PolpInvestmentContracts.Hash(payload), payload.ToJsonString()));
            }
        }
        var localIds = existing.Values.Select(x => x.Id).ToArray(); var hashes = observations.Select(x => x.Hash).ToArray();
        var known = (await db.ReservedBalanceObservations.Where(x => localIds.Contains(x.ReservedBalanceId) && hashes.Contains(x.ContentHash))
            .Select(x => new { x.ReservedBalanceId, x.ContentHash }).ToListAsync(ct)).Select(x => (x.ReservedBalanceId, x.ContentHash)).ToHashSet();
        foreach (var item in observations.Where(x => !known.Contains((x.Id, x.Hash))))
            db.ReservedBalanceObservations.Add(new() { ReservedBalanceId = item.Id, ContentHash = item.Hash, PayloadJson = item.Json, ObservedAt = observedAt });
    }
    private static bool IsCurrent(DateTime? incoming, DateTime? previous) => previous is null || (incoming.HasValue && incoming >= previous);
    private static bool IsNewer(DateTime? incoming, DateTime? previous) => incoming.HasValue && (previous is null || incoming > previous);
    private static string RawIdentifier(JsonElement row, string field) => row.GetProperty(field).ValueKind == JsonValueKind.String
        ? row.GetProperty(field).GetString()! : row.GetProperty(field).GetRawText();
}
