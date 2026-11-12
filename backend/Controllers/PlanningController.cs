using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
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
        UpdateReserveAsync(r => r.CurrentAmount = Math.Min(MaxAmount, r.CurrentAmount + dto.Amount), ct);

    [HttpPost("reserve/withdraw")]
    public Task<IActionResult> WithdrawFromReserve(ReserveAmountDto dto, CancellationToken ct) =>
        UpdateReserveAsync(r => r.CurrentAmount = Math.Max(0, r.CurrentAmount - dto.Amount), ct);

    [HttpPut("reserve")]
    public Task<IActionResult> SetReserve(ReserveAmountDto dto, CancellationToken ct) =>
        UpdateReserveAsync(r => r.CurrentAmount = dto.Amount, ct);

    [HttpPut("reserve/account")]
    public async Task<IActionResult> SetReserveAccount(SetReserveAccountDto dto, CancellationToken ct)
    {
        if (dto.BankAccountId is { } accountId && !await IsOwnInvestmentAccountAsync(accountId, ct))
            return NotFound(new { message = "Conta não encontrada." });

        return await UpdateReserveAsync(reserve =>
        {
            reserve.BankAccountId = dto.BankAccountId;
            reserve.ImportedInvestmentId = null;
            reserve.ImportedReservedBalanceId = null;
        }, ct);
    }

    [HttpPut("reserve/imported")]
    public async Task<IActionResult> SetImportedReserve(SetImportedReserveDto dto, CancellationToken ct)
    {
        if (dto.InvestmentId.HasValue == dto.ReservedBalanceId.HasValue) return BadRequest(new { message = "Selecione um investimento ou uma caixinha." });
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var resourceId = (dto.InvestmentId ?? dto.ReservedBalanceId)!.Value;
        var names = dto.InvestmentId.HasValue
            ? await db.ImportedInvestments.Where(x => x.Id == resourceId && x.UserId == UserId
                && db.PolpIntegrations.Any(p => p.Id == x.IntegrationId && p.UserId == UserId && p.Status != PolpDisconnectService.PendingStatus))
                .Select(x => x.Name).ToListAsync(ct)
            : await db.ImportedReservedBalances.Where(x => x.Id == resourceId && x.UserId == UserId
                && db.PolpIntegrations.Any(p => p.Id == x.IntegrationId && p.UserId == UserId && p.Status != PolpDisconnectService.PendingStatus))
                .Select(x => x.Name).ToListAsync(ct);
        if (names.Count == 0) return NotFound(new { message = "Investimento não encontrado." });
        var reserve = await db.EmergencyReserves.SingleOrDefaultAsync(x => x.UserId == UserId, ct);
        if (reserve is null) { reserve = new EmergencyReserve { UserId = UserId }; db.EmergencyReserves.Add(reserve); }
        if (EmergencyReserveNames.IsAuto(names[0]))
        {
            EmergencyReserveNames.SetExcluded(reserve, resourceId, !dto.Enabled);
            if (!dto.Enabled && (reserve.ImportedInvestmentId == resourceId || reserve.ImportedReservedBalanceId == resourceId))
            {
                reserve.ImportedInvestmentId = null;
                reserve.ImportedReservedBalanceId = null;
            }
        }
        else if (dto.Enabled)
        {
            reserve.BankAccountId = null;
            reserve.ImportedInvestmentId = dto.InvestmentId;
            reserve.ImportedReservedBalanceId = dto.ReservedBalanceId;
        }
        else if ((dto.InvestmentId.HasValue && reserve.ImportedInvestmentId == dto.InvestmentId)
            || (dto.ReservedBalanceId.HasValue && reserve.ImportedReservedBalanceId == dto.ReservedBalanceId))
        {
            reserve.ImportedInvestmentId = null;
            reserve.ImportedReservedBalanceId = null;
        }
        reserve.UpdatedAt = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Ok(EmergencyReserveDto.FromEntity(reserve));
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "A classificação mudou. Atualize a página e tente novamente." });
        }
        catch (System.Data.Common.DbException)
        {
            return Conflict(new { message = "A classificação mudou. Atualize a página e tente novamente." });
        }
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

    private async Task<IActionResult> UpdateReserveAsync(Action<EmergencyReserve> apply, CancellationToken ct)
    {
        var reserve = await db.EmergencyReserves.FirstOrDefaultAsync(r => r.UserId == UserId, ct);
        if (reserve is null)
        {
            reserve = new EmergencyReserve { UserId = UserId };
            db.EmergencyReserves.Add(reserve);
        }

        apply(reserve);
        reserve.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(EmergencyReserveDto.FromEntity(reserve));
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
        var reserve = await db.EmergencyReserves.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == UserId, ct);
        var goals = await db.SavingsGoals
            .AsNoTracking()
            .Where(g => g.UserId == UserId)
            .OrderByDescending(g => g.CreatedAt)
            .ToListAsync(ct);

        var rateProfiles = await db.InvestmentRateProfiles
            .AsNoTracking()
            .Where(p => p.UserId == UserId)
            .ToListAsync(ct);

        return new PlanningDto(
            reserve is null ? null : EmergencyReserveDto.FromEntity(reserve),
            goals.Select(SavingsGoalDto.FromEntity).ToList(),
            rateProfiles.Select(InvestmentRateProfileDto.FromEntity).ToList());
    }
}
