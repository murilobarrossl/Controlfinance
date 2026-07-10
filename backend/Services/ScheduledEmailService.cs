using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

// Roda uma vez por dia: (1) lembretes de vencimento pras transações que entraram na janela
// de 3 dias antes do vencimento e ainda não foram avisadas, e (2) resumo mensal, enviado
// pra cada usuário todo dia 1º, cobrindo o mês anterior (já fechado).
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

    private async Task SendDueReminders(AppDbContext db, EmailService email, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var horizon = now.AddDays(ReminderDaysBefore);

        var dueTransactions = await db.Transactions
            .Include(t => t.User)
            .Where(t => t.Status == TransactionStatus.Pending
                     && t.ReminderSentAt == null
                     && t.DueDate >= now
                     && t.DueDate <= horizon)
            .ToListAsync(ct);

        foreach (var transaction in dueTransactions)
        {
            try
            {
                await email.SendTransactionAlertEmailAsync(
                    transaction.User.Email, transaction.User.Name,
                    transaction.Name, transaction.Amount, transaction.DueDate);

                transaction.ReminderSentAt = now;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao enviar lembrete de vencimento da transação {TransactionId}", transaction.Id);
            }
        }

        if (dueTransactions.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    private async Task SendMonthlySummaries(AppDbContext db, EmailService email, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var currentMonthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var previousMonthStart = currentMonthStart.AddMonths(-1);

        var users = await db.Users
            .Where(u => u.IsActive && (u.LastMonthlySummarySentAt == null || u.LastMonthlySummarySentAt < currentMonthStart))
            .ToListAsync(ct);

        foreach (var user in users)
        {
            var monthlyTransactions = await db.Transactions
                .Where(t => t.UserId == user.Id && t.DueDate >= previousMonthStart && t.DueDate < currentMonthStart)
                .ToListAsync(ct);

            if (monthlyTransactions.Count == 0) continue; // nada a resumir, não manda e-mail vazio

            var income = monthlyTransactions.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
            var expense = monthlyTransactions.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);

            try
            {
                await email.SendMonthlySummaryEmailAsync(user.Email, user.Name, income, expense, income - expense);
                user.LastMonthlySummarySentAt = now;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao enviar resumo mensal pro usuário {UserId}", user.Id);
            }
        }

        if (users.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
