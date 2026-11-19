using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/planning")]
[Authorize]
public class PlanningController(AppDbContext db) : ApiControllerBase
{
    private const int MaxGoals = 100;
    private const int MaxNameLength = 100;
    private const decimal MaxAmount = 999_999_999_999.99m;

    [HttpGet]
    [EnableRateLimiting(InvestmentReadRateLimit.Policy)]
    [InvestmentReadMetrics]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await LoadAsync(ct));

    [HttpPut("goals/{id:guid}/allocations")]
    public async Task<IActionResult> SetGoalAllocation(Guid id, AllocationChangeDto dto, CancellationToken ct)
    {
        if (!await db.SavingsGoals.AnyAsync(g => g.Id == id && g.UserId == UserId, ct))
            return NotFound(new { message = "Meta não encontrada." });
        if (dto.InvestmentId.HasValue == dto.ReservedBalanceId.HasValue)
            return BadRequest(new { message = "Selecione um investimento ou uma caixinha." });
        var resource = await FindResourceAsync(dto, ct);
        var allocation = await FindAllocationAsync(dto, ct);
        if (resource is null && dto.Enabled) return NotFound(new { message = "Investimento não encontrado." });
        if (dto.Enabled)
        {
            if (allocation is not null && allocation.GoalId != id)
                return allocation.GoalId is { } other ? await AllocatedToGoalAsync(other, ct) : InReserve();
            if (allocation is null)
            {
                // Caixinha identificada como reserva pelo nome já conta na reserva: numa meta contaria duas vezes.
                if (resource!.Auto) return InReserve();
                db.PlanningAllocations.Add(NewAllocation(dto, id));
            }
        }
        else if (allocation is not null && allocation.GoalId == id)
        {
            db.PlanningAllocations.Remove(allocation);
        }

        return await SaveAllocationAsync(ct);
    }

    [HttpPut("investments/{accountId:guid}/rate")]
    public async Task<IActionResult> SetInvestmentRate(Guid accountId, SetInvestmentRateDto dto, CancellationToken ct)
    {
        if (!InvestmentRateKinds.IsValid(dto.Kind))
            return BadRequest(new { message = "Tipo de rendimento inválido." });
        if (RateRangeError(dto.Kind, dto.Value) is { } error)
            return BadRequest(new { message = error });
        if (!await IsOwnInvestmentAccountAsync(accountId, ct))
            return NotFound(new { message = "Conta não encontrada." });

        var profile = await db.InvestmentRateProfiles.FirstOrDefaultAsync(p => p.BankAccountId == accountId && p.UserId == UserId, ct);
        if (profile is null)
        {
            profile = new InvestmentRateProfile { BankAccountId = accountId, UserId = UserId };
            db.InvestmentRateProfiles.Add(profile);
        }

        profile.Kind = dto.Kind;
        profile.Value = dto.Value;
        profile.IncomeTaxExempt = dto.IncomeTaxExempt;
        profile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(InvestmentRateProfileDto.FromEntity(profile));
    }

    [HttpDelete("investments/{accountId:guid}/rate")]
    public async Task<IActionResult> DeleteInvestmentRate(Guid accountId, CancellationToken ct)
    {
        await db.InvestmentRateProfiles.Where(p => p.BankAccountId == accountId && p.UserId == UserId).ExecuteDeleteAsync(ct);
        return NoContent();
    }

    [HttpPost("goals")]
    public async Task<IActionResult> CreateGoal(CreateSavingsGoalDto dto, CancellationToken ct)
    {
        var name = dto.Name.Trim();
        if (name.Length == 0)
            return BadRequest(new { message = "Nome da meta é obrigatório." });

        if (await db.SavingsGoals.CountAsync(g => g.UserId == UserId, ct) >= MaxGoals)
            return BadRequest(new { message = $"Limite de {MaxGoals} metas atingido." });

        var goal = new SavingsGoal
        {
            UserId = UserId,
            Name = name,
            TargetAmount = dto.TargetAmount,
            Deadline = dto.Deadline,
        };
        db.SavingsGoals.Add(goal);
        await db.SaveChangesAsync(ct);
        return Ok(new SavingsGoalDto(goal.Id, goal.Name, goal.TargetAmount, goal.Deadline, goal.CreatedAt, [], []));
    }

    [HttpDelete("goals/{id:guid}")]
    public async Task<IActionResult> DeleteGoal(Guid id, CancellationToken ct)
    {
        var deleted = await db.SavingsGoals.Where(g => g.Id == id && g.UserId == UserId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? NotFound(new { message = "Meta não encontrada." }) : NoContent();
    }

    // Leva para a conta as metas que estavam só no localStorage antes desta tabela existir. Nunca
    // sobrescreve: uma meta só entra se o id dela ainda não existe. O valor "já guardado" digitado no
    // navegador não vem junto, porque o progresso agora sai só dos investimentos vinculados. O cliente
    // apaga do navegador apenas o que esta resposta confirma como guardado.
    [HttpPost("import")]
    public async Task<IActionResult> ImportLegacy(ImportLegacyPlanningDto dto, CancellationToken ct)
    {
        var legacyGoals = (dto.Goals ?? []).Where(g => g.Id != Guid.Empty).DistinctBy(g => g.Id).ToList();
        var legacyIds = legacyGoals.Select(g => g.Id).ToList();
        var takenIds = (await db.SavingsGoals.Where(g => legacyIds.Contains(g.Id)).Select(g => g.Id).ToListAsync(ct)).ToHashSet();
        var count = await db.SavingsGoals.CountAsync(g => g.UserId == UserId, ct);

        foreach (var legacy in legacyGoals)
        {
            if (takenIds.Contains(legacy.Id) || count >= MaxGoals) continue;

            var name = (legacy.Name ?? string.Empty).Trim();
            if (name.Length == 0 || legacy.TargetAmount <= 0 || legacy.TargetAmount > MaxAmount) continue;

            db.SavingsGoals.Add(new SavingsGoal
            {
                Id = legacy.Id,
                UserId = UserId,
                Name = name.Length > MaxNameLength ? name[..MaxNameLength] : name,
                TargetAmount = legacy.TargetAmount,
                Deadline = legacy.Deadline,
                CreatedAt = legacy.CreatedAt?.ToUniversalTime() ?? DateTime.UtcNow,
            });
            count++;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DatabaseFailure.IsUniqueViolation(ex))
        {
            // Duas abas importando ao mesmo tempo: a outra já gravou. Nada se perde, o cliente só
            // apaga do navegador o que aparece como guardado na resposta.
            db.ChangeTracker.Clear();
        }

        var planning = await LoadAsync(ct);
        var stored = planning.Goals.Select(g => g.Id).Where(legacyIds.Contains).ToList();
        return Ok(new ImportLegacyPlanningResultDto(planning, stored));
    }

    private sealed record PlanningResource(Guid Id, bool Auto);

    // Só recursos do próprio usuário, de conexões que não estão sendo desfeitas.
    private async Task<PlanningResource?> FindResourceAsync(AllocationChangeDto dto, CancellationToken ct)
    {
        if (dto.InvestmentId.HasValue == dto.ReservedBalanceId.HasValue) return null;
        var id = (dto.InvestmentId ?? dto.ReservedBalanceId)!.Value;
        var names = dto.InvestmentId.HasValue
            ? await db.ImportedInvestments.Where(x => x.Id == id && x.UserId == UserId
                && db.PolpIntegrations.Any(p => p.Id == x.IntegrationId && p.UserId == UserId && p.Status != PolpDisconnectService.PendingStatus && !PolpAuthorizationEnd.EndedStatuses.Contains(p.Status)))
                .Select(x => x.Name).ToListAsync(ct)
            : await db.ImportedReservedBalances.Where(x => x.Id == id && x.UserId == UserId
                && db.PolpIntegrations.Any(p => p.Id == x.IntegrationId && p.UserId == UserId && p.Status != PolpDisconnectService.PendingStatus && !PolpAuthorizationEnd.EndedStatuses.Contains(p.Status)))
                .Select(x => x.Name).ToListAsync(ct);
        return names.Count == 0 ? null : new PlanningResource(id, EmergencyReserveNames.IsAuto(names[0], dto.ReservedBalanceId.HasValue));
    }

    private Task<PlanningAllocation?> FindAllocationAsync(AllocationChangeDto dto, CancellationToken ct) =>
        dto.InvestmentId is { } investmentId
            ? db.PlanningAllocations.SingleOrDefaultAsync(a => a.UserId == UserId && a.ImportedInvestmentId == investmentId, ct)
            : db.PlanningAllocations.SingleOrDefaultAsync(a => a.UserId == UserId && a.ImportedReservedBalanceId == dto.ReservedBalanceId, ct);

    private PlanningAllocation NewAllocation(AllocationChangeDto dto, Guid? goalId) => new()
    {
        UserId = UserId,
        GoalId = goalId,
        ImportedInvestmentId = dto.InvestmentId,
        ImportedReservedBalanceId = dto.ReservedBalanceId,
    };

    private async Task<IActionResult> AllocatedToGoalAsync(Guid goalId, CancellationToken ct)
    {
        var name = await db.SavingsGoals.Where(g => g.Id == goalId && g.UserId == UserId).Select(g => g.Name).FirstOrDefaultAsync(ct);
        return Conflict(new { message = name is null ? "Este investimento já está vinculado a uma meta." : $"Este investimento já está vinculado à meta “{name}”." });
    }

    private ConflictObjectResult InReserve() => Conflict(new { message = "Este investimento já faz parte da reserva de emergência." });

    private async Task<IActionResult> SaveAllocationAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DatabaseFailure.IsUniqueViolation(ex))
        {
            // O índice único por recurso barrou duas abas vinculando o mesmo investimento ao mesmo tempo.
            return Conflict(new { message = "Este investimento acabou de ser vinculado em outro lugar. Atualize a página." });
        }
        return Ok(await LoadAsync(ct));
    }

    // Cartão é BankAccount também, mas não guarda dinheiro: não pode virar reserva nem ter taxa.
    private async Task<bool> IsOwnInvestmentAccountAsync(Guid accountId, CancellationToken ct)
    {
        var account = await db.BankAccounts
            .AsNoTracking()
            .Where(b => b.Id == accountId && b.UserId == UserId && b.IsActive)
            .Select(b => new { b.PolpAccountType, b.PolpAccountSubtype })
            .FirstOrDefaultAsync(ct);
        return account is not null && !CreditCardAccountDetector.LooksLikeCreditCard(account.PolpAccountType, account.PolpAccountSubtype);
    }

    private static string? RateRangeError(string kind, decimal value) => kind switch
    {
        InvestmentRateKinds.CdiPercent when value is <= 0 or > 300 => "Informe um percentual do CDI entre 0 e 300.",
        InvestmentRateKinds.Prefixed when value is <= 0 or > 100 => "Informe uma taxa prefixada entre 0 e 100% ao ano.",
        InvestmentRateKinds.SelicSpread when value is < -5 or > 10 => "Informe um adicional sobre a Selic entre -5 e 10 pontos ao ano.",
        _ => null,
    };

    private async Task<PlanningDto> LoadAsync(CancellationToken ct)
    {
        var goals = await db.SavingsGoals
            .AsNoTracking()
            .Where(g => g.UserId == UserId)
            .OrderByDescending(g => g.CreatedAt)
            .ToListAsync(ct);
        var allocations = await db.PlanningAllocations.AsNoTracking().Where(a => a.UserId == UserId).ToListAsync(ct);
        var rateProfiles = await db.InvestmentRateProfiles
            .AsNoTracking()
            .Where(p => p.UserId == UserId)
            .ToListAsync(ct);

        // Caixinha vinculada a uma meta antes da regra automática fica na meta: o vínculo diz a finalidade.
        var inGoals = allocations.Where(a => a.GoalId != null && a.ImportedReservedBalanceId != null)
            .Select(a => a.ImportedReservedBalanceId!.Value).ToHashSet();
        var balances = await db.ImportedReservedBalances.AsNoTracking().Where(x => x.UserId == UserId
                && db.PolpIntegrations.Any(p => p.Id == x.IntegrationId && p.UserId == UserId && p.Status != PolpDisconnectService.PendingStatus && !PolpAuthorizationEnd.EndedStatuses.Contains(p.Status)))
            .OrderBy(x => x.FirstObservedAt).ThenBy(x => x.Id).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        var reserveIds = balances.Where(b => !inGoals.Contains(b.Id) && EmergencyReserveNames.IsAuto(b.Name, true)).Select(b => b.Id).ToList();

        List<Guid> Investments(Guid? goalId) => allocations.Where(a => a.GoalId == goalId && a.ImportedInvestmentId != null)
            .OrderBy(a => a.CreatedAt).Select(a => a.ImportedInvestmentId!.Value).ToList();
        List<Guid> Balances(Guid? goalId) => allocations.Where(a => a.GoalId == goalId && a.ImportedReservedBalanceId != null)
            .OrderBy(a => a.CreatedAt).Select(a => a.ImportedReservedBalanceId!.Value).ToList();

        return new PlanningDto(
            new EmergencyReserveDto(reserveIds),
            goals.Select(g => new SavingsGoalDto(g.Id, g.Name, g.TargetAmount, g.Deadline, g.CreatedAt, Investments(g.Id), Balances(g.Id))).ToList(),
            rateProfiles.Select(InvestmentRateProfileDto.FromEntity).ToList());
    }
}
