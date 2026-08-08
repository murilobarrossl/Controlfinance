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

    // CreditCard é só pra cartão 100% manual, nunca conectado por nenhum banco — um cartão
    // sincronizado da Polp é representado pela própria BankAccount (ver
    // BankAccount.CreditLimit/ClosingDay/DueDay + CreditCardAccountDetector), sem entidade
    // separada. Não existe vínculo com BankAccount aqui de propósito, pra não duplicar.

    public ICollection<Installment> Installments { get; set; } = [];
}
