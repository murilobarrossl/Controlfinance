namespace ControlFinance.API.Models;

public class EmergencyReserve
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? BankAccountId { get; set; }
    public string ExcludedAutoReserveIdsJson { get; set; } = "[]";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
