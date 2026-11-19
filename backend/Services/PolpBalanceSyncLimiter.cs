using System.Security.Cryptography;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

// Saldo sob demanda (POST /accounts/{id}/balance/sync). A Polp limita essa rota a 30 req/min por cliente,
// somando todos os usuários; passar disso devolve 429, e o 429 vira cooldown global (PolpApiCooldownStore)
// que pausa também a inbox de todos. Por isso a reserva é feita no banco, valendo para todas as instâncias:
// uma janela por conta e um limitador global (GCRA) no ritmo documentado. Mesma tabela do cooldown, só
// com chaves derivadas (sem id de usuário em claro).
public static class PolpBalanceSyncLimiter
{
    public static readonly TimeSpan AccountWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Spacing = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Burst = TimeSpan.FromMinutes(1) - Spacing;
    public const string KeyPrefix = "balance-sync:";
    private const string GlobalKey = KeyPrefix + "global";

    private static string AccountKey(Guid accountId) =>
        KeyPrefix + Convert.ToHexString(SHA256.HashData(accountId.ToByteArray()))[..40];

    /// <summary>Nulo quando a chamada foi reservada; senão, quanto esperar.</summary>
    public static async Task<TimeSpan?> TryReserveAsync(AppDbContext db, Guid accountId, DateTime now, CancellationToken ct)
    {
        var key = AccountKey(accountId);
        var accountUntil = now + AccountWindow;
        if (!await ReserveAccountAsync(db, key, now, accountUntil, ct))
            return Wait(await RetryAtAsync(db, key, ct), now);
        if (await ReserveGlobalAsync(db, now, ct)) return null;

        // Sem vaga global: a janela da conta não fica gasta por uma chamada que não aconteceu.
        await db.PolpApiCooldowns.Where(x => x.ClientKey == key && x.RetryAt == accountUntil).ExecuteDeleteAsync(ct);
        return Wait(await RetryAtAsync(db, GlobalKey, ct) - Burst, now);
    }

    private static async Task<bool> ReserveAccountAsync(AppDbContext db, string key, DateTime now, DateTime until, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            return await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "PolpApiCooldowns" ("ClientKey", "RetryAt") VALUES ({key}, {until})
                ON CONFLICT ("ClientKey") DO UPDATE SET "RetryAt" = EXCLUDED."RetryAt"
                WHERE "PolpApiCooldowns"."RetryAt" <= {now}
                """, ct) > 0;
        return await UpsertAsync(db, key, current => current is null || current <= now ? until : null, ct);
    }

    private static async Task<bool> ReserveGlobalAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            return await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "PolpApiCooldowns" ("ClientKey", "RetryAt") VALUES ({GlobalKey}, {now + Spacing})
                ON CONFLICT ("ClientKey") DO UPDATE SET "RetryAt" = GREATEST("PolpApiCooldowns"."RetryAt", {now}) + {Spacing}
                WHERE "PolpApiCooldowns"."RetryAt" <= {now + Burst}
                """, ct) > 0;
        return await UpsertAsync(db, GlobalKey,
            current => current is null ? now + Spacing : current <= now + Burst ? (current > now ? current.Value : now) + Spacing : null, ct);
    }

    // Caminho sem Postgres (testes em SQLite): mesma regra, sem a atomicidade do ON CONFLICT.
    private static async Task<bool> UpsertAsync(AppDbContext db, string key, Func<DateTime?, DateTime?> next, CancellationToken ct)
    {
        var row = await db.PolpApiCooldowns.SingleOrDefaultAsync(x => x.ClientKey == key, ct);
        if (next(row?.RetryAt) is not { } value) return false;
        if (row is null) db.PolpApiCooldowns.Add(new PolpApiCooldown { ClientKey = key, RetryAt = value });
        else row.RetryAt = value;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static Task<DateTime?> RetryAtAsync(AppDbContext db, string key, CancellationToken ct) =>
        db.PolpApiCooldowns.AsNoTracking().Where(x => x.ClientKey == key).Select(x => (DateTime?)x.RetryAt).SingleOrDefaultAsync(ct);

    private static TimeSpan Wait(DateTime? until, DateTime now) =>
        until is { } u && u > now ? u - now : TimeSpan.FromSeconds(1);
}
