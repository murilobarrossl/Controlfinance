using System.Text.Json;
using ControlFinance.API.Models;

namespace ControlFinance.API.Services.Investments;

public sealed record InvestmentIndicator(string Status, string? Reason, object? Value = null);
public sealed record InvestmentMoney(string Amount, string Currency);

// Operation types have different meanings by family. ENTRADA/SAIDA refers to the investment,
// not necessarily to cash received/spent. Never manufacture a signed portfolio cash flow.
public static class InvestmentFinancialRules
{
    public static string Classify(string family, string? type) => (family, type) switch
    {
        ("bank-fixed-incomes", "APLICACAO") or ("funds", "APLICACAO") => "application",
        ("bank-fixed-incomes", "RESGATE") or ("funds", "RESGATE") => "redemption",
        ("credit-fixed-incomes" or "treasure-titles" or "variable-incomes", "COMPRA") => "purchase",
        ("credit-fixed-incomes" or "treasure-titles" or "variable-incomes", "VENDA") => "sale",
        ("bank-fixed-incomes" or "credit-fixed-incomes" or "treasure-titles", "PAGAMENTO_JUROS") => "interest_payment",
        ("bank-fixed-incomes" or "credit-fixed-incomes" or "treasure-titles" or "funds", "AMORTIZACAO") => "amortization_not_pure_income",
        ("bank-fixed-incomes" or "credit-fixed-incomes" or "treasure-titles", "VENCIMENTO") => "maturity_not_pure_income",
        ("bank-fixed-incomes" or "credit-fixed-incomes" or "treasure-titles", "CANCELAMENTO") => "unlinked_cancellation",
        ("credit-fixed-incomes", "PREMIO") => "premium",
        ("credit-fixed-incomes", "MULTA") => "penalty",
        ("credit-fixed-incomes", "MORA") => "late_interest",
        ("funds", "COME_COTAS") => "quota_tax_event",
        ("funds", "TRANSFERENCIA_COTAS") => "quota_transfer",
        ("variable-incomes", "DIVIDENDOS") => "dividends",
        ("variable-incomes", "JCP") => "interest_on_equity",
        ("variable-incomes", "ALUGUEIS") => "securities_rental",
        (_, "TRANSFERENCIA_CUSTODIA" or "TRANSFERENCIA_TITULARIDADE") => "non_cash_transfer",
        _ => "unclassified"
    };

    public static InvestmentMoney? Money(JsonElement values, JsonElement availability, string field)
    {
        if (!availability.TryGetProperty(field, out var state) || state.GetString() != "available"
            || !values.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("amount", out var amount) || amount.ValueKind != JsonValueKind.String
            || !value.TryGetProperty("currency", out var currency) || currency.ValueKind != JsonValueKind.String) return null;
        return new(ExactInvestmentDecimal.Parse(amount.GetString()!).Original, currency.GetString()!);
    }

    public static InvestmentIndicator Difference(JsonElement from, JsonElement to, string field)
    {
        var a = Money(from.GetProperty("values"), from.GetProperty("availability"), field);
        var b = Money(to.GetProperty("values"), to.GetProperty("availability"), field);
        if (a is null || b is null) return new("unavailable", "missing_comparable_amount");
        if (a.Currency != b.Currency) return new("unavailable", "currency_changed");
        return new("derived_from_observed_positions", null,
            new InvestmentMoney(ExactInvestmentDecimal.Parse(b.Amount).Subtract(ExactInvestmentDecimal.Parse(a.Amount)).Original, a.Currency));
    }

    public static InvestmentIndicator QuantityDifference(JsonElement from, JsonElement to, string field)
    {
        var a = Scalar(from, field); var b = Scalar(to, field);
        return a is null || b is null ? new("unavailable", "missing_comparable_quantity")
            : new("derived_from_observed_positions", null, ExactInvestmentDecimal.Parse(b).Subtract(ExactInvestmentDecimal.Parse(a)).Original);
    }

