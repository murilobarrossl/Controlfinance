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
public class PolpController(AppDbContext db, IPolpService polp, IServiceScopeFactory scopeFactory, ILogger<PolpController> logger) : ApiControllerBase
{
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
    // que isso foi abandonada (aba fechada, MFA nunca completado). Bloquear pra sempre trancaria
    // o usuário de tentar de novo. UPDATED sempre bloqueia (já está conectado de verdade). A linha
    // antiga abandonada fica órfã no banco até a limpeza, o que foge do escopo deste guard.
    private static readonly TimeSpan StaleIntegrationThreshold = TimeSpan.FromHours(1);

    // Throttle do "sincronizar agora" (manual e automático no mount do dashboard): já tivemos um
    // bug de polling sem limite martelando uma API externa, então tanto o botão manual quanto o
    // sync-all automático respeitam essa janela por integração, não só a UI.
    private static readonly TimeSpan SyncThrottleWindow = TimeSpan.FromMinutes(5);

    private static bool IsSyncThrottled(PolpIntegration local) =>
        local.LastSyncedAt is { } last && DateTime.UtcNow - last < SyncThrottleWindow;

    private static int SyncThrottleRemainingSeconds(PolpIntegration local) =>
        local.LastSyncedAt is { } last
            ? Math.Max(0, (int)Math.Ceiling((SyncThrottleWindow - (DateTime.UtcNow - last)).TotalSeconds))
            : 0;

    // Impede o clique duplo (ou um retry depois de um timeout) de criar uma segunda
    // PolpIntegration local pro mesmo banco e, pior, uma segunda integração do lado da Polp
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
        // PolpIntegrationId vai junto só pra agrupar visualmente no seletor de contas (ex.: conta
        // corrente + cartão de crédito do mesmo banco, sincronizados na mesma conexão). Cada
        // conta continua com sua própria visão de dados, nada é somado entre elas.
        var accounts = await db.BankAccounts
            .Where(b => b.UserId == UserId && b.IsActive)
            .Select(b => new { b.Id, b.Name, b.Balance, b.BankCode, b.PolpIntegrationId })
            .ToListAsync(ct);

        // BankAccount.PolpIntegrationId guarda o id remoto (int) da Polp, mas o botão de
        // "sincronizar agora" precisa do Guid local (PolpIntegration.Id) pra chamar o endpoint de
        // sync. Por isso busca esse mapeamento de uma vez em vez de uma query por conta.
        var localByRemoteId = await db.PolpIntegrations
            .Where(p => p.UserId == UserId)
            .ToDictionaryAsync(p => p.PolpIntegrationId, p => new { p.Id, p.LastSyncedAt }, ct);

        var result = accounts.Select(a =>
        {
            var local = a.PolpIntegrationId.HasValue && localByRemoteId.TryGetValue(a.PolpIntegrationId.Value, out var info)
                ? info
                : null;

            return new
            {
                a.Id,
                a.Name,
                a.Balance,
                a.BankCode,
                a.PolpIntegrationId,
                localIntegrationId = local?.Id,
                lastSyncedAt = local?.LastSyncedAt
            };
        });

        return Ok(result);
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

    // Só deve ser chamado quando o status já é UPDATED (senão a Polp ainda não tem contas prontas
    // pra buscar). Dispara o sync em background e responde na hora (202): segurar a conexão HTTP
    // até o sync inteiro terminar estourava o timeout do gateway (Railway/DigitalOcean, ~30-60s)
    // bem antes da Polp acabar de responder: uma chamada sozinha já levou até 20s nos logs, e o
    // sync passa por várias contas e páginas de transação.
    [HttpPost("integrations/{id:guid}/sync")]
    public async Task<IActionResult> Sync(Guid id, CancellationToken ct)
    {
        var local = await db.PolpIntegrations
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == UserId, ct);

        if (local is null) return NotFound();

        if (IsSyncThrottled(local))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = "Essa conexão já foi sincronizada há pouco. Tente novamente em instantes.",
                retryAfterSeconds = SyncThrottleRemainingSeconds(local)
            });
        }

        // Reserva a janela de throttle já aqui, antes de disparar em background: sem isso, dois
        // cliques rápidos (ou o clique cruzando com o sync-all automático do dashboard) passavam
        // os dois pela checagem acima antes de qualquer um marcar LastSyncedAt, e disparavam dois
        // syncs da mesma integração em paralelo. O lock em PolpSyncService evita corrupção de
        // dado nesse caso, mas não evita bater na Polp duas vezes à toa.
        local.LastSyncedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        QueueBackgroundSync(local.Id, UserId);

        return Accepted(new { status = "syncing" });
    }

    // Sincroniza todas as integrações do usuário de uma vez, usado pra atualizar saldo e
    // transações automaticamente ao carregar o dashboard, sem precisar reconectar o banco
    // nem esperar um job periódico. Uma integração com falha (banco fora do ar, token
    // expirado etc.) não trava a sincronização das outras. Mesmo motivo do endpoint acima pra
    // rodar em background: com várias integrações, o total facilmente estoura o timeout do gateway.
    [HttpPost("integrations/sync-all")]
    public async Task<IActionResult> SyncAll(CancellationToken ct)
    {
        var integrations = await db.PolpIntegrations
            .Where(p => p.UserId == UserId)
            .ToListAsync(ct);

        var toSync = integrations.Where(i => !IsSyncThrottled(i)).ToList();
        foreach (var local in toSync)
            local.LastSyncedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        foreach (var local in toSync)
            QueueBackgroundSync(local.Id, UserId);

        return Accepted(new { queuedIntegrations = toSync.Count, totalIntegrations = integrations.Count });
    }

    // Roda com seu próprio escopo de DI (próprio DbContext, próprio PolpSyncService): o
    // DbContext desta requisição é descartado assim que ela responde, e usá-lo depois disso numa
    // Task solta derrubaria com ObjectDisposedException. CancellationToken.None de propósito: a
    // requisição que disparou isso já terminou (e cancelaria o token dela), mas o sync em si deve
    // seguir até o fim mesmo assim.
    private void QueueBackgroundSync(Guid integrationId, Guid userId)
    {
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var scopedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var syncService = scope.ServiceProvider.GetRequiredService<PolpSyncService>();

            try
            {
                var local = await scopedDb.PolpIntegrations
                    .FirstOrDefaultAsync(p => p.Id == integrationId && p.UserId == userId);
                if (local is null) return;

                await PolpSyncService.SyncOneWithLockAsync(
                    () => syncService.SyncOneAsync(local, userId, CancellationToken.None),
                    integrationId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao sincronizar integração {IntegrationId} em background", integrationId);
            }
        });
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
