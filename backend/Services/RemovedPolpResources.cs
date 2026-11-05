using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public static class RemovedPolpResources
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    // ON CONFLICT em vez de checar e inserir: desconexão e exclusão de conta podem marcar o mesmo
    // id ao mesmo tempo, e uma violação de PK abortaria a transação que apaga os dados.
    internal static FormattableString PostgresMarkSql(Guid[] ids, DateTime now) => $"""
        INSERT INTO "RemovedPolpResources" ("PolpId", "RemovedAt")
        SELECT unnest({ids}), {now}
        ON CONFLICT ("PolpId") DO UPDATE SET "RemovedAt" = EXCLUDED."RemovedAt"
        """;

    public static async Task MarkAsync(AppDbContext db, IEnumerable<Guid> polpIds, DateTime now, CancellationToken ct)
    {
        var ids = polpIds.Distinct().ToArray();
        if (ids.Length == 0) return;

        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlAsync(PostgresMarkSql(ids, now), ct);
            return;
        }

        // SQLite nos testes, sem concorrência.
        var existing = await db.RemovedPolpResources.Where(r => ids.Contains(r.PolpId)).ToListAsync(ct);
        foreach (var row in existing) row.RemovedAt = now;
        var known = existing.Select(r => r.PolpId).ToHashSet();
        db.RemovedPolpResources.AddRange(ids.Where(id => !known.Contains(id))
            .Select(id => new RemovedPolpResource { PolpId = id, RemovedAt = now }));
        await db.SaveChangesAsync(ct);
    }

    public static Task<bool> IsRemovedAsync(AppDbContext db, Guid polpId, CancellationToken ct) =>
        db.RemovedPolpResources.AsNoTracking().AnyAsync(r => r.PolpId == polpId, ct);

    public static async Task ClearAsync(AppDbContext db, IEnumerable<Guid> polpIds, CancellationToken ct)
    {
        var ids = polpIds.Distinct().ToList();
        if (ids.Count == 0) return;
        await db.RemovedPolpResources.Where(r => ids.Contains(r.PolpId)).ExecuteDeleteAsync(ct);
    }

    public static Task<int> PurgeAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var cutoff = now - Retention;
        return db.RemovedPolpResources.Where(r => r.RemovedAt < cutoff).ExecuteDeleteAsync(ct);
    }
}