    public static InvestmentIndicator UnitPriceDifference(string family, JsonElement from, JsonElement to)
    {
        if (family == "variable-incomes")
        {
            var a = Scalar(from, "price_factor"); var b = Scalar(to, "price_factor");
            if (a is null || b is null || ExactInvestmentDecimal.Parse(a).Coefficient.Sign <= 0
                || ExactInvestmentDecimal.Parse(a).Subtract(ExactInvestmentDecimal.Parse(b)).Coefficient != 0)
                return new("unavailable", "price_factor_not_comparable");
        }
        return Difference(from, to, family == "funds" ? "quota_gross_price_value" : family == "variable-incomes" ? "closing_price" : "updated_unit_price");
    }

    private static string? Scalar(JsonElement document, string field)
    {
        var values = document.GetProperty("values"); var availability = document.GetProperty("availability");
        return availability.TryGetProperty(field, out var status) && status.GetString() == "available"
            && values.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    public static InvestmentIndicator SumReported(IReadOnlyList<InvestmentMovement> rows, string field, string? blockingReason = null)
    {
        if (blockingReason is not null) return new("unavailable", blockingReason);
        if (rows.Count == 0) return new("unavailable", "no_operations_reported");
        var sums = new SortedDictionary<string, ExactInvestmentDecimal>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            using var values = JsonDocument.Parse(row.ValuesJson);
            using var availability = JsonDocument.Parse(row.AvailabilityJson);
            var money = Money(values.RootElement, availability.RootElement, field);
            if (money is null) return new("unavailable", "field_not_provided_for_every_operation");
            sums[money.Currency] = sums.GetValueOrDefault(money.Currency, ExactInvestmentDecimal.Zero).Add(ExactInvestmentDecimal.Parse(money.Amount));
        }
        return new("sum_of_reported_operations", null, sums.Select(x => new InvestmentMoney(x.Value.Original, x.Key)).ToArray());
    }

    public static object Projection(ImportedInvestment asset, InvestmentContract? contract)
    {
        using var values = JsonDocument.Parse(contract?.ValuesJson ?? "{}");
        using var availability = JsonDocument.Parse(contract?.AvailabilityJson ?? "{}");
        using var provenance = JsonDocument.Parse(contract?.ProvenanceJson ?? "{}");
        var source = values.RootElement;
        var hasTerms = source.TryGetProperty("remuneration", out var terms) && terms.ValueKind == JsonValueKind.Object;
        var indexer = hasTerms && terms.TryGetProperty("values", out var rule) && rule.TryGetProperty("indexer", out var idx) ? idx.GetString() : null;
        var basis = indexer switch { "CDI" => "CDI", "DI" => "DI", "SELIC" => "SELIC", "IPCA" => "IPCA", "PRE_FIXADO" => "PREFIXED", _ => "OTHER_OR_MISSING" };
        // The official v2 page contradicts itself: a.a. 16.76/100% CDI 100 in its
        // interpretation table; 0.150000/1.000000 in the nested schema. Magnitude is
        // not a reliable way to resolve units. Do not normalize either contract field.
        var reason = asset.Family switch
        {
            "funds" => "fund_returns_and_quota_taxation_not_predictable_from_contract",
            "variable-incomes" => "market_prices_and_distributions_not_predictable_from_contract",
            _ when !hasTerms || indexer is null => "missing_contractual_remuneration",
            _ when basis is "OTHER_OR_MISSING" or "DI" => "indexer_rule_not_implemented",
            _ => "ambiguous_documented_rate_scale"
        };
        return new
        {
            status = "unavailable", reason, source = "imported_contract", family = asset.Family, indexer, basis,
            contractualValues = source.Clone(), contractualAvailability = availability.RootElement.Clone(),
            contractualProvenance = provenance.RootElement.Clone(),
            normalization = "not_performed", series = (object?)null, includesEffectiveTaxes = false,
            requiredEvidence = new[] { "unambiguous_field_units", "applicable_compounding_and_day_count", "valuation_basis",
                "cash_flow_and_maturity_rules", "future_indexer_scenario_for_indexed_products" },
            financialKind = "projection_capability_not_historical_return"
        };
    }
}
