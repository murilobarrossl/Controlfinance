using System.Globalization;
using ControlFinance.API.Controllers.Filters;
using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

// Funcionários da empresa autenticada. Cadastro administrativo, digitado pela empresa: não cria
// movimentação financeira nem inicia pagamento.
[ApiController]
[Route("api/employees")]
[Authorize]
[BusinessHolderOnly]
public class EmployeesController(AppDbContext db, IEncryptionService encryption) : ApiControllerBase
{
    // Nome é cifrado: busca e ordenação por nome rodam em memória sobre o filtro de situação e
    // departamento (que roda no banco). O teto mantém isso barato e limita abuso do cadastro.
    internal const int MaxEmployeesPerCompany = 5000;
    private const decimal MaxSalary = 999_999_999.99m;
    private static readonly CompareInfo PtBr = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? department,
        [FromQuery] string? payment,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        if (!PayeePaymentStatus.IsValidFilter(payment)) return BadRequest(new { message = "Filtro de pagamento inválido." });

        var query = db.Employees.AsNoTracking().Where(e => e.UserId == UserId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<EmployeeStatus>(status, true, out var parsed) || !Enum.IsDefined(parsed))
                return BadRequest(new { message = "Situação inválida." });
            query = query.Where(e => e.Status == parsed);
        }
        if (!string.IsNullOrWhiteSpace(department))
        {
            var dep = department.Trim();
            query = query.Where(e => e.Department == dep);
        }

        var rows = (await query
            .Select(e => new
            {
                e.Id, e.FullName, e.Role, e.Department, e.AdmissionDate, e.EmploymentType, e.GrossSalary,
                e.Status, e.TerminationDate,
                HasPix = e.PaymentMethods.Any(m => m.Kind == PayeePaymentKind.Pix),
                HasBank = e.PaymentMethods.Any(m => m.Kind == PayeePaymentKind.Bank)
            })
            .ToListAsync(ct));

        var payments = await PayeePaymentStatus.EmployeesAsync(db, encryption, UserId, await UserTodayAsync(db, ct), ct);

        IEnumerable<EmployeeListItemDto> filtered = rows
            .Select(e => new EmployeeListItemDto(e.Id, e.FullName, e.Role, e.Department, e.AdmissionDate, e.EmploymentType?.ToString(),
                e.GrossSalary, e.Status.ToString(), e.TerminationDate, e.HasPix, e.HasBank, PayeePaymentStatus.Label(payments.States, e.Id), payments.BankTotal(e.Id)));
        if (!string.IsNullOrWhiteSpace(search))
            filtered = filtered.Where(e => TextSearch.Matches(e.FullName, search));
        if (payment is not null)
            filtered = filtered.Where(e => PayeePaymentStatus.Passes(payment, e.PaymentStatus));

        // Ordem estável entre páginas: nome (pt-BR, sem diferenciar maiúsculas) e, no empate, o id.
        var ordered = filtered
            .OrderBy(e => e.FullName, Comparer<string>.Create((a, b) => PtBr.Compare(a, b, CompareOptions.IgnoreCase)))
            .ThenBy(e => e.Id)
            .ToList();

        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Ok(new EmployeeListDto(items, ordered.Count, page, pageSize));
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var own = db.Employees.AsNoTracking().Where(e => e.UserId == UserId);
        // Salário é cifrado: a soma é feita depois de decifrar, em decimal, sem arredondamento.
        var activeSalaries = await own.Where(e => e.Status == EmployeeStatus.Active).Select(e => e.GrossSalary).ToListAsync(ct);
        var total = await own.CountAsync(ct);
        var departments = await own.Where(e => e.Department != null)
            .Select(e => e.Department!).Distinct().ToListAsync(ct);
        departments.Sort((a, b) => PtBr.Compare(a, b, CompareOptions.IgnoreCase));

        return Ok(new EmployeeSummaryDto(activeSalaries.Count, total, activeSalaries.Sum(), departments));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking().Include(e => e.PaymentMethods)
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == UserId, ct);
        return employee is null ? NotFound() : Ok(ToDetail(employee));
    }

    [HttpGet("{id:guid}/payment-month")]
    public async Task<IActionResult> GetPaymentMonth(Guid id, CancellationToken ct)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == id && e.UserId == UserId, ct)) return NotFound();
        return Ok(await PaymentMonthAsync(id, await UserTodayAsync(db, ct), ct));
    }

    [HttpPut("{id:guid}/payment-month")]
    public async Task<IActionResult> SetPaymentMonth(Guid id, [FromBody] PayeePaymentMarkDto dto, CancellationToken ct)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == id && e.UserId == UserId, ct)) return NotFound();
        var today = await UserTodayAsync(db, ct);
        var month = PayeePaymentStatus.ReferenceMonth(today);
        if (!PayeePaymentStatus.IsCurrentMonth(dto.ReferenceMonth, month))
            return Conflict(new { message = "O mês mudou. Atualize a página para marcar o pagamento do mês atual." });

        // Pagamentos bancários do momento vão para o evento de auditoria; a resposta é relida depois da
        // gravação para refletir também uma alteração concorrente.
        var before = await PayeePaymentStatus.EmployeesAsync(db, encryption, UserId, today, ct);
        await PayeePaymentStatus.SaveMarkAsync(db,
            () => db.EmployeePaymentMarks.FirstOrDefaultAsync(m => m.UserId == UserId && m.EmployeeId == id && m.ReferenceMonth == month, ct),
            () => new EmployeePaymentMark { UserId = UserId, EmployeeId = id, ReferenceMonth = month },
            PayeeKind.Employee, UserId, id, month, dto.Paid, before.Bank.GetValueOrDefault(id), ct);
        return Ok(await PaymentMonthAsync(id, today, ct));
    }

    private async Task<PayeePaymentMonthDto> PaymentMonthAsync(Guid id, DateTime today, CancellationToken ct)
    {
        var status = await PayeePaymentStatus.EmployeesAsync(db, encryption, UserId, today, ct);
        return PayeePaymentStatus.Describe(status, id, PayeePaymentStatus.ReferenceMonth(today));
    }

    // Cadastro rápido: só os dados principais. Departamento, vínculo e pagamento ficam para o
    // detalhe do funcionário e nunca recebem valor presumido.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EmployeeCoreDto dto, CancellationToken ct)
    {
        if (await db.Employees.CountAsync(e => e.UserId == UserId, ct) >= MaxEmployeesPerCompany)
            return BadRequest(new { message = $"Limite de {MaxEmployeesPerCompany} funcionários por empresa atingido." });

        var employee = new Employee { UserId = UserId };
        var error = await ApplyCoreAsync(employee, dto, isNew: true, ct);
        if (error is not null) return BadRequest(new { message = error });

        db.Employees.Add(employee);
        EmployeeCostDefaults.Seed(db, employee);
        var conflict = await SaveAsync(ct);
        if (conflict is not null) return Conflict(new { message = conflict });

        return CreatedAtAction(nameof(Get), new { id = employee.Id }, ToDetail(employee));
    }

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, [FromBody] EmployeeCoreDto dto, CancellationToken ct) =>
        ChangeAsync(id, (employee) => ApplyCoreAsync(employee, dto, isNew: false, ct), ct);

    [HttpPut("{id:guid}/classification")]
    public Task<IActionResult> SetClassification(Guid id, [FromBody] EmployeeClassificationDto dto, CancellationToken ct) =>
        ChangeAsync(id, (employee) =>
        {
            var department = string.IsNullOrWhiteSpace(dto.Department) ? null : dto.Department.Trim();
            if (department is { Length: > 80 }) return Task.FromResult<string?>("O departamento pode ter até 80 caracteres.");

            EmploymentType? employmentType = null;
            if (!string.IsNullOrWhiteSpace(dto.EmploymentType))
            {
                if (!Enum.TryParse<EmploymentType>(dto.EmploymentType, true, out var parsed) || !Enum.IsDefined(parsed))
                    return Task.FromResult<string?>("Tipo de vínculo inválido.");
                employmentType = parsed;
            }

            employee.Department = department;
            employee.EmploymentType = employmentType;
            return Task.FromResult<string?>(null);
        }, ct);

    // Pix ou conta para TED, para reconhecer pagamentos no futuro. Formato válido não prova de quem é
    // a chave ou a conta. Cada método é independente: incluir um nunca sobrescreve os outros.
    [HttpPost("{id:guid}/payment-methods")]
    public Task<IActionResult> AddPaymentMethod(Guid id, [FromBody] PayeePaymentMethodDto dto, CancellationToken ct) =>
        ChangeAsync(id, async (employee) =>
        {
            if (employee.PaymentMethods.Count >= PayeePayment.MaxMethodsPerPayee)
                return $"Limite de {PayeePayment.MaxMethodsPerPayee} métodos de pagamento por funcionário.";
            var method = new EmployeePaymentMethod { UserId = UserId, EmployeeId = employee.Id };
            var error = await ApplyPaymentMethodAsync(employee, method, dto, ct);
            // Add explícito: com o Id já preenchido, entrar só pela coleção faria o EF tratar o método
            // como existente (UPDATE em vez de INSERT).
            if (error is null)
            {
                employee.PaymentMethods.Add(method);
                db.EmployeePaymentMethods.Add(method);
            }
            return error;
        }, ct);

    [HttpPut("{id:guid}/payment-methods/{methodId:guid}")]
    public async Task<IActionResult> UpdatePaymentMethod(Guid id, Guid methodId, [FromBody] PayeePaymentMethodDto dto, CancellationToken ct)
    {
        if (!await PaymentMethodExistsAsync(id, methodId, ct)) return NotFound();
        return await ChangeAsync(id, (employee) =>
            ApplyPaymentMethodAsync(employee, employee.PaymentMethods.Single(m => m.Id == methodId), dto, ct), ct);
    }

    [HttpDelete("{id:guid}/payment-methods/{methodId:guid}")]
    public async Task<IActionResult> RemovePaymentMethod(Guid id, Guid methodId, CancellationToken ct)
    {
        if (!await PaymentMethodExistsAsync(id, methodId, ct)) return NotFound();
        return await ChangeAsync(id, (employee) =>
        {
            var method = employee.PaymentMethods.Single(m => m.Id == methodId);
            employee.PaymentMethods.Remove(method);
            db.EmployeePaymentMethods.Remove(method);
            return Task.FromResult<string?>(null);
        }, ct);
    }

    // A chave completa sai só por esta rota, usada pelo detalhe do funcionário; listagem e
    // exportação de outras telas seguem mascaradas.
    [HttpGet("{id:guid}/payment-methods/{methodId:guid}/pix-key")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RevealPixKey(Guid id, Guid methodId, CancellationToken ct)
    {
        var pix = await db.EmployeePaymentMethods.AsNoTracking()
            .Where(m => m.Id == methodId && m.EmployeeId == id && m.UserId == UserId && m.Kind == PayeePaymentKind.Pix)
            .Select(m => new { m.PixKeyType, m.PixKey })
            .FirstOrDefaultAsync(ct);
        if (pix?.PixKeyType is not { } type || pix.PixKey is not { } key) return NotFound();
        return Ok(new { type = type.ToString(), key });
    }

    [HttpPost("{id:guid}/termination")]
    public async Task<IActionResult> Terminate(Guid id, [FromBody] EmployeeTerminationDto dto, CancellationToken ct)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id && e.UserId == UserId, ct);
        if (employee is null) return NotFound();
        if (employee.Status == EmployeeStatus.Terminated)
            return Conflict(new { message = "Este funcionário já está desligado." });

        var today = DateOnly.FromDateTime(await UserTodayAsync(db, ct));
        if (dto.TerminationDate < employee.AdmissionDate)
            return BadRequest(new { message = "A data de desligamento não pode ser anterior à admissão." });
        if (dto.TerminationDate > today.AddYears(1))
            return BadRequest(new { message = "Data de desligamento inválida." });

        employee.Status = EmployeeStatus.Terminated;
        employee.TerminationDate = dto.TerminationDate;
        employee.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(ToDetail(employee));
    }

    [HttpPost("{id:guid}/reactivation")]
    public async Task<IActionResult> Reactivate(Guid id, CancellationToken ct)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id && e.UserId == UserId, ct);
        if (employee is null) return NotFound();
        if (employee.Status == EmployeeStatus.Active)
            return Conflict(new { message = "Este funcionário já está ativo." });

        employee.Status = EmployeeStatus.Active;
        employee.TerminationDate = null;
        employee.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(ToDetail(employee));
    }

    // Exclusão definitiva, só de quem já foi desligado: o desligamento é o passo que preserva o
    // histórico, e exigir os dois evita apagar por engano alguém que ainda está na folha. Apaga a
    // linha inteira (dados pessoais, salário e pagamento cifrados), sem cópia.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id && e.UserId == UserId, ct);
        if (employee is null) return NotFound();
        if (employee.Status != EmployeeStatus.Terminated)
            return Conflict(new { message = "Desligue o funcionário antes de excluir o cadastro." });

        db.Employees.Remove(employee);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Carrega o funcionário da empresa, aplica a alteração e salva. A alteração valida tudo antes de
    // tocar na entidade: um erro nunca deixa metade do cadastro alterada.
    private async Task<IActionResult> ChangeAsync(Guid id, Func<Employee, Task<string?>> change, CancellationToken ct)
    {
        var employee = await db.Employees.Include(e => e.PaymentMethods)
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == UserId, ct);
        if (employee is null) return NotFound();

        var error = await change(employee);
        if (error is not null) return BadRequest(new { message = error });

        employee.UpdatedAt = DateTime.UtcNow;
        var conflict = await SaveAsync(ct);
        return conflict is not null ? Conflict(new { message = conflict }) : Ok(ToDetail(employee));
    }

    private Task<bool> PaymentMethodExistsAsync(Guid employeeId, Guid methodId, CancellationToken ct) =>
        db.EmployeePaymentMethods.AnyAsync(m => m.Id == methodId && m.EmployeeId == employeeId && m.UserId == UserId, ct);

    // Valida tudo antes de tocar no método: um erro nunca deixa o método pela metade.
    private async Task<string?> ApplyPaymentMethodAsync(Employee employee, EmployeePaymentMethod method, PayeePaymentMethodDto dto, CancellationToken ct)
    {
        var error = PayeePayment.ParseMethod(dto, out var parsed);
        if (error is not null) return error;

        string? hash = null;
        if (parsed!.Kind == PayeePaymentKind.Pix)
        {
            var candidates = encryption.ComputeLookupHashCandidates(EmployeeKeys.PixLookupInput(UserId, parsed.PixType, parsed.PixKey));
            var owner = await db.EmployeePaymentMethods
                .Where(m => m.UserId == UserId && m.Id != method.Id && m.PixKeyHash != null && candidates.Contains(m.PixKeyHash))
                .Select(m => (Guid?)m.EmployeeId)
                .FirstOrDefaultAsync(ct);
            if (owner is { } other)
                return other == employee.Id
                    ? "Esta chave Pix já está cadastrada para este funcionário."
                    : "Esta chave Pix já está cadastrada para outro funcionário.";
            hash = encryption.ComputeLookupHash(EmployeeKeys.PixLookupInput(UserId, parsed.PixType, parsed.PixKey));
        }
        else if (employee.PaymentMethods.Any(m => m.Id != method.Id && PayeePayment.SameAccount(m, parsed.Bank!)))
            return "Esta conta já está cadastrada para este funcionário.";

        PayeePayment.Assign(method, parsed, hash);
        return null;
    }

    private async Task<string?> ApplyCoreAsync(Employee employee, EmployeeCoreDto dto, bool isNew, CancellationToken ct)
    {
        var fullName = string.Join(' ', (dto.FullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (fullName.Length < 3) return "Informe o nome completo.";
        if (fullName.Length > 150) return "O nome pode ter até 150 caracteres.";

        var role = (dto.Role ?? string.Empty).Trim();
        if (role.Length == 0) return "Informe o cargo ou função.";
        if (role.Length > 100) return "O cargo pode ter até 100 caracteres.";

        var today = DateOnly.FromDateTime(await UserTodayAsync(db, ct));
        if (dto.AdmissionDate < new DateOnly(1900, 1, 1) || dto.AdmissionDate > today.AddYears(1))
            return "Data de admissão inválida.";
        if (employee.TerminationDate is { } terminated && dto.AdmissionDate > terminated)
            return "A admissão não pode ser posterior ao desligamento.";

        if (dto.GrossSalary <= 0 || dto.GrossSalary > MaxSalary || decimal.Round(dto.GrossSalary, 2) != dto.GrossSalary)
            return "Informe um salário bruto válido, com até duas casas decimais.";

        // CPF: obrigatório no cadastro; na edição, vazio mantém o atual.
        var cpfDigits = PayeeKeys.Digits(dto.Cpf);
        string? cpfHash = null;
        if (isNew || cpfDigits.Length > 0)
        {
            if (!DocumentValidator.IsValidCpf(cpfDigits)) return "CPF inválido.";
            var candidates = encryption.ComputeLookupHashCandidates(EmployeeKeys.CpfLookupInput(UserId, cpfDigits));
            if (await db.Employees.AnyAsync(e => e.UserId == UserId && e.Id != employee.Id && candidates.Contains(e.CpfHash), ct))
                return "Já existe um funcionário com este CPF. Se ele foi desligado, reative o cadastro existente.";
            cpfHash = encryption.ComputeLookupHash(EmployeeKeys.CpfLookupInput(UserId, cpfDigits));
        }

        employee.FullName = fullName;
        employee.Role = role;
        employee.AdmissionDate = dto.AdmissionDate;
        employee.GrossSalary = dto.GrossSalary;
        if (cpfHash is not null)
        {
            employee.Cpf = cpfDigits;
            employee.CpfHash = cpfHash;
        }
        return null;
    }

    // A checagem de duplicidade acima não cobre dois cadastros simultâneos do mesmo CPF ou chave;
    // o índice único cobre, e a resposta é a mesma.
    private async Task<string?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return "Já existe um funcionário com este CPF ou com esta chave Pix.";
        }
    }

    private static EmployeeDetailDto ToDetail(Employee e) => new(
        e.Id, e.FullName, PayeeKeys.MaskCpf(e.Cpf), e.Role, e.Department, e.AdmissionDate,
        e.EmploymentType?.ToString(), e.GrossSalary, e.Status.ToString(), e.TerminationDate,
        PayeePayment.Views(e.PaymentMethods),
        e.CreatedAt, e.UpdatedAt);
}
