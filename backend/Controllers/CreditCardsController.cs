using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
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
            .Where(c => c.UserId == UserId && c.IsActive)
            .ToListAsync();

        return Ok(cards.Select(CreditCardDto.FromEntity));
    }

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
