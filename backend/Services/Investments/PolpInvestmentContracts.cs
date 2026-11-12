using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ControlFinance.API.Services.Investments;

public sealed record PolpInvestmentPage(IReadOnlyList<JsonElement> Data, string? NextCursor);

public sealed record InvestmentFamily(string Path, string Event, string ParentField, string NameField,
    IReadOnlyDictionary<string, string> Identity, IReadOnlyDictionary<string, string> Contract,
    IReadOnlyDictionary<string, string> Position, IReadOnlyDictionary<string, string> Movement);
public sealed record InvestmentDocument(JsonObject Values, JsonObject Availability)
{
    public string Json => Values.ToJsonString();
}

// Whitelists are deliberately per modality. In particular gross/net amounts are positions,
// fund *_provision fields remain provisions, and movement taxes stay on the actual operation.
public static class PolpInvestmentContracts
{
    private static Dictionary<string, string> Fields(string kind, params string[] names) => names.ToDictionary(n => n, _ => kind);
    private static Dictionary<string, string> Join(params Dictionary<string, string>[] sets) => sets.SelectMany(s => s).ToDictionary(x => x.Key, x => x.Value);
    private static readonly Dictionary<string, string> Remuneration = Join(
        Fields("text", "indexer", "indexer_additional_info", "rate_type", "rate_periodicity", "calculation"),
        Fields("decimal", "pre_fixed_rate", "post_fixed_indexer_percentage"));
    private static readonly Dictionary<string, string> FixedPosition = Join(
        Fields("text", "reference_date_time", "updated_at"), Fields("decimal", "quantity"),
        Fields("money", "updated_unit_price", "gross_amount", "net_amount", "income_tax", "financial_transaction_tax", "blocked_balance", "purchase_unit_price"));
    private static readonly Dictionary<string, string> FixedMovement = Join(
        Fields("text", "type", "transaction_type", "transaction_type_additional_info", "transaction_date", "created_at", "updated_at"),
        Fields("decimal", "transaction_quantity", "remuneration_transaction_rate", "indexer_percentage"),
        Fields("money", "transaction_unit_price", "transaction_gross_value", "income_tax", "financial_transaction_tax", "transaction_net_value"));
    private static readonly Dictionary<string, string> FixedContract = Join(
        Fields("date", "issue_date", "purchase_date", "due_date", "grace_period_date"),
        Fields("money", "issue_unit_price"), Fields("remuneration", "remuneration"));
    private static readonly Dictionary<string, string> FixedIdentity = Fields("text", "investment_type", "issuer_institution_cnpj_number", "isin_code", "clearing_code");

