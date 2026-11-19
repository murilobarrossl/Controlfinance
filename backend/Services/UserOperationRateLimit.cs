using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ControlFinance.API.Services;

// Operações caras ou sensíveis, limitadas por usuário (mesma partição de InvestmentReadRateLimit): criar
// conexão bancária chama a Polp e pode consumir limites regulatórios do documento; a exportação decifra e
// transmite todos os dados da conta; a chave Pix completa só aparece no detalhe do cadastro. Tetos folgados
// para o uso real e fatais para scripts. Contadores em memória de cada instância, como os de Investimentos.
public static class UserOperationRateLimit
{
    public const string Connect = "polp-connect";
    public const string Export = "account-export";
    public const string PixKey = "pix-key";

    public static IServiceCollection AddUserOperationRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(Connect, new Policy(10, TimeSpan.FromHours(1)));
            options.AddPolicy(Export, new Policy(5, TimeSpan.FromHours(1)));
            options.AddPolicy(PixKey, new Policy(60, TimeSpan.FromMinutes(10)));
        });

    // OnRejected próprio: o global (InvestmentReadRateLimit) classifica e mede só as leituras de Investimentos.
    private sealed class Policy(int permits, TimeSpan window) : IRateLimiterPolicy<string>
    {
        public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected { get; } = async (rejected, ct) =>
        {
            var seconds = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                ? (int)Math.Ceiling(retryAfter.TotalSeconds)
                : (int)Math.Ceiling(window.TotalSeconds);
            var response = rejected.HttpContext.Response;
            response.StatusCode = StatusCodes.Status429TooManyRequests;
            response.Headers.RetryAfter = seconds.ToString();
            await response.WriteAsJsonAsync(new { message = "Muitas solicitações em pouco tempo. Tente de novo mais tarde.", retryAfterSeconds = seconds }, ct);
        };

        public RateLimitPartition<string> GetPartition(HttpContext httpContext) =>
            RateLimitPartition.GetFixedWindowLimiter(InvestmentReadRateLimit.PartitionKey(httpContext), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = window,
                QueueLimit = 0,
            });
    }
}
