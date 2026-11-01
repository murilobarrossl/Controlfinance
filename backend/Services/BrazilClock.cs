namespace ControlFinance.API.Services;

public static class BrazilClock
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    public static DateTime Today => DateTime.SpecifyKind(Now.Date, DateTimeKind.Utc);
}
