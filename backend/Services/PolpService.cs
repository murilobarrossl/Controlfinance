using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlFinance.API.Services.Investments;

namespace ControlFinance.API.Services;

// ──────────────────────────────────────────
//  DTOs de resposta da Polp API v2 / Celcoin (snake_case)
//  Campos conferidos contra a documentação oficial; o que a documentação não especifica
//  (ex.: forma exata de "institution") fica nullable para não quebrar a desserialização.
// ──────────────────────────────────────────

public class PolpMoney
{
    // A Polp manda valores como string ("1500.00"); unbilled_amount é o único número, e não é usado.
    [JsonPropertyName("amount")] public string? Amount { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }

    [JsonIgnore]
    public decimal? Value =>
        decimal.TryParse(Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
}

// Schema oficial v2: /credit-cards/{creditCard}/recurrings (campos camelCase).
// A detecção é derivada pela Polp; não é uma confirmação de assinatura pela instituição.
public class PolpCardRecurringDto
{
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("periodMonths")] public int? PeriodMonths { get; set; }
    [JsonPropertyName("expectedDay")] public int? ExpectedDay { get; set; }
    [JsonPropertyName("nextExpectedAt")] public DateOnly? NextExpectedAt { get; set; }
    [JsonPropertyName("occurrences")] public List<Guid> Occurrences { get; set; } = [];
}

public class PolpListMeta
{
    [JsonPropertyName("next_cursor")] public string? NextCursor { get; set; }
}

public class PolpListEnvelope<T>
{
    [JsonPropertyName("data")] public List<T> Data { get; set; } = [];
    [JsonPropertyName("meta")] public PolpListMeta? Meta { get; set; }
}

public class PolpInstitutionDto
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("logo_url")] public string? LogoUrl { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

public class PolpConsentInstitutionDto
{
    [JsonPropertyName("id")] public Guid? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public class PolpConsentDto
{
    [JsonPropertyName("cliente_user_id")] public string? ClientUserId { get; set; }
    [JsonPropertyName("products")] public List<string>? Products { get; set; }
    [JsonPropertyName("updated_at")] public DateTime? UpdatedAt { get; set; }
    [JsonPropertyName("created_at")] public DateTime? CreatedAt { get; set; }
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("institution_id")] public Guid InstitutionId { get; set; }
    [JsonPropertyName("institution")] public PolpConsentInstitutionDto? Institution { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("execution_status")] public string? ExecutionStatus { get; set; }
    [JsonPropertyName("flags")] public List<string> Flags { get; set; } = [];
    [JsonPropertyName("url_to_authenticate")] public string? UrlToAuthenticate { get; set; }
    [JsonPropertyName("url_to_authenticate_expires_at")] public DateTime? UrlToAuthenticateExpiresAt { get; set; }
    // "error" é null ou objeto, dependendo da falha. JsonElement aceita os dois formatos.
    [JsonPropertyName("error")] public JsonElement? Error { get; set; }

    /// <summary>Extrai uma mensagem de erro legível, seja "error" string, objeto ou null.</summary>
    [JsonIgnore]
    public string? ErrorMessage
    {
        get
        {
            if (Error is not { } el || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return null;

            if (el.ValueKind == JsonValueKind.String) return el.GetString();

            if (el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                    return msg.GetString();
                if (el.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                    return code.GetString();
            }

            return el.GetRawText();
        }
    }
}

public class PolpBalanceDto
{
    [JsonPropertyName("update_date_time")] public string? UpdateDateTime { get; set; }
    [JsonPropertyName("available_amount")] public PolpMoney? AvailableAmount { get; set; }
    [JsonPropertyName("has_reserved_balance")] public bool? HasReservedBalance { get; set; }
}

// Só os campos exibidos ao titular. Número do contrato, código IPOC, CNPJ do consignante,
// garantias e ids de pagamento ficam de fora por minimização.
public class PolpLoanDto
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("consent_id")] public Guid ConsentId { get; set; }
    [JsonPropertyName("product_type")] public string? ProductType { get; set; }
    [JsonPropertyName("product_sub_type")] public string? ProductSubType { get; set; }
    [JsonPropertyName("product_name")] public string? ProductName { get; set; }
    [JsonPropertyName("contract_date")] public string? ContractDate { get; set; }
    [JsonPropertyName("contract_amount")] public string? ContractAmount { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("cet")] public string? Cet { get; set; }
    [JsonPropertyName("instalment_periodicity")] public string? InstalmentPeriodicity { get; set; }
    [JsonPropertyName("amortization_scheduled")] public string? AmortizationScheduled { get; set; }
    [JsonPropertyName("first_instalment_due_date")] public string? FirstInstalmentDueDate { get; set; }
    [JsonPropertyName("due_date")] public string? DueDate { get; set; }
    [JsonPropertyName("settlement_date")] public string? SettlementDate { get; set; }
    [JsonPropertyName("next_instalment_amount")] public string? NextInstalmentAmount { get; set; }
    [JsonPropertyName("scheduled_instalments")] public PolpLoanInstalmentsDto? ScheduledInstalments { get; set; }
    [JsonPropertyName("payments")] public PolpLoanPaymentsDto? Payments { get; set; }
    [JsonPropertyName("updated_at")] public DateTime? UpdatedAt { get; set; }
}

public class PolpLoanInstalmentsDto
{
    [JsonPropertyName("total_number_of_instalments")] public int? Total { get; set; }
    [JsonPropertyName("contract_remaining_number")] public int? Remaining { get; set; }
    [JsonPropertyName("paid_instalments")] public int? Paid { get; set; }
    [JsonPropertyName("due_instalments")] public int? Due { get; set; }
    [JsonPropertyName("past_due_instalments")] public int? PastDue { get; set; }
}

public class PolpLoanPaymentsDto
{
    [JsonPropertyName("contract_outstanding_balance")] public string? OutstandingBalance { get; set; }
    [JsonPropertyName("total_remaining_amount")] public string? TotalRemainingAmount { get; set; }
}

public class PolpAccountDto
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("consent_id")] public Guid ConsentId { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("check_digit")] public string? CheckDigit { get; set; }
    // CONTA_DEPOSITO_A_VISTA | CONTA_POUPANCA | CONTA_PAGAMENTO_PRE_PAGA
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("subtype")] public string? Subtype { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    // null enquanto o saldo não foi sincronizado.
    [JsonPropertyName("balance")] public PolpBalanceDto? Balance { get; set; }
}

