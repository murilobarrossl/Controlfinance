using ControlFinance.API.Controllers.Filters;
using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

// Custos adicionais dos funcionários, com nome livre e valor opcional informado pela empresa.
[ApiController]
[Route("api/payroll")]
[Authorize]
[BusinessHolderOnly]
public class PayrollController(AppDbContext db) : ApiControllerBase
{
    // Funcionário cadastrado antes dos custos adicionais ganha os gastos iniciais na primeira abertura, sob a
    // trava do usuário e com a marca relida: duas abas abertas juntas nunca criam os gastos duas vezes.
    [HttpGet("employees/{employeeId:guid}")]
    public async Task<IActionResult> GetEmployee(Guid employeeId, CancellationToken ct)
    {
        var seeded = await db.Employees.Where(e => e.Id == employeeId && e.UserId == UserId)
            .Select(e => (bool?)e.CostsSeeded).FirstOrDefaultAsync(ct);
        if (seeded is null) return NotFound();
        if (seeded.Value) return Ok(await EmployeeViewAsync(employeeId, ct));
        return await WriteAsync(async () =>
        {
            var employee = await db.Employees.FirstAsync(e => e.Id == employeeId && e.UserId == UserId, ct);
            if (!employee.CostsSeeded)
            {
                EmployeeCostDefaults.Seed(db, employee);
                await db.SaveChangesAsync(ct);
            }
            return Ok(await EmployeeViewAsync(employeeId, ct));
        }, ct);
    }

    [HttpPost("employees/{employeeId:guid}/costs")]
    public Task<IActionResult> AddCost(Guid employeeId, [FromBody] EmployeeCostInputDto dto, CancellationToken ct) =>
        SaveCostAsync(employeeId, null, dto, ct);

    [HttpPut("employees/{employeeId:guid}/costs/{id:guid}")]
    public Task<IActionResult> UpdateCost(Guid employeeId, Guid id, [FromBody] EmployeeCostInputDto dto, CancellationToken ct) =>
        SaveCostAsync(employeeId, id, dto, ct);

    [HttpDelete("employees/{employeeId:guid}/costs/{id:guid}")]
    public Task<IActionResult> DeleteCost(Guid employeeId, Guid id, CancellationToken ct) =>
        WriteAsync(async () =>
        {
            var cost = await FindAsync(id, ct);
            if (cost is null || cost.EmployeeId != employeeId || cost.Kind != PayrollObligationKind.EmployeeCost) return NotFound();
            Remove(cost);
            await db.SaveChangesAsync(ct);
            return Ok(await EmployeeViewAsync(employeeId, ct));
        }, ct);

    private Task<IActionResult> SaveCostAsync(Guid employeeId, Guid? id, EmployeeCostInputDto dto, CancellationToken ct) =>
        WriteAsync(async () =>
        {
            if (!await db.Employees.AnyAsync(e => e.Id == employeeId && e.UserId == UserId, ct)) return NotFound();
            var name = string.Join(' ', (dto.Name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (name.Length is < 2 or > 80) return Invalid("Informe o nome do gasto (até 80 caracteres).");
            if (dto.Amount is { } amount && !PayrollObligations.IsValidAmount(amount))
                return Invalid("Informe um valor válido, com até duas casas decimais.");

            PayrollObligation? cost;
            if (id is { } existing)
            {
                cost = await FindAsync(existing, ct);
                if (cost is null || cost.EmployeeId != employeeId || cost.Kind != PayrollObligationKind.EmployeeCost) return NotFound();
                if (cost.Description == name && cost.Amount == dto.Amount) return Ok(await EmployeeViewAsync(employeeId, ct));
            }
            else
            {
                if (await db.PayrollObligations.CountAsync(o => o.UserId == UserId && o.EmployeeId == employeeId, ct) >= PayrollObligations.MaxCostsPerEmployee)
                    return Invalid($"Limite de {PayrollObligations.MaxCostsPerEmployee} gastos por funcionário.");
                cost = new PayrollObligation { UserId = UserId, EmployeeId = employeeId, Kind = PayrollObligationKind.EmployeeCost };
                db.PayrollObligations.Add(cost);
            }
            cost.Description = name;
            cost.Amount = dto.Amount;
            Touch(cost, id is null);
            await db.SaveChangesAsync(ct);
            return Ok(await EmployeeViewAsync(employeeId, ct));
        }, ct);

    // Toda escrita trava a linha do usuário para serializar alterações e respeitar o limite de gastos.
    private async Task<IActionResult> WriteAsync(Func<Task<IActionResult>> work, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DefaultBankAccounts.LockUserAsync(db, UserId, ct);
        IActionResult result;
        try
        {
            result = await work();
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return Conflict(new { message = "Já existe um registro igual. Atualize a página." });
        }
        if (result is IStatusCodeActionResult { StatusCode: >= 200 and < 300 }) await tx.CommitAsync(ct);
        return result;
    }

    private Task<PayrollObligation?> FindAsync(Guid id, CancellationToken ct) =>
        db.PayrollObligations.FirstOrDefaultAsync(o => o.Id == id && o.UserId == UserId, ct);

    private BadRequestObjectResult Invalid(string message) => BadRequest(new { message });

    private void Touch(PayrollObligation o, bool created)
    {
        o.UpdatedAt = DateTime.UtcNow;
        PayrollObligations.Audit(db, UserId, o, created ? "Created" : "Updated");
    }

    private void Remove(PayrollObligation o)
    {
        PayrollObligations.Audit(db, UserId, o, "Deleted");
        db.PayrollObligations.Remove(o);
    }

    // Ordem de criação: os gastos iniciais (13º salário, Férias) ficam no topo. Total só dos valores informados.
    private async Task<EmployeeCostsDto> EmployeeViewAsync(Guid employeeId, CancellationToken ct)
    {
        var items = await db.PayrollObligations.AsNoTracking()
            .Where(o => o.UserId == UserId && o.EmployeeId == employeeId && o.Kind == PayrollObligationKind.EmployeeCost)
            .OrderBy(o => o.CreatedAt).ThenBy(o => o.Id)
            .ToListAsync(ct);
        var views = items.Select(o => new PayrollObligationDto(
            o.Id, o.Kind.ToString(), o.EmployeeId, o.Description, o.Amount, o.UpdatedAt)).ToList();
        return new EmployeeCostsDto(views, views.Sum(v => v.Amount ?? 0m));
    }
}
