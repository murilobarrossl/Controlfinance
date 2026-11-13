using System.Text.Json;
using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services.Investments;

// Stream the new dataset in bounded memory; never load a user's full position history into
// a list. Provider cursors, authorization URLs, and operational API-client metadata are excluded.
public class InvestmentDataExport(AppDbContext db)
{
    public async Task WriteAsync(Utf8JsonWriter writer, Guid userId, Func<Task> flush, CancellationToken ct)
    {
        var assetIds = db.ImportedInvestments.Where(x => x.UserId == userId).Select(x => x.Id);
        var reserveIds = db.ImportedReservedBalances.Where(x => x.UserId == userId).Select(x => x.Id);
        writer.WritePropertyName("investimentosImportados"); writer.WriteStartObject();
        await ArrayAsync(writer, "identidades", db.ImportedInvestments.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new { x.Id, x.IntegrationId, x.SourceConsentId, x.Family, x.ExternalId, x.Name, x.ProductType,
                identity = Json(x.IdentityJson), availability = Json(x.IdentityAvailabilityJson), x.FirstObservedAt, x.LastObservedAt }).AsAsyncEnumerable(), flush, ct);
        await ArrayAsync(writer, "contratos", db.InvestmentContracts.AsNoTracking().Where(x => assetIds.Contains(x.InvestmentId))
            .Select(x => new { x.InvestmentId, values = Json(x.ValuesJson), availability = Json(x.AvailabilityJson), provenance = Json(x.ProvenanceJson), x.ObservedAt }).AsAsyncEnumerable(), flush, ct);
        await ArrayAsync(writer, "posicoesAtuais", db.InvestmentPositions.AsNoTracking().Where(x => assetIds.Contains(x.InvestmentId))
            .Select(x => new { x.InvestmentId, values = Json(x.ValuesJson), availability = Json(x.AvailabilityJson), provenance = Json(x.ProvenanceJson), x.ReferenceAt, x.ObservedAt }).AsAsyncEnumerable(), flush, ct);
        await ArrayAsync(writer, "posicoesObservadas", db.InvestmentPositionObservations.AsNoTracking().Where(x => assetIds.Contains(x.InvestmentId))
            .Select(x => new { x.Id, x.InvestmentId, payload = Json(x.PayloadJson), x.ReferenceAt, x.ObservedAt }).AsAsyncEnumerable(), flush, ct);
        await ArrayAsync(writer, "movimentacoes", db.InvestmentMovements.AsNoTracking().Where(x => assetIds.Contains(x.InvestmentId))
            .Select(x => new { x.Id, x.InvestmentId, x.ExternalId, x.Direction, x.MovementType, x.TransactionDate, x.ProviderUpdatedAt,
                values = Json(x.ValuesJson), availability = Json(x.AvailabilityJson), provenance = Json(x.ProvenanceJson), x.FirstObservedAt, x.LastObservedAt }).AsAsyncEnumerable(), flush, ct);
        await ArrayAsync(writer, "saldosReservados", db.ImportedReservedBalances.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new { x.Id, x.IntegrationId, x.BankAccountId, x.SourceConsentId, x.ExternalId, x.ReservedIdentification, x.Name,
                values = Json(x.ValuesJson), availability = Json(x.AvailabilityJson), provenance = Json(x.ProvenanceJson), x.FirstObservedAt, x.LastObservedAt }).AsAsyncEnumerable(), flush, ct);
        await ArrayAsync(writer, "reservasObservadas", db.ReservedBalanceObservations.AsNoTracking().Where(x => reserveIds.Contains(x.ReservedBalanceId))
            .Select(x => new { x.Id, x.ReservedBalanceId, payload = Json(x.PayloadJson), x.ObservedAt }).AsAsyncEnumerable(), flush, ct);
        await ArrayAsync(writer, "sincronizacao", db.InvestmentSyncStates.AsNoTracking().Where(x => x.UserId == userId && x.Kind != "discovery")
            .Select(x => new { x.IntegrationId, x.SourceConsentId, x.Family, x.Kind, x.ParentExternalId, x.Status, x.Availability,
                x.PageNumber, x.LastAttemptAt, x.LastSuccessAt, x.ErrorCode }).AsAsyncEnumerable(), flush, ct);
        await ArrayAsync(writer, "cronogramasObservados", db.InvestmentSyncSchedules.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new { x.SourceConsentId, x.IntegrationId, x.Resource, x.ExternalId, x.LastSyncAt, x.NextSyncAt, x.ObservedAt }).AsAsyncEnumerable(), flush, ct);
        writer.WriteEndObject();
    }
    private static async Task ArrayAsync<T>(Utf8JsonWriter writer, string name, IAsyncEnumerable<T> rows, Func<Task> flush, CancellationToken ct)
    {
        writer.WritePropertyName(name); writer.WriteStartArray();
        var count = 0;
        await foreach (var row in rows.WithCancellation(ct))
        {
            JsonSerializer.Serialize(writer, row);
            if (++count % 64 == 0) await flush();
        }
        writer.WriteEndArray(); await flush();
    }
    private static JsonElement Json(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
}