public class PolpCardPaymentMethodDto
{
    [JsonPropertyName("identification_number")] public string? IdentificationNumber { get; set; }
}

public class PolpCardLimitDto
{
    [JsonPropertyName("credit_line_limit_type")] public string? CreditLineLimitType { get; set; }
    [JsonPropertyName("used_amount")] public PolpMoney? UsedAmount { get; set; }
    [JsonPropertyName("limit_amount")] public PolpMoney? LimitAmount { get; set; }
    [JsonPropertyName("available_amount")] public PolpMoney? AvailableAmount { get; set; }
}

public class PolpCreditCardDto
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("consent_id")] public Guid ConsentId { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("credit_card_network")] public string? CreditCardNetwork { get; set; }
    [JsonPropertyName("product_type")] public string? ProductType { get; set; }
    [JsonPropertyName("payment_methods")] public List<PolpCardPaymentMethodDto> PaymentMethods { get; set; } = [];
    [JsonPropertyName("limits")] public List<PolpCardLimitDto> Limits { get; set; } = [];
}

public class PolpBillPaymentDto
{
    [JsonPropertyName("amount")] public string? Amount { get; set; }
    [JsonPropertyName("paymentDate")] public string? PaymentDate { get; set; }
}

public class PolpBillDto
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("credit_card_id")] public Guid CreditCardId { get; set; }
    [JsonPropertyName("due_date")] public string? DueDate { get; set; }
    [JsonPropertyName("bill_closing_date")] public string? BillClosingDate { get; set; }
    [JsonPropertyName("bill_minimum_amount")] public PolpMoney? BillMinimumAmount { get; set; }
    [JsonPropertyName("bill_total_amount")] public PolpMoney? BillTotalAmount { get; set; }
    [JsonPropertyName("payments")] public List<PolpBillPaymentDto> Payments { get; set; } = [];
}

public class PolpCounterpartyDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("alias")] public string? Alias { get; set; }
    [JsonPropertyName("tax_id")] public string? TaxId { get; set; }
}

public class PolpAccountTransactionDto
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("account_id")] public Guid AccountId { get; set; }
    [JsonPropertyName("transaction_name")] public string? TransactionName { get; set; }
    [JsonPropertyName("transaction_date_time")] public string? TransactionDateTime { get; set; }
    // CREDITO | DEBITO
    [JsonPropertyName("credit_debit_type")] public string? CreditDebitType { get; set; }
    [JsonPropertyName("transaction_amount")] public PolpMoney? TransactionAmount { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    // Agência, CPF e conta completa da contraparte só viram hash por empresa no sync (login CNPJ).
    [JsonPropertyName("partie_compe_code")] public string? PartieCompeCode { get; set; }
    [JsonPropertyName("partie_number")] public string? PartieNumber { get; set; }
    [JsonPropertyName("partie_person_type")] public string? PartiePersonType { get; set; }
    [JsonPropertyName("partie_cnpj_cpf")] public string? PartieCnpjCpf { get; set; }
    [JsonPropertyName("partie_branch_code")] public string? PartieBranchCode { get; set; }
    [JsonPropertyName("partie_check_digit")] public string? PartieCheckDigit { get; set; }
    // Preenchidos depois, de forma assíncrona (webhook accounts.transactions de novo).
    [JsonPropertyName("counterparty")] public PolpCounterpartyDto? Counterparty { get; set; }
    [JsonPropertyName("category_ref")] public string? CategoryRef { get; set; }
}

