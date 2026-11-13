namespace ControlFinance.API.Models;

// Existing storage for authenticated consent metadata linked to imported resources.
// Names/columns are retained to preserve source bindings and deletion/recovery guarantees.
// There is no endpoint or service that creates an investment-only authorization. New
// observations refer to the normal bank consent (IsSupplemental = false).
public class PolpInvestmentAuthorization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? IntegrationId { get; set; }
    public Guid InstitutionId { get; set; }
    public bool IsSupplemental { get; set; } = true;
    public Guid? PolpConsentId { get; set; }
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public string State { get; set; } = "Creating";
    public string? ProviderStatus { get; set; }
    public string? ExecutionStatus { get; set; }
    public string? ProductsJson { get; set; }
    public string? ResourcesJson { get; set; }
    public DateTime? AuthorizationUrlExpiresAt { get; set; }
    public DateTime? ProviderUpdatedAt { get; set; }
    public DateTime OperationStartedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? RecoveryCursor { get; set; }
}
