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

    // Cartões reconhecidos do usuário: contas da Polp identificadas como cartão (com ou sem
    // limite/fatura cadastrado ainda) + cartões 100% manuais — alimenta a área de Cartões, que
    // lista todos. Ver CreditCardSummaryBuilder.BuildRecognizedListAsync.
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct) =>
        Ok(await CreditCardSummaryBuilder.BuildRecognizedListAsync(db, UserId, ct));

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

    // Name e BankAccountId não são editáveis aqui de propósito: quando o cartão vem de uma conta
    // reconhecida da Polp, a identidade (nome/vínculo) é real e não deve ser sobrescrita à mão —
    // só os campos que a Polp não manda ficam abertos pra completar/corrigir.
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCreditCardDto dto)
    {
        var card = await db.CreditCards
            .Include(c => c.BankAccount)
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == UserId && c.IsActive);
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