public class PolpCardTransactionDto
{
    [JsonPropertyName("id")] public Guid Id { get; set; }
    [JsonPropertyName("credit_card_id")] public Guid CreditCardId { get; set; }
    [JsonPropertyName("transaction_name")] public string? TransactionName { get; set; }
    [JsonPropertyName("transaction_date_time")] public string? TransactionDateTime { get; set; }
    // CREDITO | DEBITO
    [JsonPropertyName("credit_debit_type")] public string? CreditDebitType { get; set; }
    // PAGAMENTO_FATURA identifica o pagamento da fatura, que não é uma despesa nova.
    [JsonPropertyName("transaction_type")] public string? TransactionType { get; set; }
    // Valor em reais; "amount" é o original (moeda estrangeira).
    [JsonPropertyName("brazilian_amount")] public PolpMoney? BrazilianAmount { get; set; }
    [JsonPropertyName("amount")] public PolpMoney? OriginalAmount { get; set; }
    // "0001-01-01" enquanto a transação não foi lançada em fatura (inclui parcelas futuras).
    [JsonPropertyName("bill_post_date")] public string? BillPostDate { get; set; }
    [JsonPropertyName("bill_id")] public Guid? BillId { get; set; }
    [JsonPropertyName("charge_identificator")] public int? ChargeIdentificator { get; set; }
    [JsonPropertyName("charge_number")] public int? ChargeNumber { get; set; }
    // AAAA-MM da fatura em que a transação cai; é a única data útil de uma parcela futura.
    [JsonPropertyName("bill_forecast_date")] public string? BillForecastDate { get; set; }
    [JsonPropertyName("identification_number")] public string? IdentificationNumber { get; set; }
    [JsonPropertyName("counterparty")] public PolpCounterpartyDto? Counterparty { get; set; }
    [JsonPropertyName("category_ref")] public string? CategoryRef { get; set; }
}

// 422 da Polp: Fields são só nomes de campo (ex.: "cnpj", "cpf"), nunca valores.
public sealed class PolpValidationException(string message, IReadOnlyList<string> fields)
    : HttpRequestException(message, null, HttpStatusCode.UnprocessableEntity)
{
    public IReadOnlyList<string> Fields { get; } = fields;
}

// Resumo de um corpo de erro de validação que pode ir para o log: código, nomes de campo e
// mensagens com todo dígito trocado por "#" (CPF, CNPJ, conta e valores somem) e tamanho limitado.
// Só chaves conhecidas são lidas; o resto do corpo é ignorado.
public sealed record PolpValidationSummary(string? Code, IReadOnlyList<string> Fields, IReadOnlyList<string> Messages)
{
    private const int MaxText = 160;
    private const int MaxItems = 8;
    private static readonly System.Text.RegularExpressions.Regex Digit = new(@"\d", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex FieldName = new(@"^[A-Za-z_][A-Za-z0-9_.]{0,39}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static PolpValidationSummary From(string body)
    {
        string? code = null;
        var fields = new List<string>();
        var messages = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new(null, fields, messages);

            code = Text(root, "code");
            AddMessage(messages, Text(root, "message"));
            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String) AddMessage(messages, error.GetString());
                else if (error.ValueKind == JsonValueKind.Object)
                {
                    code ??= Text(error, "code");
                    AddMessage(messages, Text(error, "message"));
                }
            }
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject().Take(MaxItems))
                {
                    if (!FieldName.IsMatch(field.Name)) continue;
                    fields.Add(field.Name);
                    var first = field.Value.ValueKind == JsonValueKind.Array
                        ? field.Value.EnumerateArray().FirstOrDefault()
                        : field.Value;
                    if (first.ValueKind == JsonValueKind.String) AddMessage(messages, first.GetString());
                }
            }
        }
        catch (JsonException)
        {
            // Corpo que não é JSON não entra no log nem resumido.
        }
        return new(code is null ? null : Clean(code), fields, messages);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void AddMessage(List<string> messages, string? message)
    {
        if (!string.IsNullOrWhiteSpace(message) && messages.Count < MaxItems) messages.Add(Clean(message));
    }

    private static string Clean(string text)
    {
        var masked = Digit.Replace(text.Trim(), "#");
        return masked.Length > MaxText ? masked[..MaxText] + "…" : masked;
    }
}

