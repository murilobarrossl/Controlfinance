using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ControlFinance.API.Services;

// Trabalho pesado de puxar contas, cartões, faturas e transações da Polp (API v2) e gravar no
// banco. Extraído do controller pra poder rodar em background, fora do ciclo de vida da
// requisição HTTP que disparou o sync: uma chamada à Polp já levou de 800ms a 20s nos logs, e
// somando várias contas e páginas de transações o total passa fácil dos 30-60s que a maioria dos
// gateways tolera antes de devolver 504.
//
// O fluxo normal é orientado a webhook: a Polp sincroniza por agenda com a instituição e avisa;
// cada aviso vira uma das operações pontuais abaixo, limitada à janela que ela informou. O sync
// completo (SyncOneAsync) só roda uma vez, na conexão (backfill inicial).
public class PolpSyncService(AppDbContext db, IPolpService polp, IConfiguration config, InstitutionNameResolver institutionNames, TimeProvider time)
{
    // Serializa syncs concorrentes da mesma integração (ex.: dois webhooks seguidos, ou um webhook
    // cruzando com o backfill): sem isso, duas execuções liam o mesmo snapshot de "transações já
    // existentes" antes de qualquer uma salvar, e cada uma inseria a mesma transação de novo.
    // Estático porque esse serviço é recriado a cada escopo de DI; a chave é o Guid local da
    // integração (PolpIntegration.Id).
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SyncLocks = new();

    public static async Task<int> SyncOneWithLockAsync(Func<Task<int>> syncOne, Guid integrationId, CancellationToken ct)
    {
        var syncLock = SyncLocks.GetOrAdd(integrationId, _ => new SemaphoreSlim(1, 1));
        await syncLock.WaitAsync(ct);
        try
        {
            return await syncOne();
        }
        finally
        {
            syncLock.Release();
        }
    }

    /// <summary>Relê o consentimento na Polp e guarda status, execution_status e o nome da
    /// instituição. PARTIAL_SUCCESS é só enriquecimento pendente: não bloqueia nada. Devolve true
    /// se o consentimento já está autorizado.</summary>
    public async Task<bool> RefreshConsentAsync(PolpIntegration local, CancellationToken ct)
    {
        var remote = await polp.GetConsentAsync(local.PolpIntegrationId, ct);

        local.Status = remote.Status;
        local.ExecutionStatus = remote.ExecutionStatus;
        if (!string.IsNullOrWhiteSpace(remote.Institution?.Name)) local.InstitutionName = remote.Institution.Name;
        local.InstitutionName ??= await institutionNames.FindAsync(local.InstitutionId, ct);
        local.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return remote.Status == "AUTHORISED";
    }

    /// <summary>Backfill inicial: contas, cartões, faturas e todo o histórico de transações.</summary>
    public async Task<int> SyncOneAsync(PolpIntegration local, Guid userId, CancellationToken ct)
    {
        // Relê dentro do lock: o usuário pode ter desconectado esse banco entre o disparo do sync
        // e agora. Sem essa checagem, um sync que já estava na fila recriava as contas e
        // transações que a desconexão tinha acabado de apagar.
        await db.Entry(local).ReloadAsync(ct);
        if (db.Entry(local).State == EntityState.Detached || local.Status == PolpDisconnectService.PendingStatus)
            return 0;

        var accounts = await SyncAccountsAndCardsAsync(local, userId, null, ct);

        // O consentimento já chega AUTHORISED/SUCCESS antes de a Polp terminar de gerar as contas
        // (no sandbox, ~2s depois): lista vazia aqui não é "banco sem contas", é cedo demais.
        // Marcar como sincronizado travaria a conexão sem nenhuma conta para sempre.
        if (accounts.Count == 0)
            return 0;

        foreach (var card in accounts.Where(CreditCardAccountDetector.LooksLikeCreditCard))
            await SyncBillsAsync(card, null, ct);

        await SyncTransactionsAsync(userId, accounts, null, ct);

        local.SyncedLocally = true;
        local.UpdatedAt = DateTime.UtcNow;
        local.LastSyncedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return accounts.Count;
    }

