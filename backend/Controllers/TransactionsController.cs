using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/transactions")]
[Authorize]
public class TransactionsController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] string? type, [FromQuery] Guid? bankAccountId)
    {
        var query = db.Transactions
            .Where(t => t.UserId == UserId)
            // Sem isso, transações de uma conta desativada (ex.: duplicada por uma reconexão
            // com a Polp, ou removida manualmente em BankAccountsController) continuavam entrando
            // nas somas de receitas/despesas mesmo depois de "desconectada".
            .Where(t => t.BankAccount == null || t.BankAccount.IsActive);

        if (bankAccountId.HasValue)
            query = query.Where(t => t.BankAccountId == bankAccountId);

        query = ApplyStatusFilter(query, status);

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<TransactionType>(type, true, out var tp))
            query = query.Where(t => t.Type == tp);

        // Cap de segurança: Categorias/ReceitasDespesas hoje esperam a lista completa pra
        // agregar no cliente, então isso não pagina de verdade, só limita o pior caso (uma
        // conta com muitos anos de histórico sincronizado da Polp). O Relatórios tem paginação
        // de verdade no endpoint /report abaixo.
        const int MaxResults = 2000;

        var result = await ProjectToDto(query.OrderByDescending(t => t.DueDate).Take(MaxResults)).ToListAsync();

        var ownerName = await db.Users.Where(u => u.Id == UserId).Select(u => u.Name).FirstOrDefaultAsync();
        result = result
            .Select(t => t with
            {
                IsTransfer = TransferDetection.IsSelfTransfer(t.Name, t.CategoryName, ownerName),
                Status = t.Status == nameof(TransactionStatus.Pending) && t.DueDate.Date < DateTime.UtcNow.Date
                    ? nameof(TransactionStatus.Overdue)
                    : t.Status,
            })
            .ToList();

        return Ok(result);
    }

    // Pending "vencido" é calculado na leitura (TransactionDto.EffectiveStatus), não gravado no
    // banco — então o filtro por status precisa enxergar a mesma coisa, senão status=Overdue não
    // acha nada (nenhuma linha guarda literalmente Overdue a não ser quando o usuário seta à mão)
    // e status=Pending mostra até pendências que a tela já rotula como Atrasado.
    private static IQueryable<Transaction> ApplyStatusFilter(IQueryable<Transaction> query, string? status)
    {
        if (string.IsNullOrEmpty(status) || !Enum.TryParse<TransactionStatus>(status, true, out var s))
            return query;

        // Por dia, não pelo instante exato: uma conta que vence hoje ainda é Pending, não Overdue.
        var today = DateTime.UtcNow.Date;
        return s switch
        {
            TransactionStatus.Overdue => query.Where(t =>
                t.Status == TransactionStatus.Overdue || (t.Status == TransactionStatus.Pending && t.DueDate < today)),
            TransactionStatus.Pending => query.Where(t => t.Status == TransactionStatus.Pending && t.DueDate >= today),
            _ => query.Where(t => t.Status == s),
        };
    }

    // Paginado, filtrado e ordenado no backend, ao contrário do GetAll acima. Necessário porque
    // Relatórios lista o histórico inteiro numa tabela: carregar tudo de uma vez pra filtrar no
    // cliente fica pesado à medida que o histórico cresce.
    [HttpGet("report")]
    public async Task<IActionResult> GetReport(
        [FromQuery] string? status,
        [FromQuery] string? type,
        [FromQuery] string? search,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] string sortBy = "dueDate",
        [FromQuery] string sortDir = "desc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var desc = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);

        var query = db.Transactions
            .Where(t => t.UserId == UserId)
            .Where(t => t.BankAccount == null || t.BankAccount.IsActive);

        query = ApplyStatusFilter(query, status);

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<TransactionType>(type, true, out var tp))
            query = query.Where(t => t.Type == tp);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(t => EF.Functions.ILike(t.Name, $"%{search}%"));

        // year/month chegam como inteiros (não como DateTime cru na query string) de propósito:
        // o model binder de DateTime da ASP.NET não garante Kind=Utc a partir de string, e essa
        // ambiguidade já causou bug de fronteira de mês em outro lugar do dashboard. Montar o
        // intervalo aqui, igual o DashboardController faz, evita a mesma armadilha.
        if (year.HasValue && month.HasValue)
        {
            var startOfMonth = new DateTime(year.Value, month.Value, 1, 0, 0, 0, DateTimeKind.Utc);
            var endOfMonth = startOfMonth.AddMonths(1);
            query = query.Where(t => t.DueDate >= startOfMonth && t.DueDate < endOfMonth);
        }

        var totalCount = await query.CountAsync();
        var ownerName = await db.Users.Where(u => u.Id == UserId).Select(u => u.Name).FirstOrDefaultAsync();

        // Amount é criptografado (guardado como texto): "ORDER BY"/"SUM" direto na coluna
        // cifrada ou dá erro de SQL, ou (no caso do ORDER BY) roda sem erro nenhum e devolve uma
        // ordem sem sentido, porque estaria ordenando o texto cifrado, não o valor real. Por
        // isso, quando a ordenação pedida é por valor, materializa (descriptografando) até um
        // teto de segurança, ordena e soma em memória, e só então corta a página pedida.
        const int MaxRows = 2000;

        List<TransactionDto> items;
        decimal totalIncome;
        decimal totalExpense;

        if (string.Equals(sortBy, "amount", StringComparison.OrdinalIgnoreCase))
        {
            var all = await ProjectToDto(query.OrderByDescending(t => t.DueDate).Take(MaxRows)).ToListAsync();
            all = all
                .Select(t => t with
                {
                    IsTransfer = TransferDetection.IsSelfTransfer(t.Name, t.CategoryName, ownerName),
                    Status = t.Status == nameof(TransactionStatus.Pending) && t.DueDate.Date < DateTime.UtcNow.Date
                        ? nameof(TransactionStatus.Overdue)
                        : t.Status,
                })
                .ToList();
            var sorted = desc ? all.OrderByDescending(t => t.Amount) : all.OrderBy(t => t.Amount);
            items = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            // Transferência pra si mesmo conta normalmente nos totais, igual qualquer outra
            // movimentação — decisão do usuário.
            totalIncome = all.Where(t => t.Type == "Income").Sum(t => t.Amount);
            totalExpense = all.Where(t => t.Type == "Expense").Sum(t => t.Amount);
        }
        else
        {
            var ordered = ApplySort(query, sortBy, desc);
            items = await ProjectToDto(ordered.Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync();
            items = items
                .Select(t => t with
                {
                    IsTransfer = TransferDetection.IsSelfTransfer(t.Name, t.CategoryName, ownerName),
                    Status = t.Status == nameof(TransactionStatus.Pending) && t.DueDate.Date < DateTime.UtcNow.Date
                        ? nameof(TransactionStatus.Overdue)
                        : t.Status,
                })
                .ToList();

            // Totais do filtro inteiro, não só da página atual: mesma limitação do Amount
            // criptografado, então soma em memória depois de trazer só Type+Amount
            // (mais barato que montar o TransactionDto inteiro com os joins de conta).
            var forTotals = await query
                .Take(MaxRows)
                .Select(t => new { t.Type, t.Amount })
                .ToListAsync();
            totalIncome = forTotals.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount);
            totalExpense = forTotals.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);
        }

        return Ok(new TransactionReportDto(items, totalCount, totalIncome, totalExpense));
    }

    private static IQueryable<TransactionDto> ProjectToDto(IQueryable<Transaction> query) =>
        query.Select(t => new TransactionDto(
            t.Id, t.Name, t.Description,
            t.Type.ToString(), t.Status.ToString(),
            t.Amount, t.DueDate, t.PaidAt,
            t.Category != null ? t.Category.Name : null,
            t.BankAccount != null ? t.BankAccount.Name : null,
            t.IsFixed,
            false
        ));

    // Todo critério de ordenação termina com ThenBy(Id): DueDate (e os outros campos) frequentemente
    // se repetem entre transações, e sem um desempate estável o Postgres não garante a mesma ordem
    // entre duas execuções do Skip/Take — a mesma linha podia aparecer em duas páginas do Relatórios
    // (ou sumir de todas) só por causa disso.
    private static IQueryable<Transaction> ApplySort(IQueryable<Transaction> query, string sortBy, bool desc) => sortBy switch
    {
        "name" => desc
            ? query.OrderByDescending(t => t.Name).ThenBy(t => t.Id)
            : query.OrderBy(t => t.Name).ThenBy(t => t.Id),
        "categoryName" => desc
            ? query.OrderByDescending(t => t.Category != null ? t.Category.Name : "").ThenBy(t => t.Id)
            : query.OrderBy(t => t.Category != null ? t.Category.Name : "").ThenBy(t => t.Id),
        "bankAccountName" => desc
            ? query.OrderByDescending(t => t.BankAccount != null ? t.BankAccount.Name : "").ThenBy(t => t.Id)
            : query.OrderBy(t => t.BankAccount != null ? t.BankAccount.Name : "").ThenBy(t => t.Id),
        "type" => desc
            ? query.OrderByDescending(t => t.Type).ThenBy(t => t.Id)
            : query.OrderBy(t => t.Type).ThenBy(t => t.Id),
        "status" => desc
            ? query.OrderByDescending(t => t.Status).ThenBy(t => t.Id)
            : query.OrderBy(t => t.Status).ThenBy(t => t.Id),
        _ => desc
            ? query.OrderByDescending(t => t.DueDate).ThenBy(t => t.Id)
            : query.OrderBy(t => t.DueDate).ThenBy(t => t.Id),
    };

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTransactionDto dto)
    {
        if (!Enum.TryParse<TransactionType>(dto.Type, true, out var type))
            return BadRequest(new { message = "Tipo inválido. Use 'Income' ou 'Expense'." });

        if (!Enum.TryParse<TransactionStatus>(dto.Status, true, out var status))
            return BadRequest(new { message = "Status inválido. Use 'Pending', 'Paid' ou 'Overdue'." });

        if (!await OwnsReferencesAsync(dto.BankAccountId, dto.CategoryId))
            return BadRequest(new { message = "Conta bancária ou categoria inválida." });

        var transaction = new Transaction
        {
            UserId = UserId,
            Name = dto.Name,
            Description = dto.Description,
            Type = type,
            Status = status,
            Amount = dto.Amount,
            // O JSON manda datas como "2026-08-20" (sem timezone), então o binder do ASP.NET
            // gera Kind=Unspecified; o Npgsql exige Kind=Utc pra colunas timestamptz.
            DueDate = DateTime.SpecifyKind(dto.DueDate, DateTimeKind.Utc),
            BankAccountId = dto.BankAccountId,
            CategoryId = dto.CategoryId,
            IsFixed = dto.IsFixed,
            PaidAt = status == TransactionStatus.Paid ? DateTime.UtcNow : null
        };

        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), new { id = transaction.Id }, TransactionDto.FromEntity(transaction));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateTransactionDto dto)
    {
        var transaction = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId);
        if (transaction is null) return NotFound();

        if (!Enum.TryParse<TransactionType>(dto.Type, true, out var type))
            return BadRequest(new { message = "Tipo inválido." });

        if (!Enum.TryParse<TransactionStatus>(dto.Status, true, out var status))
            return BadRequest(new { message = "Status inválido." });

        if (!await OwnsReferencesAsync(dto.BankAccountId, dto.CategoryId))
            return BadRequest(new { message = "Conta bancária ou categoria inválida." });

        transaction.Name = dto.Name;
        transaction.Description = dto.Description;
        transaction.Type = type;
        transaction.Status = status;
        transaction.Amount = dto.Amount;
        transaction.DueDate = DateTime.SpecifyKind(dto.DueDate, DateTimeKind.Utc);
        transaction.BankAccountId = dto.BankAccountId;
        transaction.CategoryId = dto.CategoryId;
        transaction.IsFixed = dto.IsFixed;
        transaction.PaidAt = status == TransactionStatus.Paid ? DateTime.UtcNow : null;

        await db.SaveChangesAsync();
        return Ok();
    }

    // Endpoint dedicado só pra alternar IsFixed: reaproveitar o Update genérico exigiria montar
    // o CreateTransactionDto inteiro no cliente a partir do TransactionDto (que não expõe
    // BankAccountId/CategoryId, só os nomes), arriscando sobrescrever campos com dado incompleto.
    [HttpPut("{id}/fixed")]
    public async Task<IActionResult> SetFixed(Guid id, [FromBody] SetTransactionFixedDto dto)
    {
        var transaction = await db.Transactions
            .Include(t => t.Category)
            .Include(t => t.BankAccount)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId);
        if (transaction is null) return NotFound();

        transaction.IsFixed = dto.IsFixed;
        await db.SaveChangesAsync();
        return Ok(TransactionDto.FromEntity(transaction));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var transaction = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId);
        if (transaction is null) return NotFound();

        db.Transactions.Remove(transaction);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Impede que um usuário associe a transação a uma conta bancária ou categoria de outra pessoa
    // (BOLA/IDOR): os ids vêm do corpo da requisição e não podiam ser confiados sem essa checagem.
    private async Task<bool> OwnsReferencesAsync(Guid? bankAccountId, Guid? categoryId)
    {
        if (bankAccountId.HasValue &&
            !await db.BankAccounts.AnyAsync(b => b.Id == bankAccountId && b.UserId == UserId))
            return false;

        if (categoryId.HasValue &&
            !await db.Categories.AnyAsync(c => c.Id == categoryId && c.UserId == UserId))
            return false;

        return true;
    }
}
