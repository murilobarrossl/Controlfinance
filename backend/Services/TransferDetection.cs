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

    private const string CardBillPaymentRef = "LOAN_PAYMENTS_CREDIT_CARD_PAYMENT";
    private const string SelfTransferInRef = "TRANSFER_IN_ACCOUNT_TRANSFER";
    private const string SelfTransferOutRef = "TRANSFER_OUT_ACCOUNT_TRANSFER";

    public static PolpMovementKind ToMovementKind(string? categoryRef)
    {
        if (string.Equals(categoryRef, CardBillPaymentRef, StringComparison.Ordinal))
            return PolpMovementKind.CardBillPayment;
        if (string.Equals(categoryRef, SelfTransferInRef, StringComparison.Ordinal)
            || string.Equals(categoryRef, SelfTransferOutRef, StringComparison.Ordinal))
            return PolpMovementKind.OwnTransfer;
        return PolpMovementKind.None;
    }

    public static bool IsSelfTransfer(PolpMovementKind kind) => kind != PolpMovementKind.None;
}
