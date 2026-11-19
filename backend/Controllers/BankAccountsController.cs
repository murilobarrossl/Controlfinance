using ControlFinance.API.Controllers.Filters;
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
            .AsNoTracking()
            .Where(b => b.UserId == UserId && b.IsActive)
            .ToListAsync();

        return Ok(accounts.Select(BankAccountDto.FromEntity));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var account = await db.BankAccounts
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == UserId);

        if (account is null) return NotFound();

        return Ok(BankAccountDto.FromEntity(account));
    }

    // Contas e cartões só nascem e mudam pelo sync da Polp: não há criação nem edição manual de
    // nome/saldo. A única correção do usuário é o chute pessoal/empresa/mista.
    [HttpPut("{id}/ownership")]
    public async Task<IActionResult> SetOwnership(Guid id, [FromBody] SetAccountOwnershipDto dto)
    {
        if (!Enum.TryParse<AccountOwnership>(dto.Ownership, true, out var ownership) || !Enum.IsDefined(ownership))
            return BadRequest(new { message = "Ownership inválido. Use 'Personal', 'Business' ou 'Mixed'." });

        var account = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.UserId == UserId);
        if (account is null) return NotFound();

        account.Ownership = ownership;
        await db.SaveChangesAsync();
        return Ok(BankAccountDto.FromEntity(account));
    }

    [HttpPut("{id:guid}/default")]
    [BusinessHolderOnly]
    public async Task<IActionResult> SetDefault(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DefaultBankAccounts.LockUserAsync(db, UserId, ct);

        var account = await db.BankAccounts.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == UserId && b.IsActive && b.PolpAccountId != null, ct);
        if (account is null || CreditCardAccountDetector.LooksLikeCreditCard(account))
            return NotFound();
        if (!await db.PolpIntegrations.AnyAsync(p => p.UserId == UserId && p.PolpIntegrationId == account.PolpIntegrationId && p.Status == "AUTHORISED", ct))
            return Conflict(new { message = "Só uma conta de banco conectado pode ser a conta padrão." });

        await db.BankAccounts.Where(b => b.UserId == UserId && b.IsDefault && b.Id != id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsDefault, false), ct);
        var updated = await db.BankAccounts.Where(b => b.Id == id && b.UserId == UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsDefault, true), ct);
        if (updated != 1) return NotFound();
        await DefaultBankAccounts.ClearPendingAsync(db, UserId, ct);

        await tx.CommitAsync(ct);
        return NoContent();
    }

    [HttpDelete("default")]
    [BusinessHolderOnly]
    public async Task<IActionResult> ClearDefault(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DefaultBankAccounts.LockUserAsync(db, UserId, ct);
        await db.BankAccounts.Where(b => b.UserId == UserId && b.IsDefault)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsDefault, false), ct);
        await DefaultBankAccounts.ClearPendingAsync(db, UserId, ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    // Remove uma conta cadastrada manualmente, junto com as transações e parcelamentos dela.
    // Antes era só IsActive = false (o extrato inteiro continuava guardado). Conta que veio de
    // um banco conectado não sai por aqui: ela é parte de uma conexão (conta corrente + cartão
    // do mesmo banco), que só pode ser encerrada inteira em DELETE /api/polp/integrations/{id},
    // onde também é pedida a exclusão na Polp. Apagar só a linha local deixaria a autorização
    // no fornecedor ativa e o próximo sync recriaria a conta.
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var account = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.UserId == UserId, ct);
        if (account is null) return NotFound();

        if (account.PolpIntegrationId.HasValue)
            return Conflict(new { message = "Esta conta veio de um banco conectado. Para removê-la, desconecte o banco." });

        await PolpDisconnectService.DeleteAccountsDataAsync(db, UserId, [account.Id], ct);
        return NoContent();
    }
}
