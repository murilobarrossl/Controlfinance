using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ControlFinance.API.Services;

// Trabalho pesado de puxar contas + transações da Polp e gravar no banco. Extraído do
// PolpController pra poder rodar em background, fora do ciclo de vida da requisição HTTP que
// disparou o sync. Uma chamada à Polp já levou de 800ms a 20s nos logs; somando várias contas e
// páginas de transações, o sync inteiro passa fácil dos 30-60s que a maioria dos gateways
// (Railway, DigitalOcean App Platform) tolera antes de devolver 504. Por isso o controller só
// dispara esse serviço e responde na hora, sem esperar terminar.
public class PolpSyncService(AppDbContext db, IPolpService polp)
{
    // Serializa syncs concorrentes da mesma integração (ex.: o auto-sync do dashboard cruzando
    // com um sync manual, ou dois cliques no botão antes do primeiro terminar): sem isso, duas
    // execuções liam o mesmo snapshot de "transações já existentes" antes de qualquer uma salvar,
    // e cada uma inseria a mesma transação da Polp de novo. Estático porque esse serviço é
    // recriado a cada escopo de DI (um por requisição, ou um por Task.Run em background); a chave
    // é o Guid local da integração (PolpIntegration.Id).
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

    public async Task<int> SyncOneAsync(PolpIntegration local, Guid userId, CancellationToken ct)
    {
        var remoteAccounts = await polp.GetAccountsAsync(local.PolpIntegrationId, ct);

        if (!await db.Categories.AnyAsync(c => c.UserId == userId, ct))
        {
            db.Categories.AddRange(
                new Category { UserId = userId, Name = "Alimentação", Color = "#4988D4" },
                new Category { UserId = userId, Name = "Transporte",  Color = "#DB5824" },
                new Category { UserId = userId, Name = "Salário",     Color = "#30A67D" },
                new Category { UserId = userId, Name = "Lazer",       Color = "#BF8A22" },
                new Category { UserId = userId, Name = "Saúde",       Color = "#CD517E" },
                new Category { UserId = userId, Name = "Outros",      Color = "#328532" }
            );
            await db.SaveChangesAsync(ct);
        }

        // Carrega as contas já existentes do usuário de uma vez (por PolpAccountId) em vez de
        // uma query por conta remota: evita N+1 quando a integração tem várias contas.
        var existingAccounts = await db.BankAccounts
            .Where(b => b.UserId == userId && b.PolpAccountId != null)
            .ToDictionaryAsync(b => b.PolpAccountId!.Value, b => b, ct);

        var createdAccounts = new List<BankAccount>();

        // Chute inicial do Radar de Recorrências pra conta nova (CNPJ = Business, CPF = Personal,
        // ver AccountOwnershipDefault).
        var ownerDocument = await db.Users.Where(u => u.Id == userId).Select(u => u.Document).FirstAsync(ct);

        foreach (var remoteAccount in remoteAccounts)
        {
            if (existingAccounts.TryGetValue(remoteAccount.Id, out var account))
            {
                account.Balance = remoteAccount.Balance;
                account.PolpAccountType = remoteAccount.Type;
                account.PolpAccountSubtype = remoteAccount.Subtype;
                account.Number = remoteAccount.Number;
            }
            else
            {
                account = new BankAccount
                {
                    UserId = userId,
                    Name = remoteAccount.MarketingName ?? remoteAccount.Name ?? "Conta",
                    BankCode = local.InstitutionId.ToString(),
                    PolpAccountId = remoteAccount.Id,
                    PolpIntegrationId = local.PolpIntegrationId,
                    Balance = remoteAccount.Balance,
                    Ownership = AccountOwnershipDefault.FromDocument(ownerDocument),
                    PolpAccountType = remoteAccount.Type,
                    PolpAccountSubtype = remoteAccount.Subtype,
                    Number = remoteAccount.Number
                };
                db.BankAccounts.Add(account);
            }

            createdAccounts.Add(account);
        }

        await db.SaveChangesAsync(ct);

        // Pra cada conta já reconhecida como cartão (CreditCardAccountDetector), busca limite/dia
        // de fechamento/vencimento (endpoint /credit-cards) e a fatura atual (endpoint /bills,
        // aninhado no cartão). Uma integração sem cartão nenhum, ou uma falha pontual na Polp, não
        // pode travar o resto do sync — só essa parte fica sem dado, os campos continuam null e a
        // UI pede pra completar manualmente (ver CreditCardsController/BankAccountsController).
        var cardAccounts = createdAccounts.Where(CreditCardAccountDetector.LooksLikeCreditCard).ToList();
        if (cardAccounts.Count > 0)
        {
            List<PolpCreditCardDto> remoteCreditCards;
            try
            {
                remoteCreditCards = await polp.GetCreditCardsAsync(local.PolpIntegrationId, ct);
            }
            catch (HttpRequestException)
            {
                remoteCreditCards = [];
            }

            foreach (var account in cardAccounts)
            {
                // AccountId é o campo que deveria cruzar de volta pro id de /accounts, mas o schema
                // ainda não está confirmado — cai pro próprio Id do cartão como segunda tentativa.
                var remoteCard = remoteCreditCards.FirstOrDefault(c => c.AccountId == account.PolpAccountId)
                    ?? remoteCreditCards.FirstOrDefault(c => c.Id == account.PolpAccountId);
                if (remoteCard is null) continue;

                account.Brand = remoteCard.BrandName;
                account.CreditLimit = remoteCard.CreditLimit;
                account.UsedLimit = remoteCard.UsedCreditLimit
                    ?? (remoteCard.CreditLimit.HasValue && remoteCard.AvailableCreditLimit.HasValue
                        ? remoteCard.CreditLimit - remoteCard.AvailableCreditLimit
                        : null);
                account.ClosingDay = remoteCard.CloseDay;
                account.DueDay = remoteCard.DueDay;

                try
                {
                    var bills = await polp.GetBillsAsync(remoteCard.Id, ct);
                    // Fatura "atual" = a de vencimento mais próximo que ainda não passou; se todas
                    // já venceram (ou não vier nenhuma), cai pra mais recente disponível.
                    var today = DateTime.UtcNow.Date;
                    var parsedBills = bills
                        .Select(b => (bill: b, dueDate: ParseDateAsUtc(b.DueDate)))
                        .OrderBy(x => x.dueDate)
                        .ToList();
                    var current = parsedBills.FirstOrDefault(x => x.dueDate.Date >= today);
                    if (current.bill is null && parsedBills.Count > 0) current = parsedBills[^1];

                    if (current.bill is not null)
                    {
                        account.CurrentInvoiceAmount = current.bill.TotalAmount;
                        account.InvoiceDueDate = current.dueDate;
                    }
                }
                catch (HttpRequestException)
                {
                    // segue sem fatura; limite/dias já foram gravados acima mesmo assim
                }
            }

            await db.SaveChangesAsync(ct);
        }

        // Categorias do usuário carregadas uma única vez (entidade completa, não só o id: precisa
        // pra poder corrigir a cor de categorias antigas presas no cinza legado); novas categorias
        // entram no mesmo dicionário conforme são criadas, sem round-trip ao banco por transação.
        //
        // GroupBy + First em vez de ToDictionaryAsync: duas execuções concorrentes deste método
        // (ex.: dois syncs disparados ao mesmo tempo) podiam criar a mesma categoria em paralelo
        // antes de qualquer uma commitar, gerando duas linhas com o mesmo Name, e ToDictionaryAsync
        // estourava ArgumentException na segunda ocorrência, derrubando o sync inteiro. Fica com a
        // mais antiga de cada nome (critério consistente com a limpeza dos dados já existentes).
        // Caso normal (sem duplicata) dá o resultado idêntico de antes.
        var categoriesByName = (await db.Categories
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync(ct))
            .GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => g.First());

