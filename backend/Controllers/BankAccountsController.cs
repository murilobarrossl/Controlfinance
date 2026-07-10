using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/bank-accounts")]
[Authorize]
public class BankAccountsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var accounts = await db.BankAccounts
            .Where(b => b.UserId == UserId && b.IsActive)
            .Select(b => new BankAccountDto(b.Id, b.Name, b.BankCode, b.Balance, b.IsActive))
            .ToListAsync();

        return Ok(accounts);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var account = await db.BankAccounts
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == UserId);

        if (account is null) return NotFound();

        return Ok(new BankAccountDto(account.Id, account.Name, account.BankCode, account.Balance, account.IsActive));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBankAccountDto dto)
    {
        var account = new BankAccount
        {
            UserId = UserId,
            Name = dto.Name,
            BankCode = dto.BankCode,
            Balance = dto.Balance
        };

        db.BankAccounts.Add(account);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = account.Id },
            new BankAccountDto(account.Id, account.Name, account.BankCode, account.Balance, account.IsActive));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateBankAccountDto dto)
    {
        var account = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.UserId == UserId);
        if (account is null) return NotFound();

        account.Name = dto.Name;
        account.BankCode = dto.BankCode;
        account.Balance = dto.Balance;

        await db.SaveChangesAsync();
        return Ok(new BankAccountDto(account.Id, account.Name, account.BankCode, account.Balance, account.IsActive));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var account = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.UserId == UserId);
        if (account is null) return NotFound();

        account.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
