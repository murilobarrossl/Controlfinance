namespace ControlFinance.API.Models;

public class Supplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string LegalName { get; set; } = string.Empty;
    public string? TradeName { get; set; }

    public string DocumentType { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
    public string DocumentHash { get; set; } = string.Empty;

    public string? Category { get; set; }

    public PixKeyType? PixKeyType { get; set; }
    public string? PixKey { get; set; }
    public string? PixKeyHash { get; set; }

    public string? PayeeBankCode { get; set; }
    public string? PayeeBranch { get; set; }
    public string? PayeeAccount { get; set; }
    public PayeeAccountType? PayeeAccountType { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
