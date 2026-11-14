using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ControlFinance.API.Services;

// Métricas da aba Investimentos, sem pacote extra: qualquer coletor de System.Diagnostics.Metrics
// (dotnet-counters hoje, OpenTelemetry depois) lê o medidor "ControlFinance.Investments". Somam-se
// aos medidores nativos do ASP.NET Core ("Microsoft.AspNetCore.RateLimiting" e
// "Microsoft.AspNetCore.Hosting"). Etiquetas só com o modelo da rota e motivos fixos: nunca id de
// usuário, de investimento ou valores.
public static class InvestmentMetrics
{
    public const string MeterName = "ControlFinance.Investments";
    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> RejectedCounter = Meter.CreateCounter<long>(
        "controlfinance.investments.rejected", description: "Leituras recusadas pelos limites, por motivo.");
    private static readonly Counter<long> RefreshCounter = Meter.CreateCounter<long>(
        "controlfinance.investments.refresh_requests", description: "Pedidos do botão Atualizar dados, aceitos ou recusados pelo intervalo.");
    private static readonly Histogram<double> ReadDuration = Meter.CreateHistogram<double>(
        "controlfinance.investments.read.duration", unit: "ms", description: "Tempo de execução das leituras, por rota.");

    public static string Route(HttpContext context) =>
        (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "desconhecida";

    public static void Rejected(string reason, HttpContext context) =>
        RejectedCounter.Add(1, new("reason", reason), new("route", Route(context)));

    public static void Refresh(bool accepted) => RefreshCounter.Add(1, new KeyValuePair<string, object?>("result", accepted ? "accepted" : "throttled"));

    public static void ReadCompleted(string route, int status, double milliseconds) =>
        ReadDuration.Record(milliseconds, new("route", route), new("status", status));
}

// Mede o tempo da ação (consulta e decifração), sem a fila do limitador nem a serialização.
public sealed class InvestmentReadMetricsAttribute : ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var started = Stopwatch.GetTimestamp();
        var executed = await next();
        var status = executed.Exception is not null && !executed.ExceptionHandled ? StatusCodes.Status500InternalServerError
            : (executed.Result as Microsoft.AspNetCore.Mvc.Infrastructure.IStatusCodeActionResult)?.StatusCode ?? StatusCodes.Status200OK;
        InvestmentMetrics.ReadCompleted(InvestmentMetrics.Route(context.HttpContext), status, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
}
