using ControlFinance.API.Services;

namespace ControlFinance.API.Models;

public enum PolpWebhookEventStatus
{
    Pending,
    Processing,
    Done,
    Dead
}

/// <summary>
/// Inbox dos avisos da Polp (webhooks e eventos internos internal.*): gravado antes de responder
/// 200 e processado em série pelo PolpInboxWorker. Só ids técnicos, nunca dado financeiro. Sem FK
/// para usuário/integração: o aviso pode chegar antes do consentimento local existir.
/// </summary>
public class PolpWebhookEvent
{
    public const int EventMaxLength = 64;
    public const int ResourceMaxLength = 64;
    public const int ResourceIdMaxLength = 64;
    public const int QueryParametersMaxLength = 1024;
    public const int DedupKeyMaxLength = 64;
    public const int LastErrorMaxLength = 500;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Event { get; set; } = string.Empty;
    public string? Resource { get; set; }
    public string? ResourceId { get; set; }
    public string? QueryParameters { get; set; }

    /// <summary>SHA-256 hex de event|resource|resource_id|query_parameters.</summary>
    public string DedupKey { get; set; } = string.Empty;

    public PolpWebhookEventStatus Status { get; set; } = PolpWebhookEventStatus.Pending;
    public PolpInboxPriority Priority { get; set; } = PolpInboxPriority.High;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
