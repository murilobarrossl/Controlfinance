using System.Globalization;
using System.Text;
using System.Text.Json;
using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

// Mesma regra de utils/importedInvestments.js (isEmergencyReserveName). O banco não informa a
// finalidade de uma caixinha, então o nome é o único sinal: caixinha com a palavra "reserva" entra na
// reserva sozinha; aplicação (CDB, fundo...) só com o nome exato, porque "reserva" ali é ambíguo.
public static class EmergencyReserveNames
{
    private const string Target = "reserva de emergencia";

    public static bool IsAuto(string? name, bool reservedBalance)
    {
        var words = Words(name);
        return reservedBalance ? words.Any(w => w is "reserva" or "reservas") : string.Join(' ', words) == Target;
    }

    private static string[] Words(string? name)
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

    public static List<Guid> Excluded(EmergencyReserve reserve)
    {
        try { return JsonSerializer.Deserialize<List<Guid>>(reserve.ExcludedAutoReserveIdsJson) ?? []; }
        catch (JsonException) { return []; }
    }

    public static void SetExcluded(EmergencyReserve reserve, Guid id, bool excluded)
    {
        var ids = Excluded(reserve);
        if (excluded) { if (!ids.Contains(id)) ids.Add(id); }
        else ids.Remove(id);
        reserve.ExcludedAutoReserveIdsJson = JsonSerializer.Serialize(ids);
    }
}
