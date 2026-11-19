using System.Globalization;
using System.Text;

namespace ControlFinance.API.Services;

// A Polp não informa a finalidade de uma caixinha: o único sinal é o nome que o próprio usuário deu a ela.
// Só caixinhas (saldos reservados) entram; CDB, fundo, ação e Tesouro nunca, nem com liquidez diária.
public static class EmergencyReserveNames
{
    private static readonly HashSet<string> Words = ["reserva", "reservas", "emergencia", "emergencias", "colchao"];

    public static bool IsAuto(string? name, bool reservedBalance) => reservedBalance && Split(name).Any(Words.Contains);

    private static string[] Split(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return [];
        var plain = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            plain.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
