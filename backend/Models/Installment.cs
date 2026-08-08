namespace ControlFinance.API.Models;

public class Installment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Mutuamente exclusivos: uma parcela é de um cartão 100% manual (CreditCardId) OU
    /// de uma conta reconhecida como cartão pela Polp (BankAccountId) — nunca os dois. Os dois
    /// nulos = parcelamento fora de cartão (boleto, Pix parcelado etc.).</summary>
    public Guid? CreditCardId { get; set; }
    public CreditCard? CreditCard { get; set; }
    public Guid? BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }
    public Guid UserId { get; set; }

    public string Description { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public int TotalInstallments { get; set; }
    public int CurrentInstallment { get; set; }
    public decimal InstallmentAmount { get; set; }
    public DateTime NextDueDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
