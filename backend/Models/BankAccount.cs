namespace ControlFinance.API.Models;

public class BankAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? BankCode { get; set; }
    public decimal Balance { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>ID da conta (account) na Polp, usado para buscar transações reais.</summary>
    public int? PolpAccountId { get; set; }

    /// <summary>ID da integração na Polp à qual esta conta pertence.</summary>
    public int? PolpIntegrationId { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = [];
}
