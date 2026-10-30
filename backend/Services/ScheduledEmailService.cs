using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

// Roda uma vez por dia: (1) lembrete de vencimento (um por usuário) quando há transações
// pendentes na janela de 3 dias antes do vencimento ainda não avisadas, e (2) aviso mensal, todo
// dia 1º, só pra quem ligou essa opção.
public class ScheduledEmailService(IServiceScopeFactory scopeFactory, ILogger<ScheduledEmailService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private const int ReminderDaysBefore = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);

        do
        {
            try
            {
                await RunChecksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao rodar verificações de e-mail agendado.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunChecksAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<EmailService>();

        await SendDueReminders(db, email, ct);

        if (DateTime.UtcNow.Day == 1)
            await SendMonthlySummaries(db, email, ct);
    }

    // Um e-mail por usuário, não um por transação, e sem nome nem valor de nenhuma conta (ver
    // EmailService). Só usuários ativos que não desligaram o lembrete, e só transações de contas
    // ainda conectadas (ou manuais): antes uma conta "desconectada" continuava gerando lembrete.
    private async Task SendDueReminders(AppDbContext db, EmailService email, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var horizon = now.AddDays(ReminderDaysBefore);

        var dueTransactions = await db.Transactions
            .Include(t => t.User)
            .Where(t => t.Status == TransactionStatus.Pending
                     && t.ReminderSentAt == null
                     && t.DueDate >= now
                     && t.DueDate <= horizon
                     && t.User.IsActive
                     && t.User.DueRemindersEnabled
                     && (t.BankAccount == null || t.BankAccount.IsActive))
            .ToListAsync(ct);

        foreach (var group in dueTransactions.GroupBy(t => t.UserId))
        {
            var items = group.ToList();
            try
            {
                await email.SendDueReminderAsync(items[0].User.Email, items.Count, items.Min(t => t.DueDate));
                foreach (var transaction in items)
                    transaction.ReminderSentAt = now;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Falha ao enviar lembrete de vencimento pro usuário {UserId} ({ErrorType})", group.Key, ex.GetType().Name);
            }
        }

        if (dueTransactions.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    // Opt-in (User.MonthlySummaryEnabled, desligado por padrão) e sem números: só avisa que o
    // resumo do mês está no painel. Antes ia pra todo mundo, com receitas, despesas e saldo.
    private async Task SendMonthlySummaries(AppDbContext db, EmailService email, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var previousMonthStart = currentMonthStart.AddMonths(-1);

        var users = await db.Users
            .Where(u => u.IsActive
                     && u.MonthlySummaryEnabled
                     && (u.LastMonthlySummarySentAt == null || u.LastMonthlySummarySentAt < currentMonthStart)
                     && db.Transactions.Any(t => t.UserId == u.Id && t.DueDate >= previousMonthStart && t.DueDate < currentMonthStart))
            .ToListAsync(ct);

        foreach (var user in users)
        {
            try
            {
                await email.SendMonthlyNoticeAsync(user.Email);
                user.LastMonthlySummarySentAt = now;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Falha ao enviar aviso mensal pro usuário {UserId} ({ErrorType})", user.Id, ex.GetType().Name);
            }
        }

        if (users.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
