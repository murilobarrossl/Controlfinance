namespace ControlFinance.API.Models;

public class CreditCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal UsedLimit { get; set; }
    public int ClosingDay { get; set; }
    public int DueDay { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Installment> Installments { get; set; } = [];
}
