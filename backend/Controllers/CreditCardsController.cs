using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/credit-cards")]
[Authorize]
public class CreditCardsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        // Materializa antes de fazer a conta: CreditLimit/UsedLimit são criptografados (guardados
        // como text no Postgres), e subtrair um do outro dentro do Select fazia o EF tentar
        // traduzir a subtração pra SQL direto na coluna de texto ("operator does not exist: text - text").
        var cards = await db.CreditCards
            .Include(c => c.BankAccount)
            .Where(c => c.UserId == UserId && c.IsActive)
            .ToListAsync();

        return Ok(cards.Select(CreditCardDto.FromEntity));
    }

    // Fatura/vencimento/parcelas ativas de cada cartão do usuário (não só o primeiro, como o
    // resumo do Dashboard Inteligente usa) — alimenta a área de Cartões, que lista todos.
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var cards = await db.CreditCards
            .Include(c => c.Installments)
            .Include(c => c.BankAccount)
            .Where(c => c.UserId == UserId && c.IsActive)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        var result = new List<CreditCardDashboardDto>();
        foreach (var card in cards)
            result.Add(await CreditCardSummaryBuilder.BuildAsync(db, card, ct));

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCreditCardDto dto)
    {
        if (dto.BankAccountId.HasValue)
        {
            var account = await db.BankAccounts.FirstOrDefaultAsync(
                a => a.Id == dto.BankAccountId && a.UserId == UserId && a.IsActive);
            if (account is null) return BadRequest("Conta bancária inválida.");

            var alreadyLinked = await db.CreditCards.AnyAsync(
                c => c.BankAccountId == dto.BankAccountId && c.IsActive);
            if (alreadyLinked) return BadRequest("Essa conta já está vinculada a outro cartão.");
        }

        var card = new CreditCard
        {
            UserId = UserId,
            Name = dto.Name,
            Brand = dto.Brand,
            CreditLimit = dto.CreditLimit,
            UsedLimit = 0,
            ClosingDay = dto.ClosingDay,
            DueDay = dto.DueDay,
            BankAccountId = dto.BankAccountId
        };

        db.CreditCards.Add(card);
        await db.SaveChangesAsync();

        // Recarrega com BankAccount incluído: sem isso, CreditCardDto.FromEntity devolveria
        // BankAccountName nulo mesmo quando um vínculo válido acabou de ser gravado.
        if (card.BankAccountId.HasValue) await db.Entry(card).Reference(c => c.BankAccount).LoadAsync();

        return CreatedAtAction(nameof(GetAll), new { id = card.Id }, CreditCardDto.FromEntity(card));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var card = await db.CreditCards.FirstOrDefaultAsync(c => c.Id == id && c.UserId == UserId);
        if (card is null) return NotFound();

        card.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
