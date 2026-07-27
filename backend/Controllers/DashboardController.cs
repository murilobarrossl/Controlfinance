using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
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
        var account = bankAccountId.HasValue
            ? await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == bankAccountId && b.UserId == UserId && b.IsActive)
            : await db.BankAccounts.FirstOrDefaultAsync(b => b.UserId == UserId && b.IsActive);

        if (account is null)
            return Ok(new { message = "Nenhuma conta bancária cadastrada." });

        var accountDto = new BankAccountDto(account.Id, account.Name, account.BankCode, account.Balance, account.IsActive, account.Ownership.ToString());

        var ownerName = await db.Users.Where(u => u.Id == UserId).Select(u => u.Name).FirstOrDefaultAsync();

        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endOfMonth = startOfMonth.AddMonths(1);

        var monthlyTransactions = await db.Transactions
            .Include(t => t.Category)
            .Where(t => t.UserId == UserId
                     && t.BankAccountId == account.Id
                     && t.DueDate >= startOfMonth
                     && t.DueDate < endOfMonth)
            .ToListAsync();

        // Transferências entre contas do próprio usuário (Pix/TED pra outro banco seu) entram
        // como receita E despesa ao mesmo tempo na Polp; sem excluir dos totais, "receita" e
        // "despesa" do mês ficam sem sentido. Continuam aparecendo no extrato (pendingEntities
        // abaixo não passa por esse filtro), só não entram nas somas/gráfico de categoria.
        var nonTransferMonthly = monthlyTransactions
            .Where(t => !TransferDetection.IsSelfTransfer(t.Name, t.Category?.Name, ownerName))
            .ToList();

        var totalIncome = nonTransferMonthly
            .Where(t => t.Type == TransactionType.Income)
            .Sum(t => t.Amount);

        var totalExpense = nonTransferMonthly
            .Where(t => t.Type == TransactionType.Expense)
            .Sum(t => t.Amount);

        // contas pendentes: filtra e ordena no banco em vez de trazer o histórico inteiro da
        // conta pra memória; PaidTransactions nunca foi consumido pelo frontend, então saiu do DTO.
        var pendingEntities = await db.Transactions
            .Include(t => t.Category)
            .Include(t => t.BankAccount)
            .Where(t => t.UserId == UserId
                     && t.BankAccountId == account.Id
                     && (t.Status == TransactionStatus.Pending || t.Status == TransactionStatus.Overdue))
            .OrderBy(t => t.DueDate)
            .Take(100)
            .ToListAsync();

        var pending = pendingEntities.Select(t => TransactionDto.FromEntity(t, ownerName)).ToList();

        var expensesByCategory = nonTransferMonthly
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
            .Where(c => c.UserId == UserId && categoryIds.Contains(c.Id))
            .ToListAsync();

        var totalExpenseForCategories = expensesByCategory.Sum(e => e.Amount);

        var categoryExpenses = expensesByCategory.Select(e => new CategoryExpenseDto(
            categories.FirstOrDefault(c => c.Id == e.CategoryId)?.Name ?? "Sem categoria",
            e.Amount,
            totalExpenseForCategories > 0 ? Math.Round(e.Amount / totalExpenseForCategories * 100, 1) : 0
        )).ToList();

        var card = await db.CreditCards
            .Include(c => c.Installments)
            .FirstOrDefaultAsync(c => c.UserId == UserId && c.IsActive);

        CreditCardDashboardDto? cardDto = null;
        if (card is not null)
        {
            var cardInfo = CreditCardDto.FromEntity(card);

            var currentInvoice = card.Installments.Sum(i => i.InstallmentAmount);

            // próximo vencimento da fatura: clampa pro último dia válido do mês (ex.: DueDay=31
            // em fevereiro derrubaria o dashboard inteiro com ArgumentOutOfRangeException).
            var today = DateTime.UtcNow;
            var safeDueDay = Math.Min(card.DueDay, DateTime.DaysInMonth(today.Year, today.Month));
            var dueDate = new DateTime(today.Year, today.Month, safeDueDay, 0, 0, 0, DateTimeKind.Utc);
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
            categoryExpenses,
            cardDto
        );

        return Ok(summary);
    }
}
