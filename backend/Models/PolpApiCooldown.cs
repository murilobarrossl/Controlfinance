namespace ControlFinance.API.Models;

// Operational metadata only: no user id, credentials, URLs, or financial payloads.
public class PolpApiCooldown
{
    public string ClientKey { get; set; } = "";
    public DateTime RetryAt { get; set; }
}
