using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetSummary([FromQuery] Guid? bankAccountId)
    {
        // conta bancária ativa
        var account = bankAccountId.HasValue
            ? await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == bankAccountId && b.UserId == UserId && b.IsActive)
            : await db.BankAccounts.FirstOrDefaultAsync(b => b.UserId == UserId && b.IsActive);

        if (account is null)
            return Ok(new { message = "Nenhuma conta bancária cadastrada." });

        var accountDto = new BankAccountDto(account.Id, account.Name, account.BankCode, account.Balance, account.IsActive);

        // receitas e despesas do mês atual
        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endOfMonth = startOfMonth.AddMonths(1);

        var monthlyTransactions = await db.Transactions
            .Where(t => t.UserId == UserId
                     && t.BankAccountId == account.Id
                     && t.DueDate >= startOfMonth
                     && t.DueDate < endOfMonth)
            .ToListAsync();

        var totalIncome = monthlyTransactions
            .Where(t => t.Type == TransactionType.Income)
            .Sum(t => t.Amount);

        var totalExpense = monthlyTransactions
            .Where(t => t.Type == TransactionType.Expense)
            .Sum(t => t.Amount);

        // contas pendentes e pagas
        var allTransactions = await db.Transactions
            .Include(t => t.Category)
            .Include(t => t.BankAccount)
            .Where(t => t.UserId == UserId && t.BankAccountId == account.Id)
            .OrderByDescending(t => t.DueDate)
            .ToListAsync();

        var pending = allTransactions
            .Where(t => t.Status == TransactionStatus.Pending || t.Status == TransactionStatus.Overdue)
            .Select(TransactionDto.FromEntity)
            .ToList();

        var paid = allTransactions
            .Where(t => t.Status == TransactionStatus.Paid)
            .Select(TransactionDto.FromEntity)
            .ToList();

        // gastos por categoria (mês atual, só despesas)
        var expensesByCategory = monthlyTransactions
            .Where(t => t.Type == TransactionType.Expense && t.CategoryId.HasValue)
            .GroupBy(t => t.CategoryId)
            .Select(g => new
            {
                CategoryId = g.Key,
                Amount = g.Sum(t => t.Amount)
            })
            .ToList();

        var categoryIds = expensesByCategory.Select(e => e.CategoryId).ToList();
        var categories = await db.Categories
            .Where(c => categoryIds.Contains(c.Id))
            .ToListAsync();

        var totalExpenseForCategories = expensesByCategory.Sum(e => e.Amount);

        var categoryExpenses = expensesByCategory.Select(e => new CategoryExpenseDto(
            categories.FirstOrDefault(c => c.Id == e.CategoryId)?.Name ?? "Sem categoria",
            e.Amount,
            totalExpenseForCategories > 0 ? Math.Round(e.Amount / totalExpenseForCategories * 100, 1) : 0
        )).ToList();

        // cartão ativo
        var card = await db.CreditCards
            .Include(c => c.Installments)
            .FirstOrDefaultAsync(c => c.UserId == UserId && c.IsActive);

        CreditCardDashboardDto? cardDto = null;
        if (card is not null)
        {
            var cardInfo = CreditCardDto.FromEntity(card);

            var currentInvoice = card.Installments.Sum(i => i.InstallmentAmount);

            // próximo vencimento da fatura
            var today = DateTime.UtcNow;
            var dueDate = new DateTime(today.Year, today.Month, card.DueDay, 0, 0, 0, DateTimeKind.Utc);
            if (dueDate < today) dueDate = dueDate.AddMonths(1);

            var installments = card.Installments
                .OrderBy(i => i.NextDueDate)
                .Select(InstallmentDto.FromEntity)
                .ToList();

            cardDto = new CreditCardDashboardDto(cardInfo, currentInvoice, dueDate, installments);
        }

        var summary = new DashboardSummaryDto(
            accountDto,
            totalIncome,
            totalExpense,
            pending,
            paid,
            categoryExpenses,
            cardDto
        );

        return Ok(summary);
    }
}
