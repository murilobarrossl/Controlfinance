using System.Buffers.Binary;
using System.Security.Cryptography;
using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

// Divide o intervalo em rodadas de no máximo 15 min e cada integração cai em uma delas, para não
// despejar todas na inbox de uma vez. A rodada vem do relógio (não de um contador em memória) para
// ser a mesma entre reinícios e instâncias. Com intervalo que não é múltiplo do tick, slots é
// arredondado para baixo e o ciclo real (slots × tick) fica um pouco menor que o intervalo.
public readonly record struct ReconciliationSchedule(TimeSpan Tick, int Slots)
{
    public static readonly TimeSpan MaxTick = TimeSpan.FromMinutes(15);

    public static ReconciliationSchedule FromMinutes(int intervalMinutes)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, intervalMinutes));
        var tick = interval < MaxTick ? interval : MaxTick;
        return new ReconciliationSchedule(tick, Math.Max(1, (int)(interval.Ticks / tick.Ticks)));
    }

    public long TickIndex(DateTimeOffset now) => now.ToUnixTimeMilliseconds() / (long)Tick.TotalMilliseconds;

    public int Round(DateTimeOffset now) => (int)(TickIndex(now) % Slots);

    // GetHashCode de Guid não é estável entre processos; SHA-256 dos bytes espalha bem mesmo ids
    // sequenciais.
    public int Bucket(Guid id) =>
        (int)(BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(id.ToByteArray())) % (ulong)Slots);

    public DateTimeOffset NextTickAt(DateTimeOffset now) =>
        DateTimeOffset.FromUnixTimeMilliseconds((TickIndex(now) + 1) * (long)Tick.TotalMilliseconds);
}

// Rede de segurança para avisos da Polp que nunca chegaram (a política de reenvio dela não é
// documentada) e para backfills que não terminaram. Só enfileira, com prioridade baixa: quem chama
// a Polp é o worker da inbox, em série, por causa do rate limit global do x-api-client. A dedupe da
// inbox impede que rodadas seguidas acumulem eventos iguais. Rodada perdida (app fora do ar) não é
// recuperada: aquelas integrações esperam o próximo ciclo.
public class PolpReconciliationService(
    IServiceScopeFactory scopeFactory, IJobLock jobLock, IConfiguration config, TimeProvider clock,
    ILogger<PolpReconciliationService> logger)
    : BackgroundService
{
    public ReconciliationSchedule Schedule { get; } =
        ReconciliationSchedule.FromMinutes(config.GetValue<int?>("Polp:ReconciliationIntervalMinutes") ?? 1440);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ConfigSecrets.IsPolpEnabled(config)) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao enfileirar a reconciliação da Polp.");
            }

            // Alinhado à virada do tick: esperar um tick inteiro a partir de agora deixaria o
            // atraso acumular até pular uma rodada.
            var now = clock.GetUtcNow();
            await Task.Delay(Schedule.NextTickAt(now) - now + TimeSpan.FromSeconds(1), clock, stoppingToken);
        }
    }

    /// <summary>Uma rodada (a fração do slot atual). False se outra instância já está rodando.</summary>
    public Task<bool> RunOnceAsync(CancellationToken ct) =>
        jobLock.RunExclusiveAsync(JobLockKeys.PolpReconciliation, async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inbox = scope.ServiceProvider.GetRequiredService<IPolpInbox>();
            var now = clock.GetUtcNow();
            var round = Schedule.Round(now);
            var recoveryBefore = now.UtcDateTime.AddMinutes(-15);
            Guid? after = null;
            while (true)
            {
                var query = db.PolpIntegrations.AsNoTracking().Where(p => p.Status == PolpSyncService.AuthorisedStatus);
                if (after is { } last) query = query.Where(p => p.Id.CompareTo(last) > 0);
                var integrations = await query.OrderBy(p => p.Id).Select(p => new
                {
                    p.Id, p.PolpIntegrationId, p.SyncedLocally,
                    NeedsTransactionDetails = p.SyncedLocally && p.TransactionDetailsVersion < PolpSyncService.TransactionDetailsVersion,
                    // Upgrade/startup recovery must not wait for this connection's daily bucket.
                    NeedsInvestmentRecovery = !db.InvestmentSyncStates.Any(s => s.UserId == p.UserId
                        && s.IntegrationId == p.Id && s.Kind == "discovery")
                        || db.InvestmentSyncStates.Any(s => s.UserId == p.UserId && s.IntegrationId == p.Id
                            && ((s.Kind == "discovery" && s.Status != "Complete" && s.StartedAt < recoveryBefore)
                                || ((s.Status == "Error" || s.Status == "Pending" || s.Status == "Reading" || s.Status == "Running")
                                    && s.Kind != "discovery" && (s.LastAttemptAt ?? s.StartedAt) < recoveryBefore)))
                }).Take(200).ToListAsync(ct);
                if (integrations.Count == 0) break;
                foreach (var integration in integrations)
                {
                    if (integration.NeedsTransactionDetails)
                        await inbox.EnqueueAsync(new PolpInboxEvent(
                            PolpInternalEvents.TransactionDetails, "consents", integration.PolpIntegrationId.ToString(), null, PolpInboxPriority.Low), ct);
                    var scheduled = Schedule.Bucket(integration.Id) == round;
                    if (!scheduled && !integration.NeedsInvestmentRecovery) continue;
                    if (scheduled)
                    {
                        var evt = integration.SyncedLocally ? PolpInternalEvents.Reconcile : PolpInternalEvents.Backfill;
                        await inbox.EnqueueAsync(new PolpInboxEvent(
                            evt, "consents", integration.PolpIntegrationId.ToString(), null, PolpInboxPriority.Low), ct);
                    }
                    await inbox.EnqueueAsync(new PolpInboxEvent(
                        PolpInternalEvents.ConsentMetadata, "consents", integration.PolpIntegrationId.ToString(), null, PolpInboxPriority.Low), ct);
                    await inbox.EnqueueAsync(new PolpInboxEvent(
                        PolpInternalEvents.InvestmentDiscovery, "integrations", integration.Id.ToString(), null, PolpInboxPriority.Low), ct);
                }
                after = integrations[^1].Id;
            }
        }, ct);
}
