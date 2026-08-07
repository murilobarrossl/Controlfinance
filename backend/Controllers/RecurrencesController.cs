using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/recurrences")]
[Authorize]
public class RecurrencesController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var accounts = await db.BankAccounts
            .Where(b => b.UserId == UserId && b.IsActive)
            .ToDictionaryAsync(b => b.Id, b => b);

        var transactions = await db.Transactions
            .Where(t => t.UserId == UserId
                     && t.Type == TransactionType.Expense
                     && t.BankAccountId != null
                     && accounts.Keys.Contains(t.BankAccountId!.Value))
            .ToListAsync();

        var decisions = await db.RecurrenceDecisions
            .Where(d => d.UserId == UserId)
            .ToDictionaryAsync(d => (d.BankAccountId, d.NormalizedName), d => d);

        var now = DateTime.UtcNow;
        var detected = RecurrenceDetection.DetectGroups(transactions, now);
        var applied = RecurrenceDetection.ApplyDecisions(detected, transactions, decisions, now);

        // Custo anual ao lado do mensal é a ideia central da feature (ver comentário no DTO):
        // ordenar por ele já deixa a lista no formato que a tela vai querer (maior impacto primeiro).
        var result = applied
            .Select(g =>
            {
                var account = accounts[g.BankAccountId];
                var annualCost = g.Frequency == RecurrenceFrequency.Monthly ? g.CurrentAmount * 12 : g.CurrentAmount;

                return new RecurrenceDto(
                    g.NormalizedName, g.DisplayName, g.BankAccountId, account.Name, account.Ownership.ToString(),
                    g.Frequency.ToString(), g.CurrentAmount, annualCost, g.NextExpectedDate, g.DaysUntilNextCharge,
                    g.TotalSinceFirst, g.FirstOccurrence, g.OccurrenceCount,
                    g.SilentIncrease, g.IsNew, g.AnnualFeeUpcoming, g.IsManuallyConfirmed, g.ReminderRequested);
            })
            .OrderByDescending(r => r.AnnualCost)
            .ToList();

        return Ok(result);
    }

    // Upsert parcial: Status e ReminderRequested são independentes (ver comentário no DTO). Só
    // atualiza o que veio na requisição, preserva o resto da linha existente. A chave única em
    // RecurrenceDecisions (UserId, BankAccountId, NormalizedName) é quem garante que dispensar e
    // depois confirmar a mesma recorrência atualiza a linha existente em vez de duplicar.
    [HttpPost("decision")]
    public async Task<IActionResult> SetDecision([FromBody] SetRecurrenceDecisionDto dto)
    {
        var statusProvided = !string.IsNullOrEmpty(dto.Status);
        RecurrenceDecisionStatus? status = null;
        if (statusProvided)
        {
            if (!Enum.TryParse<RecurrenceDecisionStatus>(dto.Status, true, out var parsedStatus))
                return BadRequest(new { message = "Status inválido. Use 'Confirmed' ou 'Dismissed'." });
            status = parsedStatus;
        }

        RecurrenceFrequency? assumedFrequency = null;
        if (!string.IsNullOrEmpty(dto.AssumedFrequency))
        {
            if (!Enum.TryParse<RecurrenceFrequency>(dto.AssumedFrequency, true, out var frequency))
                return BadRequest(new { message = "AssumedFrequency inválida. Use 'Monthly' ou 'Yearly'." });
            assumedFrequency = frequency;
        }

        var accountExists = await db.BankAccounts.AnyAsync(b => b.Id == dto.BankAccountId && b.UserId == UserId);
        if (!accountExists) return BadRequest(new { message = "Conta bancária inválida." });

        var normalizedName = RecurrenceDetection.Normalize(dto.Name);

        var decision = await db.RecurrenceDecisions.FirstOrDefaultAsync(d =>
            d.UserId == UserId && d.BankAccountId == dto.BankAccountId && d.NormalizedName == normalizedName);

        // Dois casos precisam saber se a detecção automática já pegou essa recorrência sozinha:
        // confirmar com pouco histórico (aí a frequência assumida é obrigatória) e pedir lembrete
        // sem confirmar/dispensar nada (não dá pra lembrar de uma cobrança que não existe pro
        // sistema ainda). Busca uma vez só e reaproveita nos dois, em vez de duas idas ao banco.
        var needsDetectionCheck =
            (status == RecurrenceDecisionStatus.Confirmed && assumedFrequency is null) ||
            (!statusProvided && dto.ReminderRequested == true && decision?.Status != RecurrenceDecisionStatus.Confirmed);

        if (needsDetectionCheck)
        {
            var transactions = await db.Transactions
                .Where(t => t.UserId == UserId && t.BankAccountId == dto.BankAccountId && t.Type == TransactionType.Expense)
                .ToListAsync();
            var alreadyDetected = RecurrenceDetection.DetectGroups(transactions, DateTime.UtcNow)
                .Any(g => g.NormalizedName == normalizedName);

            if (!alreadyDetected)
            {
                if (status == RecurrenceDecisionStatus.Confirmed)
                    return BadRequest(new
                    {
                        message = "Informe assumedFrequency ('Monthly' ou 'Yearly') pra confirmar uma recorrência que ainda não foi detectada automaticamente."
                    });

                return BadRequest(new
                {
                    message = "Confirme que isso é uma recorrência (status e, se preciso, assumedFrequency) antes de pedir lembrete."
                });
            }
        }

        if (decision is null)
        {
            decision = new RecurrenceDecision
            {
                UserId = UserId,
                BankAccountId = dto.BankAccountId,
                NormalizedName = normalizedName
            };
            db.RecurrenceDecisions.Add(decision);
        }

        if (statusProvided)
        {
            decision.Status = status;
            decision.AssumedFrequency = assumedFrequency;
        }
        if (dto.ReminderRequested.HasValue)
            decision.ReminderRequested = dto.ReminderRequested.Value;

        decision.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok();
    }
}
