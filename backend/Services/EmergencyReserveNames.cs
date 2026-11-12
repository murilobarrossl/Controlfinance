using System.Globalization;
using System.Text;
using System.Text.Json;
using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

// Mesma regra de utils/importedInvestments.js (isEmergencyReserveName).
public static class EmergencyReserveNames
{
    private const string Target = "reserva de emergencia";

    public static bool IsAuto(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var plain = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) plain.Append(c);
        return string.Join(' ', plain.ToString().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) == Target;
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
