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
public class PolpController(
    AppDbContext db,
    IPolpService polp,
    IEncryptionService encryption,
    PolpDisconnectService disconnectService,
    IServiceScopeFactory scopeFactory,
    ILogger<PolpController> logger) : ApiControllerBase
{
    [HttpGet("connectors")]
    public async Task<IActionResult> GetConnectors(CancellationToken ct)
    {
        List<PolpInstitutionDto> institutions;
        try
        {
            institutions = await polp.GetInstitutionsAsync(ct);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível carregar os bancos disponíveis." });
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

    // AWAITING_AUTHORIZATION só bloqueia uma conexão nova se atualizada há menos de 1h: autorizar
    // um banco aqui leva 5-10min no máximo, então um consentimento parado há mais tempo que isso
    // foi abandonado (aba fechada, autorização nunca concluída). Bloquear pra sempre trancaria o
    // usuário de tentar de novo. AUTHORISED sempre bloqueia (já está conectado de verdade).
    private static readonly TimeSpan StaleConsentThreshold = TimeSpan.FromHours(1);

    // Consulta de status do consentimento só quando o webhook "consents" ainda não chegou: a Polp
    // orienta a não fazer polling (240 req/min por usuário), então no máximo uma consulta por
    // janela, por conexão.
    private static readonly TimeSpan StatusRefreshWindow = TimeSpan.FromSeconds(5);

    // Impede o clique duplo (ou um retry depois de um timeout) de criar uma segunda PolpIntegration
    // local pro mesmo banco.
    private async Task<PolpIntegration?> FindBlockingIntegrationAsync(Guid institutionId, CancellationToken ct)
    {
        var candidates = await db.PolpIntegrations
            .Where(p => p.UserId == UserId && p.InstitutionId == institutionId)
            .ToListAsync(ct);

        var cutoff = DateTime.UtcNow - StaleConsentThreshold;

        return candidates.FirstOrDefault(p =>
            p.Status == "AUTHORISED" ||
            (p.Status == "AWAITING_AUTHORIZATION" && p.UpdatedAt > cutoff));
    }

    [HttpPost("integrations")]
    public async Task<IActionResult> Connect([FromBody] PolpConnectDto dto, CancellationToken ct)
    {
        if (!Guid.TryParse(dto.ConnectorId, out var institutionId))
            return BadRequest(new { message = "connectorId inválido." });

        // O aviso da tela de conexão (quais dados, pra quê, quem recebe, como desconectar) tem
        // versão; só aceita a conexão se o usuário confirmou exatamente o texto vigente, e esse
        // aceite fica registrado (LGPD art. 8º, § 2º; Termos da Polp, seção 06).
        if (dto.ConsentVersion != LegalDocuments.BankConnectionNoticeVersion)
            return BadRequest(new { message = "Confirme as informações sobre a conexão bancária antes de continuar." });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == UserId, ct);
        if (user is null) return Unauthorized();

        // O CPF/CNPJ não fica mais guardado em forma recuperável: a Polp precisa dele pra criar o
        // consentimento, então o usuário digita na hora, ele vai direto pra Polp e é descartado.
        // Conferir contra o hash do cadastro impede conectar o banco de outra pessoa nesta conta.
        var document = new string((dto.Document ?? string.Empty).Where(char.IsDigit).ToArray());
        if (!DocumentValidator.IsValid(document) || !encryption.ComputeLookupHashCandidates(document).Contains(user.DocumentHash))
            return BadRequest(new { message = "O CPF/CNPJ informado não confere com o do seu cadastro." });

        // Mesmo motivo do login por CPF: o documento acabou de ser digitado, então regrava o hash
        // com a chave de busca ativa se ele ainda estiver numa chave antiga.
        if (!encryption.IsActiveLookupHash(user.DocumentHash))
            user.DocumentHash = encryption.ComputeLookupHash(document);

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

        PolpConsentDto created;
        try
        {
            created = await polp.CreateConsentAsync(new CreatePolpConsentRequest(institutionId, document, UserId), ct);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível iniciar a conexão com o banco." });
        }

        // avoidDuplicates pode devolver um consentimento que já temos (mesmo id): reaproveita a
        // linha local em vez de violar o índice único.
        var localIntegration = await db.PolpIntegrations
            .FirstOrDefaultAsync(p => p.PolpIntegrationId == created.Id, ct);
        if (localIntegration is not null && localIntegration.UserId != UserId)
            return Conflict(new { message = "Não foi possível iniciar a conexão com o banco." });

        if (localIntegration is null)
        {
            localIntegration = new PolpIntegration
            {
                UserId = UserId,
                PolpIntegrationId = created.Id,
                InstitutionId = institutionId
            };
            db.PolpIntegrations.Add(localIntegration);
        }

        localIntegration.Status = created.Status;
        localIntegration.ExecutionStatus = created.ExecutionStatus;
        localIntegration.InstitutionName = created.Institution?.Name ?? localIntegration.InstitutionName;
        localIntegration.UpdatedAt = DateTime.UtcNow;

        db.ConsentRecords.Add(new ConsentRecord
        {
            UserId = UserId,
            Kind = ConsentKinds.BankConnection,
            Version = LegalDocuments.BankConnectionNoticeVersion,
            Reference = institutionId.ToString()
        });
        await db.SaveChangesAsync(ct);

        // Sandbox (e consentimento reaproveitado) já nasce AUTHORISED e a Polp pode não mandar o
        // webhook "consents": dispara o backfill inicial aqui para não depender dele.
        if (created.Status == "AUTHORISED" && !localIntegration.SyncedLocally)
            QueueBackgroundSync(localIntegration.Id, UserId);

        return Ok(new
        {
            integrationId = localIntegration.Id,          // id local (usado pelo frontend para acompanhar o status)
            polpIntegrationId = created.Id,
            status = created.Status,
            urlToAuthenticate = created.UrlToAuthenticate
        });
    }

    [HttpGet("integrations")]
    public async Task<IActionResult> GetIntegrations(CancellationToken ct)
    {
        // PolpIntegrationId vai junto só pra agrupar visualmente no seletor de contas (ex.: conta
        // corrente + cartão de crédito do mesmo banco, vindos do mesmo consentimento). Cada
        // conta continua com sua própria visão de dados, nada é somado entre elas.
        var accounts = await db.BankAccounts
            .Where(b => b.UserId == UserId && b.IsActive)
            .Select(b => new { b.Id, b.Name, b.Balance, b.BankCode, b.PolpIntegrationId, b.PolpAccountId, b.PolpAccountType, b.PolpAccountSubtype })
            .ToListAsync(ct);

        // BankAccount.PolpIntegrationId guarda o id do consentimento na Polp, mas o frontend
        // precisa do Guid local (PolpIntegration.Id) pra desconectar. Busca o mapeamento de uma
        // vez em vez de uma query por conta.
        var localByRemoteId = await db.PolpIntegrations
            .Where(p => p.UserId == UserId)
            .ToDictionaryAsync(p => p.PolpIntegrationId, p => new { p.Id, p.LastSyncedAt, p.ExecutionStatus }, ct);

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
                lastSyncedAt = local?.LastSyncedAt,
                // PARTIAL_SUCCESS não bloqueia nada: só avisa que categoria/counterparty ainda
                // estão sendo refinadas pela Polp.
                enrichmentPending = local?.ExecutionStatus == "PARTIAL_SUCCESS",
                // Só contas (não cartões) têm saldo que a Polp consegue consultar na hora.
                canSyncBalance = a.PolpAccountId.HasValue && !CreditCardAccountDetector.LooksLikeCreditCard(a.PolpAccountType, a.PolpAccountSubtype),
                // Tipo cru da Polp + a mesma decisão de "é cartão" que Cartões/Dashboard já usam
                // (CreditCardAccountDetector), pra o frontend não apresentar um cartão como se fosse
                // outra conta bancária nem reimplementar essa heurística do lado de lá. O tipo cru
                // também é o que separa conta corrente de poupança/investimento em Investimentos.
                accountType = a.PolpAccountType,
                accountSubtype = a.PolpAccountSubtype,
                isCreditCard = CreditCardAccountDetector.LooksLikeCreditCard(a.PolpAccountType, a.PolpAccountSubtype)
            };
        });

        return Ok(result);
    }

    // {id} aqui é o Guid local salvo em PolpIntegration, não o id do consentimento na Polp.
    [HttpGet("integrations/{id:guid}")]
    public async Task<IActionResult> GetIntegrationStatus(Guid id, CancellationToken ct)
    {
        var local = await db.PolpIntegrations
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == UserId && p.Status != PolpDisconnectService.PendingStatus, ct);

        if (local is null) return NotFound();

        string? urlToAuthenticate = null;
        string? error = null;

        // O webhook "consents" mantém o status local em dia. Só consulta a Polp enquanto o
        // consentimento ainda não foi autorizado (é o que a tela de conexão acompanha), e no
        // máximo uma vez por janela.
        if (local.Status == "AWAITING_AUTHORIZATION" && DateTime.UtcNow - local.UpdatedAt > StatusRefreshWindow)
        {
            try
            {
                var remote = await polp.GetConsentAsync(local.PolpIntegrationId, ct);
                local.Status = remote.Status;
                local.ExecutionStatus = remote.ExecutionStatus;
                local.InstitutionName = remote.Institution?.Name ?? local.InstitutionName;
                urlToAuthenticate = remote.UrlToAuthenticate;
                error = remote.ErrorMessage;
            }
            catch (HttpRequestException)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível consultar o status da conexão." });
            }

            // A URL de autenticação vai direto pro navegador e não é gravada: é de uso único.
            local.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            if (local.Status == "AUTHORISED" && !local.SyncedLocally)
                QueueBackgroundSync(local.Id, UserId);
        }

        // Mapeia os status da Polp para os estados que o frontend já conhece.
        var mappedStatus = local.Status switch
        {
            "AWAITING_AUTHORIZATION" => "waiting_user_input",
            // Autorizado, mas a Polp ainda está importando os dados da instituição.
            "AUTHORISED" when local.ExecutionStatus == "AWAITING_RESOURCES" || !local.SyncedLocally => "syncing",
            "AUTHORISED" => "active",
            "REJECTED" => "login_error",
            "EXPIRED" => "outdated",
            _ => "unknown"
        };

        return Ok(new
        {
            status = mappedStatus,
            rawStatus = local.Status,
            error,
            urlToAuthenticate
        });
    }

    // Atualização de saldo a pedido do usuário. É a única consulta "na hora" que sobrou: o resto
    // chega por webhook, no ritmo da agenda da Polp. A Polp limita essa rota (30 req/min para
    // todos os usuários juntos) e o saldo consome a mesma cota mensal da sincronização
    // automática, por isso a janela de 1 minuto por conta.
    private static readonly TimeSpan BalanceSyncWindow = TimeSpan.FromMinutes(1);
    private static readonly Dictionary<Guid, DateTime> LastBalanceSync = [];

    [HttpPost("accounts/{id:guid}/balance-sync")]
    public async Task<IActionResult> SyncBalance(Guid id, CancellationToken ct)
    {
        var account = await db.BankAccounts
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == UserId && b.IsActive, ct);

        if (account?.PolpAccountId is not Guid remoteId || CreditCardAccountDetector.LooksLikeCreditCard(account))
            return NotFound();

        lock (LastBalanceSync)
        {
            if (LastBalanceSync.TryGetValue(account.Id, out var last) && DateTime.UtcNow - last < BalanceSyncWindow)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    message = "O saldo foi atualizado há pouco. Tente novamente em instantes.",
                    retryAfterSeconds = (int)Math.Ceiling((BalanceSyncWindow - (DateTime.UtcNow - last)).TotalSeconds)
                });
            }
            LastBalanceSync[account.Id] = DateTime.UtcNow;
        }

        PolpAccountDto remote;
        try
        {
            remote = await polp.SyncBalanceAsync(remoteId, ct);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível atualizar o saldo agora." });
        }

        if (remote.Balance?.AvailableAmount?.Value is { } balance)
        {
            account.Balance = balance;
            await db.SaveChangesAsync(ct);
        }

        return Ok(new { balance = account.Balance });
    }

    // Desconecta uma conexão bancária inteira (conta corrente + cartão do mesmo banco vêm no mesmo
    // consentimento e não dá pra revogar uma sem a outra). Apaga na hora tudo que veio dessa
    // conexão e revoga o consentimento na Polp. A resposta diz se a Polp aceitou o pedido ou se
    // ele ficou pendente: a tela não pode afirmar que o fornecedor apagou algo que ele não
    // confirmou. Desconectar não exclui a conta do Control Finance (isso é DELETE /api/account).
    [HttpDelete("integrations/{id:guid}")]
    public async Task<IActionResult> Disconnect(Guid id, CancellationToken ct)
    {
        var local = await db.PolpIntegrations
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == UserId, ct);

        if (local is null) return NotFound();

        var outcome = await disconnectService.DisconnectAsync(local, ct);

        return Ok(new
        {
            localDataDeleted = true,
            providerDeletion = outcome == RemoteDeletionOutcome.Requested ? "requested" : "pending"
        });
    }

    // Backfill inicial em background. Roda com seu próprio escopo de DI (próprio DbContext,
    // próprio PolpSyncService): o DbContext desta requisição é descartado assim que ela responde,
    // e usá-lo depois disso numa Task solta derrubaria com ObjectDisposedException.
    // CancellationToken.None de propósito: a requisição que disparou isso já terminou (e
    // cancelaria o token dela), mas o sync em si deve seguir até o fim mesmo assim.
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

public record PolpConnectDto(string ConnectorId, string? Document, string? ConsentVersion);
