namespace ControlFinance.API.Models;

public class ImportedInvestment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid IntegrationId { get; set; }
    public Guid SourceConsentId { get; set; }
    public string Family { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string? Name { get; set; }
    public string? ProductType { get; set; }
    public string IdentityJson { get; set; } = "{}";
    public string IdentityAvailabilityJson { get; set; } = "{}";
    public DateTime? ProviderUpdatedAt { get; set; }
    public DateTime FirstObservedAt { get; set; }
    public DateTime LastObservedAt { get; set; }
}

public class InvestmentContract
{
    public Guid InvestmentId { get; set; }
    public string ValuesJson { get; set; } = "{}";
    public string AvailabilityJson { get; set; } = "{}";
    public string ProvenanceJson { get; set; } = "{}";
    public DateTime ObservedAt { get; set; }
}

public class InvestmentPosition
{
    public Guid InvestmentId { get; set; }
    public string ValuesJson { get; set; } = "{}";
    public string AvailabilityJson { get; set; } = "{}";
    public string ProvenanceJson { get; set; } = "{}";
    public DateTime? ReferenceAt { get; set; }
    public DateTime ObservedAt { get; set; }
}

// One immutable source observation per financial content/reference hash. Corrections at the
// same reference are separate observations. Never synthesize missing months from this table.
public class InvestmentPositionObservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvestmentId { get; set; }
    public string ContentHash { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public DateTime? ReferenceAt { get; set; }
    public DateTime ObservedAt { get; set; }
}

public class InvestmentMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvestmentId { get; set; }
    public string ExternalId { get; set; } = "";
    public string? Direction { get; set; }
    public string? MovementType { get; set; }
    public DateTime? TransactionDate { get; set; }
    public DateTime? ProviderUpdatedAt { get; set; }
    public string ValuesJson { get; set; } = "{}";
    public string AvailabilityJson { get; set; } = "{}";
    public string ProvenanceJson { get; set; } = "{}";
    public DateTime FirstObservedAt { get; set; }
    public DateTime LastObservedAt { get; set; }
}

public class ImportedReservedBalance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid IntegrationId { get; set; }
    public Guid BankAccountId { get; set; }
    public Guid SourceConsentId { get; set; }
    public string ExternalId { get; set; } = "";
    public string ReservedIdentification { get; set; } = "";
    public string? Name { get; set; }
    public string ValuesJson { get; set; } = "{}";
    public string AvailabilityJson { get; set; } = "{}";
    public string ProvenanceJson { get; set; } = "{}";
    public DateTime? ProviderUpdatedAt { get; set; }
    public DateTime FirstObservedAt { get; set; }
    public DateTime LastObservedAt { get; set; }
}

public class ReservedBalanceObservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReservedBalanceId { get; set; }
    public string ContentHash { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public DateTime ObservedAt { get; set; }
}

// Each list/movement stream has an independent cursor, generation and failure state. A failed
// family never rolls back already committed pages of another family, nor reports full success.
public class InvestmentSyncState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid IntegrationId { get; set; }
    public Guid SourceConsentId { get; set; }
    public Guid? AuthorizationId { get; set; }
    public Guid? AuthorizationOperationId { get; set; }
    public string Family { get; set; } = "";
    public string Kind { get; set; } = "list";
    public string ParentExternalId { get; set; } = "";
    public Guid RunId { get; set; } = Guid.NewGuid();
    public string Status { get; set; } = "Pending";
    public string? Cursor { get; set; }
    public string? WindowQuery { get; set; }
    public string SeenCursorsJson { get; set; } = "[]";
    public string Availability { get; set; } = "unknown";
    public int PageNumber { get; set; }
    public bool ReplayRequested { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? LastFullSuccessAt { get; set; }
    public string? ErrorCode { get; set; }
}

// Provider schedules are operational evidence, not a promise of real-time position updates.
public class InvestmentSyncSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid IntegrationId { get; set; }
    public Guid SourceConsentId { get; set; }
    public string Resource { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public DateTime? LastSyncAt { get; set; }
    public DateTime? NextSyncAt { get; set; }
    public DateTime ObservedAt { get; set; }
    public Guid ObservedRunId { get; set; }
}
