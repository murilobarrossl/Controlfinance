namespace ControlFinance.API.Models;

public static class InvestmentRateKinds
{
    public const string CdiPercent = "CdiPercent";
    public const string Prefixed = "Prefixed";
    public const string SelicSpread = "SelicSpread";

    public static bool IsValid(string? kind) => kind is CdiPercent or Prefixed or SelicSpread;
}

// A Polp não informa taxa nem indexador de caixinha/CDB/título: a regra de rendimento usada nas
// projeções é a que o próprio usuário confere no app do banco e informa aqui, uma por conta.
public class InvestmentRateProfile
{
    public Guid BankAccountId { get; set; }
    public Guid UserId { get; set; }
    public string Kind { get; set; } = InvestmentRateKinds.CdiPercent;
    public decimal Value { get; set; }
    public bool IncomeTaxExempt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
