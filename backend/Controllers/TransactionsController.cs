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

        // Limite da listagem legada. Relatórios e extratos paginados usam /report.
        const int MaxResults = 2000;

        var result = await ProjectToDto(query.OrderByDescending(t => t.DueDate).ThenBy(t => t.Id).Take(MaxResults)).ToListAsync();

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

    // O /report soma e decifra o histórico inteiro do filtro para os totais; o dashboard só mostra
    // as últimas linhas de um cartão a cada abertura, então lê apenas essas, pelo índice
    // (UserId, BankAccountId, DueDate).
    [HttpGet("recent")]
    public async Task<IActionResult> GetRecent([FromQuery] Guid? bankAccountId, [FromQuery] int limit = 5, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 10);
        var today = await UserTodayAsync(db, ct);
        var query = FilteredQuery(bankAccountId, null, null, today);

        var items = await ProjectToDto(ApplySort(query, "dueDate", desc: true, today: today).Take(limit)).ToListAsync(ct);

        return Ok(WithComputedFields(items, today));
    }

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
        [FromQuery] int pageSize = 25,
        [FromQuery] bool excludeTransfers = false,
        [FromQuery] string summary = "full", CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        if (summary is not ("full" or "totals" or "items")) return BadRequest();
        var offset = PageOffset(page, pageSize);
        var desc = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);

        var today = await UserTodayAsync(db, ct);
        var query = FilteredQuery(bankAccountId, status, type, today);

        // Receitas e despesas (CNPJ) lista o mês sem as transferências entre contas próprias, a mesma
        // base dos agregados mensais; o Relatórios mostra elas com selo e por isso não passa o filtro.
        // Filtrar antes da contagem mantém totalCount e páginas coerentes com o que é exibido.
        if (excludeTransfers)
            query = query.WithoutSelfTransfers();

        // year/month chegam como inteiros (não como DateTime cru na query string) de propósito:
        // o model binder de DateTime da ASP.NET não garante Kind=Utc a partir de string, e essa
        // ambiguidade já causou bug de fronteira de mês em outro lugar do dashboard. Montar o
        // intervalo aqui, igual o DashboardController faz, evita a mesma armadilha.
        if (year.HasValue)
        {
            if (year is < 1 or > 9998 || month is < 1 or > 12)
                return BadRequest(new { message = "Ano ou mês inválido." });

            var start = new DateTime(year.Value, month ?? 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = month.HasValue ? start.AddMonths(1) : start.AddYears(1);
            query = query.Where(t => t.DueDate >= start && t.DueDate < end);
        }

        // Amount é criptografado (guardado como texto): "ORDER BY"/"SUM" direto na coluna
        // cifrada ou dá erro de SQL, ou (no caso do ORDER BY) roda sem erro nenhum e devolve uma
        // ordem sem sentido, porque estaria ordenando o texto cifrado, não o valor real. Por
        // isso, quando a ordenação pedida é por valor, materializa (descriptografando) todo o
        // filtro, ordena, conta e soma em memória, e só então corta a página pedida.

        List<TransactionDto> items;
        decimal totalIncome = 0;
        decimal totalExpense = 0;

        int totalCount;

        // Ordenações cifradas precisam do conjunto completo. Para busca em outra ordem,
        // o banco ordena e a aplicação descarta cada nome que não passa no filtro.
        var sortAmount = string.Equals(sortBy, "amount", StringComparison.OrdinalIgnoreCase);
        var sortEncrypted = sortAmount || string.Equals(sortBy, "name", StringComparison.OrdinalIgnoreCase);

        var wantsDayTotals = summary == "full" && string.Equals(sortBy, "dueDate", StringComparison.OrdinalIgnoreCase);
        List<ReportDayTotalDto> dayTotals = [];

        if (sortEncrypted)
        {
            var all = await ProjectToDto(query).ToListAsync(ct);
            if (!string.IsNullOrWhiteSpace(search))
                all = all.Where(t => MatchesSearch(t.Name, search)).ToList();
            totalCount = all.Count;
            items = WithComputedFields(SortEncrypted(all, sortAmount, desc).Skip(offset).Take(pageSize).ToList(), today);

            if (summary != "items")
            {
                var sums = new ReportSums([]);
                foreach (var t in all) sums.Add(t.DueDate, t.Type == nameof(TransactionType.Income), t.Amount, t.IsTransfer);
                (totalIncome, totalExpense, dayTotals) = sums.Result();
            }
        }
        else if (!string.IsNullOrWhiteSpace(search))
        {
            totalCount = 0;
            items = new List<TransactionDto>(pageSize);
            var sums = new ReportSums([]);
            var currentDay = DateTime.MinValue;
            decimal dayIncome = 0, dayExpense = 0;
            var dayOnPage = false;
            var rows = ProjectToDto(ApplySort(query, sortBy, desc, today)).AsAsyncEnumerable();
            await foreach (var row in rows.WithCancellation(ct))
            {
                if (!MatchesSearch(row.Name, search)) continue;
                var onPage = totalCount >= offset && items.Count < pageSize;
                if (onPage) items.Add(row);
                totalCount++;
                if (summary == "items") continue;
                var isIncome = row.Type == nameof(TransactionType.Income);
                sums.Add(row.DueDate, isIncome, row.Amount, row.IsTransfer);
                if (!wantsDayTotals) continue;
                if (row.DueDate.Date != currentDay)
                {
                    if (dayOnPage) dayTotals.Add(new(currentDay, dayIncome, dayExpense));
                    currentDay = DateTime.SpecifyKind(row.DueDate.Date, DateTimeKind.Utc);
                    dayIncome = dayExpense = 0;
                    dayOnPage = false;
                }
                dayOnPage |= onPage;
                if (row.IsTransfer) continue;
                if (isIncome) dayIncome += row.Amount;
                else dayExpense += row.Amount;
            }
            if (dayOnPage) dayTotals.Add(new(currentDay, dayIncome, dayExpense));
            dayTotals.Sort((a, b) => a.Date.CompareTo(b.Date));
            items = WithComputedFields(items, today);
            (totalIncome, totalExpense, _) = sums.Result();
        }
        else
        {
            await using var snapshot = db.Database.IsNpgsql()
                ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct) : null;
            var ordered = ApplySort(query, sortBy, desc, today);
            items = WithComputedFields(await ProjectToDto(ordered.Skip(offset).Take(pageSize)).ToListAsync(ct), today);
            if (summary == "items")
                totalCount = await query.CountAsync(ct);
            else
            {
                var sums = new ReportSums(wantsDayTotals ? items : []);
                totalCount = 0;
                var rows = query.Select(t => new { t.DueDate, t.Type, t.Amount, t.PolpMovementKind }).AsAsyncEnumerable();
                await foreach (var t in rows.WithCancellation(ct))
                {
                    totalCount++;
                    sums.Add(t.DueDate, t.Type == TransactionType.Income, t.Amount, TransferDetection.IsSelfTransfer(t.PolpMovementKind));
                }
                (totalIncome, totalExpense, dayTotals) = sums.Result();
            }
            if (snapshot != null) await snapshot.CommitAsync(ct);
        }

        if (summary == "items") return Ok(new { items, totalCount });
        if (summary == "totals") return Ok(new { items, totalCount, totalIncome, totalExpense });
        return Ok(new TransactionReportDto(items, totalCount, totalIncome, totalExpense, dayTotals));
    }

    private static readonly Regex InstallmentSuffix = new(@"([0-9]{2}/[0-9]{2})\z", RegexOptions.Compiled);
    private static readonly char[] JsTrimChars =
        [' ', '\t', '\n', '\v', '\f', '\r', '\u00A0', '\u1680', '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005',
         '\u2006', '\u2007', '\u2008', '\u2009', '\u200A', '\u2028', '\u2029', '\u202F', '\u205F', '\u3000', '\uFEFF'];

    private static string EstablishmentKey(string name) =>
        InstallmentSuffix.IsMatch(name) ? InstallmentSuffix.Replace(name, "").Trim(JsTrimChars) : name;

    private const int MaxDailyBuckets = 62;

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
        var countsLoans = await CountsLoanDisbursementAsync(ct);
        var rows = FilteredQuery(bankAccountId, null, null, today)
            .CountedInTotals(countsLoans)
            .Where(t => t.DueDate >= start && t.DueDate < end)
            .OrderByDescending(t => t.DueDate)
            .ThenBy(t => t.Id)
            .Select(t => new { t.Type, t.Amount, t.Name, t.DueDate, CategoryName = t.Category != null ? t.Category.Name : null, t.PolpMovementKind })
            .AsNoTracking()
            .AsAsyncEnumerable();

        var months = new Dictionary<(int Year, int Month), (decimal Income, decimal Expense, int Count)>();
        var days = new Dictionary<DateOnly, (decimal Income, decimal Expense)>();
        var categories = new Dictionary<(string? Name, TransactionType Type), (decimal Amount, int Count)>();
        var establishments = new Dictionary<(string? Category, TransactionType Type, string Key), (decimal Amount, int Count, bool IsInstallment)>();
        decimal totalIncome = 0, totalExpense = 0;
        var count = 0;

        await foreach (var t in rows.WithCancellation(ct))
        {
            if (!TransferDetection.CountsInTotals(t.PolpMovementKind, t.Type, countsLoans)) continue;

            var categoryName = TransferDetection.CategoryBucket(t.PolpMovementKind, string.IsNullOrEmpty(t.CategoryName) ? null : t.CategoryName);
            var monthKey = (t.DueDate.Year, t.DueDate.Month);
            months.TryGetValue(monthKey, out var m);
            var isIncome = t.Type == TransactionType.Income;
            months[monthKey] = (m.Income + (isIncome ? t.Amount : 0), m.Expense + (isIncome ? 0 : t.Amount), m.Count + 1);
            var dayKey = DateOnly.FromDateTime(t.DueDate);
            days.TryGetValue(dayKey, out var d);
            days[dayKey] = (d.Income + (isIncome ? t.Amount : 0), d.Expense + (isIncome ? 0 : t.Amount));

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

        var dayList = new List<AggregateDayDto>();
        if ((end - start).TotalDays <= MaxDailyBuckets)
        {
            for (var cursor = DateOnly.FromDateTime(start); cursor < DateOnly.FromDateTime(end); cursor = cursor.AddDays(1))
            {
                days.TryGetValue(cursor, out var d);
                dayList.Add(new AggregateDayDto(cursor, d.Income, d.Expense));
            }
        }

        var categoryList = categories
            .Select(kv => new AggregateCategoryDto(kv.Key.Name, kv.Key.Type.ToString(), kv.Value.Amount, kv.Value.Count))
            .OrderByDescending(c => c.Amount)
            .ToList();

        var establishmentList = establishments
            .Select(kv => new AggregateEstablishmentDto(kv.Key.Key, kv.Key.Key, kv.Key.Category, kv.Key.Type.ToString(), kv.Value.Amount, kv.Value.Count, kv.Value.IsInstallment))
            .OrderByDescending(e => e.Amount)
            .ToList();

        return Ok(new TransactionAggregatesDto(totalIncome, totalExpense, count, monthList, categoryList, establishmentList, dayList));
    }

    private sealed class ReportSums(IEnumerable<TransactionDto> pageDays)
    {
        private readonly Dictionary<DateTime, (decimal Income, decimal Expense)> days = pageDays
            .Select(t => t.DueDate.Date).Distinct().ToDictionary(d => d, _ => (0m, 0m));
        private decimal income;
        private decimal expense;

        public void Add(DateTime date, bool isIncome, decimal amount, bool isTransfer)
        {
            if (isTransfer) return;
            if (isIncome) income += amount;
            else expense += amount;
            if (days.TryGetValue(date.Date, out var day))
                days[date.Date] = isIncome ? (day.Income + amount, day.Expense) : (day.Income, day.Expense + amount);
        }

        public (decimal, decimal, List<ReportDayTotalDto>) Result() =>
            (income, expense, days.OrderBy(d => d.Key).Select(d =>
                new ReportDayTotalDto(DateTime.SpecifyKind(d.Key, DateTimeKind.Utc), d.Value.Income, d.Value.Expense)).ToList());
    }

    private static bool MatchesSearch(string name, string search) => TextSearch.Matches(name, search);

    private static IEnumerable<TransactionDto> SortEncrypted(List<TransactionDto> items, bool amount, bool desc)
    {
        if (amount)
            return (desc ? items.OrderByDescending(t => t.Amount) : items.OrderBy(t => t.Amount)).ThenBy(t => t.Id);
        var comparer = StringComparer.Create(CultureInfo.GetCultureInfo("pt-BR"), ignoreCase: true);
        return (desc ? items.OrderByDescending(t => t.Name, comparer) : items.OrderBy(t => t.Name, comparer)).ThenBy(t => t.Id);
    }

    [HttpGet("details")]
    public async Task<IActionResult> GetDetails(
        [FromQuery] Guid? bankAccountId,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? type,
        [FromQuery] string? category,
        [FromQuery] string? establishment,
        CancellationToken ct,
        [FromQuery] string? cursor = null,
        [FromQuery] bool paged = false)
    {
        const int size = 25;
        var (start, end, rangeError) = ParseRange(from, to);
        if (rangeError is not null) return BadRequest(new { message = rangeError });

        var today = await UserTodayAsync(db, ct);
        var countsLoans = await CountsLoanDisbursementAsync(ct);
        var query = FilteredQuery(bankAccountId, null, type, today).AsNoTracking().CountedInTotals(countsLoans)
            .Where(t => t.DueDate >= start && t.DueDate < end);
        // O model binder converte texto vazio em null. Uma chave vazia de estabelecimento é válida
        // (por exemplo, uma descrição composta apenas pelo sufixo de parcelamento).
        if (establishment == null && Request.Query.TryGetValue("establishment", out var rawName))
            establishment = rawName.ToString();
        if (category == null && Request.Query.TryGetValue("category", out var rawCategory))
            category = rawCategory.ToString();
        if (category != null)
            query = query.Where(t => (t.PolpMovementKind == PolpMovementKind.LoanDisbursement
                || (t.Category != null && TransferDetection.LegacyLoanCategoryNames.Contains(t.Category.Name))
                ? TransferDetection.LoanCategoryName
                : t.Category == null || t.Category.Name == "" ? "Sem categoria" : t.Category.Name) == category);

        if (cursor != null)
        {
            var parts = cursor.Split('.', 2);
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks > DateTime.MaxValue.Ticks || !Guid.TryParseExact(parts[1], "N", out var id))
                return BadRequest(new { message = "Cursor inválido." });
            var date = new DateTime(ticks, DateTimeKind.Utc);
            query = query.Where(t => t.DueDate < date || (t.DueDate == date && t.Id.CompareTo(id) > 0));
        }

        // A seleção por nome e a projeção final precisam observar a mesma categoria, data e descrição.
        await using var snapshot = establishment != null && db.Database.IsNpgsql()
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct) : null;
        if (establishment != null)
        {
            var matches = new List<Guid>(size + 1);
            var names = query.OrderByDescending(t => t.DueDate).ThenBy(t => t.Id)
                .Select(t => new { t.Id, t.Name }).AsAsyncEnumerable();
            await foreach (var row in names.WithCancellation(ct))
            {
                if (EstablishmentKey(row.Name) != establishment) continue;
                matches.Add(row.Id);
                if (matches.Count > size) break;
            }
            query = query.Where(t => matches.Contains(t.Id));
        }

        var rows = await ProjectToDto(query.OrderByDescending(t => t.DueDate).ThenBy(t => t.Id).Take(size + 1))
            .ToListAsync(ct);
        if (snapshot != null) await snapshot.CommitAsync(ct);
        var items = WithComputedFields(rows.Take(size).Select(t => t with
        {
            CategoryName = TransferDetection.CategoryBucket(t.IsTransfer ? PolpMovementKind.LoanDisbursement : PolpMovementKind.None, t.CategoryName),
            IsTransfer = false
        }).ToList(), today);
        var nextCursor = rows.Count > size ? FormattableString.Invariant($"{items[^1].DueDate.Ticks}.{items[^1].Id:N}") : null;
        if (paged) return Ok(new { items, nextCursor });
        if (nextCursor != null) Response.Headers["X-Next-Cursor"] = nextCursor;
        return Ok(items);
    }

    private Task<bool> CountsLoanDisbursementAsync(CancellationToken ct) =>
        db.Users.AnyAsync(u => u.Id == UserId && u.DocumentType != DocumentTypes.Cnpj, ct);

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
    private static IQueryable<Transaction> ApplySort(IQueryable<Transaction> query, string sortBy, bool desc, DateTime today) => sortBy switch
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
            ? query.OrderByDescending(t => t.Status == TransactionStatus.Pending && t.DueDate < today ? TransactionStatus.Overdue : t.Status).ThenBy(t => t.Id)
            : query.OrderBy(t => t.Status == TransactionStatus.Pending && t.DueDate < today ? TransactionStatus.Overdue : t.Status).ThenBy(t => t.Id),
        _ => desc
            ? query.OrderByDescending(t => t.DueDate).ThenBy(t => t.Id)
            : query.OrderBy(t => t.DueDate).ThenBy(t => t.Id),
    };

    // Só lê o que o sync já gravou: abrir um lançamento nunca gera consulta ao banco/Polp.
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var t = await db.Transactions
            .AsNoTracking()
            .Include(x => x.Category)
            .Include(x => x.BankAccount)
            .Where(x => x.UserId == UserId && x.BankAccount != null && x.BankAccount.IsActive && x.BankAccount.PolpAccountId != null)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null || !Guid.TryParse(t.Description, out _)) return NotFound();

        var account = t.BankAccount!;
        var isCard = CreditCardAccountDetector.LooksLikeCreditCard(account);
        var integration = account.PolpIntegrationId is { } consentId
            ? await db.PolpIntegrations.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == UserId && p.PolpIntegrationId == consentId, ct)
            : null;
        var today = await UserTodayAsync(db, ct);

        // Nome editável (Name) nunca entra aqui: a outra parte vem da Polp ou, sem ela, da descrição original.
        var hasCounterparty = t.CounterpartyAlias is not null || t.CounterpartyName is not null;
        string? described = null;
        if (!hasCounterparty && t.PolpMovementKind != PolpMovementKind.CardBillPayment)
            described = isCard
                ? t.BankOperationType is null or "PAGAMENTO" or "ESTORNO" ? DescribedCounterparty.FromCard(t.BankDescription) : null
                : DescribedCounterparty.FromAccount(t.BankDescription, t.Type == TransactionType.Income);

        return Ok(new TransactionDetailDto(
            t.Id, t.Name, t.Type.ToString(), TransactionDto.EffectiveStatus(t.Status, t.DueDate, today),
            t.Amount, t.DueDate, t.PaidAt, t.CreatedAt,
            t.Category?.Name, t.Category?.Color, t.IsFixed, t.PolpMovementKind.ToString(),
            new TransactionAccountDto(account.Name, account.Number, isCard, integration?.InstitutionName, integration?.LastSyncedAt),
            new TransactionBankDataDto(
                t.BankDateTime, t.BankOperationType, t.BankDescription, t.CounterpartyAlias, t.CounterpartyName, described,
                t.CounterpartyCnpj, t.CounterpartyPersonType,
                t.CounterpartyBankCode, t.CounterpartyAccount, t.CardNumber,
                t.InstallmentNumber, t.InstallmentTotal, t.OriginalAmount, t.OriginalCurrency, t.BillMonth,
                await DetailsStatusAsync(t, integration, ct))));
    }

    // "ready" quando a releitura do histórico já terminou, mesmo que a Polp não tenha devolvido
    // esta transação: daí em diante a tela mostra "não informado", nunca "em importação" para sempre.
    private async Task<string> DetailsStatusAsync(Transaction t, PolpIntegration? integration, CancellationToken ct)
    {
        if (t.BankDescription is not null || t.BankOperationType is not null) return "ready";
        if (integration is null || integration.TransactionDetailsVersion >= PolpSyncService.TransactionDetailsVersion) return "ready";

        var consentId = integration.PolpIntegrationId.ToString();
        var failing = await db.PolpWebhookEvents.AsNoTracking().AnyAsync(e =>
            e.Event == PolpInternalEvents.TransactionDetails && e.ResourceId == consentId
            && (e.Status == PolpWebhookEventStatus.Pending || e.Status == PolpWebhookEventStatus.Processing)
            && e.Attempts > 0 && e.LastError != null, ct);
        return failing ? "delayed" : "importing";
    }

    // Não há criação nem edição livre de lançamento: receitas, despesas e transferências só nascem e
    // mudam pelo sync da Polp (upsert pelo id da Polp em Description). O usuário só personaliza o que
    // o sync preserva (nome e categoria, ver SetDetails) e a marca de gasto fixo. Antes existiam
    // POST /transactions e PUT /transactions/{id}; o PUT sobrescrevia valor, data, tipo, conta e até
    // o Description, o que quebrava a reconciliação com a Polp.

    // Só a marca de gasto recorrente: personalização do app, não dado do banco.
    [HttpPut("{id}/fixed")]
    public async Task<IActionResult> SetFixed(Guid id, [FromBody] SetTransactionFixedDto dto)
    {
        var transaction = await db.Transactions
            .Include(t => t.Category)
            .Include(t => t.BankAccount)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId && (t.BankAccount == null || t.BankAccount.IsActive));
        if (transaction is null) return NotFound();

        transaction.IsFixed = dto.IsFixed;
        await db.SaveChangesAsync();
        return Ok(TransactionDto.FromEntity(transaction, await UserTodayAsync(db)));
    }

    // Nome e categoria são os únicos campos que o usuário corrige (ex.: categorização errada vinda da
    // Polp). O sync respeita a correção: só completa nome/categoria que ainda são os automáticos.
    [HttpPut("{id}/details")]
    public async Task<IActionResult> SetDetails(Guid id, [FromBody] SetTransactionDetailsDto dto)
    {
        var transaction = await db.Transactions
            .Include(t => t.Category)
            .Include(t => t.BankAccount)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId && (t.BankAccount == null || t.BankAccount.IsActive));
        if (transaction is null) return NotFound();

        if (dto.CategoryId.HasValue && !await db.Categories.AnyAsync(c => c.Id == dto.CategoryId && c.UserId == UserId))
            return BadRequest(new { message = "Categoria inválida." });

        transaction.Name = dto.Name.Trim();
        transaction.CategoryId = dto.CategoryId;

        await db.SaveChangesAsync();

        // Recarrega a categoria pro DTO de resposta refletir a mudança (o Include acima ainda
        // aponta pra categoria antiga depois de só trocar o CategoryId na entidade).
        await db.Entry(transaction).Reference(t => t.Category).LoadAsync();
        return Ok(TransactionDto.FromEntity(transaction, await UserTodayAsync(db)));
    }

    // Só sobra para apagar lançamento manual antigo (de antes de o app ser só automático). Linha
    // importada tem o id da Polp em Description: apagar mudaria os totais com dado do banco e o
    // próximo webhook da mesma janela a recriaria.
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var transaction = await db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == UserId);
        if (transaction is null) return NotFound();

        if (IsImported(transaction))
            return Conflict(new { message = "Movimentações importadas do banco não podem ser apagadas." });

        db.Transactions.Remove(transaction);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static bool IsImported(Transaction t) => Guid.TryParse(t.Description, out _);
}
