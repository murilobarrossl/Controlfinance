using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

// Exclusão de contas sem atividade (UserActivity) por 3 anos. Avisos com 90, 30 e 7 dias de antecedência;
// cada aviso só conta depois de aceito pelo provedor de e-mail, e um aviso atrasado empurra a data para
// manter a antecedência prometida. A exclusão exige os três avisos para a mesma atividade, é reservada por
// um UPDATE condicional (atividade nova zera a sequência e faz a reserva falhar) e só acontece numa rodada
// seguinte, depois de reconfirmada sob trava de linha. Na dúvida, a conta fica.
public class InactiveAccountService(IServiceScopeFactory scopeFactory, IJobLock jobLock, IConfiguration config, TimeProvider time,
    ILogger<InactiveAccountService> logger) : BackgroundService
{
    private static readonly int[] LeadDays = [90, 30, 7];
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);
    private const int BatchSize = 200;
    private const int MaxDeletionsPerRound = 50;

    public static DateTime DeadlineFor(DateTime lastActivityAt) => lastActivityAt.AddYears(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(10), time, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = Interval;
            try
            {
                await jobLock.RunExclusiveAsync(JobLockKeys.InactiveAccounts, () => RunOnceAsync(stoppingToken), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Rodada interrompida (ex.: banco indisponível): nada foi decidido além do que já está gravado;
                // tenta de novo mais cedo em vez de esperar o dia seguinte.
                logger.LogError("Falha na rotina de contas inativas ({ErrorType}); nova tentativa em 1 hora.", ex.GetType().Name);
                delay = RetryInterval;
            }
            await Task.Delay(delay, time, stoppingToken);
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Sem e-mail configurado não há aviso, e sem os três avisos ninguém chega à exclusão.
        var email = ConfigSecrets.IsConfigured(config["Resend:ApiKey"]) ? scope.ServiceProvider.GetRequiredService<EmailService>() : null;
        if (email is null) logger.LogWarning("Envio de e-mail não configurado: avisos de inatividade suspensos.");

        var (sent, failed, due) = await SendNoticesAsync(db, email, now, ct);
        var deleted = await DeleteDueAsync(db, due, now, ct);
        if (sent + failed + deleted > 0)
            logger.LogInformation("Inatividade: {Sent} aviso(s) enviado(s), {Failed} falha(s) de envio, {Deleted} conta(s) excluída(s).", sent, failed, deleted);
    }

    private sealed record Candidate(Guid Id, string Email, string TimeZone, DateTime LastActivityAt, int Stage, DateTime? Epoch, DateTime? DeletionAt);

    private async Task<(int Sent, int Failed, List<Guid> Due)> SendNoticesAsync(AppDbContext db, EmailService? email, DateTime now, CancellationToken ct)
    {
        // Prazo a até 90 dias: LastActivityAt + 3 anos - 90 dias <= agora. Usa o índice de LastActivityAt.
        var threshold = now.AddDays(LeadDays[0]).AddYears(-3);
        int sent = 0, failed = 0;
        var due = new List<Guid>();

        Candidate? after = null;
        while (true)
        {
            var query = db.Users.AsNoTracking().Where(u => u.IsActive && u.LastActivityAt <= threshold);
            if (after != null)
                query = query.Where(u => u.LastActivityAt > after.LastActivityAt
                    || (u.LastActivityAt == after.LastActivityAt && u.Id.CompareTo(after.Id) > 0));
            var batch = await query.OrderBy(u => u.LastActivityAt).ThenBy(u => u.Id).Take(BatchSize)
                .Select(u => new Candidate(u.Id, u.Email, u.TimeZone, u.LastActivityAt, u.InactivityNoticeStage, u.InactivityNoticeEpoch, u.InactivityDeletionAt))
                .ToListAsync(ct);
            if (batch.Count == 0) break;

            foreach (var u in batch)
            {
                var stage = u.Epoch == u.LastActivityAt ? u.Stage : 0;
                var deletionAt = stage > 0 && u.DeletionAt is { } d ? d : DeadlineFor(u.LastActivityAt);
                if (stage >= LeadDays.Length)
                {
                    if (now >= deletionAt) due.Add(u.Id);
                    continue;
                }
                var lead = TimeSpan.FromDays(LeadDays[stage]);
                if (email is null || now < deletionAt - lead) continue;

                var announced = deletionAt > now + lead ? deletionAt : now + lead;
                try
                {
                    await email.SendInactivityNoticeAsync(u.Email, LocalDate(announced, u.TimeZone));
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    failed++;
                    logger.LogWarning("Aviso de inatividade não enviado ({ErrorType}); nova tentativa na próxima rodada.", ex.GetType().Name);
                    continue;
                }
                sent++;
                var epoch = u.LastActivityAt;
                await db.Users.Where(x => x.Id == u.Id && x.IsActive && x.LastActivityAt == epoch)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.InactivityNoticeStage, stage + 1)
                        .SetProperty(x => x.InactivityNoticeEpoch, epoch)
                        .SetProperty(x => x.InactivityDeletionAt, announced), ct);
            }
            if (batch.Count < BatchSize) break;
            after = batch[^1];
        }
        return (sent, failed, due);
    }

    // Tempo entre a reserva e a exclusão: maior que o cache de sessão (SessionValidator) e que qualquer
    // requisição em andamento, para que nenhum acesso aceito antes da reserva passe sem ser visto.
    private static readonly TimeSpan SettlePeriod = TimeSpan.FromHours(1);

    /// <summary>InactivityNoticeStage depois dos avisos: gravado na confirmação final, antes do primeiro passo
    /// destrutivo. A partir daí a conta nunca volta ao usuário; a exclusão só é retomada.</summary>
    public const int DeletionStartedStage = 4;

    // Reserva que não chegou a iniciar a exclusão nesse prazo (falha persistente na confirmação) é desfeita.
    private static readonly TimeSpan MaxClaimWithoutStart = TimeSpan.FromDays(7);

    private enum Verdict { Confirmed, Released, Gone }

    private async Task<int> DeleteDueAsync(AppDbContext db, List<Guid> due, DateTime now, CancellationToken ct)
    {
        var inactiveSince = InactiveSince(now);

        // Fase 1, reserva: um UPDATE atômico que revalida tudo no banco (3 anos sem atividade, mais a
        // granularidade do registro; os três avisos para essa mesma atividade; data informada vencida).
        // IsActive = false e a nova versão de sessão impedem login e derrubam sessões abertas.
        foreach (var id in due.Take(MaxDeletionsPerRound))
        {
            await db.Users
                .Where(u => u.Id == id && u.IsActive && u.InactivityDeletionClaimedAt == null
                    && u.InactivityNoticeStage == LeadDays.Length && u.InactivityNoticeEpoch == u.LastActivityAt
                    && u.LastActivityAt <= inactiveSince && u.InactivityDeletionAt <= now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.IsActive, false)
                    .SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1)
                    .SetProperty(u => u.InactivityDeletionClaimedAt, now), ct);
        }

        // Reservas presas antes de iniciar a exclusão voltam ao usuário com os dados intactos. A sequência de
        // avisos recomeça (nova data, novos três avisos), o que também invalida a tentativa: a reserva e a
        // confirmação exigem o aviso 3, então nenhuma rodada atrasada consegue excluir com base nela.
        var stuckBefore = now - MaxClaimWithoutStart;
        var restored = await db.Users
            .Where(u => u.InactivityDeletionClaimedAt != null && u.InactivityDeletionClaimedAt <= stuckBefore
                && u.InactivityNoticeStage != DeletionStartedStage)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.IsActive, true)
                .SetProperty(u => u.InactivityDeletionClaimedAt, (DateTime?)null)
                .SetProperty(u => u.InactivityNoticeStage, 0)
                .SetProperty(u => u.InactivityNoticeEpoch, (DateTime?)null)
                .SetProperty(u => u.InactivityDeletionAt, (DateTime?)null), ct);
        if (restored > 0)
            logger.LogWarning("Inatividade: {Restored} reserva(s) desfeita(s) após falhas persistentes antes de iniciar a exclusão; dados preservados.", restored);

        // Fase 2, exclusão: só reservas de rodadas anteriores (SettlePeriod). Inclui as interrompidas.
        var settledBefore = now - SettlePeriod;
        var claimed = await db.Users.AsNoTracking()
            .Where(u => u.InactivityDeletionClaimedAt != null && u.InactivityDeletionClaimedAt <= settledBefore)
            .OrderBy(u => u.InactivityDeletionClaimedAt).Select(u => u.Id).Take(MaxDeletionsPerRound).ToListAsync(ct);

        int deleted = 0, released = 0;
        foreach (var id in claimed)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var scope = scopeFactory.CreateScope();
                var scoped = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                switch (await ConfirmAsync(scoped, id, now, ct))
                {
                    case Verdict.Confirmed:
                        await scope.ServiceProvider.GetRequiredService<AccountDeletionService>().DeleteAsync(id);
                        deleted++;
                        break;
                    case Verdict.Released:
                        released++;
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A conta continua reservada (sem acesso) e é reconfirmada na próxima rodada; nada é forçado.
                logger.LogError("Exclusão por inatividade não concluída ({ErrorType}); nova tentativa na próxima rodada.", ex.GetType().Name);
            }
        }
        if (released > 0)
            logger.LogInformation("Inatividade: {Released} reserva(s) cancelada(s) por atividade ou dado divergente.", released);
        return deleted;
    }

    private static DateTime InactiveSince(DateTime now) => now.AddYears(-3) - UserActivity.Granularity;

    // Confirmação final sob trava da linha (Postgres): um registro de atividade concorrente espera o commit.
    // Qualquer divergência devolve a conta ao usuário; na dúvida, a conta fica. Depois do commit nenhum
    // acesso é possível (IsActive = false há mais de SettlePeriod), então a decisão vale até a exclusão.
    private static async Task<Verdict> ConfirmAsync(AppDbContext db, Guid id, DateTime now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM \"Users\" WHERE \"Id\" = {id} FOR UPDATE", ct);

        var u = await db.Users.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.IsActive, x.InactivityDeletionClaimedAt, x.InactivityNoticeStage, x.InactivityNoticeEpoch, x.LastActivityAt, x.InactivityDeletionAt })
            .FirstOrDefaultAsync(ct);
        if (u is null)
        {
            await tx.CommitAsync(ct);
            return Verdict.Gone;
        }

        // Exclusão já iniciada numa rodada anterior: pode haver dados apagados ou consentimentos revogados, então
        // nunca volta ao usuário; só é retomada (AccountDeletionService é idempotente).
        if (u.InactivityNoticeStage == DeletionStartedStage)
        {
            await tx.CommitAsync(ct);
            return Verdict.Confirmed;
        }

        var confirmed = !u.IsActive && u.InactivityDeletionClaimedAt != null
            && u.InactivityNoticeStage == LeadDays.Length && u.InactivityNoticeEpoch == u.LastActivityAt
            && u.LastActivityAt <= InactiveSince(now) && u.InactivityDeletionAt <= now;
        if (confirmed)
            await db.Users.Where(x => x.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.InactivityNoticeStage, DeletionStartedStage), ct);
        else
            await db.Users.Where(x => x.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.IsActive, true)
                    .SetProperty(x => x.InactivityDeletionClaimedAt, (DateTime?)null), ct);
        await tx.CommitAsync(ct);
        return confirmed ? Verdict.Confirmed : Verdict.Released;
    }

    private static DateTime LocalDate(DateTime utc, string timeZone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc),
            TimeZoneInfo.FindSystemTimeZoneById(UserClock.Normalize(timeZone))).Date;
}