    public static readonly IReadOnlyDictionary<string, InvestmentFamily> Families = new Dictionary<string, InvestmentFamily>
    {
        ["bank-fixed-incomes"] = new("bank-fixed-incomes", "bank_fixed_incomes", "bank_fixed_income_id", "", FixedIdentity, FixedContract, FixedPosition, FixedMovement),
        ["credit-fixed-incomes"] = new("credit-fixed-incomes", "credit_fixed_incomes", "credit_fixed_income_id", "", 
            Join(FixedIdentity, Fields("text", "debtor_cnpj_number", "debtor_name")),
            Join(FixedContract, Fields("text", "tax_exempt_product", "voucher_payment_indicator", "voucher_payment_periodicity", "voucher_payment_periodicity_additional_info")),
            Join(FixedPosition, Fields("money", "fine", "late_payment")), FixedMovement),
        ["treasure-titles"] = new("treasure-titles", "treasure_titles", "treasure_title_id", "product_name",
            Fields("text", "product_name", "isin_code"),
            Join(Fields("date", "purchase_date", "due_date"), Fields("text", "voucher_payment_indicator", "voucher_payment_periodicity", "voucher_payment_periodicity_additional_info"), Fields("remuneration", "remuneration")),
            FixedPosition, FixedMovement),
        ["funds"] = new("funds", "funds", "fund_id", "name",
            Fields("text", "name", "cnpj_number", "isin_code", "anbima_category", "anbima_class", "anbima_subclass"),
            new Dictionary<string, string>(),
            Join(Fields("text", "reference_date", "updated_at"), Fields("decimal", "quota_quantity"),
                Fields("money", "gross_amount", "net_amount", "income_tax_provision", "financial_transaction_tax_provision", "blocked_amount", "quota_gross_price_value")),
            Join(Fields("text", "type", "transaction_type", "transaction_conversion_date", "transaction_type_additional_info", "created_at", "updated_at"),
                Fields("decimal", "transaction_quota_quantity"),
                Fields("money", "transaction_quota_price", "transaction_value", "transaction_gross_value", "income_tax", "financial_transaction_tax", "transaction_exit_fee", "transaction_net_value"))),
        ["variable-incomes"] = new("variable-incomes", "variable_incomes", "variable_income_id", "",
            Fields("text", "ticker", "isin_code", "issuer_institution_cnpj_number"), new Dictionary<string, string>(),
            Join(Fields("text", "reference_date", "updated_at"), Fields("decimal", "quantity", "price_factor"), Fields("money", "gross_amount", "blocked_balance", "closing_price")),
            Join(Fields("text", "type", "transaction_type", "transaction_type_additional_info", "transaction_date", "broker_note_id", "created_at", "updated_at"),
                Fields("decimal", "price_factor", "transaction_quantity"), Fields("money", "transaction_unit_price", "transaction_value")))
    };
    public static readonly IReadOnlyDictionary<string, string> ReservedFields = Join(
        Fields("text", "reserved_name", "reserved_identification", "created_at", "updated_at"), Fields("reserves", "available_amount"));
    public static InvestmentFamily Family(string path) => Families.TryGetValue(path, out var spec) ? spec : throw new PolpPermanentException("Modalidade de investimento desconhecida.");
    public static string? EventFamily(string name) => Families.Values.FirstOrDefault(f => name == f.Event || name == f.Event + ".transactions")?.Path;
    public static string ScheduleResource(string family) => family switch
    {
        "bank-fixed-incomes" => "bank_fixed_income", "credit-fixed-incomes" => "credit_fixed_income",
        "funds" => "fund", "treasure-titles" => "treasure_title", "variable-incomes" => "variable_income",
        _ => throw new PolpPermanentException("Modalidade de cronograma desconhecida.")
    };
    public static bool IsInvestmentSchedule(string resource) => Families.Keys.Any(f =>
        resource == ScheduleResource(f) || new[] { ".product", ".balances", ".transactions" }.Any(s => resource == ScheduleResource(f) + s))
        || resource is "variable_income.broker_notes" or "account.reserved_balances";
    public static string ResourceType(string family) => family switch
    {
        "bank-fixed-incomes" => "BANK_FIXED_INCOME", "credit-fixed-incomes" => "CREDIT_FIXED_INCOME",
        "funds" => "FUND", "treasure-titles" => "TREASURE_TITLE", "variable-incomes" => "VARIABLE_INCOME",
        _ => throw new PolpPermanentException("Modalidade desconhecida.")
    };

