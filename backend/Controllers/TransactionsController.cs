using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/transactions")]
[Authorize]
public class TransactionsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] string? type)
    {
        var query = db.Transactions
            .Include(t => t.Category)
            .Include(t => t.BankAccount)
            .Where(t => t.UserId == UserId);

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<TransactionStatus>(status, true, out var s))
            query = query.Where(t => t.Status == s);

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<TransactionType>(type, true, out var tp))
            query = query.Where(t => t.Type == tp);

        var result = await query
            .OrderByDescending(t => t.DueDate)
            .Select(t => new TransactionDto(
                t.Id, t.Name, t.Description,
                t.Type.ToString(), t.Status.ToString(),
                t.Amount, t.DueDate, t.PaidAt,
                t.Category != null ? t.Category.Name : null,
                t.BankAccount != null ? t.BankAccount.Name : null
            ))
            .ToListAsync();

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTransactionDto dto)
    {
        if (!Enum.TryParse<TransactionType>(dto.Type, true, out var type))
            return BadRequest(new { message = "Tipo inválido. Use 'Income' ou 'Expense'." });

        if (!Enum.TryParse<TransactionStatus>(dto.Status, true, out var status))
            return BadRequest(new { message = "Status inválido. Use 'Pending', 'Paid' ou 'Overdue'." });

        if (!await OwnsReferencesAsync(dto.BankAccountId, dto.CategoryId))
            return BadRequest(new { message = "Conta bancária ou categoria inválida." });

        var transaction = new Transaction
        {
            UserId = UserId,
            Name = dto.Name,
            Description = dto.Description,
            Type = type,
            Status = status,
            Amount = dto.Amount,
            DueDate = dto.DueDate,
            BankAccountId = dto.BankAccountId,
            CategoryId = dto.CategoryId,
            PaidAt = status == TransactionStatus.Paid ? DateTime.UtcNow : null
        };

        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), new { id = transaction.Id }, TransactionDto.FromEntity(transaction));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateTransactionDto dto)
    {
        var transaction = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId);
        if (transaction is null) return NotFound();

        if (!Enum.TryParse<TransactionType>(dto.Type, true, out var type))
            return BadRequest(new { message = "Tipo inválido." });

        if (!Enum.TryParse<TransactionStatus>(dto.Status, true, out var status))
            return BadRequest(new { message = "Status inválido." });

        if (!await OwnsReferencesAsync(dto.BankAccountId, dto.CategoryId))
            return BadRequest(new { message = "Conta bancária ou categoria inválida." });

        transaction.Name = dto.Name;
        transaction.Description = dto.Description;
        transaction.Type = type;
        transaction.Status = status;
        transaction.Amount = dto.Amount;
        transaction.DueDate = dto.DueDate;
        transaction.BankAccountId = dto.BankAccountId;
        transaction.CategoryId = dto.CategoryId;
        transaction.PaidAt = status == TransactionStatus.Paid ? DateTime.UtcNow : null;

        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var transaction = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId);
        if (transaction is null) return NotFound();

        db.Transactions.Remove(transaction);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Impede que um usuário associe a transação a uma conta bancária ou categoria de outra pessoa
    // (BOLA/IDOR) — os ids vêm do corpo da requisição e não podiam ser confiados sem essa checagem.
    private async Task<bool> OwnsReferencesAsync(Guid? bankAccountId, Guid? categoryId)
    {
        if (bankAccountId.HasValue &&
            !await db.BankAccounts.AnyAsync(b => b.Id == bankAccountId && b.UserId == UserId))
            return false;

        if (categoryId.HasValue &&
            !await db.Categories.AnyAsync(c => c.Id == categoryId && c.UserId == UserId))
            return false;

        return true;
    }
}
