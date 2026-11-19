using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

// Movimentações entre contas do próprio usuário e pagamento de fatura entram como receita/despesa
// na Polp e contariam em dobro. A decisão usa só o category_ref recebido no sync: nome e categoria
// da transação são editáveis pelo usuário e não podem tirar nada dos totais. Por minimização, o
// código da Polp não é guardado; só o PolpMovementKind derivado dele.
public static class TransferDetection
{
    public const string SelfTransferCategoryName = "Transferência mesma titularidade";

    public const string CardBillPaymentCategoryName = "Pagamento de fatura";

    public const string LoanCategoryName = "Empréstimo";

    internal static readonly string[] LegacyLoanCategoryNames = ["Empréstimos", "Empréstimos recebidos"];

    private const string CardBillPaymentRef = "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT";
    // A taxonomia oficial da Polp separa LOAN_DISBURSEMENTS (crédito liberado) de TRANSFER_IN e
    // INCOME; só CountedInTotals decide se ele entra nos totais.
    private const string LoanDisbursementsPrefix = "LOAN_DISBURSEMENTS";
    private const string SelfTransferInRef = "TRANSFER_IN_ACCOUNT_TRANSFER";
    private const string SelfTransferOutRef = "TRANSFER_OUT_ACCOUNT_TRANSFER";

    public static PolpMovementKind ToMovementKind(string? categoryRef)
    {
        if (string.Equals(categoryRef, CardBillPaymentRef, StringComparison.Ordinal))
            return PolpMovementKind.CardBillPayment;
        if (string.Equals(categoryRef, SelfTransferInRef, StringComparison.Ordinal)
            || string.Equals(categoryRef, SelfTransferOutRef, StringComparison.Ordinal))
            return PolpMovementKind.OwnTransfer;
        if (categoryRef?.StartsWith(LoanDisbursementsPrefix, StringComparison.Ordinal) == true)
            return PolpMovementKind.LoanDisbursement;
        return PolpMovementKind.None;
    }

    public static bool IsSelfTransfer(PolpMovementKind kind) => kind != PolpMovementKind.None;

    // CNPJ fica sem a liberação: o fechamento de caixa é imutável e soma só PolpMovementKind.None.
    public static bool CountsInTotals(PolpMovementKind kind, TransactionType type, bool countsLoanDisbursement) =>
        kind == PolpMovementKind.None
        || (countsLoanDisbursement && kind == PolpMovementKind.LoanDisbursement && type == TransactionType.Income);

    public static IQueryable<Transaction> CountedInTotals(this IQueryable<Transaction> query, bool countsLoanDisbursement) =>
        countsLoanDisbursement
            ? query.Where(t => t.PolpMovementKind == PolpMovementKind.None
                || (t.PolpMovementKind == PolpMovementKind.LoanDisbursement && t.Type == TransactionType.Income))
            : query.WithoutSelfTransfers();

    public static string? CategoryBucket(PolpMovementKind kind, string? categoryName) =>
        kind == PolpMovementKind.LoanDisbursement
        || (categoryName is not null && LegacyLoanCategoryNames.Contains(categoryName))
            ? LoanCategoryName
            : categoryName;

    // Versão traduzível para SQL de IsSelfTransfer, para filtrar antes de paginar.
    public static IQueryable<Transaction> WithoutSelfTransfers(this IQueryable<Transaction> query) =>
        query.Where(t => t.PolpMovementKind == PolpMovementKind.None);
}
