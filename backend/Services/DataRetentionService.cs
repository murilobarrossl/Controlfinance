using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

// Prazos de retenção configuráveis, aplicados uma vez por dia. Tudo aqui é opt-in: sem
// configuração, nada é apagado (apagar dado é irreversível e a escolha do prazo é do operador).
//
// DataRetention:SyncedHistoryMonths — histórico bancário importado mais antigo que N meses:
//   * não é importado em novas sincronizações (PolpSyncService) e
//   * é apagado daqui, só transações de contas sincronizadas (BankAccount.PolpAccountId != null)
//     e só as já resolvidas no passado (data anterior ao corte). Lançamentos manuais,
//     parcelamentos, categorias e contas não são tocados.
// AccessLog:RetentionDays (padrão 183, ~6 meses) — registros de acesso mais antigos que isso.
public static class DataRetentionPolicy
{
    public static int? SyncedHistoryMonths(IConfiguration config)
    {
        var months = config.GetValue<int?>("DataRetention:SyncedHistoryMonths");
        return months is > 0 ? months : null;
    }

    public static DateTime? SyncedHistoryCutoff(IConfiguration config, DateTime now)
    {
        var months = SyncedHistoryMonths(config);
        return months.HasValue ? now.AddMonths(-months.Value) : null;
    }

    public static int AccessLogRetentionDays(IConfiguration config)
    {
        var days = config.GetValue<int?>("AccessLog:RetentionDays");
        return days is > 0 ? days.Value : 183;
    }
}

public class DataRetentionService(IServiceScopeFactory scopeFactory, IJobLock jobLock, IConfiguration config, ILogger<DataRetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await jobLock.RunExclusiveAsync(JobLockKeys.DataRetention, async () =>
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var now = DateTime.UtcNow;

                    var cutoff = DataRetentionPolicy.SyncedHistoryCutoff(config, now);
                    var transactions = 0;
                    if (cutoff.HasValue)
                    {
                        transactions = await db.Transactions
                            .Where(t => t.DueDate < cutoff.Value
                                     && t.Status != TransactionStatus.Pending
                                     && t.BankAccount != null
                                     && t.BankAccount.PolpAccountId != null)
                            .ExecuteDeleteAsync(stoppingToken);
                    }

                    var accessCutoff = now.AddDays(-DataRetentionPolicy.AccessLogRetentionDays(config));
                    var accessLogs = await db.AccessLogs
                        .Where(a => a.OccurredAt < accessCutoff)
                        .ExecuteDeleteAsync(stoppingToken);

                    if (transactions + accessLogs > 0)
                        logger.LogInformation("Retenção: {Transactions} transação(ões) importada(s) fora do prazo e {AccessLogs} registro(s) de acesso expirado(s) apagados.", transactions, accessLogs);
                }, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao aplicar a política de retenção.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
