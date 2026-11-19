using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

// A natureza tributária só vem do category_ref da Polp, que é classificação por IA sobre a descrição:
// nunca basta sozinho. Precisa de um dado do banco que o confirme: o destinatário (partie_cnpj_cpf) ser
// um órgão arrecadador conferido, ou a operação ser convênio de arrecadação. Nenhum dos dois basta sem
// a classificação: o CNPJ não diz o que foi pago e o convênio também cobre água, luz e telefone.
public static class TaxPaymentDetection
{
    public const string FederalRevenue = "RFB";
    public const string SocialSecurity = "INSS";

    private const string TaxPaymentRef = "GOVERNMENT_AND_NON_PROFIT_TAX_PAYMENT";
    private const string CollectionAgreementOperation = "CONVENIO_ARRECADACAO";

    // Só CNPJs conferidos na base pública do CNPJ: um CNPJ errado atribuiria o pagamento ao órgão errado.
    private static readonly Dictionary<string, string> AuthorityByCnpj = new(StringComparer.Ordinal)
    {
        ["00394460005887"] = FederalRevenue,
        ["29979036000140"] = SocialSecurity,
    };

    public static bool IsTaxPaymentClassification(string? categoryRef) =>
        string.Equals(categoryRef, TaxPaymentRef, StringComparison.Ordinal);

    public static string? AuthorityFor(Transaction t) =>
        t.CounterpartyCnpjFromBank && t.CounterpartyCnpj is { } cnpj && AuthorityByCnpj.TryGetValue(cnpj, out var authority)
            ? authority
            : null;

    public static IQueryable<Transaction> IdentifiedTaxPayments(this IQueryable<Transaction> query) =>
        query.Where(t => t.Type == TransactionType.Expense
            && t.Status == TransactionStatus.Paid
            && t.PolpMovementKind == PolpMovementKind.None
            && t.BankAccountId != null
            && t.BankAccount!.PolpAccountType != CreditCardAccountDetector.CardType
            && t.PolpTaxPayment
            && (t.TaxAuthority != null || t.BankOperationType == CollectionAgreementOperation));
}
