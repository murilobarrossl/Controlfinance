using System.Security.Claims;
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
public class CreditCardsController(AppDbContext db) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var cards = await db.CreditCards
            .Where(c => c.UserId == UserId && c.IsActive)
            .Select(c => new CreditCardDto(
                c.Id, c.Name, c.Brand,
                c.CreditLimit, c.UsedLimit,
                c.CreditLimit - c.UsedLimit,
                c.ClosingDay, c.DueDay
            ))
            .ToListAsync();

        return Ok(cards);
    }

    [HttpGet("{id}/installments")]
    public async Task<IActionResult> GetInstallments(Guid id)
    {
        var card = await db.CreditCards.FirstOrDefaultAsync(c => c.Id == id && c.UserId == UserId);
        if (card is null) return NotFound();

        var installments = await db.Installments
            .Where(i => i.CreditCardId == id)
            .OrderBy(i => i.NextDueDate)
            .Select(i => new InstallmentDto(
                i.Id, i.Description, i.InstallmentAmount,
                i.CurrentInstallment, i.TotalInstallments, i.NextDueDate
            ))
            .ToListAsync();

        return Ok(installments);
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

        return CreatedAtAction(nameof(GetAll), new { id = card.Id },
            new CreditCardDto(card.Id, card.Name, card.Brand, card.CreditLimit, card.UsedLimit, card.CreditLimit, card.ClosingDay, card.DueDay));
    }

    [HttpPost("{id}/installments")]
    public async Task<IActionResult> AddInstallment(Guid id, [FromBody] CreateInstallmentDto dto)
    {
        var card = await db.CreditCards.FirstOrDefaultAsync(c => c.Id == id && c.UserId == UserId);
        if (card is null) return NotFound();

        var installment = new Installment
        {
            CreditCardId = id,
            UserId = UserId,
            Description = dto.Description,
            TotalAmount = dto.TotalAmount,
            TotalInstallments = dto.TotalInstallments,
            CurrentInstallment = dto.CurrentInstallment,
            InstallmentAmount = dto.InstallmentAmount,
            NextDueDate = dto.NextDueDate
        };

        card.UsedLimit += dto.InstallmentAmount;

        db.Installments.Add(installment);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetInstallments), new { id },
            new InstallmentDto(installment.Id, installment.Description, installment.InstallmentAmount,
                installment.CurrentInstallment, installment.TotalInstallments, installment.NextDueDate));
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
