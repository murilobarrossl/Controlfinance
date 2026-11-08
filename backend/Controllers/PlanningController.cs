using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await LoadAsync(ct));

    [HttpPost("reserve/add")]
    public Task<IActionResult> AddToReserve(ReserveAmountDto dto, CancellationToken ct) =>
        UpdateReserveAsync(current => Math.Min(MaxAmount, current + dto.Amount), ct);

    [HttpPost("reserve/withdraw")]
    public Task<IActionResult> WithdrawFromReserve(ReserveAmountDto dto, CancellationToken ct) =>
        UpdateReserveAsync(current => Math.Max(0, current - dto.Amount), ct);

    [HttpPut("reserve")]
    public Task<IActionResult> SetReserve(ReserveAmountDto dto, CancellationToken ct) =>
        UpdateReserveAsync(_ => dto.Amount, ct);

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
            CurrentAmount = dto.CurrentAmount,
            Deadline = dto.Deadline,
        };
        db.SavingsGoals.Add(goal);
        await db.SaveChangesAsync(ct);
        return Ok(SavingsGoalDto.FromEntity(goal));
    }

    [HttpPut("goals/{id:guid}/progress")]
    public async Task<IActionResult> UpdateGoalProgress(Guid id, UpdateGoalProgressDto dto, CancellationToken ct)
    {
        var goal = await db.SavingsGoals.FirstOrDefaultAsync(g => g.Id == id && g.UserId == UserId, ct);
        if (goal is null) return NotFound(new { message = "Meta não encontrada." });

        goal.CurrentAmount = dto.CurrentAmount;
        goal.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(SavingsGoalDto.FromEntity(goal));
    }

    [HttpDelete("goals/{id:guid}")]
    public async Task<IActionResult> DeleteGoal(Guid id, CancellationToken ct)
    {
        var deleted = await db.SavingsGoals.Where(g => g.Id == id && g.UserId == UserId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? NotFound(new { message = "Meta não encontrada." }) : NoContent();
    }

    // Leva para a conta o que estava só no localStorage antes desta tabela existir. Nunca sobrescreve:
    // a reserva só é criada se a conta ainda não tem uma, e uma meta só entra se o id dela ainda não
    // existe. O cliente apaga do navegador apenas o que esta resposta confirma como guardado.
    [HttpPost("import")]
    public async Task<IActionResult> ImportLegacy(ImportLegacyPlanningDto dto, CancellationToken ct)
    {
        var reserveImported = false;
        if (dto.ReserveAmount is { } amount && amount >= 0 && amount <= MaxAmount
            && !await db.EmergencyReserves.AnyAsync(r => r.UserId == UserId, ct))
        {
            db.EmergencyReserves.Add(new EmergencyReserve { UserId = UserId, CurrentAmount = amount });
            reserveImported = true;
        }

        var legacyGoals = (dto.Goals ?? []).Where(g => g.Id != Guid.Empty).DistinctBy(g => g.Id).ToList();
        var legacyIds = legacyGoals.Select(g => g.Id).ToList();
        var existing = await db.SavingsGoals
            .Where(g => legacyIds.Contains(g.Id))
            .Select(g => new { g.Id, g.UserId })
            .ToListAsync(ct);
        var takenIds = existing.Select(e => e.Id).ToHashSet();
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
                CurrentAmount = Math.Clamp(legacy.CurrentAmount, 0, MaxAmount),
                Deadline = legacy.Deadline,
                CreatedAt = legacy.CreatedAt?.ToUniversalTime() ?? DateTime.UtcNow,
            });
            count++;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Duas abas importando ao mesmo tempo: a outra já gravou. Nada se perde, o cliente só
            // apaga do navegador o que aparece como guardado na resposta.
            db.ChangeTracker.Clear();
            reserveImported = false;
        }

        var planning = await LoadAsync(ct);
        var stored = planning.Goals.Select(g => g.Id).Where(legacyIds.Contains).ToList();
        return Ok(new ImportLegacyPlanningResultDto(planning, reserveImported, stored));
    }

    private async Task<IActionResult> UpdateReserveAsync(Func<decimal, decimal> next, CancellationToken ct)
    {
        var reserve = await db.EmergencyReserves.FirstOrDefaultAsync(r => r.UserId == UserId, ct);
        if (reserve is null)
        {
            reserve = new EmergencyReserve { UserId = UserId };
            db.EmergencyReserves.Add(reserve);
        }

        reserve.CurrentAmount = next(reserve.CurrentAmount);
        reserve.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(EmergencyReserveDto.FromEntity(reserve));
    }

    private async Task<PlanningDto> LoadAsync(CancellationToken ct)
    {
        var reserve = await db.EmergencyReserves.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == UserId, ct);
        var goals = await db.SavingsGoals
            .AsNoTracking()
            .Where(g => g.UserId == UserId)
            .OrderByDescending(g => g.CreatedAt)
            .ToListAsync(ct);

        return new PlanningDto(reserve is null ? null : EmergencyReserveDto.FromEntity(reserve), goals.Select(SavingsGoalDto.FromEntity).ToList());
    }
}
