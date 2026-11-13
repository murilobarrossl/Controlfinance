using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

public enum AccountScopeKind { Checking, Payment, Savings, CreditCard }

public static class AccountScope
{
    public static AccountScopeKind KindOf(BankAccount account) => account.PolpAccountType switch
    {
        _ when CreditCardAccountDetector.LooksLikeCreditCard(account) => AccountScopeKind.CreditCard,
        "CONTA_POUPANCA" => AccountScopeKind.Savings,
        { } t when t.StartsWith("CONTA_PAGAMENTO", StringComparison.Ordinal) => AccountScopeKind.Payment,
        _ => AccountScopeKind.Checking,
    };
}
