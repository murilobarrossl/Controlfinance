using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ControlFinance.API.Services;

// A API v2 da Polp não devolve cor da instituição (a v1 devolvia), só o logo.
public static class BankBrandColors
{
    private static readonly (Regex Pattern, string Color)[] Brands =
    [
        (Word("nubank|nu pagamentos"), "#820AD1"),
        (Word("itau"), "#EC7000"),
        (Word("bradesco"), "#CC092F"),
        (Word("santander"), "#EC0000"),
        (Word("banco do brasil"), "#FAE128"),
        (Word("caixa"), "#005CA9"),
        (Word("inter"), "#FF7A00"),
        (Word("c6"), "#242424"),
        (Word("btg"), "#001E62"),
        (Word("picpay"), "#21C25E"),
        (Word("mercado pago"), "#00B1EA"),
        (Word("sicoob"), "#003641"),
        (Word("sicredi"), "#3FA110"),
        (Word("banrisul"), "#0052A5"),
        (Word("original"), "#00A859"),
    ];

    public static string? Find(string? institutionName)
    {
        if (string.IsNullOrWhiteSpace(institutionName)) return null;

        var normalized = RemoveAccents(institutionName).ToLowerInvariant();
        foreach (var (pattern, color) in Brands)
            if (pattern.IsMatch(normalized)) return color;

        return null;
    }

    private static Regex Word(string alternatives) =>
        new($@"\b({alternatives})\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string RemoveAccents(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