    /// <summary>Webhooks accounts / credit_cards: cria ou atualiza contas e cartões do
    /// consentimento (dentro da janela informada, se houver).</summary>
    public async Task<List<BankAccount>> SyncAccountsAndCardsAsync(
        PolpIntegration local, Guid userId, PolpQueryWindow? window, CancellationToken ct)
    {
        await EnsureDefaultCategoriesAsync(userId, ct);

        // Carrega as contas já existentes do usuário de uma vez (por PolpAccountId) em vez de
        // uma query por recurso remoto: evita N+1 quando a conexão tem várias contas.
        var existing = await db.BankAccounts
            .Where(b => b.UserId == userId && b.PolpAccountId != null)
            .ToDictionaryAsync(b => b.PolpAccountId!.Value, b => b, ct);

        // Chute inicial do Radar de Recorrências pra conta nova (CNPJ = Business, CPF = Personal,
        // ver AccountOwnershipDefault).
        var ownerDocumentType = await db.Users.Where(u => u.Id == userId).Select(u => u.DocumentType).FirstAsync(ct);
        local.InstitutionName ??= await institutionNames.FindAsync(local.InstitutionId, ct);
        var bankName = local.InstitutionName ?? "Banco";

        var touched = new List<BankAccount>();

        foreach (var remote in await polp.GetAccountsAsync(local.PolpIntegrationId, window, ct))
        {
            if (!existing.TryGetValue(remote.Id, out var account))
            {
                account = new BankAccount
                {
                    UserId = userId,
                    // O nome não é tocado nas atualizações seguintes: o usuário pode ter renomeado.
                    Name = $"{bankName} · {AccountKindLabel(remote.Type)}",
                    BankCode = local.InstitutionId.ToString(),
                    PolpAccountId = remote.Id,
                    PolpIntegrationId = local.PolpIntegrationId,
                    Ownership = AccountOwnershipDefault.FromDocumentType(ownerDocumentType)
                };
                db.BankAccounts.Add(account);
                existing[remote.Id] = account;
            }

            // Saldo null = ainda não sincronizado na Polp: mantém o que já temos em vez de zerar.
            if (remote.Balance?.AvailableAmount?.Value is { } balance) account.Balance = balance;
            account.PolpAccountType = remote.Type;
            account.PolpAccountSubtype = remote.Subtype;
            account.Number = AccountNumberMask.Mask($"{remote.Number}{remote.CheckDigit}");
            touched.Add(account);
        }

        foreach (var remote in await polp.GetCreditCardsAsync(local.PolpIntegrationId, window, ct))
        {
            if (!existing.TryGetValue(remote.Id, out var card))
            {
                card = new BankAccount
                {
                    UserId = userId,
                    Name = remote.Name ?? $"{bankName} · Cartão de crédito",
                    BankCode = local.InstitutionId.ToString(),
                    PolpAccountId = remote.Id,
                    PolpIntegrationId = local.PolpIntegrationId,
                    Ownership = AccountOwnershipDefault.FromDocumentType(ownerDocumentType)
                };
                db.BankAccounts.Add(card);
                existing[remote.Id] = card;
            }

            // A v2 não separa conta de cartão por "type": o cartão é um recurso próprio, então
            // o tipo é fixado aqui para o resto do app (CreditCardAccountDetector) reconhecê-lo.
            card.PolpAccountType = CreditCardAccountDetector.CardType;
            card.ProductType = remote.ProductType;
            card.Brand = remote.CreditCardNetwork;
            card.Number = AccountNumberMask.Mask(remote.PaymentMethods.FirstOrDefault()?.IdentificationNumber);

            var limit = remote.Limits.FirstOrDefault(l => l.CreditLineLimitType == "LIMITE_CREDITO_TOTAL")
                ?? remote.Limits.FirstOrDefault();
            if (limit is not null)
            {
                card.CreditLimit = limit.LimitAmount?.Value ?? card.CreditLimit;
                card.UsedLimit = limit.UsedAmount?.Value ?? card.UsedLimit;
            }
            touched.Add(card);
        }

        await db.SaveChangesAsync(ct);
        return touched;
    }