    public static string Identifier(JsonElement row, string field)
    {
        if (!row.TryGetProperty(field, out var value)) throw new PolpPermanentException("Identificador de investimento ausente.");
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
        return ValidateIdentifier(text);
    }
    public static string ValidateIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 160 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new PolpPermanentException("Identificador de investimento inválido.");
        return Guid.TryParse(value, out var guid) ? guid.ToString("D") : value;
    }
    public static DateTime? Date(JsonElement row, string field)
    {
        if (!row.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw new PolpPermanentException("Data de investimento fora do contrato.");
        return ParseDate(value.GetString() ?? "");
    }
    public static DateTime ParseDate(string value)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc); // civil date, not inferred acquisition time
        var hasOffset = value.EndsWith('Z') || (value.Length > 19 && (value[19..].Contains('+') || value[19..].Contains('-')));
        if (hasOffset && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)) return instant.UtcDateTime;
        throw new PolpPermanentException("Data de investimento inválida ou sem fuso horário.");
    }
    public static InvestmentDocument Select(JsonElement source, IReadOnlyDictionary<string, string> fields, bool legacyProduct = false)
    {
        var values = new JsonObject();
        var availability = new JsonObject();
        foreach (var (field, kind) in fields)
        {
            var present = source.ValueKind == JsonValueKind.Object && source.TryGetProperty(field, out _);
            JsonElement raw = default;
            if (present) raw = source.GetProperty(field);
            else if (legacyProduct && source.ValueKind == JsonValueKind.Object && source.TryGetProperty("product", out var product)
                && product.ValueKind == JsonValueKind.Object && product.TryGetProperty(field, out raw)) present = true;
            availability[field] = !present ? "not_provided" : raw.ValueKind == JsonValueKind.Null ? "not_informed" : "available";
            if (!present || raw.ValueKind == JsonValueKind.Null) continue;
            values[field] = ReadValue(raw, kind);
            if (values[field] is null || (kind == "money" && !CompleteMoney(values[field]))
                || (kind == "reserves" && values[field]!.AsArray().Any(x => !CompleteMoney(x)))) availability[field] = "incomplete";
        }
        return new(values, availability);
    }
    private static JsonNode? ReadValue(JsonElement raw, string kind)
    {
        switch (kind)
        {
            case "money":
                if (raw.ValueKind != JsonValueKind.Object) throw new PolpPermanentException("Valor monetário fora do contrato.");
                var result = new JsonObject();
                if (raw.TryGetProperty("amount", out var amount) && amount.ValueKind != JsonValueKind.Null) result["amount"] = DecimalText(amount);
                if (raw.TryGetProperty("currency", out var currency) && currency.ValueKind != JsonValueKind.Null)
                {
                    if (currency.ValueKind != JsonValueKind.String) throw new PolpPermanentException("Moeda de investimento fora do contrato.");
                    var code = currency.GetString();
                    if (code is null || code.Length != 3 || code.Any(c => c < 'A' || c > 'Z')) throw new PolpPermanentException("Moeda de investimento inválida.");
                    result["currency"] = code;
                }
                return result;
            case "decimal": return JsonValue.Create(DecimalText(raw));
            case "date":
                if (raw.ValueKind != JsonValueKind.String) throw new PolpPermanentException("Data de contrato fora do contrato.");
                var date = raw.GetString() ?? throw new PolpPermanentException("Data de contrato ausente.");
                if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    throw new PolpPermanentException("Data de contrato inválida.");
                return JsonValue.Create(date);
            case "remuneration":
                if (raw.ValueKind != JsonValueKind.Object) throw new PolpPermanentException("Remuneração fora do contrato.");
                // Null subfields are retained as unavailable in the latest snapshot, not as zeros.
                var terms = Select(raw, Remuneration);
                return new JsonObject { ["values"] = terms.Values, ["availability"] = terms.Availability };
            case "reserves":
                if (raw.ValueKind != JsonValueKind.Array) throw new PolpPermanentException("Saldos reservados fora do contrato.");
                var balances = new JsonArray();
                foreach (var item in raw.EnumerateArray())
                {
                    var money = ReadValue(item, "money");
                    if (money is null) throw new PolpPermanentException("Saldo reservado inválido.");
                    if (item.TryGetProperty("remuneration", out var rule) && rule.ValueKind != JsonValueKind.Null)
                        money["remuneration"] = ReadValue(rule, "remuneration");
                    money["remuneration_availability"] = !item.TryGetProperty("remuneration", out var supplied) ? "not_provided"
                        : supplied.ValueKind == JsonValueKind.Null ? "not_informed" : "available";
                    balances.Add(money);
                }
                return balances;
            default:
                if (raw.ValueKind != JsonValueKind.String) throw new PolpPermanentException("Campo de investimento fora do contrato.");
                return JsonValue.Create(raw.GetString());
        }
    }
    private static string DecimalText(JsonElement value)
    {
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
        if (text is null) throw new PolpPermanentException("Decimal de investimento ausente.");
        return ExactInvestmentDecimal.Parse(text).Original;
    }
    private static bool CompleteMoney(JsonNode? node) => node is JsonObject money && money["amount"] is not null && money["currency"] is not null;
    public static DateTime? Reference(InvestmentDocument doc)
    {
        var value = doc.Values["reference_date_time"] ?? doc.Values["reference_date"];
        return value is null ? null : ParseDate(value.GetValue<string>());
    }
    public static string Hash(JsonNode node)
    {
        // Serializer insertion order is fixed by the modality schema, not provider key order.
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString()))).ToLowerInvariant();
    }
    public static (string Values, string Availability, string Provenance) Merge(string previous, string provenance,
        InvestmentDocument incoming, DateTime observedAt, DateTime? referenceAt, bool replaceKnown = true)
    {
        var values = JsonNode.Parse(previous)!.AsObject();
        var sources = JsonNode.Parse(provenance)!.AsObject();
        var availability = incoming.Availability.DeepClone().AsObject();
        foreach (var (key, value) in incoming.Values)
        {
            if (value is null) continue;
            if (incoming.Availability.TryGetPropertyValue(key, out var state) && state?.GetValue<string>() != "available") continue;
            if (!replaceKnown && values[key] is { } known && !JsonNode.DeepEquals(known, value))
            {
                availability[key] = "conflicting_source_version";
                continue;
            }
            if (key == "remuneration" && value is JsonObject terms && values[key] is JsonObject oldTerms)
            {
                var oldValues = oldTerms["values"]!.AsObject();
                foreach (var field in terms["values"]!.AsObject())
                {
                    if (field.Value is null) continue;
                    oldValues[field.Key] = field.Value.DeepClone();
                    sources[key + "." + field.Key] = Source(observedAt, referenceAt);
                }
                oldTerms["availability"] = terms["availability"]!.DeepClone();
                continue;
            }
            // Monetary objects and currency arrays are atomic. Never combine an old amount
            // with a new currency, and never sum or coerce different currencies.
            values[key] = value.DeepClone();
            sources[key] = Source(observedAt, referenceAt);
        }
        return (values.ToJsonString(), availability.ToJsonString(), sources.ToJsonString());
    }
    private static JsonObject Source(DateTime observedAt, DateTime? referenceAt) => new()
    {
        ["observed_at"] = observedAt.ToString("O"), ["reference_at"] = referenceAt?.ToString("O")
    };
}