// OperatorCpf: só em conexão de empresa (Document = CNPJ); a Polp exige o CPF de quem opera a conta.
public record CreatePolpConsentRequest(Guid InstitutionId, string Document, Guid ClientUserId, string? OperatorCpf = null);

public class PolpConsentResourceDto
{
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("resource_id")] public string? ResourceId { get; set; }
}

/// <summary>
/// Janela de sincronização que a Polp manda no webhook (query_parameters). O texto vem de uma
/// requisição pública (o webhook só é protegido por um token na URL), então nunca é repassado cru
/// à Polp: só os quatro campos conhecidos, e só se forem datas válidas, são remontados.
/// </summary>
public sealed record PolpQueryWindow(string Query)
{
    private static readonly string[] AllowedKeys = ["fromCreatedAt", "toCreatedAt", "fromUpdatedAt", "toUpdatedAt"];

    // A Polp lê valor sem sufixo como horário de Brasília e com "Z" como UTC (confirmado no
    // sandbox). Reemitir um instante UTC sem o "Z" deslocava a janela em 3 h e perdia lançamentos.
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById(UserClock.DefaultTimeZoneId);

    public static PolpQueryWindow? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 400) return null;

        var parts = new List<string>();
        foreach (var pair in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length != 2 || !AllowedKeys.Contains(kv[0])) continue;

            var value = Uri.UnescapeDataString(kv[1]);
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                continue;

            var wallClock = parsed.Kind switch
            {
                DateTimeKind.Unspecified => parsed,
                _ => TimeZoneInfo.ConvertTimeFromUtc(parsed.ToUniversalTime(), Brasilia)
            };
            parts.Add(Format(kv[0], wallClock));
        }

        return parts.Count == 0 ? null : new PolpQueryWindow(string.Join('&', parts));
    }

    /// <summary>Tudo que foi criado OU alterado desde o instante dado (a Polp combina os dois
    /// filtros com OR).</summary>
    public static PolpQueryWindow ChangedSince(DateTimeOffset instant)
    {
        var wallClock = TimeZoneInfo.ConvertTimeFromUtc(instant.UtcDateTime, Brasilia);
        return new PolpQueryWindow($"{Format("fromCreatedAt", wallClock)}&{Format("fromUpdatedAt", wallClock)}");
    }

    private static string Format(string key, DateTime wallClock) =>
        $"{key}={Uri.EscapeDataString(wallClock.ToString("yyyy-MM-ddTHH:mm:ss.ffffff", CultureInfo.InvariantCulture))}";
}

// ──────────────────────────────────────────
//  Interface + implementação
// ──────────────────────────────────────────

public interface IPolpService
{
    Task<PolpInvestmentPage> GetInvestmentSchedulePageAsync(Guid consentId, string? cursor, CancellationToken ct = default) => throw new NotSupportedException();
    Task<PolpInvestmentPage> GetInvestmentPageAsync(string family, string parentId, bool transactions,
        string? cursor, PolpQueryWindow? window, CancellationToken ct = default) => throw new NotSupportedException();
    Task<JsonElement> GetInvestmentDetailAsync(string family, string externalId, CancellationToken ct = default) => throw new NotSupportedException();
    Task<PolpListEnvelope<PolpConsentDto>> GetConsentPageAsync(string? cursor, CancellationToken ct = default) =>
        throw new NotSupportedException();
    Task<List<PolpConsentResourceDto>> GetConsentResourcesAsync(Guid consentId, CancellationToken ct = default) =>
        throw new NotSupportedException();
    Task<List<PolpInstitutionDto>> GetInstitutionsAsync(CancellationToken ct = default);
    Task<PolpConsentDto> CreateConsentAsync(CreatePolpConsentRequest request, CancellationToken ct = default);
    Task<PolpConsentDto> RecreateConsentAsync(Guid consentId, bool business, CancellationToken ct = default) => throw new NotSupportedException();
    Task<List<PolpLoanDto>> GetLoansAsync(Guid consentId, CancellationToken ct = default) => throw new NotSupportedException();
    Task<PolpLoanDto> GetLoanAsync(Guid loanId, CancellationToken ct = default) => throw new NotSupportedException();
    Task<PolpConsentDto> GetConsentAsync(Guid consentId, CancellationToken ct = default);
    Task<List<PolpAccountDto>> GetAccountsAsync(Guid consentId, PolpQueryWindow? window = null, CancellationToken ct = default);
    Task<List<PolpCreditCardDto>> GetCreditCardsAsync(Guid consentId, PolpQueryWindow? window = null, CancellationToken ct = default);
    Task<List<PolpBillDto>> GetBillsAsync(Guid creditCardId, PolpQueryWindow? window = null, CancellationToken ct = default);
    Task<List<PolpAccountTransactionDto>> GetAccountTransactionsAsync(Guid accountId, PolpQueryWindow? window = null, CancellationToken ct = default);
    Task<List<PolpCardRecurringDto>?> GetCardRecurringsAsync(Guid creditCardId, CancellationToken ct = default) =>
        Task.FromResult<List<PolpCardRecurringDto>?>(null);
    Task<List<PolpCardTransactionDto>> GetCardTransactionsAsync(Guid creditCardId, PolpQueryWindow? window = null, CancellationToken ct = default);