        var accountIds = createdAccounts.Select(a => a.Id).ToList();
        var existingTransactionsByKey = (await db.Transactions
                .Where(t => t.UserId == userId && t.BankAccountId != null && accountIds.Contains(t.BankAccountId.Value))
                .ToListAsync(ct))
            .ToDictionary(t => (t.BankAccountId!.Value, t.Description), t => t);

        // Busca e persiste todo o histórico de transações de cada conta (paginado dentro do
        // PolpService, até o teto de segurança).
        foreach (var account in createdAccounts)
        {
            if (account.PolpAccountId is not int polpAccountId) continue;

            List<PolpTransactionDto> remoteTransactions;
            try
            {
                remoteTransactions = await polp.GetTransactionsAsync(polpAccountId, ct);
            }
            catch (HttpRequestException)
            {
                continue; // não trava o sync inteiro por causa de uma conta com falha
            }

            foreach (var rt in remoteTransactions)
            {
                var polpTransactionId = rt.Id.ToString();
                // A Polp já manda a direção explícita ("DEBIT"/"CREDIT"): usa ela em vez de
                // inferir pelo sinal do valor. Pra conta corrente as duas coisas coincidiam, mas
                // em cartão de crédito o sinal vinha invertido do que a gente assumia (compra
                // normal virando "Receita", pagamento de fatura virando "Despesa"). Sinal só entra
                // como fallback se a Polp mandar um valor de "type" que não seja nenhum dos dois.
                var type = rt.Type.ToUpperInvariant() switch
                {
                    "CREDIT" => TransactionType.Income,
                    "DEBIT" => TransactionType.Expense,
                    _ => rt.Amount >= 0 ? TransactionType.Income : TransactionType.Expense,
                };
                var status = rt.Status == "PENDING" ? TransactionStatus.Pending : TransactionStatus.Paid;
                var amount = Math.Abs(rt.Amount);
                var dueDate = ParseDateAsUtc(rt.Date);
                var paidAt = status == TransactionStatus.Pending ? (DateTime?)null : dueDate;

                if (existingTransactionsByKey.TryGetValue((account.Id, polpTransactionId), out var existing))
                {
                    // Já sincronizada antes, mas a Polp pode corrigir o valor/status depois (ex.:
                    // pré-autorização de cartão que confirma com um valor final diferente, ou
                    // PENDING que vira PAID). Sem atualizar aqui, o valor exibido ficava congelado
                    // no que veio na primeira sincronização mesmo depois de o banco confirmar outro
                    // valor. CategoryId/Name ficam de fora de propósito, pra não sobrescrever uma
                    // recategorização/renomeação feita manualmente no app.
                    existing.Amount = amount;
                    existing.Type = type;
                    existing.Status = status;
                    existing.DueDate = dueDate;
                    existing.PaidAt = paidAt;
                    continue;
                }

                var categoryId = await ResolveCategoryIdAsync(categoriesByName, rt.Category?.Description, rt.Category?.Color, userId, ct);

                var transaction = new Transaction
                {
                    UserId = userId,
                    BankAccountId = account.Id,
                    CategoryId = categoryId,
                    Name = rt.Merchant?.Name ?? rt.Description ?? "Transação",
                    Description = polpTransactionId, // guarda o id da Polp para evitar duplicar em re-syncs
                    Type = type,
                    Status = status,
                    Amount = amount,
                    DueDate = dueDate,
                    PaidAt = paidAt
                };
                db.Transactions.Add(transaction);
                existingTransactionsByKey[(account.Id, polpTransactionId)] = transaction;
            }
        }

