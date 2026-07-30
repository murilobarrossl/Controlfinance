using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ControlFinance.API.Services;

// Identifica transferências entre contas do próprio usuário (ex.: mover saldo entre dois bancos
// via Pix/TED) sincronizadas pela Polp, que hoje entram como receita E despesa ao mesmo tempo e
// distorcem os totais. Não depende de nenhum dado novo: usa o nome/categoria que a Polp já manda.
public static class TransferDetection
{
    // A Polp manda a descrição crua de transferências como "Transferência enviada|Nome do titular"
    // quando não há merchant associado (ver PolpController: Name = merchant?.Name ?? description).
    private static readonly Regex TransferPattern = new(
        @"(transfer[êe]ncia|\bted\b|\bdoc\b)\s+(enviad[ao]|recebid[ao])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> TransferCategoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Transferências", "Transferencia", "Transferência", "TED", "DOC"
    };

    // A Polp manda essa categoria só quando já verificou (via CPF/CNPJ dos dois lados) que é uma
    // transferência entre contas do mesmo titular — sinal mais forte que a heurística de nome
    // abaixo, então confia direto nela em vez de exigir bater com ownerName. Sem esse caso, uma
    // transferência assim (ex.: "Pix enviado - <nome completo>", sem a palavra "transferência" no
    // nome da transação) caía no fallback de regex, não batia, e entrava como despesa de verdade.
    private static readonly HashSet<string> SelfOwnershipCategoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Transferência mesma titularidade"
    };

    public static bool IsSelfTransfer(string? transactionName, string? categoryName, string? ownerName)
    {
        if (string.IsNullOrWhiteSpace(transactionName))
            return false;

        if (categoryName is not null && SelfOwnershipCategoryNames.Contains(categoryName))
            return true;

        if (string.IsNullOrWhiteSpace(ownerName)) return false;

        var looksLikeTransfer = (categoryName is not null && TransferCategoryNames.Contains(categoryName))
            || TransferPattern.IsMatch(transactionName);

        if (!looksLikeTransfer) return false;

        return NamesMatch(ExtractCounterparty(transactionName), ownerName);
    }

    // Sem "|" o próprio nome já é o titular (veio de merchant.name/business_name).
    private static string ExtractCounterparty(string transactionName)
    {
        var pipeIndex = transactionName.LastIndexOf('|');
        return pipeIndex >= 0 ? transactionName[(pipeIndex + 1)..].Trim() : transactionName.Trim();
    }

    // Bancos diferentes formatam o nome do titular de formas diferentes (nome completo vs.
    // abreviado: "Igor Luis Accioly Lins Lima" x "IGOR L ACCIOLY LINS LIMA"), então a comparação
    // exige primeiro/último nome iguais e tolera abreviação nos nomes do meio.
    private static bool NamesMatch(string counterparty, string ownerName)
    {
        var a = Normalize(counterparty);
        var b = Normalize(ownerName);
        if (a.Length < 2 || b.Length < 2 || a[0] != b[0] || a[^1] != b[^1])
            return false;

        var middleA = a[1..^1];
        var middleB = b[1..^1];
        if (middleA.Length == 0 || middleB.Length == 0) return true;

        var matched = middleA.Count(x => middleB.Any(y => MiddleTokensMatch(x, y)));
        var required = Math.Max(0, Math.Min(middleA.Length, middleB.Length) - 1);
        return matched >= required;
    }

    private static bool MiddleTokensMatch(string x, string y) =>
        x == y || (x[0] == y[0] && (x.Length == 1 || y.Length == 1));

    private static string[] Normalize(string value) =>
        RemoveDiacritics(value.ToUpperInvariant()).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
