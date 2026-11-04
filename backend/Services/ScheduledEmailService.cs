using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

// Roda de hora em hora e decide por usuário, no calendário local dele (UserClock): (1) lembrete
// de vencimento, no máximo um por dia local, a partir das 8h, com as pendências de hoje até daqui
// a 3 dias ainda não avisadas, e (2) aviso mensal do mês anterior, a partir das 8h do dia 1, só
// pra quem ligou essa opção.
public class ScheduledEmailService(IServiceScopeFactory scopeFactory, IJobLock jobLock, TimeProvider time, ILogger<ScheduledEmailService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private const int ReminderDaysAhead = 3;
    private const int SendFromLocalHour = 8;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval, time);

        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var email = scope.ServiceProvider.GetRequiredService<EmailService>();
                await RunRoundAsync(jobLock, db, email, time.GetUtcNow(), logger, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao rodar verificações de e-mail agendado.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    // Com várias instâncias da API, cada uma roda este laço: sem o lock, duas rodadas simultâneas
    // liam as mesmas pendências antes de qualquer uma gravar e o usuário recebia o e-mail em dobro.
    public static Task<bool> RunRoundAsync(IJobLock jobLock, AppDbContext db, EmailService email, DateTimeOffset utcNow, ILogger logger, CancellationToken ct) =>
        jobLock.RunExclusiveAsync(JobLockKeys.ScheduledEmail, () => RunOnceAsync(db, email, utcNow, logger, ct), ct);

    public static async Task RunOnceAsync(AppDbContext db, EmailService email, DateTimeOffset utcNow, ILogger logger, CancellationToken ct)
    {
        await SendDueRemindersAsync(db, email, utcNow, logger, ct);
        await SendMonthlySummariesAsync(db, email, utcNow, logger, ct);
    }

    // Um e-mail por usuário, não um por transação, e sem nome nem valor de nenhuma conta (ver
    // EmailService). Só usuários ativos que não desligaram o lembrete, e só transações de contas
    // ainda conectadas (ou manuais): antes uma conta "desconectada" continuava gerando lembrete.
    private static async Task SendDueRemindersAsync(AppDbContext db, EmailService email, DateTimeOffset utcNow, ILogger logger, CancellationToken ct)
    {
        // Nenhum fuso se afasta mais de 14h do UTC: o "hoje" local de qualquer usuário cai no dia
        // UTC ±1. A busca usa essa janela larga e o recorte exato é feito por usuário.
        var utcDay = utcNow.UtcDateTime.Date;
        var from = utcDay.AddDays(-1);
        var to = utcDay.AddDays(2 + ReminderDaysAhead);

        var candidates = await db.Transactions
            .Include(t => t.User)
            .Where(t => t.Status == TransactionStatus.Pending
                     && t.ReminderSentAt == null
                     && t.DueDate >= from
                     && t.DueDate < to
                     && t.User.IsActive
                     && t.User.DueRemindersEnabled
                     && (t.BankAccount == null || t.BankAccount.IsActive))
            .ToListAsync(ct);

        foreach (var group in candidates.GroupBy(t => t.UserId))
        {
            var user = group.First().User;
            var localNow = UserClock.NowIn(user.TimeZone, utcNow);
            if (localNow.Hour < SendFromLocalHour) continue;

            var localToday = DateOnly.FromDateTime(localNow);
            if (user.LastDueReminderLocalDate >= localToday) continue;

            var firstDay = localNow.Date;
            var lastDay = firstDay.AddDays(ReminderDaysAhead);
            var items = group.Where(t => t.DueDate.Date >= firstDay && t.DueDate.Date <= lastDay).ToList();
            if (items.Count == 0) continue;

            try
            {
                await email.SendDueReminderAsync(user.Email, items.Count, items.Min(t => t.DueDate));
                foreach (var transaction in items)
                    transaction.ReminderSentAt = utcNow.UtcDateTime;
                user.LastDueReminderLocalDate = localToday;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Falha ao enviar lembrete de vencimento pro usuário {UserId} ({ErrorType})", user.Id, ex.GetType().Name);
            }
        }
    }

    // Opt-in (User.MonthlySummaryEnabled, desligado por padrão) e sem números: só avisa que o
    // resumo do mês está no painel. Compara pelo mês já tratado, não por "hoje é dia 1": uma hora
    // ou um dia sem execução atrasa o aviso em vez de perdê-lo.
    private static async Task SendMonthlySummariesAsync(AppDbContext db, EmailService email, DateTimeOffset utcNow, ILogger logger, CancellationToken ct)
    {
        var latestPossibleMonth = FirstOfMonth(DateOnly.FromDateTime(utcNow.UtcDateTime.AddDays(1))).AddMonths(-1);

        var users = await db.Users
            .Where(u => u.IsActive
                     && u.MonthlySummaryEnabled
                     && (u.LastMonthlySummaryMonth == null || u.LastMonthlySummaryMonth < latestPossibleMonth))
            .ToListAsync(ct);

        foreach (var user in users)
        {
            var localNow = UserClock.NowIn(user.TimeZone, utcNow);
            if (localNow.Day == 1 && localNow.Hour < SendFromLocalHour) continue;

            var currentMonth = FirstOfMonth(DateOnly.FromDateTime(localNow));
            var previousMonth = currentMonth.AddMonths(-1);
            if (LastSummarizedMonth(user) >= previousMonth) continue;

            var start = previousMonth.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var end = currentMonth.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            try
            {
                // Mês sem nenhuma transação não gera aviso, mas fica marcado como tratado: senão uma
                // importação atrasada no meio do mês seguinte dispararia "seu mês fechou" fora de hora.
                if (await db.Transactions.AnyAsync(t => t.UserId == user.Id && t.DueDate >= start && t.DueDate < end, ct))
                {
                    await email.SendMonthlyNoticeAsync(user.Email);
                    user.LastMonthlySummarySentAt = utcNow.UtcDateTime;
                }
                user.LastMonthlySummaryMonth = previousMonth;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Falha ao enviar aviso mensal pro usuário {UserId} ({ErrorType})", user.Id, ex.GetType().Name);
            }
        }
    }

    // Quem recebeu o aviso pela versão anterior só tem LastMonthlySummarySentAt, enviado no dia 1
    // UTC sobre o mês UTC anterior: sem isso, o primeiro ciclo depois do deploy reenviaria.
    private static DateOnly? LastSummarizedMonth(User user) =>
        user.LastMonthlySummaryMonth
        ?? (user.LastMonthlySummarySentAt is DateTime sent ? new DateOnly(sent.Year, sent.Month, 1).AddMonths(-1) : null);

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);
}
