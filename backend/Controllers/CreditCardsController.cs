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
    // Só cartões 100% manuais (nunca conectados por nenhum banco) — usado hoje só como fallback
    // pro formulário de Orçamento quando o usuário quer digitar um cartão do zero. Cartões
    // reconhecidos da Polp entram só pelo /summary abaixo.
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var cards = await db.CreditCards
            .Where(c => c.UserId == UserId && c.IsActive)
            .ToListAsync();

        return Ok(cards.Select(CreditCardDto.FromEntity));
    }

    // Cartões reconhecidos do usuário: contas da Polp identificadas como cartão (com ou sem
    // limite/fatura cadastrado ainda) + cartões 100% manuais — alimenta a área de Cartões, que
    // lista todos. Ver CreditCardSummaryBuilder.BuildRecognizedListAsync.
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct) =>
        Ok(await CreditCardSummaryBuilder.BuildRecognizedListAsync(db, UserId, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCreditCardDto dto)
    {
        var card = new CreditCard
        {
            UserId = UserId,
            Name = dto.Name,
            Brand = dto.Brand,
            CreditLimit = dto.CreditLimit,
            UsedLimit = 0,
            ClosingDay = dto.ClosingDay,
            DueDay = dto.DueDay
        };

        db.CreditCards.Add(card);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), new { id = card.Id }, CreditCardDto.FromEntity(card));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCreditCardDto dto)
    {
        var card = await db.CreditCards.FirstOrDefaultAsync(c => c.Id == id && c.UserId == UserId && c.IsActive);
        if (card is null) return NotFound();

        card.Brand = dto.Brand;
        card.CreditLimit = dto.CreditLimit;
        card.ClosingDay = dto.ClosingDay;
        card.DueDay = dto.DueDay;
        await db.SaveChangesAsync();

        return Ok(CreditCardDto.FromEntity(card));
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
