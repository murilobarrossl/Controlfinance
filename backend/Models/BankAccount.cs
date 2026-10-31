namespace ControlFinance.API.Models;

// Usado pelo Radar de Recorrências (endpoint ainda existente no backend) pra separar pessoal de empresa. Mixed = conta mista (o usuário
// disse explicitamente que não dá pra separar). Nesse caso a detecção continua funcionando
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

    /// <summary>ID da conta (account) ou do cartão (credit-card) na Polp, usado para buscar
    /// transações reais. Na v2 conta e cartão são recursos separados; o cartão continua sendo uma
    /// BankAccount aqui (PolpAccountType = CreditCardAccountDetector.CardType).</summary>
    public Guid? PolpAccountId { get; set; }

    /// <summary>ID do consentimento na Polp ao qual esta conta pertence. Contas com o mesmo valor
    /// aqui vieram da mesma conexão bancária (ex.: conta corrente + cartão de crédito do mesmo
    /// banco). Usado só pra agrupar elas visualmente no seletor de contas, cada uma continua
    /// com sua própria visão de dados (nada é somado entre elas).</summary>
    public Guid? PolpIntegrationId { get; set; }

    public AccountOwnership Ownership { get; set; }

    /// <summary>"type"/"subtype" crus que a Polp manda pra essa conta (ex.: "BANK", "CREDIT") —
    /// usados só pra reconhecer automaticamente quando essa conta é um cartão de crédito
    /// (ver CreditCardAccountDetector). Antes eram descartados na sincronização.</summary>
    public string? PolpAccountType { get; set; }
    public string? PolpAccountSubtype { get; set; }

    /// <summary>Só os 4 últimos dígitos do "number" que a Polp manda, já no formato de exibição
    /// ("•••• 1234"), cifrado como NumberEncrypted. O número completo nunca é gravado: a única
    /// função dele aqui é ajudar o usuário a reconhecer qual cartão/conta é qual
    /// (ver AccountNumberMask).</summary>
    public string? Number { get; set; }

    /// <summary>Só fazem sentido quando esta conta é reconhecida como cartão de crédito
    /// (CreditCardAccountDetector). Vêm de PolpService.GetCreditCardsAsync/GetBillsAsync quando a
    /// Polp expõe esses dados pra esse banco; nulos até lá (a UI de Cartões pede pra completar só
    /// o que faltar). Nenhuma tabela separada representa o cartão — é esta mesma BankAccount.</summary>
    public string? Brand { get; set; }
    public string? ProductType { get; set; }
    public decimal? CreditLimit { get; set; }
    public decimal? UsedLimit { get; set; }
    public int? ClosingDay { get; set; }
    public int? DueDay { get; set; }
    public decimal? CurrentInvoiceAmount { get; set; }
    public DateTime? InvoiceDueDate { get; set; }

    public ICollection<Transaction> Transactions { get; set; } = [];
}
