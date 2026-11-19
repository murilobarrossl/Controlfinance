using System.Text.Json;
using ControlFinance.API.Models;

namespace ControlFinance.API.Services.Investments;

// O Control Finance não calcula valor aplicado, rendimento, rentabilidade, preço médio nem valorização: a Polp
// não envia esses campos e um número próprio poderia divergir do que o banco mostra. Só descreve as condições
// contratadas, sem projetar nada.
public static class InvestmentFinancialRules
{
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
