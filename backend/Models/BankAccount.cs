namespace ControlFinance.API.Models;

// Usado pelo Radar de Recorrências pra separar pessoal de empresa. Mixed = conta mista (o usuário
// disse explicitamente que não dá pra separar) — nesse caso a detecção continua funcionando
// normalmente, só não entra em nenhum dos dois lados na tela. Chutado a partir do documento do
// dono na criação da conta (CNPJ = Business, CPF = Personal); o usuário pode corrigir depois.
public enum AccountOwnership { Personal, Business, Mixed }

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

    /// <summary>ID da integração na Polp à qual esta conta pertence. Contas com o mesmo valor
    /// aqui vieram da mesma conexão bancária (ex.: conta corrente + cartão de crédito do mesmo
    /// banco) — usado só pra agrupar elas visualmente no seletor de contas, cada uma continua
    /// com sua própria visão de dados (nada é somado entre elas).</summary>
    public int? PolpIntegrationId { get; set; }

    public AccountOwnership Ownership { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = [];
}