    /// <summary>Consulta o saldo na instituição na hora (POST /accounts/{id}/balance/sync). Limite
    /// de 30 req/min por cliente da Polp: só para quando o usuário pede o saldo mais recente.</summary>
    Task<PolpAccountDto> SyncBalanceAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>Revoga o consentimento (DELETE /consents/{id}). 404 (já inexistente) conta como
    /// concluído.</summary>
    Task DeleteConsentAsync(Guid consentId, CancellationToken ct = default);
}

public class PolpService : IPolpService
{
    // Teto de segurança por listagem: 500 itens por página de transação, então 50 páginas cobrem
    // 25 mil lançamentos; evita um laço indefinido se a Polp devolver um cursor que não termina.
    internal const int MaxPages = 50;

    private static readonly string[] BusinessProducts = ["ACCOUNT", "CREDIT_CARD_ACCOUNT", "INVESTMENTS"];
    // CREDIT_OPERATIONS só para CPF: os contratos de empréstimo só são importados para pessoa física.
    private static readonly string[] PersonalProducts = [.. BusinessProducts, LoanContractsProduct];
    public const string LoanContractsProduct = "CREDIT_OPERATIONS";

    public static string[] ProductsFor(bool business) => business ? BusinessProducts : PersonalProducts;

    private readonly HttpClient _http;
    private readonly Uri _institutionsUri;
    private readonly string? _redirectUrl;
    private readonly ILogger<PolpService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PolpService(HttpClient http, IConfiguration config, ILogger<PolpService> logger)
    {
        _logger = logger;

        // Sem fallback pra URL de sandbox: um ambiente de teste nunca deve virar o destino real
        // dos dados de um usuário só porque a configuração ficou faltando. Em teste, aponte
        // Polp:BaseUrl para https://api.polp.com.br/api/v2/sandbox.
        var baseUrl = config["Polp:BaseUrl"]
            ?? throw new InvalidOperationException("Polp:BaseUrl não configurado.");
        if (!baseUrl.EndsWith('/')) baseUrl += "/";

        var clientId = config["Polp:ClientId"]
            ?? throw new InvalidOperationException("Polp:ClientId não configurado em appsettings.json");
        var clientSecret = config["Polp:ClientSecret"]
            ?? throw new InvalidOperationException("Polp:ClientSecret não configurado em appsettings.json");

        _redirectUrl = config["Polp:RedirectUrl"];

        // GET /institutions é público e só existe na raiz da v2, mesmo para o Sandbox (a rota
        // /sandbox/institutions não existe).
        var institutionsBase = baseUrl.EndsWith("/sandbox/", StringComparison.OrdinalIgnoreCase)
            ? baseUrl[..^"sandbox/".Length]
            : baseUrl;
        _institutionsUri = new Uri(institutionsBase + "institutions");

        _http = http;
        _http.BaseAddress = new Uri(baseUrl);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Add("x-api-client", clientId);
        _http.DefaultRequestHeaders.Add("x-api-secret", clientSecret);
    }

    public async Task<List<PolpInstitutionDto>> GetInstitutionsAsync(CancellationToken ct = default) =>
        await GetAllRawAsync<PolpInstitutionDto>(_institutionsUri.ToString(), null, "listar instituições", ct);

