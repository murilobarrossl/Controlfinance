using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using ControlFinance.API.Controllers;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;

namespace ControlFinance.API.Tests;

public class InvestmentReadRateLimitTests
{
    private static readonly Endpoint Limited = new(null, new EndpointMetadataCollection(new EnableRateLimitingAttribute(InvestmentReadRateLimit.Policy)), "investimentos");
    private static readonly Endpoint Webhook = new(null, new EndpointMetadataCollection(), "webhook");

    private static HttpContext Request(string? userId, Endpoint? endpoint = null, string ip = "203.0.113.7")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (userId is not null) context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId)], "test"));
        context.SetEndpoint(endpoint ?? Limited);
        return context;
    }

    [Fact]
    public void Janela_barra_o_mesmo_usuario_acima_de_120_e_nao_afeta_outro()
    {
        using var limiter = PartitionedRateLimiter.Create<HttpContext, string>(InvestmentReadRateLimit.Partition);
        var heavy = Request("usuario-a");

        for (var i = 0; i < InvestmentReadRateLimit.PermitsPerWindow; i++)
            Assert.True(limiter.AttemptAcquire(heavy).IsAcquired);

        using var rejected = limiter.AttemptAcquire(heavy);
        Assert.False(rejected.IsAcquired);
        Assert.True(limiter.AttemptAcquire(Request("usuario-b")).IsAcquired);
    }

    [Fact]
    public void Usuarios_no_mesmo_ip_tem_limites_separados()
    {
        Assert.NotEqual(InvestmentReadRateLimit.PartitionKey(Request("usuario-a")), InvestmentReadRateLimit.PartitionKey(Request("usuario-b")));
        Assert.Equal("ip:203.0.113.7", InvestmentReadRateLimit.PartitionKey(Request(null)));
    }

    [Fact]
    public void Concorrencia_por_usuario_isola_quem_abre_consultas_demais()
    {
        var options = new InvestmentReadRateLimit.ConcurrencyOptions { PerUser = 2 };
        using var limiter = InvestmentReadRateLimit.PerUserConcurrency(options);
        var heavy = Request("usuario-a");

        var held = new[] { limiter.AttemptAcquire(heavy), limiter.AttemptAcquire(heavy) };
        Assert.All(held, lease => Assert.True(lease.IsAcquired));
        Assert.False(limiter.AttemptAcquire(heavy).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Request("usuario-b")).IsAcquired);

        held[0].Dispose();
        Assert.True(limiter.AttemptAcquire(heavy).IsAcquired);
    }

    [Fact]
    public void Concorrencia_global_tem_teto_e_nao_alcanca_rotas_fora_da_aba()
    {
        var options = new InvestmentReadRateLimit.ConcurrencyOptions { Global = 2 };
        using var limiter = InvestmentReadRateLimit.GlobalConcurrency(options);

        Assert.True(limiter.AttemptAcquire(Request("a")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Request("b")).IsAcquired);
        Assert.False(limiter.AttemptAcquire(Request("c")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Request("c", Webhook)).IsAcquired);
    }

    [Fact]
    public void Recusa_e_classificada_pela_fila_que_encheu()
    {
        var options = new InvestmentReadRateLimit.ConcurrencyOptions { PerUserQueue = 2, GlobalQueue = 3 };
        RateLimiterStatistics Stats(long available, long queued) => new() { CurrentAvailablePermits = available, CurrentQueuedCount = queued };

        Assert.Equal(InvestmentReadRateLimit.Rejection.UserConcurrency, InvestmentReadRateLimit.Classify(Stats(0, 2), Stats(5, 0), options));
        Assert.Equal(InvestmentReadRateLimit.Rejection.GlobalConcurrency, InvestmentReadRateLimit.Classify(Stats(1, 0), Stats(0, 3), options));
        Assert.Equal(InvestmentReadRateLimit.Rejection.UserWindow, InvestmentReadRateLimit.Classify(Stats(3, 0), Stats(10, 0), options));
    }

    private static InvestmentRefreshController Refresh(TestDb t, IMemoryCache cache) =>
        t.Controller(_ => new InvestmentRefreshController(cache, t.Clock));

    [Fact]
    public void Botao_atualizar_so_vale_de_30_em_30_segundos_por_usuario()
    {
        using var t = new TestDb();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        t.Clock.UtcNow = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.IsType<OkObjectResult>(Refresh(t, cache).Reserve());

        t.Clock.UtcNow = t.Clock.UtcNow.Value.AddSeconds(10);
        var throttled = Assert.IsType<ObjectResult>(Refresh(t, cache).Reserve());
        Assert.Equal(StatusCodes.Status429TooManyRequests, throttled.StatusCode);
        Assert.Contains("retryAfterSeconds = 20", throttled.Value!.ToString());

        t.Clock.UtcNow = t.Clock.UtcNow.Value.AddSeconds(21);
        Assert.IsType<OkObjectResult>(Refresh(t, cache).Reserve());
    }
}
