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
    public async Task<IActionResult> GetSummary([FromQuery] Guid? bankAccountId, CancellationToken ct)
    {
        var account = bankAccountId.HasValue
            ? await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bankAccountId && b.UserId == UserId && b.IsActive)
            : await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(b => b.UserId == UserId && b.IsActive);

        if (account is null)
            return Ok(new { message = "Nenhum banco conectado ainda." });

        var today = await UserTodayAsync(db, ct);
        var startOfMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endOfMonth = startOfMonth.AddMonths(1);

        var monthlyTransactions = await db.Transactions
            .AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.UserId == UserId
                     && t.BankAccountId == account.Id
                     && t.DueDate >= startOfMonth
                     && t.DueDate < endOfMonth)
            .ToListAsync();

        // Transferências entre contas do próprio usuário (Pix/TED pra outro banco seu) entram
        // como receita E despesa ao mesmo tempo na Polp; sem excluir dos totais, "receita" e
        // "despesa" do mês ficam sem sentido (ex.: "% da receita gasta" passando de 100%).
        // Continuam aparecendo no extrato (pendingEntities abaixo não passa por esse filtro), só
        // não entram nas somas/gráfico de categoria.
        var nonTransferMonthly = monthlyTransactions
            .Where(t => !TransferDetection.IsSelfTransfer(t.PolpMovementKind))
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
            .AsNoTracking()
            .Include(t => t.Category)
            .Where(t => t.UserId == UserId
                     && t.BankAccountId == account.Id
                     && (t.Status == TransactionStatus.Pending || t.Status == TransactionStatus.Overdue))
            .OrderBy(t => t.DueDate)
            .Take(100)
            .ToListAsync();

        foreach (var t in pendingEntities)
            t.BankAccount = account;
        var pending = pendingEntities.Select(t => TransactionDto.FromEntity(t, today)).ToList();

        var expensesByCategory = nonTransferMonthly
            .Where(t => t.Type == TransactionType.Expense && t.CategoryId.HasValue)
            .GroupBy(t => t.CategoryId)
            .Select(g => new
            {
                Name = g.First().Category?.Name,
                Amount = g.Sum(t => t.Amount)
            })
            .ToList();

        var totalExpenseForCategories = expensesByCategory.Sum(e => e.Amount);

        var categoryExpenses = expensesByCategory.Select(e => new CategoryExpenseDto(
            e.Name ?? "Sem categoria",
            e.Amount,
            totalExpenseForCategories > 0 ? Math.Round(e.Amount / totalExpenseForCategories * 100, 1) : 0
        )).ToList();

        // Mesma lista que CreditCardsController.GetSummary; o Dashboard abre no primeiro cartão e a
        // troca é só de exibição, feita no navegador.
        var recognizedCards = await CreditCardSummaryBuilder.BuildRecognizedListAsync(db, UserId, today, ct);
        var cardDto = recognizedCards.FirstOrDefault();

        var summary = new DashboardSummaryDto(
            DashboardAccountDto.FromEntity(account),
            totalIncome,
            totalExpense,
            pending,
            categoryExpenses,
            cardDto
        );

        return Ok(summary);
    }
}
