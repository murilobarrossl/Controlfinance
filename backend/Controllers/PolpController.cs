using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/polp")]
[Authorize]
public class PolpController(AppDbContext db, IPolpService polp) : ApiControllerBase
{
    // Serializa syncs concorrentes da mesma integração (ex.: o auto-sync do dashboard cruzando
    // com um sync manual, ou a página recarregada antes do sync anterior terminar): sem isso,
    // duas requisições liam o mesmo snapshot de "transações já existentes" antes de qualquer
    // uma salvar, e cada uma inseria a mesma transação da Polp de novo — causou duplicatas reais
    // de "Transferência enviada" no extrato. Estático porque o controller é instanciado por
    // requisição; a chave é o Guid local da integração (PolpIntegration.Id).
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SyncLocks = new();

    private static async Task<int> SyncOneWithLockAsync(Func<Task<int>> syncOne, Guid integrationId, CancellationToken ct)
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

    // TEMPORÁRIO — diagnóstico do bug de fatura de cartão inflada (abril mostrando R$6 mil contra
    // R$699,86 reais). Devolve exatamente o que a Polp manda pra cada conta do usuário logado, sem
    // nenhum processamento nosso, pra confirmar se o valor total de compra parcelada e o valor de
    // "Pagamento recebido" já vêm com o mesmo dinheiro contado duas vezes do lado da Polp. Remover
    // depois de confirmar a causa.
    [HttpGet("debug/raw-transactions")]
    public async Task<IActionResult> DebugRawTransactions(CancellationToken ct)
    {
        var accounts = await db.BankAccounts
            .Where(b => b.UserId == UserId && b.PolpAccountId != null)
            .ToListAsync(ct);

        var result = new List<object>();
        foreach (var account in accounts)
        {
            if (account.PolpAccountId is not int polpAccountId) continue;
            var remoteTransactions = await polp.GetTransactionsAsync(polpAccountId, ct);
            result.Add(new { account.Id, account.Name, account.PolpAccountId, Transactions = remoteTransactions });
        }

        return Ok(result);
    }

    [HttpGet("connectors")]
    public async Task<IActionResult> GetConnectors(CancellationToken ct)
    {
        List<PolpInstitutionDto> institutions;
        try
        {
            institutions = await polp.GetInstitutionsAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível carregar os bancos disponíveis.", detail = ex.Message });
        }

        var connectors = institutions.Select(i => new
        {
            id = i.Id.ToString(),
            name = i.Name,
            color = i.Color ?? "#000000",
            textColor = ResolveTextColor(i.Color),
            initials = BuildInitials(i.Name),
            logoUrl = i.LogoUrl,
            status = i.Status
        });

        return Ok(connectors);
    }

    // UPDATING/WAITING_USER_INPUT só bloqueiam uma conexão nova se atualizados há menos de 1h:
    // autenticar um banco aqui leva 5-10min no máximo, então uma integração parada há mais tempo
    // que isso foi abandonada (aba fechada, MFA nunca completado) — bloquear pra sempre trancaria
    // o usuário de tentar de novo. UPDATED sempre bloqueia (já está conectado de verdade). A linha
    // antiga abandonada fica órfã no banco até a limpeza — fora do escopo deste guard.
    private static readonly TimeSpan StaleIntegrationThreshold = TimeSpan.FromHours(1);

    // Impede o clique duplo (ou um retry depois de um timeout) de criar uma segunda
    // PolpIntegration local pro mesmo banco — e, pior, uma segunda integração do lado da Polp
    // também, que depois recusa com 422 ITEM_IS_ALREADY_UPDATING.
    private async Task<PolpIntegration?> FindBlockingIntegrationAsync(int institutionId, CancellationToken ct)
    {
        var candidates = await db.PolpIntegrations
            .Where(p => p.UserId == UserId && p.InstitutionId == institutionId)
            .ToListAsync(ct);

        var cutoff = DateTime.UtcNow - StaleIntegrationThreshold;

        return candidates.FirstOrDefault(p =>
            p.Status == "UPDATED" ||
            ((p.Status is "UPDATING" or "WAITING_USER_INPUT") && p.UpdatedAt > cutoff));
    }

