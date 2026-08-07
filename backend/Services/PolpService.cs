using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControlFinance.API.Services;

// ──────────────────────────────────────────
//  DTOs de resposta da Polp (snake_case)
// ──────────────────────────────────────────

public class PolpEnvelope<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

public class PolpInstitutionDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("logo_url")] public string? LogoUrl { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

public class PolpIntegrationDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("institution_id")] public int InstitutionId { get; set; }
    [JsonPropertyName("tax_id")] public string? TaxId { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("execution_status")] public string? ExecutionStatus { get; set; }
    // A Polp às vezes retorna "error": null, e às vezes "error": { ... } (objeto),
    // dependendo do tipo de falha. JsonElement aceita os dois formatos sem quebrar.
    [JsonPropertyName("error")] public JsonElement? Error { get; set; }
    [JsonPropertyName("user_action")] public string? UserAction { get; set; }
    [JsonPropertyName("url_to_authenticate")] public string? UrlToAuthenticate { get; set; }
    [JsonPropertyName("url_to_authenticate_expires_at")] public DateTime? UrlToAuthenticateExpiresAt { get; set; }
    [JsonPropertyName("created_at")] public DateTime CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTime UpdatedAt { get; set; }

    /// <summary>Extrai uma mensagem de erro legível, seja "error" string, objeto ou null.</summary>
    [JsonIgnore]
    public string? ErrorMessage
    {
        get
        {
            if (Error is not { } el || el.ValueKind == JsonValueKind.Null || el.ValueKind == JsonValueKind.Undefined)
                return null;

            if (el.ValueKind == JsonValueKind.String) return el.GetString();

            if (el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                    return msg.GetString();
                if (el.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                    return code.GetString();
                return el.GetRawText();
            }

            return el.GetRawText();
        }
    }
}

public class PolpAccountDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("integration_id")] public int IntegrationId { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("subtype")] public string? Subtype { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("balance")] public decimal Balance { get; set; }
    [JsonPropertyName("currency_code")] public string? CurrencyCode { get; set; }
    [JsonPropertyName("marketing_name")] public string? MarketingName { get; set; }
    [JsonPropertyName("owner")] public string? Owner { get; set; }
}

public class PolpTransactionDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("account_id")] public int AccountId { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("currency_code")] public string? CurrencyCode { get; set; }
    [JsonPropertyName("date")] public string Date { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty; // DEBIT | CREDIT
    [JsonPropertyName("status")] public string? Status { get; set; }           // POSTED | PENDING
    [JsonPropertyName("category")] public PolpCategoryDto? Category { get; set; }
    [JsonPropertyName("merchant")] public PolpMerchantDto? Merchant { get; set; }
}

public class PolpCategoryDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
}

public class PolpMerchantDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("business_name")] public string? BusinessName { get; set; }
    [JsonPropertyName("logo_url")] public string? LogoUrl { get; set; }
}

public class PolpMeta
{
    [JsonPropertyName("current_page")] public int CurrentPage { get; set; }
    [JsonPropertyName("last_page")] public int LastPage { get; set; }
}

public class PolpListEnvelope<T>
{
    [JsonPropertyName("data")] public List<T> Data { get; set; } = [];
    [JsonPropertyName("meta")] public PolpMeta? Meta { get; set; }
}

public record CreatePolpIntegrationRequest(int InstitutionId, string? Cpf, string? Cnpj);

// ──────────────────────────────────────────
//  Interface + implementação
// ──────────────────────────────────────────

public interface IPolpService
{
    Task<List<PolpInstitutionDto>> GetInstitutionsAsync(CancellationToken ct = default);
    Task<PolpIntegrationDto> CreateIntegrationAsync(int institutionId, string document, CancellationToken ct = default);
    Task<PolpIntegrationDto> GetIntegrationAsync(int integrationId, CancellationToken ct = default);
    Task<List<PolpAccountDto>> GetAccountsAsync(int integrationId, CancellationToken ct = default);
    Task<List<PolpTransactionDto>> GetTransactionsAsync(int accountId, CancellationToken ct = default);
}

public class PolpService : IPolpService
{
    private readonly HttpClient _http;
    private readonly ILogger<PolpService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PolpService(HttpClient http, IConfiguration config, ILogger<PolpService> logger)
    {
        _logger = logger;

        var baseUrl = config["Polp:BaseUrl"] ?? "https://dev.polp.com.br/api/v1/";
        if (!baseUrl.EndsWith('/')) baseUrl += "/";

        var clientId = config["Polp:ClientId"]
            ?? throw new InvalidOperationException("Polp:ClientId não configurado em appsettings.json");
        var clientSecret = config["Polp:ClientSecret"]
            ?? throw new InvalidOperationException("Polp:ClientSecret não configurado em appsettings.json");

        _http = http;
        _http.BaseAddress = new Uri(baseUrl);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Add("x-api-client", clientId);
        _http.DefaultRequestHeaders.Add("x-api-secret", clientSecret);
    }

