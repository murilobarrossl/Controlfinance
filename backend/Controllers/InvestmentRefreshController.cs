using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;

namespace ControlFinance.API.Controllers;

// O botão "Atualizar dados" só relê o que já está no banco, pelas mesmas rotas GET que carregam a
// página e fazem a releitura automática: limitar essas rotas a uma vez por 30s quebraria a tela.
// Por isso o botão pede licença aqui antes de reler, e o intervalo vale para o pedido do usuário,
// não para as leituras. Não chama a Polp nem dispara sincronização.
[ApiController]
[Route("api/investments/refresh-requests")]
[Authorize]
[EnableRateLimiting(InvestmentReadRateLimit.Policy)]
public class InvestmentRefreshController(IMemoryCache cache, TimeProvider clock) : ApiControllerBase
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly object Gate = new();

    [HttpPost]
    public IActionResult Reserve()
    {
        TimeSpan retryAfter;
        lock (Gate)
        {
            var key = $"investments:refresh:{UserId}";
            var now = clock.GetUtcNow();
            if (cache.TryGetValue(key, out DateTimeOffset last) && now - last < Interval)
            {
                retryAfter = Interval - (now - last);
            }
            else
            {
                cache.Set(key, now, Interval);
                retryAfter = TimeSpan.Zero;
            }
        }

        InvestmentMetrics.Refresh(retryAfter == TimeSpan.Zero);
        if (retryAfter == TimeSpan.Zero) return Ok(new { nextAllowedInSeconds = (int)Interval.TotalSeconds });

        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        Response.Headers.RetryAfter = seconds.ToString();
        return StatusCode(StatusCodes.Status429TooManyRequests, new
        {
            message = "Os dados foram atualizados há pouco. Tente novamente em instantes.",
            retryAfterSeconds = seconds,
        });
    }
}