    [HttpPost("integrations")]
    public async Task<IActionResult> Connect([FromBody] PolpConnectDto dto, CancellationToken ct)
    {
        if (!int.TryParse(dto.ConnectorId, out var institutionId))
            return BadRequest(new { message = "connectorId inválido." });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == UserId, ct);
        if (user is null) return Unauthorized();

        var blocking = await FindBlockingIntegrationAsync(institutionId, ct);
        if (blocking is not null)
        {
            return Conflict(new
            {
                message = "Já existe uma conexão em andamento ou ativa para esse banco.",
                integrationId = blocking.Id,
                status = blocking.Status
            });
        }

        PolpIntegrationDto created;
        try
        {
            created = await polp.CreateIntegrationAsync(institutionId, user.Document, ct);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível iniciar a conexão com o banco.", detail = ex.Message });
        }

        var localIntegration = new PolpIntegration
        {
            UserId = UserId,
            PolpIntegrationId = created.Id,
            InstitutionId = institutionId,
            Status = created.Status,
            UrlToAuthenticate = created.UrlToAuthenticate
        };

        db.PolpIntegrations.Add(localIntegration);
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            integrationId = localIntegration.Id,          // id local (usado pelo frontend para polling)
            polpIntegrationId = created.Id,
            status = created.Status,
            urlToAuthenticate = created.UrlToAuthenticate
        });
    }

    [HttpGet("integrations")]
    public async Task<IActionResult> GetIntegrations(CancellationToken ct)
    {
        var accounts = await db.BankAccounts
            .Where(b => b.UserId == UserId && b.IsActive)
            .Select(b => new { b.Id, b.Name, b.Balance, b.BankCode })
            .ToListAsync(ct);

        return Ok(accounts);
    }

    // {id} aqui é o Guid local salvo em PolpIntegration, não o id da integração na própria Polp.
    [HttpGet("integrations/{id:guid}")]
    public async Task<IActionResult> GetIntegrationStatus(Guid id, CancellationToken ct)
    {
        var local = await db.PolpIntegrations
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == UserId, ct);

        if (local is null) return NotFound();

        PolpIntegrationDto remote;
        try
        {
            remote = await polp.GetIntegrationAsync(local.PolpIntegrationId, ct);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível consultar o status da conexão.", detail = ex.Message });
        }

        local.Status = remote.Status;
        local.UrlToAuthenticate = remote.UrlToAuthenticate;
        local.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // Mapeia os status da Polp para os estados que o frontend já conhece
        var mappedStatus = remote.Status switch
        {
            "UPDATING" => "syncing",
            "WAITING_USER_INPUT" => "waiting_user_input",
            "UPDATED" => "active",
            "LOGIN_ERROR" => "login_error",
            "OUTDATED" => "outdated",
            _ => "unknown"
        };

        return Ok(new
        {
            status = mappedStatus,
            rawStatus = remote.Status,
            error = remote.ErrorMessage,
            urlToAuthenticate = remote.UrlToAuthenticate
        });
    }

    // Só deve ser chamado quando o status já é UPDATED (senão a Polp ainda não tem contas prontas pra buscar).
    [HttpPost("integrations/{id:guid}/sync")]
    public async Task<IActionResult> Sync(Guid id, CancellationToken ct)
    {
        var local = await db.PolpIntegrations
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == UserId, ct);

        if (local is null) return NotFound();

        int accountsCount;
        try
        {
            accountsCount = await SyncOneWithLockAsync(() => SyncOneAsync(local, ct), local.Id, ct);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível buscar as contas.", detail = ex.Message });
        }

        return Ok(new { status = "synced", accountsCount });
    }

    // Sincroniza todas as integrações do usuário de uma vez, usado pra atualizar saldo e
    // transações automaticamente ao carregar o dashboard, sem precisar reconectar o banco
    // nem esperar um job periódico. Uma integração com falha (banco fora do ar, token
    // expirado etc.) não trava a sincronização das outras.
    [HttpPost("integrations/sync-all")]
    public async Task<IActionResult> SyncAll(CancellationToken ct)
    {
        var integrations = await db.PolpIntegrations
            .Where(p => p.UserId == UserId)
            .ToListAsync(ct);

        var syncedCount = 0;
        foreach (var local in integrations)
        {
            try
            {
                await SyncOneWithLockAsync(() => SyncOneAsync(local, ct), local.Id, ct);
                syncedCount++;
            }
            catch (HttpRequestException)
            {
                // segue pras próximas integrações mesmo se essa falhar
            }
        }

        return Ok(new { syncedIntegrations = syncedCount, totalIntegrations = integrations.Count });
    }

    private async Task<int> SyncOneAsync(PolpIntegration local, CancellationToken ct)
    {
        var remoteAccounts = await polp.GetAccountsAsync(local.PolpIntegrationId, ct);

        if (!await db.Categories.AnyAsync(c => c.UserId == UserId, ct))
        {
            db.Categories.AddRange(
                new Category { UserId = UserId, Name = "Alimentação", Color = "#FF6B6B" },
                new Category { UserId = UserId, Name = "Transporte",  Color = "#4ECDC4" },
                new Category { UserId = UserId, Name = "Salário",     Color = "#45B7D1" },
                new Category { UserId = UserId, Name = "Lazer",       Color = "#96CEB4" },
                new Category { UserId = UserId, Name = "Saúde",       Color = "#FFEAA7" }
            );
            await db.SaveChangesAsync(ct);
        }

        // Carrega as contas já existentes do usuário de uma vez (por PolpAccountId) em vez de
        // uma query por conta remota: evita N+1 quando a integração tem várias contas.
        var existingAccounts = await db.BankAccounts
            .Where(b => b.UserId == UserId && b.PolpAccountId != null)
            .ToDictionaryAsync(b => b.PolpAccountId!.Value, b => b, ct);

        var createdAccounts = new List<BankAccount>();

        // Chute inicial do Radar de Recorrências pra conta nova (CNPJ = Business, CPF = Personal,
        // ver AccountOwnershipDefault).
        var ownerDocument = await db.Users.Where(u => u.Id == UserId).Select(u => u.Document).FirstAsync(ct);

        foreach (var remoteAccount in remoteAccounts)
        {
            if (existingAccounts.TryGetValue(remoteAccount.Id, out var account))
            {
                account.Balance = remoteAccount.Balance;
            }
            else
            {
                account = new BankAccount
                {
                    UserId = UserId,
                    Name = remoteAccount.MarketingName ?? remoteAccount.Name ?? "Conta",
                    BankCode = local.InstitutionId.ToString(),
                    PolpAccountId = remoteAccount.Id,
                    PolpIntegrationId = local.PolpIntegrationId,
                    Balance = remoteAccount.Balance,
                    Ownership = AccountOwnershipDefault.FromDocument(ownerDocument)
                };
                db.BankAccounts.Add(account);
            }

            createdAccounts.Add(account);
        }

        await db.SaveChangesAsync(ct);

        // Categorias do usuário carregadas uma única vez (entidade completa, não só o id: precisa
        // pra poder corrigir a cor de categorias antigas presas no cinza legado); novas categorias
        // entram no mesmo dicionário conforme são criadas, sem round-trip ao banco por transação.
        //
        // GroupBy + First em vez de ToDictionaryAsync: duas execuções concorrentes deste método
        // (ex.: dois syncs disparados ao mesmo tempo) podiam criar a mesma categoria em paralelo
        // antes de qualquer uma commitar, gerando duas linhas com o mesmo Name — e ToDictionaryAsync
        // estourava ArgumentException na segunda ocorrência, derrubando o sync inteiro. Fica com a
        // mais antiga de cada nome (critério consistente com a limpeza dos dados já existentes).
        // Caso normal (sem duplicata) dá o resultado idêntico de antes.
        var categoriesByName = (await db.Categories
                .Where(c => c.UserId == UserId)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync(ct))
            .GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => g.First());

        var accountIds = createdAccounts.Select(a => a.Id).ToList();
        var existingTransactionsByKey = (await db.Transactions
                .Where(t => t.UserId == UserId && t.BankAccountId != null && accountIds.Contains(t.BankAccountId.Value))
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
                var type = rt.Amount >= 0 ? TransactionType.Income : TransactionType.Expense;
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
                    // valor — CategoryId/Name ficam de fora de propósito, pra não sobrescrever uma
                    // recategorização/renomeação feita manualmente no app.
                    existing.Amount = amount;
                    existing.Type = type;
                    existing.Status = status;
                    existing.DueDate = dueDate;
                    existing.PaidAt = paidAt;
                    continue;
                }

                var categoryId = await ResolveCategoryIdAsync(categoriesByName, rt.Category?.Description, rt.Category?.Color, ct);

                var transaction = new Transaction
                {
                    UserId = UserId,
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
    // e corrigir categorias antigas presas nesse cinza — não é mais atribuído a categoria nova.
    private const string LegacyFallbackColor = "#999999";

    // Mesma paleta usada no fallback dos gráficos no frontend (frontend/src/components/charts/chartTheme.js,
    // FALLBACK_COLORS) — mantém as duas em sincronia se uma mudar.
    private static readonly string[] FallbackPalette =
    [
        "#ED4A31", "#4ECDC4", "#45B7D1", "#96CEB4", "#FFEAA7", "#B39DDB", "#F4A261"
    ];

    private async Task<Guid?> ResolveCategoryIdAsync(
        Dictionary<string, Category> categoriesByName, string? categoryName, string? polpColor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(categoryName)) return null;

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
            UserId = UserId,
            Name = categoryName,
            Color = IsValidHexColor(polpColor) ? polpColor : PickFallbackColor(categoryName)
        };
        db.Categories.Add(created);

        try
        {
            // Salva na hora, em vez de deixar acumulada pro SaveChangesAsync em lote do fim do
            // sync: com a constraint de unicidade (UserId, Name) — ainda por vir —, uma corrida
            // com outro sync concorrente criando essa mesma categoria só pode ser detectada e
            // tratada aqui, isolada. Se ficasse no lote do fim, a violação derrubaria a transação
            // inteira e junto ia todo lançamento já processado nesse sync, não só a categoria.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Perdeu a corrida: a outra requisição commitou essa categoria primeiro. Descarta
            // nossa tentativa (senão ela fica presa no change tracker e quebra o próximo save)
            // e usa a que já existe, em vez de propagar o erro.
            db.Entry(created).State = EntityState.Detached;

            var winner = await db.Categories.FirstOrDefaultAsync(c => c.UserId == UserId && c.Name == categoryName, ct);
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

    private static string ResolveTextColor(string? hexColor)
    {
        if (string.IsNullOrWhiteSpace(hexColor) || hexColor.Length < 7) return "#fff";

        try
        {
            var r = Convert.ToInt32(hexColor.Substring(1, 2), 16);
            var g = Convert.ToInt32(hexColor.Substring(3, 2), 16);
            var b = Convert.ToInt32(hexColor.Substring(5, 2), 16);
            var brightness = (r * 299 + g * 587 + b * 114) / 1000.0;
            return brightness > 150 ? "#000" : "#fff";
        }
        catch
        {
            return "#fff";
        }
    }

    private static string BuildInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "??";
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)];
        return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
    }
}

public record PolpConnectDto(string ConnectorId);
