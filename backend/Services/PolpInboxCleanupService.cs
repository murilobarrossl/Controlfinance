using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public sealed class PolpInboxCleanupService(
    IServiceScopeFactory scopes,
    IJobLock jobLock,
    TimeProvider clock,
    ILogger<PolpInboxCleanupService> logger) : BackgroundService
{
    public static readonly TimeSpan DoneRetention = TimeSpan.FromDays(30);
    public static readonly TimeSpan DeadRetention = TimeSpan.FromDays(90);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public static async Task<int> RunRoundAsync(IJobLock jobLock, AppDbContext db, DateTime now, CancellationToken ct)
    {
        var deleted = 0;
        await jobLock.RunExclusiveAsync(JobLockKeys.PolpInboxCleanup, async () =>
        {
            var doneCutoff = now - DoneRetention;
            var deadCutoff = now - DeadRetention;
            deleted = await db.PolpWebhookEvents
                .Where(e => (e.Status == PolpWebhookEventStatus.Done && e.ProcessedAt < doneCutoff)
                         || (e.Status == PolpWebhookEventStatus.Dead && e.ProcessedAt < deadCutoff))
                .ExecuteDeleteAsync(ct);
            // Avisos atrasados de recurso removido param bem antes disso (8 tentativas ≈ 1 dia).
            await RemovedPolpResources.PurgeAsync(db, now, ct);
        }, ct);
        return deleted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var deleted = await RunRoundAsync(jobLock, db, clock.GetUtcNow().UtcDateTime, stoppingToken);
                if (deleted > 0)
                    logger.LogInformation("Inbox da Polp: {Deleted} evento(s) antigo(s) apagado(s).", deleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError("Falha na limpeza da inbox da Polp ({ErrorType})", ex.GetType().Name);
            }

            await Task.Delay(Interval, clock, stoppingToken);
        }
    }
}
