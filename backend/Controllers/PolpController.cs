using ControlFinance.API.Data;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/polp")]
[Authorize]
public class PolpController(AppDbContext db, IPolpService polp) : ApiControllerBase
{
    // ──────────────────────────────────────────
    //  GET /api/polp/connectors
    //  Lista as instituições disponíveis (estado "seleção de banco")
    // ──────────────────────────────────────────
    [HttpGet("connectors")]
    public async Task<IActionResult> GetConnectors(CancellationToken ct)
    {
        var institutions = await polp.GetInstitutionsAsync(ct);

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

    // ──────────────────────────────────────────
    //  POST /api/polp/integrations
    //  Cria a integração na Polp e salva o vínculo local (estado "redirect")
    // ──────────────────────────────────────────
    [HttpPost("integrations")]
    public async Task<IActionResult> Connect([FromBody] PolpConnectDto dto, CancellationToken ct)
    {
        if (!int.TryParse(dto.ConnectorId, out var institutionId))
            return BadRequest(new { message = "connectorId inválido." });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == UserId, ct);
        if (user is null) return Unauthorized();

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

    // ──────────────────────────────────────────
    //  GET /api/polp/integrations
    //  Lista as contas já sincronizadas do usuário
    // ──────────────────────────────────────────
    [HttpGet("integrations")]
    public async Task<IActionResult> GetIntegrations(CancellationToken ct)
    {
        var accounts = await db.BankAccounts
            .Where(b => b.UserId == UserId && b.IsActive)
            .Select(b => new { b.Id, b.Name, b.Balance, b.BankCode })
            .ToListAsync(ct);

        return Ok(accounts);
    }

    // ──────────────────────────────────────────
    //  GET /api/polp/integrations/{id}
    //  Consulta o status da integração (estado "sincronizando").
    //  {id} aqui é o Guid local salvo em PolpIntegration.
    // ──────────────────────────────────────────
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

        // Mapeia os status da Polp para os estados que o frontend (Igor) já conhece
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

    // ──────────────────────────────────────────
    //  POST /api/polp/integrations/{id}/sync
    //  Busca contas + transações reais da Polp e persiste localmente.
    //  Só deve ser chamado quando o status já é UPDATED.
    // ──────────────────────────────────────────
    [HttpPost("integrations/{id:guid}/sync")]
    public async Task<IActionResult> Sync(Guid id, CancellationToken ct)
    {
        var local = await db.PolpIntegrations
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == UserId, ct);

        if (local is null) return NotFound();

        int accountsCount;
        try
        {
            accountsCount = await SyncOneAsync(local, ct);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível buscar as contas.", detail = ex.Message });
        }

        return Ok(new { status = "synced", accountsCount });
    }

    // ──────────────────────────────────────────
    //  POST /api/polp/integrations/sync-all
    //  Sincroniza todas as integrações do usuário de uma vez, usado pra atualizar saldo e
    //  transações automaticamente ao carregar o dashboard, sem precisar reconectar o banco
    //  nem esperar um job periódico. Uma integração com falha (banco fora do ar, token
    //  expirado etc.) não trava a sincronização das outras.
    // ──────────────────────────────────────────
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
                await SyncOneAsync(local, ct);
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
                    Balance = remoteAccount.Balance
                };
                db.BankAccounts.Add(account);
            }

            createdAccounts.Add(account);
        }

        await db.SaveChangesAsync(ct);

        // Categorias do usuário carregadas uma única vez; novas categorias entram no mesmo
        // dicionário conforme são criadas, sem round-trip ao banco por transação.
        var categoryByName = await db.Categories
            .Where(c => c.UserId == UserId)
            .ToDictionaryAsync(c => c.Name, c => c.Id, ct);

        var accountIds = createdAccounts.Select(a => a.Id).ToList();
        var existingTransactionKeys = (await db.Transactions
                .Where(t => t.UserId == UserId && t.BankAccountId != null && accountIds.Contains(t.BankAccountId.Value))
                .Select(t => new { t.BankAccountId, t.Description })
                .ToListAsync(ct))
            .Select(t => (t.BankAccountId!.Value, t.Description))
            .ToHashSet();

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
                if (!existingTransactionKeys.Add((account.Id, polpTransactionId)))
                    continue; // já sincronizada em uma execução anterior

                var categoryId = ResolveCategoryId(categoryByName, rt.Category?.Description);

                db.Transactions.Add(new Transaction
                {
                    UserId = UserId,
                    BankAccountId = account.Id,
                    CategoryId = categoryId,
                    Name = rt.Merchant?.Name ?? rt.Description ?? "Transação",
                    Description = polpTransactionId, // guarda o id da Polp para evitar duplicar em re-syncs
                    Type = rt.Amount >= 0 ? TransactionType.Income : TransactionType.Expense,
                    Status = rt.Status == "PENDING" ? TransactionStatus.Pending : TransactionStatus.Paid,
                    Amount = Math.Abs(rt.Amount),
                    DueDate = ParseDateAsUtc(rt.Date),
                    PaidAt = rt.Status == "PENDING" ? null : ParseDateAsUtc(rt.Date)
                });
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

    private Guid? ResolveCategoryId(Dictionary<string, Guid> categoryByName, string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName)) return null;

        if (categoryByName.TryGetValue(categoryName, out var existingId)) return existingId;

        var created = new Category { UserId = UserId, Name = categoryName, Color = "#999999" };
        db.Categories.Add(created);
        categoryByName[categoryName] = created.Id;
        return created.Id;
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