    public async Task<PolpConsentDto> CreateConsentAsync(CreatePolpConsentRequest request, CancellationToken ct = default)
    {
        // Pessoa física: só "cpf". Empresa (regra BUSINESS da v2): "cnpj" junto do "cpf" de quem
        // opera a conta; sem os dois a Polp responde 422.
        var isCnpj = request.Document.Length > 11;

        var body = new Dictionary<string, object?>
        {
            ["institution_id"] = request.InstitutionId,
            // Id interno e opaco do usuário, nunca e-mail: a Polp é um terceiro e o campo é livre.
            ["cliente_user_id"] = request.ClientUserId.ToString(),
            ["products"] = ProductsFor(isCnpj),
            // Reconectar o mesmo CPF consome o teto mensal regulatório de consultas: reaproveita
            // um consentimento ativo da mesma instituição em vez de criar outro.
            // Reusing a consent never expands its permissions. All new bank journeys request
            // the complete product set; there is no investment-only creation path.
            ["avoidDuplicates"] = true
        };
        if (isCnpj)
        {
            body["cnpj"] = request.Document;
            if (!string.IsNullOrWhiteSpace(request.OperatorCpf)) body["cpf"] = request.OperatorCpf;
        }
        else
        {
            body["cpf"] = request.Document;
        }
        if (!string.IsNullOrWhiteSpace(_redirectUrl)) body["redirectUrl"] = _redirectUrl;

        var resp = await _http.PostAsJsonAsync("consents", body, ct);
        try
        {
            await EnsureSuccess(resp, "criar consentimento");
        }
        // Banco sem operações de crédito não pode impedir a conexão: só uma recusa que aponta o
        // campo products tira CREDIT_OPERATIONS, e os contratos simplesmente não são importados.
        catch (PolpValidationException ex) when (!isCnpj && ex.Fields.Any(f => f.StartsWith("products", StringComparison.Ordinal)))
        {
            body["products"] = BusinessProducts;
            resp = await _http.PostAsJsonAsync("consents", body, ct);
            await EnsureSuccess(resp, "criar consentimento");
        }
        return await ReadResourceAsync<PolpConsentDto>(resp, "criar consentimento", ct);
    }