    /// <summary>Webhook bills: fatura atual, dia de fechamento e de vencimento do cartão. Esses
    /// dias não existem no recurso do cartão na v2; só as faturas os trazem. Falha pontual na
    /// Polp não trava o resto: o cartão segue sem fatura e a UI pede pra completar à mão.</summary>
    public async Task SyncBillsAsync(BankAccount card, PolpQueryWindow? window, CancellationToken ct)
    {
        if (card.PolpAccountId is not Guid cardId) return;

        List<PolpBillDto> bills;
        try
        {
            bills = await polp.GetBillsAsync(cardId, window, ct);
        }
        catch (HttpRequestException)
        {
            return;
        }

        // Fatura "atual" = a de vencimento mais próximo que ainda não passou; se todas já
        // venceram (ou não vier nenhuma), cai pra mais recente disponível. "Hoje" é o do dono do
        // cartão: em UTC, a fatura que vence hoje já contava como vencida a partir das 21h de Brasília.
        var today = await UserClock.TodayForUserAsync(db, card.UserId, time, ct);
        var parsed = bills
            .Select(b => (bill: b, due: ParseDateOrNull(b.DueDate)))
            .Where(x => x.due.HasValue)
            .OrderBy(x => x.due)
            .ToList();
        if (parsed.Count == 0) return;

        var current = parsed.FirstOrDefault(x => x.due!.Value.Date >= today);
        if (current.bill is null) current = parsed[^1];

        card.CurrentInvoiceAmount = current.bill.BillTotalAmount?.Value ?? card.CurrentInvoiceAmount;
        card.InvoiceDueDate = current.due;
        card.DueDay = current.due!.Value.Day;
        if (ParseDateOrNull(current.bill.BillClosingDate) is { } closing) card.ClosingDay = closing.Day;

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Webhooks accounts.transactions / credit_cards.transactions (e backfill): upsert por
    /// id da Polp, que é estável (o mesmo lançamento mantém o id ao passar de aberto para
    /// faturado). Counterparty e categoria chegam depois, num novo webhook; por isso o upsert
    /// também completa nome e categoria, sem sobrescrever o que o usuário já editou.</summary>
    public async Task SyncTransactionsAsync(
        Guid userId, IReadOnlyCollection<BankAccount> accounts, PolpQueryWindow? window, CancellationToken ct)
    {
        if (accounts.Count == 0) return;

        // Categorias do usuário carregadas uma única vez (entidade completa, não só o id: precisa
        // pra poder corrigir a cor de categorias antigas presas no cinza legado); novas categorias
        // entram no mesmo dicionário conforme são criadas, sem round-trip ao banco por transação.
        //
        // GroupBy + First em vez de ToDictionaryAsync: duas execuções concorrentes deste método
        // podiam criar a mesma categoria em paralelo antes de qualquer uma commitar, gerando duas
        // linhas com o mesmo Name, e ToDictionaryAsync estourava ArgumentException na segunda
        // ocorrência. Fica com a mais antiga de cada nome.
        var categoriesByName = (await db.Categories
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync(ct))
            .GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => g.First());

        var accountIds = accounts.Select(a => a.Id).ToList();
        var existingByKey = (await db.Transactions
                .Where(t => t.UserId == userId && t.BankAccountId != null && accountIds.Contains(t.BankAccountId.Value))
                .ToListAsync(ct))
            .ToDictionary(t => (t.BankAccountId!.Value, t.Description), t => t);

        // Prazo máximo de histórico (opt-in, DataRetention:SyncedHistoryMonths): o que é mais
        // antigo que isso nem entra no banco (minimização na origem), em vez de ser gravado pra
        // ser apagado depois pelo DataRetentionService.
        var historyCutoff = DataRetentionPolicy.SyncedHistoryCutoff(config, DateTime.UtcNow);
        categoriesByName.TryGetValue(UncategorizedFallbackName, out var fallbackCategory);

        foreach (var account in accounts)
        {
            if (account.PolpAccountId is not Guid remoteId) continue;

            List<RemoteTransaction> remote;
            try
            {
                remote = CreditCardAccountDetector.LooksLikeCreditCard(account)
                    ? (await polp.GetCardTransactionsAsync(remoteId, window, ct)).Select(RemoteTransaction.From).ToList()
                    : (await polp.GetAccountTransactionsAsync(remoteId, window, ct)).Select(RemoteTransaction.From).ToList();
            }
            catch (HttpRequestException)
            {
                continue; // não trava o sync inteiro por causa de uma conta com falha
            }

            foreach (var rt in remote)
            {
                var polpTransactionId = rt.Id.ToString();
                var dueDate = ParseDateAsUtc(rt.DateTime);
                var paidAt = rt.IsPending ? (DateTime?)null : dueDate;
                var status = rt.IsPending ? TransactionStatus.Pending : TransactionStatus.Paid;
                var type = rt.IsCredit ? TransactionType.Income : TransactionType.Expense;
                var categoryName = PolpCategoryMap.ToCategoryName(rt.CategoryRef);

                if (historyCutoff.HasValue && dueDate < historyCutoff.Value && !rt.IsPending)
                    continue;

                if (existingByKey.TryGetValue((account.Id, polpTransactionId), out var existing))
                {
                    // Já sincronizada antes, mas a Polp pode corrigir valor/status depois
                    // (pré-autorização de cartão que confirma com outro valor, ou lançamento
                    // futuro que entra na fatura). Sem atualizar aqui o valor ficava congelado.
                    existing.Amount = rt.Amount;
                    existing.Type = type;
                    existing.Status = status;
                    existing.DueDate = dueDate;
                    existing.PaidAt = paidAt;

                    // Enriquecimento tardio (counterparty/categoria chegam depois do lançamento).
                    // Só completa o que ainda é o valor automático: nome igual ao que veio do
                    // banco e categoria ainda no fallback "Outros" — recategorização ou
                    // renomeação feita no app nunca é sobrescrita.
                    if (rt.EnrichedName is not null && existing.Name == rt.RawName)
                        existing.Name = rt.EnrichedName;
                    if (categoryName != UncategorizedFallbackName
                        && (existing.CategoryId is null || existing.CategoryId == fallbackCategory?.Id))
                        existing.CategoryId = await ResolveCategoryIdAsync(categoriesByName, categoryName, null, userId, ct);
                    continue;
                }

                var transaction = new Transaction
                {
                    UserId = userId,
                    BankAccountId = account.Id,
                    CategoryId = await ResolveCategoryIdAsync(categoriesByName, categoryName, null, userId, ct),
                    Name = rt.EnrichedName ?? rt.RawName,
                    Description = polpTransactionId, // guarda o id da Polp para evitar duplicar em re-syncs
                    Type = type,
                    Status = status,
                    Amount = rt.Amount,
                    DueDate = dueDate,
                    PaidAt = paidAt
                };
                db.Transactions.Add(transaction);
                existingByKey[(account.Id, polpTransactionId)] = transaction;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // Visão comum de transação de conta e de cartão, só com o que o upsert precisa.
    private sealed record RemoteTransaction(
        Guid Id, string RawName, string? EnrichedName, decimal Amount, bool IsCredit, bool IsPending,
        string? DateTime, string? CategoryRef)
    {
        public static RemoteTransaction From(PolpAccountTransactionDto t) => new(
            t.Id,
            t.TransactionName ?? "Transação",
            t.Counterparty?.Alias ?? t.Counterparty?.Name,
            Math.Abs(t.TransactionAmount?.Value ?? 0),
            string.Equals(t.CreditDebitType, "CREDITO", StringComparison.OrdinalIgnoreCase),
            false,
            t.TransactionDateTime,
            t.CategoryRef);

        public static RemoteTransaction From(PolpCardTransactionDto t) => new(
            t.Id,
            t.TransactionName ?? "Transação",
            t.Counterparty?.Alias ?? t.Counterparty?.Name,
            Math.Abs(t.BrazilianAmount?.Value ?? 0),
            string.Equals(t.CreditDebitType, "CREDITO", StringComparison.OrdinalIgnoreCase),
            // Sem fatura e com a data-sentinela: lançamento futuro (parcela ou agendado).
            t.BillId is null && (t.BillPostDate?.StartsWith("0001-01-01", StringComparison.Ordinal) ?? true),
            t.TransactionDateTime,
            t.CategoryRef);
    }

    private async Task EnsureDefaultCategoriesAsync(Guid userId, CancellationToken ct)
    {
        if (await db.Categories.AnyAsync(c => c.UserId == userId, ct)) return;

        db.Categories.AddRange(
            new Category { UserId = userId, Name = "Alimentação", Color = "#C9923E" },
            new Category { UserId = userId, Name = "Transporte",  Color = "#5B86B5" },
            new Category { UserId = userId, Name = "Salário",     Color = "#3FA9A1" },
            new Category { UserId = userId, Name = "Lazer",       Color = "#8573A8" },
            new Category { UserId = userId, Name = "Saúde",       Color = "#B04A5E" },
            new Category { UserId = userId, Name = "Outros",      Color = "#8A8F98" }
        );
        await db.SaveChangesAsync(ct);
    }

    private static string AccountKindLabel(string type) => type switch
    {
        "CONTA_POUPANCA" => "Poupança",
        "CONTA_PAGAMENTO_PRE_PAGA" => "Conta de pagamento",
        _ => "Conta corrente"
    };

    // O Npgsql exige DateTimeKind.Utc para colunas timestamptz; DateTime.TryParse sozinho
    // devolve Kind=Unspecified (ou Local), o que derruba o SaveChangesAsync com uma exceção
    // não tratada, e por consequência a resposta perde os headers de CORS.
    private static DateTime? ParseDateOrNull(string? dateStr) =>
        DateTime.TryParse(
            dateStr,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;

    private static DateTime ParseDateAsUtc(string? dateStr) => ParseDateOrNull(dateStr) ?? DateTime.UtcNow;

    // Cinza fixo que toda categoria criada automaticamente recebia antes desse ajuste (a Polp
    // quase sempre manda a cor da categoria, mas isso nunca era usado). Serve só pra identificar
    // e corrigir categorias antigas presas nesse cinza. Não é mais atribuído a categoria nova.
    private const string LegacyFallbackColor = "#999999";

    // Mesma paleta usada no fallback dos gráficos no frontend (frontend/src/components/charts/chartTheme.js,
    // FALLBACK_COLORS). Mantém as duas em sincronia se uma mudar.
    private static readonly string[] FallbackPalette =
    [
        "#ED4A31", "#C9923E", "#3FA9A1", "#B04A5E", "#5B86B5", "#8573A8", "#8E9B5A", "#B98B73"
    ];

    // Categoria de fallback pra quando a própria Polp não manda nenhuma categoria pra uma
    // transação. Antes ficava CategoryId=null e a tela mostrava o texto "Sem categoria". Só que
    // isso não é uma categoria de verdade, então não dava pra editar/reatribuir. "Outros" é uma
    // Category normal (mesmo fluxo de criação/cor das demais), então o usuário pode renomeá-la ou
    // mover a transação pra outra categoria como qualquer outra.
    private const string UncategorizedFallbackName = "Outros";

    private async Task<Guid?> ResolveCategoryIdAsync(
        Dictionary<string, Category> categoriesByName, string? categoryName, string? polpColor, Guid userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            categoryName = UncategorizedFallbackName;
            polpColor = null; // cor da Polp não se aplica a uma categoria que ela não mandou
        }

        if (categoriesByName.TryGetValue(categoryName, out var existing))
        {
            // Corrige categorias criadas antes desse ajuste, todas presas no mesmo cinza fixo e
            // por isso indistinguíveis nos gráficos de rosca/barras.
            if (existing.Color == LegacyFallbackColor)
                existing.Color = IsValidHexColor(polpColor) ? polpColor : PickFallbackColor(categoryName);

            return existing.Id;
        }

        var created = new Category
        {
            UserId = userId,
            Name = categoryName,
            Color = IsValidHexColor(polpColor) ? polpColor : PickFallbackColor(categoryName)
        };
        db.Categories.Add(created);

        try
        {
            // Salva na hora, em vez de deixar acumulada pro SaveChangesAsync em lote do fim do
            // sync: com a constraint de unicidade (UserId, Name), uma corrida com outro sync
            // concorrente criando essa mesma categoria só pode ser detectada e tratada aqui,
            // isolada. Se ficasse no lote do fim, a violação derrubaria a transação inteira e
            // junto ia todo lançamento já processado nesse sync, não só a categoria.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Perdeu a corrida: a outra requisição commitou essa categoria primeiro. Descarta
            // nossa tentativa (senão ela fica presa no change tracker e quebra o próximo save)
            // e usa a que já existe, em vez de propagar o erro.
            db.Entry(created).State = EntityState.Detached;

            var winner = await db.Categories.FirstOrDefaultAsync(c => c.UserId == userId && c.Name == categoryName, ct);
            if (winner is null) throw; // não deveria acontecer: a violação só dispara se a linha já existe

            categoriesByName[categoryName] = winner;
            return winner.Id;
        }

        categoriesByName[categoryName] = created;
        return created.Id;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static bool IsValidHexColor([NotNullWhen(true)] string? color) =>
        color is { Length: 7 } && color[0] == '#' && color[1..].All(Uri.IsHexDigit);

    // Escolhe uma cor da paleta de forma determinística por nome de categoria, pra mesma categoria
    // sempre cair na mesma cor entre syncs (em vez de depender da ordem de criação).
    private static string PickFallbackColor(string categoryName)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in categoryName) hash = hash * 31 + c;
            var index = ((hash % FallbackPalette.Length) + FallbackPalette.Length) % FallbackPalette.Length;
            return FallbackPalette[index];
        }
    }
}
