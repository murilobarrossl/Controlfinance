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

    public List<SupplierPaymentMethod> PaymentMethods { get; set; } = [];


    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
