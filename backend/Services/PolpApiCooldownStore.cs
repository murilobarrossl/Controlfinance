using System.Net;
using System.Security.Cryptography;
using System.Text;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public sealed class PolpRateLimitedException(DateTime retryAt, bool requestWasSent)
    : HttpRequestException("Limite de consultas da Polp atingido.", null, HttpStatusCode.TooManyRequests)
{
    public DateTime RetryAt { get; } = retryAt;
    public bool RequestWasSent { get; } = requestWasSent;
}

// All nodes sharing a Polp API client respect the same Retry-After, including other streams.
// Each DB operation is short and independent of network calls and integration transactions.
public sealed class PolpApiCooldownStore(IServiceScopeFactory scopes, IConfiguration config, TimeProvider clock)
{
    private readonly string _key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(config["Polp:ClientId"] ?? "disabled")));
    public async Task CheckAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var retry = await db.PolpApiCooldowns.AsNoTracking().Where(x => x.ClientKey == _key).Select(x => (DateTime?)x.RetryAt).SingleOrDefaultAsync(ct);
        if (retry > clock.GetUtcNow().UtcDateTime) throw new PolpRateLimitedException(retry.Value, requestWasSent: false);
    }
    public async Task DeferAsync(DateTime until, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "PolpApiCooldowns" ("ClientKey", "RetryAt") VALUES ({_key}, {until})
                ON CONFLICT ("ClientKey") DO UPDATE SET "RetryAt" = GREATEST("PolpApiCooldowns"."RetryAt", EXCLUDED."RetryAt")
                """, ct);
        }
        else
        {
            var existing = await db.PolpApiCooldowns.SingleOrDefaultAsync(x => x.ClientKey == _key, ct);
            if (existing is null) db.PolpApiCooldowns.Add(new PolpApiCooldown { ClientKey = _key, RetryAt = until });
            else if (existing.RetryAt < until) existing.RetryAt = until;
            await db.SaveChangesAsync(ct);
        }
    }
}