    public async Task<PolpConsentDto> RecreateConsentAsync(Guid consentId, bool business, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["products"] = ProductsFor(business) };
        if (!string.IsNullOrWhiteSpace(_redirectUrl)) body["redirectUrl"] = _redirectUrl;
        var resp = await _http.PostAsJsonAsync($"consents/{consentId:D}/recreate", body, ct);
        await EnsureSuccess(resp, "recriar consentimento");
        return await ReadResourceAsync<PolpConsentDto>(resp, "recriar consentimento", ct);
    }

    // CREDIT_OPERATIONS fica fora da conferência: o banco pode não oferecer o produto, e recriar
    // o consentimento a cada reconexão não mudaria isso.
    public static bool HasAllRequestedProducts(IEnumerable<string>? products) =>
        products is not null && BusinessProducts.All(products.Contains);

    public async Task<PolpConsentDto> GetConsentAsync(Guid consentId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"consents/{consentId}", ct);
        await EnsureSuccess(resp, "consultar consentimento");
        return await ReadResourceAsync<PolpConsentDto>(resp, "consultar consentimento", ct);
    }

    public async Task<PolpListEnvelope<PolpConsentDto>> GetConsentPageAsync(string? cursor, CancellationToken ct = default)
    {
        var path = cursor is null ? "consents" : $"consents?cursor={Uri.EscapeDataString(cursor)}";
        var resp = await _http.GetAsync(path, ct);
        await EnsureSuccess(resp, "recuperar confirmação de autorização");
        return await resp.Content.ReadFromJsonAsync<PolpListEnvelope<PolpConsentDto>>(JsonOptions, ct)
            ?? throw new JsonException("Resposta de consentimentos ausente.");
    }

    public async Task<List<PolpConsentResourceDto>> GetConsentResourcesAsync(Guid consentId, CancellationToken ct = default)
    {
        // Unlike other lists, /resources returns the complete data array (no cursors).
        var resp = await _http.GetAsync($"consents/{consentId}/resources", ct);
        await EnsureSuccess(resp, "consultar disponibilidade dos investimentos");
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new JsonException("Resposta de recursos ausente.");
        var result = data.Deserialize<List<PolpConsentResourceDto>>(JsonOptions)
            ?? throw new JsonException("Resposta de recursos ausente.");
        if (result.Any(r => string.IsNullOrWhiteSpace(r.Type) || string.IsNullOrWhiteSpace(r.Status)))
            throw new JsonException("Estado de recurso ausente.");
        return result;
    }

    public async Task<PolpInvestmentPage> GetInvestmentPageAsync(string family, string parentId, bool transactions,
        string? cursor, PolpQueryWindow? window, CancellationToken ct = default)
    {
        var parent = Uri.EscapeDataString(PolpInvestmentContracts.ValidateIdentifier(parentId));
        string path;
        if (family == "reserved-balances" && !transactions) path = $"accounts/{parent}/reserved-balances";
        else
        {
            var spec = PolpInvestmentContracts.Family(family);
            path = transactions ? $"{spec.Path}/{parent}/transactions" : $"consents/{parent}/{spec.Path}";
        }
        var query = new List<string>();
        if (window is not null) query.Add(window.Query);
        if (cursor is not null) query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        if (query.Count > 0) path += "?" + string.Join('&', query);
        return await InvestmentPageAsync(path, transactions ? 500 : 15, ct);
    }

    public Task<PolpInvestmentPage> GetInvestmentSchedulePageAsync(Guid consentId, string? cursor, CancellationToken ct = default)
    {
        var path = $"consents/{consentId:D}/sync-schedules";
        if (cursor is not null) path += "?cursor=" + Uri.EscapeDataString(cursor);
        return InvestmentPageAsync(path, 50, ct);
    }

    private async Task<PolpInvestmentPage> InvestmentPageAsync(string path, int pageSize, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        await EnsureSuccess(response, "consultar página de investimentos");
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("meta", out var meta) || meta.ValueKind != JsonValueKind.Object || !meta.TryGetProperty("next_cursor", out var next))
            throw new JsonException("Página de investimentos incompleta.");
        if (next.ValueKind is not (JsonValueKind.Null or JsonValueKind.String)) throw new JsonException("Cursor inválido.");
        if (data.GetArrayLength() > pageSize) throw new JsonException("Página de investimentos excede o contrato.");
        if (data.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Object)) throw new JsonException("Registro de investimento inválido.");
        var nextCursor = next.ValueKind == JsonValueKind.String ? next.GetString() : null;
        if (nextCursor?.Length > 4096 || (data.GetArrayLength() == 0 && !string.IsNullOrEmpty(nextCursor)))
            throw new JsonException("Paginação de investimentos inconsistente.");
        return new(data.EnumerateArray().Select(x => x.Clone()).ToList(), string.IsNullOrEmpty(nextCursor) ? null : nextCursor);
    }

    public async Task<JsonElement> GetInvestmentDetailAsync(string family, string externalId, CancellationToken ct = default)
    {
        var spec = PolpInvestmentContracts.Family(family);
        var id = Uri.EscapeDataString(PolpInvestmentContracts.ValidateIdentifier(externalId));
        using var response = await _http.GetAsync($"{spec.Path}/{id}", ct);
        await EnsureSuccess(response, "consultar identificação do investimento");
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Investimento ausente.");
        return root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object ? data.Clone() : root.Clone();
    }

    public Task<List<PolpAccountDto>> GetAccountsAsync(Guid consentId, PolpQueryWindow? window = null, CancellationToken ct = default) =>
        GetAllAsync<PolpAccountDto>($"consents/{consentId}/accounts", window, "listar contas", ct);

    public Task<List<PolpCreditCardDto>> GetCreditCardsAsync(Guid consentId, PolpQueryWindow? window = null, CancellationToken ct = default) =>
        GetAllAsync<PolpCreditCardDto>($"consents/{consentId}/credit-cards", window, "listar cartões de crédito", ct);

    public Task<List<PolpBillDto>> GetBillsAsync(Guid creditCardId, PolpQueryWindow? window = null, CancellationToken ct = default) =>
        GetAllAsync<PolpBillDto>($"credit-cards/{creditCardId}/bills", window, "listar faturas do cartão", ct);

    public Task<List<PolpAccountTransactionDto>> GetAccountTransactionsAsync(Guid accountId, PolpQueryWindow? window = null, CancellationToken ct = default) =>
        GetAllAsync<PolpAccountTransactionDto>($"accounts/{accountId}/transactions", window, "listar transações da conta", ct);

    public Task<List<PolpCardTransactionDto>> GetCardTransactionsAsync(Guid creditCardId, PolpQueryWindow? window = null, CancellationToken ct = default) =>
        GetAllAsync<PolpCardTransactionDto>($"credit-cards/{creditCardId}/transactions", window, "listar transações do cartão", ct);

    public Task<List<PolpLoanDto>> GetLoansAsync(Guid consentId, CancellationToken ct = default) =>
        GetAllAsync<PolpLoanDto>($"consents/{consentId}/loans", null, "listar empréstimos", ct);

    public async Task<List<PolpCardRecurringDto>?> GetCardRecurringsAsync(Guid creditCardId, CancellationToken ct = default)
    {
        try
        {
            return await GetAllAsync<PolpCardRecurringDto>($"credit-cards/{creditCardId}/recurrings", null, "listar recorrências do cartão", ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.NotImplemented)
        {
            // Ausência/bloqueio do recurso não significa ausência de assinaturas.
            return null;
        }
    }

    public async Task<PolpLoanDto> GetLoanAsync(Guid loanId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"loans/{loanId}", ct);
        await EnsureSuccess(resp, "consultar empréstimo");
        return await ReadResourceAsync<PolpLoanDto>(resp, "consultar empréstimo", ct);
    }

    public async Task<PolpAccountDto> SyncBalanceAsync(Guid accountId, CancellationToken ct = default)
    {
        // skipScheduleUpdate=false (padrão): a consulta avulsa substitui a próxima sincronização
        // automática do saldo, então não gasta cota mensal extra da instituição.
        var resp = await _http.PostAsJsonAsync($"accounts/{accountId}/balance/sync", new { skipScheduleUpdate = false }, ct);
        await EnsureSuccess(resp, "sincronizar saldo");
        return await ReadResourceAsync<PolpAccountDto>(resp, "sincronizar saldo", ct);
    }

    public async Task DeleteConsentAsync(Guid consentId, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"consents/{consentId}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return;
        await EnsureSuccess(resp, "revogar consentimento");
    }

    // Segue meta.next_cursor até acabar. Nenhum corpo de resposta da Polp vai pro log: ele traz
    // número de conta, saldo, nome e extrato.
    private Task<List<T>> GetAllAsync<T>(string path, PolpQueryWindow? window, string action, CancellationToken ct) =>
        GetAllRawAsync<T>(path, window?.Query, action, ct);

    private async Task<List<T>> GetAllRawAsync<T>(string path, string? query, string action, CancellationToken ct)
    {
        var items = new List<T>();
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(query)) parts.Add(query);
            if (cursor is not null) parts.Add($"cursor={Uri.EscapeDataString(cursor)}");
            var url = parts.Count == 0 ? path : $"{path}?{string.Join('&', parts)}";

            var resp = await _http.GetAsync(url, ct);
            await EnsureSuccess(resp, action);

            var envelope = await resp.Content.ReadFromJsonAsync<PolpListEnvelope<T>>(JsonOptions, ct);
            if (envelope?.Data is null || envelope.Data.Count == 0) return items;

            items.AddRange(envelope.Data);
            cursor = envelope.Meta?.NextCursor;
            if (string.IsNullOrEmpty(cursor)) return items;
        }

        // Devolver a lista parcial gravaria um histórico incompleto como se fosse completo. Erro
        // permanente: a mesma consulta esgotaria o teto de novo, e repetir só gasta o rate limit.
        _logger.LogError("Polp API: listagem ao {Action} passou de {MaxPages} páginas", action, MaxPages);
        throw new PolpPermanentException($"Listagem da Polp ao {action} passou de {MaxPages} páginas.");
    }

    // Create devolve {"data": {...}}; os "show" documentam o recurso direto na raiz. Aceita os
    // dois formatos em vez de depender de qual deles a Polp usa em cada rota.
    private static async Task<T> ReadResourceAsync<T>(HttpResponseMessage resp, string action, CancellationToken ct)
    {
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object)
            root = data;

        return root.Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException($"Resposta vazia da Polp ao {action}.");
    }

    // Só ação e status HTTP vão pro log e pra exceção: o corpo de erro da Polp pode repetir o
    // CPF/CNPJ enviado, dados da conta ou detalhes internos, e a mensagem da exceção chegava até
    // o navegador do usuário. A exceção é o 422 (validação): sem saber qual campo a Polp recusou,
    // uma conexão recusada não tem diagnóstico. Dele sai só um resumo (PolpValidationSummary),
    // nunca o corpo.
    private async Task EnsureSuccess(HttpResponseMessage resp, string action)
    {
        if (resp.IsSuccessStatusCode) return;

        if (resp.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var summary = PolpValidationSummary.From(await ReadBoundedAsync(resp));
            _logger.LogWarning("Polp API: falha ao {Action} (HTTP {Status}). Código: {Code}. Campos: {Fields}. Mensagens: {Messages}",
                action, (int)resp.StatusCode, summary.Code ?? "-", string.Join(", ", summary.Fields), string.Join(" | ", summary.Messages));
            throw new PolpValidationException($"Falha ao {action} na Polp (HTTP 422).", summary.Fields);
        }

        // 401/403 aqui é configuração, não instabilidade: a Polp autentica cada chamada só pelos
        // cabeçalhos x-api-client/x-api-secret (não há token a renovar), então credencial recusada
        // falha em toda rota até alguém corrigir Polp:ClientId/Polp:ClientSecret. Vai como erro, com
        // o nome das chaves e nunca o valor.
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            _logger.LogError("Polp API: credenciais recusadas ao {Action} (HTTP {Status}). Confira Polp:ClientId e Polp:ClientSecret no painel da Polp.",
                action, (int)resp.StatusCode);
        else
            _logger.LogWarning("Polp API: falha ao {Action} (HTTP {Status})", action, (int)resp.StatusCode);
        throw new HttpRequestException($"Falha ao {action} na Polp (HTTP {(int)resp.StatusCode}).", null, resp.StatusCode);
    }

    private static async Task<string> ReadBoundedAsync(HttpResponseMessage resp)
    {
        try
        {
            var text = await resp.Content.ReadAsStringAsync();
            return text.Length > 8192 ? text[..8192] : text;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return string.Empty;
        }
    }
}
