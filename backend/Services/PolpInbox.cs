using System.Security.Cryptography;
using System.Text;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public static class PolpInboxTime
{
    // Postgres guarda microssegundos: valores truncados aqui voltam do banco idênticos, o que
    // permite usar LockedUntil/ReceivedAt como marca de posse em comparações de igualdade.
    public static DateTime Now(TimeProvider clock)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
    }
}

public sealed class PolpInbox(IServiceScopeFactory scopes, TimeProvider clock) : IPolpInbox
{
    private const int MaxEnqueueRounds = 3;

    public static string DedupKey(PolpInboxEvent evt)
    {
        var raw = $"{evt.Event}|{evt.Resource}|{evt.ResourceId}|{evt.QueryParameters}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    // Contexto próprio: o SaveChanges aqui não pode levar junto alterações pendentes do chamador, e
    // uma violação de índice único não pode abortar uma transação dele no Postgres.
    public async Task<bool> EnqueueAsync(PolpInboxEvent evt, CancellationToken ct)
    {
        var key = DedupKey(evt);

        for (var round = 1; ; round++)
        {
            var now = PolpInboxTime.Now(clock);
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.PolpWebhookEvents.Add(new PolpWebhookEvent
            {
                Event = evt.Event,
                Resource = evt.Resource,
                ResourceId = evt.ResourceId,
                QueryParameters = evt.QueryParameters,
                Priority = evt.Priority,
                DedupKey = key,
                Status = PolpWebhookEventStatus.Pending,
                NextAttemptAt = now,
                ReceivedAt = now
            });

            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException) when (round <= MaxEnqueueRounds)
            {
                db.ChangeTracker.Clear();
            }

            // Sem depender do código de erro de cada banco: se existe um igual pendente, a falha
            // foi o índice único e basta avisar o worker (ReceivedAt muda) que chegou de novo. A
            // prioridade só sobe: um aviso da Polp igual a uma reconciliação na fila não pode
            // esperar atrás das outras reconciliações.
            var priority = evt.Priority;
            var touched = await db.PolpWebhookEvents
                .Where(e => e.DedupKey == key
                         && (e.Status == PolpWebhookEventStatus.Pending || e.Status == PolpWebhookEventStatus.Processing))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.ReceivedAt, now)
                    .SetProperty(e => e.Priority, e => e.Priority < priority ? e.Priority : priority), ct);
            if (touched > 0) return false;
        }
    }
}

public sealed record ClaimedPolpEvent(
    Guid Id,
    PolpInboxEvent Event,
    int Attempts,
    DateTime ReceivedAt,
    DateTime LockedUntil,
    bool Dead = false);

public enum PolpInboxCompletion
{
    Done,
    Requeued,
    LeaseLost
}

public static class PolpInboxStore
{
    public static bool UsesSkipLocked(AppDbContext db) => db.Database.IsNpgsql();

    public const string LeaseExpiredError = "Lease vencido: processamento interrompido";

    // Lease vencido conta como tentativa (o processamento caiu no meio, possivelmente derrubando o
    // processo): sem isso, um evento que derruba a instância voltaria a cada lease para sempre.
    internal static FormattableString PostgresClaimSql(DateTime now, DateTime lease, int max, int maxAttempts) => $"""
        UPDATE "PolpWebhookEvents" AS e SET
            "Attempts" = CASE WHEN e."Status" = 'Processing' THEN e."Attempts" + 1 ELSE e."Attempts" END,
            "Status" = CASE WHEN e."Status" = 'Processing' AND e."Attempts" + 1 >= {maxAttempts} THEN 'Dead' ELSE 'Processing' END,
            "LockedUntil" = CASE WHEN e."Status" = 'Processing' AND e."Attempts" + 1 >= {maxAttempts} THEN NULL ELSE {lease} END,
            "ProcessedAt" = CASE WHEN e."Status" = 'Processing' AND e."Attempts" + 1 >= {maxAttempts} THEN {now} ELSE e."ProcessedAt" END,
            "LastError" = CASE WHEN e."Status" = 'Processing' THEN {LeaseExpiredError} ELSE e."LastError" END
        WHERE e."Id" IN (
            SELECT "Id" FROM "PolpWebhookEvents"
            WHERE ("Status" = 'Pending' AND "NextAttemptAt" <= {now})
               OR ("Status" = 'Processing' AND "LockedUntil" < {now})
            ORDER BY "Priority", "NextAttemptAt"
            LIMIT {max}
            FOR UPDATE SKIP LOCKED)
        RETURNING e.*
        """;