    public async Task<List<PolpInstitutionDto>> GetInstitutionsAsync(CancellationToken ct = default)
    {
        var institutions = new List<PolpInstitutionDto>();
        var page = 1;
        int lastPage;

        do
        {
            var resp = await _http.GetAsync($"institutions?page={page}", ct);
            await EnsureSuccess(resp, "listar instituições");

            var envelope = await resp.Content.ReadFromJsonAsync<PolpListEnvelope<PolpInstitutionDto>>(JsonOptions, ct);
            if (envelope?.Data is null || envelope.Data.Count == 0) break;

            institutions.AddRange(envelope.Data);
            lastPage = envelope.Meta?.LastPage ?? page;
            page++;

        } while (page <= lastPage);

        return institutions;
    }

    public async Task<PolpIntegrationDto> CreateIntegrationAsync(int institutionId, string document, CancellationToken ct = default)
    {
        // A Polp espera cpf OU cnpj dependendo do tamanho do documento
        var isCnpj = document.Length > 11;

        var body = new Dictionary<string, object?>
        {
            ["institution_id"] = institutionId,
            [isCnpj ? "cnpj" : "cpf"] = document
        };

        var resp = await _http.PostAsJsonAsync("integrations", body, ct);
        await EnsureSuccess(resp, "criar integração");

        var envelope = await resp.Content.ReadFromJsonAsync<PolpEnvelope<PolpIntegrationDto>>(JsonOptions, ct);
        return envelope?.Data ?? throw new InvalidOperationException("Resposta vazia da Polp ao criar integração.");
    }

    public async Task<PolpIntegrationDto> GetIntegrationAsync(int integrationId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"integrations/{integrationId}", ct);
        await EnsureSuccess(resp, "consultar integração");

        var envelope = await resp.Content.ReadFromJsonAsync<PolpEnvelope<PolpIntegrationDto>>(JsonOptions, ct);
        return envelope?.Data ?? throw new InvalidOperationException("Resposta vazia da Polp ao consultar integração.");
    }

    public async Task<List<PolpAccountDto>> GetAccountsAsync(int integrationId, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"integrations/{integrationId}/accounts", ct);
        await EnsureSuccess(resp, "listar contas");

        // TEMP: captura crua pra descobrir onde a Polp expõe limite/fatura/parcela de cartão de
        // crédito: PolpAccountDto só declara os campos que já usamos, e a desserialização tipada
        // descarta silenciosamente qualquer campo extra que a Polp já esteja mandando (não tem
        // JsonExtensionData). Lê como string em vez de ReadFromJsonAsync direto só pra poder logar
        // o corpo inteiro antes de desserializar. Resultado do envelope é idêntico ao de antes.
        // REMOVER esse bloco (as 2 linhas de log) depois do diagnóstico.
        var rawJson = await resp.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("=== POLP RAW CARD JSON (accounts, integrationId={IntegrationId}) === {RawJson}", integrationId, rawJson);

        var envelope = JsonSerializer.Deserialize<PolpListEnvelope<PolpAccountDto>>(rawJson, JsonOptions);
        return envelope?.Data ?? [];
    }

    // Antes buscava só a página 1: contas com muito histórico (ex.: vários anos de Nubank)
    // ficavam sem transações antigas, porque a Polp devolve as mais recentes primeiro e o
    // resto simplesmente nunca era buscado. Agora pagina até o fim, com um teto de segurança
    // pra não fazer um número indefinido de requisições numa conta com histórico gigante.
    private const int MaxTransactionPages = 50;

    public async Task<List<PolpTransactionDto>> GetTransactionsAsync(int accountId, CancellationToken ct = default)
    {
        var transactions = new List<PolpTransactionDto>();
        var page = 1;
        int lastPage;

        do
        {
            var resp = await _http.GetAsync($"accounts/{accountId}/transactions?page={page}", ct);
            await EnsureSuccess(resp, "listar transações");

            // TEMP: mesma captura crua da conta, agora pro extrato: parcela (ex.: "3/12") é da
            // compra, não da conta, então é mais provável que apareça aqui do que no /accounts.
            // REMOVER junto com o bloco equivalente em GetAccountsAsync.
            var rawJson = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogInformation(
                "=== POLP RAW CARD JSON (transactions, accountId={AccountId}, page={Page}) === {RawJson}",
                accountId, page, rawJson);

            var envelope = JsonSerializer.Deserialize<PolpListEnvelope<PolpTransactionDto>>(rawJson, JsonOptions);
            if (envelope?.Data is null || envelope.Data.Count == 0) break;

            transactions.AddRange(envelope.Data);
            lastPage = Math.Min(envelope.Meta?.LastPage ?? page, MaxTransactionPages);
            page++;

        } while (page <= lastPage);

        return transactions;
    }

    private async Task EnsureSuccess(HttpResponseMessage resp, string action)
    {
        if (resp.IsSuccessStatusCode) return;

        var content = await resp.Content.ReadAsStringAsync();
        _logger.LogError("Polp API error ao {Action}: {Status}, {Body}", action, resp.StatusCode, content);
        throw new HttpRequestException($"Falha ao {action} na Polp ({(int)resp.StatusCode}): {content}");
    }
}
