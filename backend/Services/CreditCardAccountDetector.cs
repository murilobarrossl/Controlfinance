using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

// Reconhece se uma BankAccount sincronizada da Polp é um cartão de crédito, usando o "type"/
// "subtype" reais que a Polp manda (capturados em PolpSyncService, ver BankAccount.PolpAccountType/
// PolpAccountSubtype) — não uma heurística de posição/ordem. O valor exato que a Polp usa pra
// cartão não foi confirmado ainda (convenção comum em agregadores open finance é algo como
// "CREDIT"); como o valor cru agora fica salvo no banco, se esse filtro não bater com uma conta
// real, é só olhar PolpAccountType dela e ajustar aqui — não precisa adivinhar de novo.
public static class CreditCardAccountDetector
{
    public static bool LooksLikeCreditCard(BankAccount account) =>
        MatchesHint(account.PolpAccountType) || MatchesHint(account.PolpAccountSubtype);

    private static bool MatchesHint(string? value) =>
        value is not null && (
            value.Contains("credit", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("card", StringComparison.OrdinalIgnoreCase));
}
