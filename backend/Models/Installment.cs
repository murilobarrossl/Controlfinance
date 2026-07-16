namespace ControlFinance.API.Models;

public class Installment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Nulo pra parcelamentos fora de cartão (boleto, Pix parcelado etc.).</summary>
    public Guid? CreditCardId { get; set; }
    public CreditCard? CreditCard { get; set; }
    public Guid UserId { get; set; }

    public string Description { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int TotalInstallments { get; set; }
    public int CurrentInstallment { get; set; }
    public decimal InstallmentAmount { get; set; }
    public DateTime NextDueDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
