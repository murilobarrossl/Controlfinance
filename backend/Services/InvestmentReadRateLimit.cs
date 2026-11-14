using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ControlFinance.API.Services;

// Leituras da aba Investimentos (lista, detalhe, histórico, planejamento). Não chamam a Polp, mas
// cada uma decifra posições e movimentações no banco: sem limite, um botão martelado ou um script
// multiplica a carga do Postgres por milhares de usuários. Três camadas, só nessas rotas (webhooks,
// sincronização e o resto da API não passam por aqui):
// 1. Janela por usuário: 120 leituras/min, folgado para o uso real (releitura automática de 60s e
//    vários detalhes abertos) e fatal para scripts.
// 2. Concorrência por usuário: uma pessoa não ocupa sozinha as vagas globais. Excedente espera numa
//    fila curta em vez de falhar, porque a própria página dispara várias leituras em paralelo.
// 3. Concorrência global: teto de consultas simultâneas abaixo do pool do Npgsql (100), para um
//    pico de muitos usuários virar espera em fila, não conexões esgotadas para o app inteiro.
// Os contadores ficam na memória de cada instância (como o limite de saldo): com várias instâncias
// cada uma conta a sua parte. Teto global exato exigiria um armazenamento compartilhado.
public static class InvestmentReadRateLimit
{
    public const string Policy = "investments-read";
    public const int PermitsPerWindow = 120;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public sealed class ConcurrencyOptions
    {
        public int PerUser { get; set; } = 6;
        public int PerUserQueue { get; set; } = 32;
        public int Global { get; set; } = 64;
        public int GlobalQueue { get; set; } = 512;
    }

    public static string PartitionKey(HttpContext context)
    {
        var user = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return user is not null ? $"user:{user}" : $"ip:{context.Connection.RemoteIpAddress}";
    }

    public static RateLimitPartition<string> Partition(HttpContext context) =>
        RateLimitPartition.GetSlidingWindowLimiter(PartitionKey(context), _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = PermitsPerWindow,
            Window = Window,
            SegmentsPerWindow = 6,
            QueueLimit = 0,
        });

    public static bool AppliesTo(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == Policy;

    public static PartitionedRateLimiter<HttpContext> PerUserConcurrency(ConcurrencyOptions options) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context => AppliesTo(context)
            ? RateLimitPartition.GetConcurrencyLimiter(PartitionKey(context), _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = options.PerUser,
                QueueLimit = options.PerUserQueue,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            })
            : RateLimitPartition.GetNoLimiter("outside"));

    public static PartitionedRateLimiter<HttpContext> GlobalConcurrency(ConcurrencyOptions options) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context => AppliesTo(context)
            ? RateLimitPartition.GetConcurrencyLimiter("investments-global", _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = options.Global,
                QueueLimit = options.GlobalQueue,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            })
            : RateLimitPartition.GetNoLimiter("outside"));

    public enum Rejection { UserWindow, UserConcurrency, GlobalConcurrency }

    // A janela deslizante não informa RetryAfter na recusa, então o motivo sai das filas: se nenhuma
    // das duas está cheia, quem recusou foi a janela por usuário.
    public static Rejection Classify(RateLimiterStatistics? perUser, RateLimiterStatistics? global, ConcurrencyOptions options)
    {
        static bool Full(RateLimiterStatistics? stats, int queue) => stats is { CurrentAvailablePermits: 0 } && stats.CurrentQueuedCount >= queue;
        if (Full(perUser, options.PerUserQueue)) return Rejection.UserConcurrency;
        if (Full(global, options.GlobalQueue)) return Rejection.GlobalConcurrency;
        return Rejection.UserWindow;
    }

    public static IServiceCollection AddInvestmentReadRateLimit(this IServiceCollection services, IConfiguration configuration)
    {
        var concurrency = configuration.GetSection("RateLimiting:Investments").Get<ConcurrencyOptions>() ?? new ConcurrencyOptions();
        var perUser = PerUserConcurrency(concurrency);
        var global = GlobalConcurrency(concurrency);
        var windowRetry = (int)Math.Ceiling(Window.TotalSeconds / 6);
        return services.AddRateLimiter(options =>
        {
            options.AddPolicy(Policy, Partition);
            // Por usuário primeiro: o excesso de uma pessoa fica na fila dela e não entra na global.
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(perUser, global);
            options.OnRejected = async (rejected, ct) =>
            {
                var context = rejected.HttpContext;
                var reason = Classify(perUser.GetStatistics(context), global.GetStatistics(context), concurrency);
                var (tag, status, seconds, message) = reason switch
                {
                    Rejection.UserConcurrency => ("user_concurrency", StatusCodes.Status429TooManyRequests, 2,
                        "Muitas consultas ao mesmo tempo. Tente de novo em instantes."),
                    Rejection.GlobalConcurrency => ("global_concurrency", StatusCodes.Status503ServiceUnavailable, 5,
                        "Muitos acessos neste momento. Tente de novo em instantes."),
                    _ => ("user_window", StatusCodes.Status429TooManyRequests, windowRetry,
                        "Muitas atualizações em pouco tempo. Tente de novo em instantes."),
                };

                InvestmentMetrics.Rejected(tag, context);
                if (reason == Rejection.GlobalConcurrency)
                    context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(InvestmentReadRateLimit))
                        .LogWarning("Leituras de Investimentos acima da capacidade global: requisição recusada em {Route}.", InvestmentMetrics.Route(context));

                context.Response.StatusCode = status;
                context.Response.Headers.RetryAfter = seconds.ToString();
                await context.Response.WriteAsJsonAsync(new { message, retryAfterSeconds = seconds }, ct);
            };
        });
    }
}
