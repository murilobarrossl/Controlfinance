using System.Data;
using System.Text.Json;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services.Investments;

public class InvestmentFinancialService(AppDbContext db, IConfiguration config, TimeProvider clock)
{
    private const int MaxOperations = 5000;

    public async Task<object?> MonthAsync(Guid userId, Guid investmentId, DateOnly month, CancellationToken ct)
    {
        // A single database snapshot avoids mixing a newly committed page with older totals
        // or availability. No provider request and no historical result is persisted as fact.
        await using var transaction = await db.Database.BeginTransactionAsync(
            db.Database.IsNpgsql() ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, ct);
        var asset = await db.ImportedInvestments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == investmentId && x.UserId == userId
            && db.PolpIntegrations.Any(p => p.Id == x.IntegrationId && p.UserId == userId && p.Status != PolpDisconnectService.PendingStatus), ct);
        if (asset is null) return null;
        var start = month.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = start.AddMonths(1);
        var key = month.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        // Offset timestamps may straddle UTC days. Select a bounded wider range, then use
        // the actual calendar date reported in transaction_date/conversion_date, not a guessed timezone.
        var candidates = await db.InvestmentMovements.AsNoTracking().Where(x => x.InvestmentId == asset.Id
            && x.TransactionDate >= start.AddDays(-2) && x.TransactionDate < end.AddDays(2))
            .OrderBy(x => x.TransactionDate).ThenBy(x => x.Id).Take(MaxOperations + 1).ToListAsync(ct);
        var unknownDates = await db.InvestmentMovements.AnyAsync(x => x.InvestmentId == asset.Id && x.TransactionDate == null, ct);
        var cancellations = await db.InvestmentMovements.AnyAsync(x => x.InvestmentId == asset.Id && x.MovementType == "CANCELAMENTO", ct);
        var stream = await db.InvestmentSyncStates.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId
            && x.IntegrationId == asset.IntegrationId && x.SourceConsentId == asset.SourceConsentId && x.Family == asset.Family
            && x.Kind == "movements" && x.ParentExternalId == asset.ExternalId, ct);
        var contract = await db.InvestmentContracts.AsNoTracking().SingleOrDefaultAsync(x => x.InvestmentId == asset.Id, ct);
        var opening = await ObservationAsync(asset.Id, start, ct);
        var closing = await ObservationAsync(asset.Id, end, ct);
        await transaction.CommitAsync(ct);

        var overLimit = candidates.Count > MaxOperations;
        var dateField = asset.Family == "funds" ? "transaction_conversion_date" : "transaction_date";
        List<InvestmentMovement> rows = overLimit ? [] : candidates.Where(x => ReportedMonth(x, dateField) == key).ToList();
        var incompleteDates = !overLimit && candidates.Any(x => !Available(x, dateField));
        var incompleteTypes = rows.Any(x => !Available(x, "transaction_type"));
        var futureOperations = rows.Any(x => x.TransactionDate > clock.GetUtcNow().UtcDateTime);
        var cutoff = DataRetentionPolicy.SyncedHistoryCutoff(config, clock.GetUtcNow().UtcDateTime);
        var reasons = new List<string> { "provider_does_not_guarantee_full_calendar_history" };
        var streamAvailable = stream is { Status: "Complete", Availability: "available" };
        if (overLimit) reasons.Add("aggregation_limit_exceeded_use_paginated_operations");
        if (!streamAvailable) reasons.Add("synchronization_not_fully_available");
        if (stream?.LastFullSuccessAt is null) reasons.Add("full_scan_not_confirmed");
        if (cutoff > start) reasons.Add("retention_intersects_period");
        if (unknownDates) reasons.Add("operations_without_reference_date");
        if (incompleteDates) reasons.Add("operations_with_unconfirmed_reference_date");
        if (incompleteTypes) reasons.Add("operations_with_unconfirmed_classification");
        if (futureOperations) reasons.Add("future_dated_operations_not_confirmed_as_realized");
        if (end > clock.GetUtcNow().UtcDateTime) reasons.Add("calendar_period_not_closed");
        if (cancellations) reasons.Add("cancellation_without_original_operation_link");
        var blocked = overLimit ? "aggregation_limit_exceeded" : cancellations ? "unlinked_cancellation_prevents_reconciliation"
            : unknownDates || incompleteDates ? "operations_without_confirmed_reference_date"
            : incompleteTypes ? "operations_without_confirmed_classification" : futureOperations ? "future_dated_operations_not_confirmed_as_realized" : !streamAvailable
                ? "synchronization_not_fully_available" : null;
        var groups = rows.GroupBy(x => x.MovementType ?? "NOT_PROVIDED").OrderBy(x => x.Key).Select(g =>
        {
            var operations = g.ToList();
            var fields = asset.Family == "variable-incomes" ? new[] { "transaction_value" }
                : asset.Family == "funds" ? new[] { "transaction_value", "transaction_gross_value", "transaction_net_value", "income_tax", "financial_transaction_tax", "transaction_exit_fee" }
                : new[] { "transaction_gross_value", "transaction_net_value", "income_tax", "financial_transaction_tax" };
            return new
            {
                sourceType = g.Key, classification = InvestmentFinancialRules.Classify(asset.Family, g.Key),
                count = operations.Count, positionDirections = operations.Select(x => x.Direction).Distinct().ToArray(),
                values = fields.ToDictionary(f => f, f => InvestmentFinancialRules.SumReported(operations, f, blocked)),
                semantics = "reported_operations_not_total_return_or_principal_recovered"
            };
        }).ToArray();
        object variation = new InvestmentIndicator("unavailable", "insufficient_unambiguous_referenced_positions");
        if (opening is not null && closing is not null && opening.ReferenceAt < closing.ReferenceAt)
        {
            using var a = JsonDocument.Parse(opening.PayloadJson);
            using var b = JsonDocument.Parse(closing.PayloadJson);
            variation = new
            {
                status = "derived_from_observed_positions", financialKind = "patrimonial_variation_not_income",
                fromReferenceAt = opening.ReferenceAt, toReferenceAt = closing.ReferenceAt,
                fromObservedAt = opening.ObservedAt, toObservedAt = closing.ObservedAt,
                gross = InvestmentFinancialRules.Difference(a.RootElement, b.RootElement, "gross_amount"),
                net = InvestmentFinancialRules.Difference(a.RootElement, b.RootElement, "net_amount"),
                quantity = InvestmentFinancialRules.QuantityDifference(a.RootElement, b.RootElement, asset.Family == "funds" ? "quota_quantity" : "quantity"),
                unitPrice = InvestmentFinancialRules.UnitPriceDifference(asset.Family, a.RootElement, b.RootElement),
                unitPriceMeaning = "unadjusted_observed_price_change_not_investment_return",
                containsUnseparatedEffects = new[] { "applications", "redemptions", "distributions", "amortizations", "taxes", "price_changes" }
            };
        }
        return new
        {
            investmentId = asset.Id, month = key, numericEncoding = "exact-base10-strings",
            rounding = "none_exact_base10_addition_and_subtraction", operationCalendar = "provider_reported_calendar_date",
            coverage = new { status = "reported_subset_not_proven_calendar_coverage", reasons,
                lastFullSuccessAt = stream?.LastFullSuccessAt, lastSuccessAt = stream?.LastSuccessAt,
                observedOperationCount = overLimit ? (int?)null : rows.Count },
            operations = groups, observedIntervalVariation = variation,
            monthlyGrossIncome = new InvestmentIndicator("unavailable", "reconciled_cash_flows_and_comparable_month_boundaries_not_proven"),
            monthlyNetIncome = new InvestmentIndicator("unavailable", "effective_tax_and_cash_flow_coverage_not_proven"),
            monthlyReturnPercent = new InvestmentIndicator("unavailable", "capital_timing_and_adjusted_valuation_not_proven"),
            lifetimeApplied = new InvestmentIndicator("unavailable", "lifetime_operation_coverage_not_proven"),
            totalEffectiveTax = new InvestmentIndicator("unavailable", "only_operation_associated_taxes_are_reported"),
            projection = InvestmentFinancialRules.Projection(asset, contract)
        };
    }

    private static string? ReportedMonth(InvestmentMovement row, string field)
    {
        if (!Available(row, field)) return null;
        using var doc = JsonDocument.Parse(row.ValuesJson);
        return doc.RootElement.TryGetProperty(field, out var date) && date.ValueKind == JsonValueKind.String && date.GetString() is { Length: >= 7 } text ? text[..7] : null;
    }

    private static bool Available(InvestmentMovement row, string field)
    {
        using var doc = JsonDocument.Parse(row.AvailabilityJson);
        return doc.RootElement.TryGetProperty(field, out var availability) && availability.GetString() == "available";
    }

    private async Task<InvestmentPositionObservation?> ObservationAsync(Guid id, DateTime before, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var query = db.InvestmentPositionObservations.AsNoTracking().Where(x => x.InvestmentId == id && x.ReferenceAt < before && x.ReferenceAt <= now);
        var reference = await query.OrderByDescending(x => x.ReferenceAt).Select(x => x.ReferenceAt).FirstOrDefaultAsync(ct);
        if (reference is null) return null;
        var versions = await query.Where(x => x.ReferenceAt == reference).OrderByDescending(x => x.ObservedAt).ThenBy(x => x.Id).Take(101).ToListAsync(ct);
        if (versions.Count > 100) return null; // No silent truncation of source corrections.
        var stamped = versions.Select(x =>
        {
            using var doc = JsonDocument.Parse(x.PayloadJson);
            return (Row: x, Updated: PolpInvestmentContracts.Date(doc.RootElement.GetProperty("values"), "updated_at"));
        }).ToList();
        if (stamped.Count == 1) return stamped[0].Row;
        if (stamped.Any(x => x.Updated is null)) return null;
        var newest = stamped.Max(x => x.Updated);
        var latest = stamped.Where(x => x.Updated == newest).ToList();
        return latest.Count == 1 ? latest[0].Row : null; // Conflicting content with identical provider version.
    }
}
