using System.Text.Json;

namespace ControlFinance.API.Services;

public static class PolpFailure
{
    public static bool IsProviderFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or JsonException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested);
}