        local.SyncedLocally = true;
        local.UpdatedAt = DateTime.UtcNow;
        local.LastSyncedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return createdAccounts.Count;
    }

    // O Npgsql exige DateTimeKind.Utc para colunas timestamptz; DateTime.TryParse sozinho
    // devolve Kind=Unspecified (ou Local), o que derruba o SaveChangesAsync com uma exceção
    // não tratada, e por consequência a resposta perde os headers de CORS.
    private static DateTime ParseDateAsUtc(string? dateStr)
    {
        if (DateTime.TryParse(
                dateStr,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
            return parsed;

        return DateTime.UtcNow;
    }

    // Cinza fixo que toda categoria criada automaticamente recebia antes desse ajuste (a Polp
    // quase sempre manda a cor da categoria, mas isso nunca era usado). Serve só pra identificar
    // e corrigir categorias antigas presas nesse cinza. Não é mais atribuído a categoria nova.
    private const string LegacyFallbackColor = "#999999";

    // Mesma paleta usada no fallback dos gráficos no frontend (frontend/src/components/charts/chartTheme.js,
    // FALLBACK_COLORS). Mantém as duas em sincronia se uma mudar.
    private static readonly string[] FallbackPalette =
    [
        "#4988D4", "#DB5824", "#30A67D", "#BF8A22", "#CD517E", "#328532", "#7569D3", "#D45454"
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
