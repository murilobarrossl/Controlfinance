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
    /// <summary>Valor gravado em BankAccount.PolpAccountType para cartões da API v2 (na v2 o
    /// cartão é um recurso próprio, sem "type" de conta). Casa com o filtro "credit" abaixo.</summary>
    public const string CardType = "CREDIT_CARD";

    public static bool LooksLikeCreditCard(BankAccount account) =>
        LooksLikeCreditCard(account.PolpAccountType, account.PolpAccountSubtype);

    public static bool LooksLikeCreditCard(string? type, string? subtype) =>
        MatchesHint(type) || MatchesHint(subtype);

    private static bool MatchesHint(string? value) =>
        value is not null && (
            value.Contains("credit", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("card", StringComparison.OrdinalIgnoreCase));
}
