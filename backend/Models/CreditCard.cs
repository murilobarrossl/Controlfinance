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

    // Vínculo opcional com a conta sincronizada da Polp que corresponde a este cartão (ex.: a 2ª
    // conta de uma mesma conexão bancária, que o seletor de contas já trata como "o cartão").
    // Escolhido manualmente pelo usuário no cadastro: não existe campo na Polp que diga "isto é
    // conta de cartão", então não dá pra inferir isso sozinho.
    public Guid? BankAccountId { get; set; }
    public BankAccount? BankAccount { get; set; }

    public ICollection<Installment> Installments { get; set; } = [];
}
