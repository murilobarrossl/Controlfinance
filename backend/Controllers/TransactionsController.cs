using System.Globalization;
using System.Text.RegularExpressions;
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
        var today = await UserTodayAsync(db);
        var query = FilteredQuery(bankAccountId, status, type, today);

        // Cap de segurança: Categorias/ReceitasDespesas hoje esperam a lista completa pra
        // agregar no cliente, então isso não pagina de verdade, só limita o pior caso (uma
        // conta com muitos anos de histórico sincronizado da Polp). O Relatórios tem paginação
        // de verdade no endpoint /report abaixo.
        const int MaxResults = 2000;

        var result = await ProjectToDto(query.OrderByDescending(t => t.DueDate).Take(MaxResults)).ToListAsync();

        result = WithComputedFields(result, today);

        return Ok(result);
    }

    private IQueryable<Transaction> FilteredQuery(Guid? bankAccountId, string? status, string? type, DateTime today)
    {
        var query = db.Transactions
            .Where(t => t.UserId == UserId)
            // Sem isso, transações de uma conta desativada (ex.: duplicada por uma reconexão
            // com a Polp, ou removida manualmente em BankAccountsController) continuavam entrando
            // nas somas de receitas/despesas mesmo depois de "desconectada".
            .Where(t => t.BankAccount == null || t.BankAccount.IsActive);

        if (bankAccountId.HasValue)
            query = query.Where(t => t.BankAccountId == bankAccountId);

        query = ApplyStatusFilter(query, status, today);

        if (!string.IsNullOrEmpty(type) && Enum.TryParse<TransactionType>(type, true, out var tp))
            query = query.Where(t => t.Type == tp);

        return query;
    }

    private static List<TransactionDto> WithComputedFields(List<TransactionDto> items, DateTime today) =>
        items
            .Select(t => t with
            {
                Status = t.Status == nameof(TransactionStatus.Pending) && t.DueDate.Date < today
                    ? nameof(TransactionStatus.Overdue)
                    : t.Status,
            })
            .ToList();

    // Pending "vencido" é calculado na leitura (TransactionDto.EffectiveStatus), não gravado no
    // banco, então o filtro por status precisa enxergar a mesma coisa, senão status=Overdue não
    // acha nada (nenhuma linha guarda literalmente Overdue a não ser quando o usuário seta à mão)
    // e status=Pending mostra até pendências que a tela já rotula como Atrasado.
    private static IQueryable<Transaction> ApplyStatusFilter(IQueryable<Transaction> query, string? status, DateTime today)
    {
        if (string.IsNullOrEmpty(status) || !Enum.TryParse<TransactionStatus>(status, true, out var s))
            return query;

        // Por dia, não pelo instante exato: uma conta que vence hoje ainda é Pending, não Overdue.
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
        [FromQuery] Guid? bankAccountId = null,
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

        var today = await UserTodayAsync(db);
        var query = FilteredQuery(bankAccountId, status, type, today);

        // year/month chegam como inteiros (não como DateTime cru na query string) de propósito:
        // o model binder de DateTime da ASP.NET não garante Kind=Utc a partir de string, e essa
        // ambiguidade já causou bug de fronteira de mês em outro lugar do dashboard. Montar o
        // intervalo aqui, igual o DashboardController faz, evita a mesma armadilha.
        if (year.HasValue && month.HasValue)
        {
            if (year is < 1 or > 9998 || month is < 1 or > 12)
                return BadRequest(new { message = "Ano ou mês inválido." });

            var startOfMonth = new DateTime(year.Value, month.Value, 1, 0, 0, 0, DateTimeKind.Utc);
            var endOfMonth = startOfMonth.AddMonths(1);
            query = query.Where(t => t.DueDate >= startOfMonth && t.DueDate < endOfMonth);
        }

        // Amount é criptografado (guardado como texto): "ORDER BY"/"SUM" direto na coluna
        // cifrada ou dá erro de SQL, ou (no caso do ORDER BY) roda sem erro nenhum e devolve uma
        // ordem sem sentido, porque estaria ordenando o texto cifrado, não o valor real. Por
        // isso, quando a ordenação pedida é por valor, materializa (descriptografando) todo o
        // filtro, ordena, conta e soma em memória, e só então corta a página pedida.

        List<TransactionDto> items;
        decimal totalIncome;
        decimal totalExpense;

        int totalCount;

        // Nome (Name) também é cifrado agora: busca por descrição e ordenação por nome caem no
        // mesmo caminho em memória da ordenação por valor.
        var needsInMemory = string.Equals(sortBy, "amount", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sortBy, "name", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(search);

        var wantsDayTotals = string.Equals(sortBy, "dueDate", StringComparison.OrdinalIgnoreCase);
        List<ReportDayTotalDto> dayTotals;

        if (needsInMemory)
        {
            var all = await ProjectToDto(query.OrderByDescending(t => t.DueDate)).ToListAsync();
            if (!string.IsNullOrWhiteSpace(search))
                all = all.Where(t => MatchesSearch(t.Name, search)).ToList();
            totalCount = all.Count;
            all = WithComputedFields(all, today);
            items = SortInMemory(all, sortBy, desc).Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // O filtro inteiro já está decifrado em memória: somar daqui evita ler e decifrar
            // todas as linhas de novo no banco só pra chegar no mesmo total.
            var rows = all.Select(t => new SumRow(t.DueDate, Enum.Parse<TransactionType>(t.Type), t.Amount, t.IsTransfer)).ToList();
            (totalIncome, totalExpense) = SumTotals(rows);
            dayTotals = wantsDayTotals ? SumDayTotals(rows, items) : [];
        }
        else
        {
            totalCount = await query.CountAsync();
            var ordered = ApplySort(query, sortBy, desc);
            items = await ProjectToDto(ordered.Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync();
            items = WithComputedFields(items, today);

            (totalIncome, totalExpense) = await SumTotalsAsync(SumRowsAsync(query));
            dayTotals = wantsDayTotals && items.Count > 0
                ? SumDayTotals(await CollectAsync(SumRowsAsync(DayRange(query, items))), items)
                : [];
        }

        return Ok(new TransactionReportDto(items, totalCount, totalIncome, totalExpense, dayTotals));
    }

    private static readonly Regex InstallmentSuffix = new(@"([0-9]{2}/[0-9]{2})\z", RegexOptions.Compiled);
    private static readonly char[] JsTrimChars =
        [' ', '\t', '\n', '\v', '\f', '\r', '\u00A0', '\u1680', '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005',
         '\u2006', '\u2007', '\u2008', '\u2009', '\u200A', '\u2028', '\u2029', '\u202F', '\u205F', '\u3000', '\uFEFF'];

    private static string EstablishmentKey(string name) =>
        InstallmentSuffix.IsMatch(name) ? InstallmentSuffix.Replace(name, "").Trim(JsTrimChars) : name;

    [HttpGet("aggregates")]
    public async Task<IActionResult> GetAggregates(
        [FromQuery] Guid? bankAccountId,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct)
    {
        var (start, end, rangeError) = ParseRange(from, to);
        if (rangeError is not null)
            return BadRequest(new { message = rangeError });

        var today = await UserTodayAsync(db, ct);
        var rows = FilteredQuery(bankAccountId, null, null, today)
            .Where(t => t.DueDate >= start && t.DueDate < end)
            .OrderByDescending(t => t.DueDate)
            .ThenBy(t => t.Id)
            .Select(t => new { t.Type, t.Amount, t.Name, t.DueDate, CategoryName = t.Category != null ? t.Category.Name : null, t.PolpMovementKind })
            .AsNoTracking()
            .AsAsyncEnumerable();

        var months = new Dictionary<(int Year, int Month), (decimal Income, decimal Expense, int Count)>();
        var categories = new Dictionary<(string? Name, TransactionType Type), (decimal Amount, int Count)>();
        var establishments = new Dictionary<(string? Category, TransactionType Type, string Key), (decimal Amount, int Count, bool IsInstallment)>();
        decimal totalIncome = 0, totalExpense = 0;
        var count = 0;

        await foreach (var t in rows.WithCancellation(ct))
        {
            if (TransferDetection.IsSelfTransfer(t.PolpMovementKind)) continue;

            var categoryName = string.IsNullOrEmpty(t.CategoryName) ? null : t.CategoryName;
            var monthKey = (t.DueDate.Year, t.DueDate.Month);
            months.TryGetValue(monthKey, out var m);
            var isIncome = t.Type == TransactionType.Income;
            months[monthKey] = (m.Income + (isIncome ? t.Amount : 0), m.Expense + (isIncome ? 0 : t.Amount), m.Count + 1);

            if (isIncome) totalIncome += t.Amount; else totalExpense += t.Amount;
            count++;

            var categoryKey = (categoryName, t.Type);
            categories.TryGetValue(categoryKey, out var c);
            categories[categoryKey] = (c.Amount + t.Amount, c.Count + 1);

            var key = EstablishmentKey(t.Name);
            var establishmentKey = (categoryName, t.Type, key);
            establishments.TryGetValue(establishmentKey, out var e);
            establishments[establishmentKey] = (e.Amount + t.Amount, e.Count + 1, e.IsInstallment || InstallmentSuffix.IsMatch(t.Name));
        }

        var monthList = new List<AggregateMonthDto>();
        for (var cursor = new DateTime(start.Year, start.Month, 1, 0, 0, 0, DateTimeKind.Utc); cursor < end; cursor = cursor.AddMonths(1))
        {
            months.TryGetValue((cursor.Year, cursor.Month), out var m);
            monthList.Add(new AggregateMonthDto(cursor.Year, cursor.Month, m.Income, m.Expense, m.Count));
        }

        var categoryList = categories
            .Select(kv => new AggregateCategoryDto(kv.Key.Name, kv.Key.Type.ToString(), kv.Value.Amount, kv.Value.Count))
            .OrderByDescending(c => c.Amount)
            .ToList();

        var establishmentList = establishments
            .Select(kv => new AggregateEstablishmentDto(kv.Key.Key, kv.Key.Key, kv.Key.Category, kv.Key.Type.ToString(), kv.Value.Amount, kv.Value.Count, kv.Value.IsInstallment))
            .OrderByDescending(e => e.Amount)
            .ToList();

        return Ok(new TransactionAggregatesDto(totalIncome, totalExpense, count, monthList, categoryList, establishmentList));
    }

    private sealed record SumRow(DateTime DueDate, TransactionType Type, decimal Amount, bool IsTransfer);

    // Só as colunas que a soma usa: sem o nome (cifrado), que este caminho não filtra, cada
    // página do relatório decifraria o nome de todas as linhas do filtro só pra descartar.
    private static async IAsyncEnumerable<SumRow> SumRowsAsync(IQueryable<Transaction> query)
    {
        var rows = query
            .Select(t => new { t.DueDate, t.Type, t.Amount, t.PolpMovementKind })
            .AsAsyncEnumerable();
        await foreach (var t in rows)
            yield return new SumRow(t.DueDate, t.Type, t.Amount, TransferDetection.IsSelfTransfer(t.PolpMovementKind));
    }

    private static async Task<List<SumRow>> CollectAsync(IAsyncEnumerable<SumRow> rows)
    {
        var list = new List<SumRow>();
        await foreach (var row in rows) list.Add(row);
        return list;
    }

    private static async Task<(decimal Income, decimal Expense)> SumTotalsAsync(IAsyncEnumerable<SumRow> rows)
    {
        decimal income = 0, expense = 0;
        await foreach (var t in rows)
        {
            if (t.IsTransfer) continue;
            if (t.Type == TransactionType.Income) income += t.Amount;
            else if (t.Type == TransactionType.Expense) expense += t.Amount;
        }
        return (income, expense);
    }

    private static IQueryable<Transaction> DayRange(IQueryable<Transaction> query, List<TransactionDto> items)
    {
        var start = DateTime.SpecifyKind(items.Min(i => i.DueDate).Date, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(items.Max(i => i.DueDate).Date.AddDays(1), DateTimeKind.Utc);
        return query.Where(t => t.DueDate >= start && t.DueDate < end);
    }

    private static (decimal Income, decimal Expense) SumTotals(IEnumerable<SumRow> rows)
    {
        decimal income = 0, expense = 0;
        foreach (var t in rows)
        {
            if (t.IsTransfer) continue;
            if (t.Type == TransactionType.Income) income += t.Amount;
            else if (t.Type == TransactionType.Expense) expense += t.Amount;
        }
        return (income, expense);
    }

    // Subtotal de cada dia presente na página, somado sobre o filtro inteiro: um dia que continua
    // na página seguinte não pode aparecer com a soma só das linhas desta página.
    private static List<ReportDayTotalDto> SumDayTotals(IEnumerable<SumRow> rows, List<TransactionDto> items)
    {
        if (items.Count == 0) return [];

        var days = items.Select(i => i.DueDate.Date).ToHashSet();
        return rows
            .Where(r => days.Contains(r.DueDate.Date))
            .GroupBy(r => r.DueDate.Date)
            .Select(g =>
            {
                var (income, expense) = SumTotals(g);
                return (Day: g.Key, Income: income, Expense: expense);
            })
            .Concat(days.Select(d => (Day: d, Income: 0m, Expense: 0m)))
            .GroupBy(x => x.Day)
            .Select(g => new ReportDayTotalDto(
                DateTime.SpecifyKind(g.Key, DateTimeKind.Utc), g.Sum(x => x.Income), g.Sum(x => x.Expense)))
            .OrderBy(d => d.Date)
            .ToList();
    }

    // Busca sem diferenciar maiúsculas nem acentos ("pao" acha "Pão"), como o ILIKE fazia para
    // maiúsculas, agora sobre o nome já decifrado em memória.
    private static bool MatchesSearch(string name, string search) =>
        System.Globalization.CultureInfo.InvariantCulture.CompareInfo.IndexOf(
            name, search.Trim(),
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;

    private static IEnumerable<TransactionDto> SortInMemory(List<TransactionDto> items, string sortBy, bool desc)
    {
        Func<TransactionDto, object?> key = sortBy switch
        {
            "amount" => t => t.Amount,
            "name" => t => t.Name,
            "categoryName" => t => t.CategoryName ?? "",
            "bankAccountName" => t => t.BankAccountName ?? "",
            "type" => t => t.Type,
            "status" => t => t.Status,
            _ => t => t.DueDate,
        };
        var comparer = Comparer<object?>.Create((a, b) => a is string sa && b is string sb
            ? string.Compare(sa, sb, System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), System.Globalization.CompareOptions.IgnoreCase)
            : Comparer<object?>.Default.Compare(a, b));
        var ordered = desc ? items.OrderByDescending(key, comparer) : items.OrderBy(key, comparer);
        return ordered.ThenBy(t => t.Id);
    }

    [HttpGet("details")]
    public async Task<IActionResult> GetDetails(
        [FromQuery] Guid? bankAccountId,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? type,
        [FromQuery] string? category,
        [FromQuery] string? establishment,
        CancellationToken ct)
    {
        var (start, end, rangeError) = ParseRange(from, to);
        if (rangeError is not null)
            return BadRequest(new { message = rangeError });

        var today = await UserTodayAsync(db, ct);
        var rows = ProjectToDto(FilteredQuery(bankAccountId, null, type, today)
                .Where(t => t.DueDate >= start && t.DueDate < end)
                .OrderByDescending(t => t.DueDate)
                .ThenBy(t => t.Id)
                .AsNoTracking())
            .AsAsyncEnumerable();

        var items = new List<TransactionDto>();
        await foreach (var t in rows.WithCancellation(ct))
        {
            if (t.IsTransfer) continue;
            if (category is not null && (string.IsNullOrEmpty(t.CategoryName) ? "Sem categoria" : t.CategoryName) != category) continue;
            if (establishment is not null && EstablishmentKey(t.Name) != establishment) continue;
            items.Add(t);
        }

        return Ok(WithComputedFields(items, today));
    }

    private static (DateTime Start, DateTime End, string? Error) ParseRange(string? from, string? to)
    {
        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDate)
            || !DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDate)
            || toDate <= fromDate)
            return (default, default, "Intervalo de datas inválido.");

        if (toDate.DayNumber - fromDate.DayNumber > 366 * 50)
            return (default, default, "Intervalo de datas grande demais.");

        return (new DateTime(fromDate.Year, fromDate.Month, fromDate.Day, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(toDate.Year, toDate.Month, toDate.Day, 0, 0, 0, DateTimeKind.Utc),
            null);
    }

    private static IQueryable<TransactionDto> ProjectToDto(IQueryable<Transaction> query) =>
        query.Select(t => new TransactionDto(
            t.Id, t.Name, t.Description,
            t.Type.ToString(), t.Status.ToString(),
            t.Amount, t.DueDate, t.PaidAt,
            t.Category != null ? t.Category.Name : null,
            t.BankAccount != null ? t.BankAccount.Name : null,
            t.IsFixed,
            TransferDetection.IsSelfTransfer(t.PolpMovementKind)
        ));

    // Todo critério de ordenação termina com ThenBy(Id): DueDate (e os outros campos) frequentemente
    // se repetem entre transações, e sem um desempate estável o Postgres não garante a mesma ordem
    // entre duas execuções do Skip/Take. A mesma linha podia aparecer em duas páginas do Relatórios
    // (ou sumir de todas) só por causa disso.
    private static IQueryable<Transaction> ApplySort(IQueryable<Transaction> query, string sortBy, bool desc) => sortBy switch
    {
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
        if (!Enum.TryParse<TransactionType>(dto.Type, true, out var type) || !Enum.IsDefined(type))
            return BadRequest(new { message = "Tipo inválido. Use 'Income' ou 'Expense'." });

        if (!Enum.TryParse<TransactionStatus>(dto.Status, true, out var status) || !Enum.IsDefined(status))
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

        return CreatedAtAction(nameof(GetAll), new { id = transaction.Id }, TransactionDto.FromEntity(transaction, await UserTodayAsync(db)));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CreateTransactionDto dto)
    {
        var transaction = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId);
        if (transaction is null) return NotFound();

        if (!Enum.TryParse<TransactionType>(dto.Type, true, out var type) || !Enum.IsDefined(type))
            return BadRequest(new { message = "Tipo inválido." });

        if (!Enum.TryParse<TransactionStatus>(dto.Status, true, out var status) || !Enum.IsDefined(status))
            return BadRequest(new { message = "Status inválido." });

        if (!await OwnsReferencesAsync(dto.BankAccountId, dto.CategoryId))
            return BadRequest(new { message = "Conta bancária ou categoria inválida." });

        transaction.Name = dto.Name;
        transaction.Description = dto.Description;
        transaction.Type = type;
        transaction.Status = status;
        transaction.Amount = dto.Amount;
        var newDueDate = DateTime.SpecifyKind(dto.DueDate, DateTimeKind.Utc);
        if (newDueDate != transaction.DueDate) transaction.ReminderSentAt = null;
        transaction.DueDate = newDueDate;
        transaction.BankAccountId = dto.BankAccountId;
        transaction.CategoryId = dto.CategoryId;
        transaction.IsFixed = dto.IsFixed;
        transaction.PaidAt = status == TransactionStatus.Paid ? transaction.PaidAt ?? DateTime.UtcNow : null;

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
        return Ok(TransactionDto.FromEntity(transaction, await UserTodayAsync(db)));
    }

    // Endpoint dedicado só pra corrigir nome/categoria (ex.: categorização errada vinda da Polp).
    // Ver comentário no SetTransactionDetailsDto sobre por que não dá pra reaproveitar o Update genérico.
    [HttpPut("{id}/details")]
    public async Task<IActionResult> SetDetails(Guid id, [FromBody] SetTransactionDetailsDto dto)
    {
        var transaction = await db.Transactions
            .Include(t => t.Category)
            .Include(t => t.BankAccount)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId);
        if (transaction is null) return NotFound();

        if (dto.CategoryId.HasValue && !await db.Categories.AnyAsync(c => c.Id == dto.CategoryId && c.UserId == UserId))
            return BadRequest(new { message = "Categoria inválida." });

        transaction.Name = dto.Name;
        transaction.CategoryId = dto.CategoryId;

        await db.SaveChangesAsync();

        // Recarrega a categoria pro DTO de resposta refletir a mudança (o Include acima ainda
        // aponta pra categoria antiga depois de só trocar o CategoryId na entidade).
        await db.Entry(transaction).Reference(t => t.Category).LoadAsync();
        return Ok(TransactionDto.FromEntity(transaction, await UserTodayAsync(db)));
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