    /// <summary>Pega até <paramref name="max"/> eventos. Os que vierem com Dead=true esgotaram as
    /// tentativas por lease vencido e já foram encerrados aqui: não devem ser processados.</summary>
    public static async Task<List<ClaimedPolpEvent>> ClaimAsync(AppDbContext db, DateTime now, TimeSpan lease, int max, int maxAttempts, CancellationToken ct)
    {
        var lockedUntil = now + lease;
        List<PolpWebhookEvent> rows;

        if (UsesSkipLocked(db))
        {
            rows = await db.PolpWebhookEvents
                .FromSql(PostgresClaimSql(now, lockedUntil, max, maxAttempts))
                .AsNoTracking()
                .ToListAsync(ct);
        }
        else
        {
            // Sem SKIP LOCKED (SQLite nos testes): o UPDATE condicional por id e estado lido faz o
            // papel do lock, só um concorrente consegue mudar a linha.
            var candidates = await Claimable(db.PolpWebhookEvents, now)
                .AsNoTracking()
                .OrderBy(e => e.Priority)
                .ThenBy(e => e.NextAttemptAt)
                .Take(max)
                .ToListAsync(ct);

            rows = [];
            foreach (var c in candidates)
            {
                int claimed;
                if (c.Status == PolpWebhookEventStatus.Pending)
                {
                    claimed = await db.PolpWebhookEvents
                        .Where(e => e.Id == c.Id && e.Status == PolpWebhookEventStatus.Pending && e.NextAttemptAt <= now)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(e => e.Status, PolpWebhookEventStatus.Processing)
                            .SetProperty(e => e.LockedUntil, lockedUntil), ct);
                }
                else
                {
                    var attempts = c.Attempts + 1;
                    var dead = attempts >= maxAttempts;
                    claimed = await db.PolpWebhookEvents
                        .Where(e => e.Id == c.Id && e.Status == PolpWebhookEventStatus.Processing
                                 && e.LockedUntil == c.LockedUntil && e.Attempts == c.Attempts)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(e => e.Attempts, attempts)
                            .SetProperty(e => e.Status, dead ? PolpWebhookEventStatus.Dead : PolpWebhookEventStatus.Processing)
                            .SetProperty(e => e.LockedUntil, dead ? null : lockedUntil)
                            .SetProperty(e => e.ProcessedAt, dead ? now : c.ProcessedAt)
                            .SetProperty(e => e.LastError, LeaseExpiredError), ct);
                }
                if (claimed == 1)
                    rows.Add(await db.PolpWebhookEvents.AsNoTracking().SingleAsync(e => e.Id == c.Id, ct));
            }
        }

        return rows
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.NextAttemptAt)
            .Select(r => new ClaimedPolpEvent(
                r.Id,
                new PolpInboxEvent(r.Event, r.Resource, r.ResourceId, r.QueryParameters, r.Priority),
                r.Attempts,
                AsUtc(r.ReceivedAt),
                r.LockedUntil is { } until ? AsUtc(until) : default,
                r.Status == PolpWebhookEventStatus.Dead))
            .ToList();
    }

    private static IQueryable<PolpWebhookEvent> Claimable(IQueryable<PolpWebhookEvent> q, DateTime now) =>
        q.Where(e => (e.Status == PolpWebhookEventStatus.Pending && e.NextAttemptAt <= now)
                  || (e.Status == PolpWebhookEventStatus.Processing && e.LockedUntil < now));

    private static IQueryable<PolpWebhookEvent> Owned(AppDbContext db, ClaimedPolpEvent claim, DateTime lockedUntil) =>
        db.PolpWebhookEvents.Where(e => e.Id == claim.Id
                                     && e.Status == PolpWebhookEventStatus.Processing
                                     && e.LockedUntil == lockedUntil);

    public static async Task<DateTime?> RenewAsync(AppDbContext db, ClaimedPolpEvent claim, DateTime lockedUntil, DateTime newLockedUntil, CancellationToken ct)
    {
        var renewed = await Owned(db, claim, lockedUntil)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.LockedUntil, newLockedUntil), ct);
        return renewed == 1 ? newLockedUntil : null;
    }

    // Done só se nenhum aviso igual chegou depois do claim (ReceivedAt intacto). Igualdade, e não
    // comparação com o relógio, porque outra instância pode ter o relógio atrasado.
    public static async Task<PolpInboxCompletion> CompleteAsync(AppDbContext db, ClaimedPolpEvent claim, DateTime lockedUntil, DateTime now, CancellationToken ct)
    {
        var done = await Owned(db, claim, lockedUntil)
            .Where(e => e.ReceivedAt == claim.ReceivedAt)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, PolpWebhookEventStatus.Done)
                .SetProperty(e => e.ProcessedAt, now)
                .SetProperty(e => e.LockedUntil, (DateTime?)null)
                .SetProperty(e => e.LastError, (string?)null), ct);
        if (done == 1) return PolpInboxCompletion.Done;

        return await ReleaseAsync(db, claim, lockedUntil, now, ct)
            ? PolpInboxCompletion.Requeued
            : PolpInboxCompletion.LeaseLost;
    }

    public static async Task<bool> ReleaseAsync(AppDbContext db, ClaimedPolpEvent claim, DateTime lockedUntil, DateTime now, CancellationToken ct) =>
        await Owned(db, claim, lockedUntil)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, PolpWebhookEventStatus.Pending)
                .SetProperty(e => e.NextAttemptAt, now)
                .SetProperty(e => e.LockedUntil, (DateTime?)null), ct) == 1;

    public static async Task<bool> RetryAsync(AppDbContext db, ClaimedPolpEvent claim, DateTime lockedUntil, int attempts, DateTime nextAttemptAt, string lastError, CancellationToken ct) =>
        await Owned(db, claim, lockedUntil)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, PolpWebhookEventStatus.Pending)
                .SetProperty(e => e.Attempts, attempts)
                .SetProperty(e => e.NextAttemptAt, nextAttemptAt)
                .SetProperty(e => e.LockedUntil, (DateTime?)null)
                .SetProperty(e => e.LastError, lastError), ct) == 1;

    public static async Task<bool> DeadAsync(AppDbContext db, ClaimedPolpEvent claim, DateTime lockedUntil, int attempts, DateTime now, string lastError, CancellationToken ct) =>
        await Owned(db, claim, lockedUntil)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, PolpWebhookEventStatus.Dead)
                .SetProperty(e => e.Attempts, attempts)
                .SetProperty(e => e.ProcessedAt, now)
                .SetProperty(e => e.LockedUntil, (DateTime?)null)
                .SetProperty(e => e.LastError, lastError), ct) == 1;

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
