using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    [JsonPropertyName("available_amount")] public PolpMoney? AvailableAmount { get; set; }
    [JsonPropertyName("has_reserved_balance")] public bool HasReservedBalance { get; set; }
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

public record CreatePolpConsentRequest(Guid InstitutionId, string Document, Guid ClientUserId);

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
    Task<List<PolpInstitutionDto>> GetInstitutionsAsync(CancellationToken ct = default);
    Task<PolpConsentDto> CreateConsentAsync(CreatePolpConsentRequest request, CancellationToken ct = default);
    Task<PolpConsentDto> GetConsentAsync(Guid consentId, CancellationToken ct = default);
    Task<List<PolpAccountDto>> GetAccountsAsync(Guid consentId, PolpQueryWindow? window = null, CancellationToken ct = default);
    Task<List<PolpCreditCardDto>> GetCreditCardsAsync(Guid consentId, PolpQueryWindow? window = null, CancellationToken ct = default);
    Task<List<PolpBillDto>> GetBillsAsync(Guid creditCardId, PolpQueryWindow? window = null, CancellationToken ct = default);
    Task<List<PolpAccountTransactionDto>> GetAccountTransactionsAsync(Guid accountId, PolpQueryWindow? window = null, CancellationToken ct = default);
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

    private static readonly string[] DefaultProducts = ["ACCOUNT", "CREDIT_CARD_ACCOUNT"];

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
        // A Polp espera cpf OU cnpj dependendo do tamanho do documento.
        var isCnpj = request.Document.Length > 11;

        var body = new Dictionary<string, object?>
        {
            ["institution_id"] = request.InstitutionId,
            [isCnpj ? "cnpj" : "cpf"] = request.Document,
            // Id interno e opaco do usuário, nunca e-mail: a Polp é um terceiro e o campo é livre.
            ["cliente_user_id"] = request.ClientUserId.ToString(),
            ["products"] = DefaultProducts,
            // Reconectar o mesmo CPF consome o teto mensal regulatório de consultas: reaproveita
            // um consentimento ativo da mesma instituição em vez de criar outro.
            ["avoidDuplicates"] = true
        };
        if (!string.IsNullOrWhiteSpace(_redirectUrl)) body["redirectUrl"] = _redirectUrl;

        var resp = await _http.PostAsJsonAsync("consents", body, ct);
        await EnsureSuccess(resp, "criar consentimento");
        return await ReadResourceAsync<PolpConsentDto>(resp, "criar consentimento", ct);
    }

    public async Task<PolpConsentDto> GetConsentAsync(Guid consentId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"consents/{consentId}", ct);
        await EnsureSuccess(resp, "consultar consentimento");
        return await ReadResourceAsync<PolpConsentDto>(resp, "consultar consentimento", ct);
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
    // o navegador do usuário.
    private Task EnsureSuccess(HttpResponseMessage resp, string action)
    {
        if (resp.IsSuccessStatusCode) return Task.CompletedTask;

        _logger.LogWarning("Polp API: falha ao {Action} (HTTP {Status})", action, (int)resp.StatusCode);
        throw new HttpRequestException($"Falha ao {action} na Polp (HTTP {(int)resp.StatusCode}).", null, resp.StatusCode);
    }
}
