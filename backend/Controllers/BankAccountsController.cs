using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
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
            .Select(b => new BankAccountDto(b.Id, b.Name, b.BankCode, b.Balance, b.IsActive, b.Ownership.ToString()))
            .ToListAsync();

        return Ok(accounts);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var account = await db.BankAccounts
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == UserId);

        if (account is null) return NotFound();

        return Ok(new BankAccountDto(account.Id, account.Name, account.BankCode, account.Balance, account.IsActive, account.Ownership.ToString()));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBankAccountDto dto)
    {
        // Mesmo chute inicial que o sync da Polp usa pra conta nova: CNPJ vira Business, CPF
        // vira Personal (ver AccountOwnershipDefault). Contas criadas manualmente (fora do fluxo
        // da Polp) não podiam ficar de fora desse default.
        var document = await db.Users.Where(u => u.Id == UserId).Select(u => u.Document).FirstAsync();

        var account = new BankAccount
        {
            UserId = UserId,
            Name = dto.Name,
            BankCode = dto.BankCode,
            Balance = dto.Balance,
            Ownership = AccountOwnershipDefault.FromDocument(document)
        };

        db.BankAccounts.Add(account);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = account.Id },
            new BankAccountDto(account.Id, account.Name, account.BankCode, account.Balance, account.IsActive, account.Ownership.ToString()));
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
        return Ok(new BankAccountDto(account.Id, account.Name, account.BankCode, account.Balance, account.IsActive, account.Ownership.ToString()));
    }

    // Endpoint dedicado só pra corrigir o chute pessoal/empresa/mista: reaproveitar o Update
    // genérico exigiria mandar nome/código/saldo de novo só pra mudar uma tag, igual o SetFixed
    // de TransactionsController já evita pra IsFixed.
    [HttpPut("{id}/ownership")]
    public async Task<IActionResult> SetOwnership(Guid id, [FromBody] SetAccountOwnershipDto dto)
    {
        if (!Enum.TryParse<AccountOwnership>(dto.Ownership, true, out var ownership))
            return BadRequest(new { message = "Ownership inválido. Use 'Personal', 'Business' ou 'Mixed'." });

        var account = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.UserId == UserId);
        if (account is null) return NotFound();

        account.Ownership = ownership;
        await db.SaveChangesAsync();
        return Ok(new BankAccountDto(account.Id, account.Name, account.BankCode, account.Balance, account.IsActive, account.Ownership.ToString()));
    }

    // Só completa campos de cartão que a Polp não mandou (ficaram null) — nunca sobrescreve um
    // valor que já veio real da sincronização. Pra uma conta que a Polp não reconheceu como
    // cartão, esses campos continuam sem uso nenhum, mas nada impede de gravar (ex.: uma conta
    // que a heurística de tipo ainda não pegou).
    [HttpPut("{id}/card-details")]
    public async Task<IActionResult> SetCardDetails(Guid id, [FromBody] UpdateBankAccountCardDetailsDto dto)
    {
        var account = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.UserId == UserId && b.IsActive);
        if (account is null) return NotFound();

        if (!account.CreditLimit.HasValue) account.CreditLimit = dto.CreditLimit;
        if (!account.ClosingDay.HasValue) account.ClosingDay = dto.ClosingDay;
        if (!account.DueDay.HasValue) account.DueDay = dto.DueDay;

        await db.SaveChangesAsync();
        return Ok(await CreditCardSummaryBuilder.BuildRecognizedListAsync(db, UserId));
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
