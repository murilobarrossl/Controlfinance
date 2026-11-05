namespace ControlFinance.API.Models;

/// <summary>
/// Rastreia uma conexão bancária com a Polp (API v2/Celcoin): um consentimento Open Finance,
/// desde a criação (POST /consents) até ficar AUTHORISED e as contas/cartões locais
/// (BankAccount) serem criados. O nome da classe e das colunas é anterior à v2 e foi mantido
/// para não espalhar renomeação; "integração" aqui é sempre "consentimento".
/// </summary>
public class PolpIntegration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>ID do consentimento na Polp (campo "id" da resposta). É o resource_id dos
    /// webhooks de recurso (accounts, credit_cards).</summary>
    public Guid PolpIntegrationId { get; set; }

    public Guid InstitutionId { get; set; }
    public string? InstitutionName { get; set; }

    /// <summary>
    /// Último status conhecido do consentimento: AWAITING_AUTHORIZATION, AUTHORISED, REJECTED
    /// ou EXPIRED (mais DISCONNECT_PENDING, que é só local).
    /// </summary>
    public string Status { get; set; } = "AWAITING_AUTHORIZATION";

    /// <summary>AWAITING_RESOURCES, SUCCESS ou PARTIAL_SUCCESS. PARTIAL_SUCCESS não bloqueia
    /// nada: só indica enriquecimento (categoria/counterparty) ainda pendente na Polp.</summary>
    public string? ExecutionStatus { get; set; }

    /// <summary>Marca se as contas/transações já foram sincronizadas localmente.</summary>
    public bool SyncedLocally { get; set; } = false;

    /// <summary>Quando os dados foram puxados da Polp pela última vez (backfill ou webhook). Não
    /// reusa UpdatedAt porque esse campo também muda em atualização de status, que não conta
    /// como sincronização de dados.</summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>A Polp ainda não terminou de categorizar/identificar contrapartes (flags
    /// ERROR_PROCESSING_CATEGORIES/COUNTERPARTIES do consentimento).</summary>
    public bool EnrichmentPending { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
