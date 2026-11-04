using System.Text.RegularExpressions;
using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public static partial class UserClock
{
    public const string DefaultTimeZoneId = "America/Sao_Paulo";

    // Só aceita id IANA: no Linux o .NET também resolve ids do Windows ("E. South America Standard
    // Time") convertendo por baixo, e gravar isso tornaria a coluna dependente do SO do servidor.
    public static bool IsValid(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId)
        && timeZoneId.Length <= 64
        && IanaIdShape().IsMatch(timeZoneId)
        && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)
        && zone.HasIanaId;

    public static string Normalize(string? timeZoneId) =>
        IsValid(timeZoneId) ? timeZoneId! : DefaultTimeZoneId;

    public static DateTime NowIn(string? timeZoneId, DateTimeOffset utcNow) =>
        TimeZoneInfo.ConvertTime(utcNow, TimeZoneInfo.FindSystemTimeZoneById(Normalize(timeZoneId))).DateTime;

    public static DateTime TodayIn(string? timeZoneId, DateTimeOffset utcNow) =>
        DateTime.SpecifyKind(NowIn(timeZoneId, utcNow).Date, DateTimeKind.Utc);

    public static async Task<DateTime> TodayForUserAsync(
        AppDbContext db, Guid userId, TimeProvider time, CancellationToken ct = default)
    {
        var timeZoneId = await db.Users.Where(u => u.Id == userId).Select(u => u.TimeZone).FirstOrDefaultAsync(ct);
        return TodayIn(timeZoneId, time.GetUtcNow());
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_+\-]*(/[A-Za-z0-9_+\-]+)*$")]
    private static partial Regex IanaIdShape();
}
