namespace ControlFinance.API.Models;

/// <summary>
/// Rastreia o processo de integração bancária com a Polp, desde a criação
/// (POST /integrations) até o momento em que os dados ficam disponíveis
/// (status UPDATED) e as contas locais (BankAccount) são criadas.
/// </summary>
public class PolpIntegration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>ID da integração retornado pela Polp (campo "id" da resposta).</summary>
    public int PolpIntegrationId { get; set; }

    public int InstitutionId { get; set; }
    public string? InstitutionName { get; set; }

    /// <summary>
    /// Último status conhecido: UPDATING, WAITING_USER_INPUT, UPDATED,
    /// LOGIN_ERROR, OUTDATED, etc.
    /// </summary>
    public string Status { get; set; } = "UPDATING";

    /// <summary>URL de autenticação (MFA/redirect), quando aplicável.</summary>
    public string? UrlToAuthenticate { get; set; }

    /// <summary>Marca se as contas/transações já foram sincronizadas localmente.</summary>
    public bool SyncedLocally { get; set; } = false;

    /// <summary>Timestamp dedicado ao throttle de re-sync — não pode reusar UpdatedAt porque esse
    /// campo também é sobrescrito pelo polling de status (GetIntegrationStatus), que não conta
    /// como sincronização de dados.</summary>
    public DateTime? LastSyncedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
