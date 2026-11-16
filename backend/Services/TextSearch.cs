using System.Globalization;

namespace ControlFinance.API.Services;

// Busca sem diferenciar maiúsculas nem acentos ("pao" acha "Pão"), sobre texto já decifrado em
// memória: nomes cifrados no banco não podem ser filtrados com LIKE/ILIKE.
public static class TextSearch
{
    public static bool Matches(string text, string search) =>
        CultureInfo.InvariantCulture.CompareInfo.IndexOf(
            text, search.Trim(), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
}
