using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/installments")]
[Authorize]
public class InstallmentsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        // Não filtra pelo IsActive do cartão vinculado: a exclusão de cartão é soft delete
        // (CreditCardsController.Delete só marca IsActive = false, nunca apaga a linha), então
        // uma parcela presa a um cartão "removido" continua com CreditCardId real e precisa
        // continuar aparecendo aqui, senão o valor simplesmente some da tela sem explicação.
        var installments = await db.Installments
            .Include(i => i.CreditCard)
            .Include(i => i.BankAccount)
            .Where(i => i.UserId == UserId)
            .OrderBy(i => i.NextDueDate)
            .ToListAsync();

        var active = await InstallmentProgress.ReconcileAsync(db, installments);

        return Ok(active.OrderBy(i => i.NextDueDate).Select(InstallmentDto.FromEntity));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInstallmentDto dto)
    {
        if (dto.CreditCardId.HasValue && dto.BankAccountId.HasValue)
            return BadRequest(new { message = "Escolha um cartão manual ou uma conta reconhecida, não os dois." });

        if (dto.CurrentInstallment > dto.TotalInstallments)
            return BadRequest(new { message = "Parcela atual não pode ser maior que o total de parcelas." });

        // Valor ainda comprometido no limite do cartão: as parcelas que faltam pagar, não só a
        // próxima. Uma compra de 12x de R$100 bloqueia ~R$1200 do limite, não R$100.
        var remainingInstallments = dto.TotalInstallments - dto.CurrentInstallment + 1;
        var remainingCommitted = dto.InstallmentAmount * remainingInstallments;

        CreditCard? card = null;
        BankAccount? account = null;

        if (dto.CreditCardId.HasValue)
        {
            card = await db.CreditCards.FirstOrDefaultAsync(c => c.Id == dto.CreditCardId && c.UserId == UserId && c.IsActive);
            if (card is null)
                return BadRequest(new { message = "Cartão inválido." });

            if (card.UsedLimit + remainingCommitted > card.CreditLimit)
                return BadRequest(new { message = "Essa parcela ultrapassa o limite disponível do cartão." });
        }
        else if (dto.BankAccountId.HasValue)
        {
            account = await db.BankAccounts.FirstOrDefaultAsync(a => a.Id == dto.BankAccountId && a.UserId == UserId && a.IsActive);
            if (account is null || !CreditCardAccountDetector.LooksLikeCreditCard(account))
                return BadRequest(new { message = "Conta inválida." });

            // Sem limite conhecido ainda (Polp não mandou, ou o usuário não completou) não dá pra
            // validar — deixa passar em vez de bloquear por um limite que a gente nem sabe qual é.
            if (account.CreditLimit.HasValue && (account.UsedLimit ?? 0) + remainingCommitted > account.CreditLimit)
                return BadRequest(new { message = "Essa parcela ultrapassa o limite disponível do cartão." });
        }

        var installment = new Installment
        {
            CreditCardId = dto.CreditCardId,
            BankAccountId = dto.BankAccountId,
            UserId = UserId,
            Description = dto.Description,
            TotalAmount = dto.TotalAmount,
            TotalInstallments = dto.TotalInstallments,
            CurrentInstallment = dto.CurrentInstallment,
            InstallmentAmount = dto.InstallmentAmount,
            // Mesma questão do TransactionsController: data sem timezone vem com Kind=Unspecified,
            // e o Npgsql exige Utc pra timestamptz.
            NextDueDate = DateTime.SpecifyKind(dto.NextDueDate, DateTimeKind.Utc)
        };

        if (card is not null)
            card.UsedLimit += remainingCommitted;
        if (account is not null)
            account.UsedLimit = (account.UsedLimit ?? 0) + remainingCommitted;

        db.Installments.Add(installment);
        await db.SaveChangesAsync();

        installment.CreditCard = card;
        installment.BankAccount = account;
        return CreatedAtAction(nameof(GetAll), null, InstallmentDto.FromEntity(installment));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var installment = await db.Installments
            .Include(i => i.CreditCard)
            .Include(i => i.BankAccount)
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == UserId);
        if (installment is null) return NotFound();

        var remainingInstallments = installment.TotalInstallments - installment.CurrentInstallment + 1;
        var remainingCommitted = installment.InstallmentAmount * remainingInstallments;

        if (installment.CreditCard is not null)
            installment.CreditCard.UsedLimit = Math.Max(0, installment.CreditCard.UsedLimit - remainingCommitted);
        if (installment.BankAccount is { UsedLimit: not null } account)
            account.UsedLimit = Math.Max(0, account.UsedLimit.Value - remainingCommitted);

        db.Installments.Remove(installment);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
