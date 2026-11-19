using System.Net;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Services.Investments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/polp")]
[Authorize]
public class PolpController(
    AppDbContext db,
    IPolpService polp,
    InstitutionNameResolver institutionNames,
    IEncryptionService encryption,
    PolpDisconnectService disconnectService,
    IPolpInbox inbox,
    IMemoryCache cache, IIntegrationLock? balanceHistoryLock = null) : ApiControllerBase
{
    private const string ConnectorsCacheKey = "polp:connectors";
    private static readonly TimeSpan ConnectorsCacheDuration = TimeSpan.FromMinutes(10);

    [HttpGet("connectors")]
    public async Task<IActionResult> GetConnectors(CancellationToken ct)
    {
        if (!cache.TryGetValue(ConnectorsCacheKey, out List<PolpInstitutionDto>? institutions) || institutions is null)
        {
            try
            {
                institutions = await polp.GetInstitutionsAsync(ct);
                cache.Set(ConnectorsCacheKey, institutions, ConnectorsCacheDuration);
            }
            catch (Exception ex) when (PolpFailure.IsProviderFailure(ex, ct))
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível carregar os bancos disponíveis." });
            }
        }

        var connectors = institutions.Select(i =>
        {
            var color = i.Color ?? BankBrandColors.Find(i.Name);
            return new
            {
                id = i.Id.ToString(),
                name = i.Name,
                color,
                textColor = ResolveTextColor(color),
                initials = BuildInitials(i.Name),
                logoUrl = SafeLogoUrl(i.LogoUrl),
                status = i.Status,
                type = i.Type
            };
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
            .AsNoTracking()
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
        // A mensagem nomeia só o tipo do cadastro da própria sessão; nunca diz a quem pertence o
        // documento digitado nem se ele existe em outra conta.
        if (!DocumentValidator.IsValid(document) || !encryption.ComputeLookupHashCandidates(document).Contains(user.DocumentHash))
            return BadRequest(new
            {
                message = user.DocumentType == DocumentTypes.Cnpj
                    ? "O CNPJ informado não é o da empresa cadastrada nesta conta. Só é possível conectar contas desse CNPJ."
                    : user.DocumentType == DocumentTypes.Cpf
                        ? "O CPF informado não é o do cadastro desta conta. Só é possível conectar contas desse CPF."
                        : "O CPF/CNPJ informado não confere com o do seu cadastro."
            });

        // Conexão de empresa: a Polp exige, além do CNPJ, o CPF de quem opera a conta no banco (sem
        // ele responde 422). Esse CPF não é conferido com nada nosso (só guardamos o hash do CNPJ do
        // cadastro): vai direto pra Polp, em memória, e é descartado, como o próprio CNPJ.
        string? operatorCpf = null;
        if (document.Length == 14)
        {
            operatorCpf = new string((dto.OperatorDocument ?? string.Empty).Where(char.IsDigit).ToArray());
            if (!DocumentValidator.IsValidCpf(operatorCpf))
                return BadRequest(new { message = "Informe o CPF de quem opera a conta da empresa no banco." });
        }

        var documentType = DocumentTypes.FromDigits(document);
        if (!DocumentTypes.InstitutionAccepts(await institutionNames.FindTypeAsync(institutionId, ct), documentType))
            return BadRequest(new
            {
                message = documentType == DocumentTypes.Cnpj
                    ? "Esse banco atende só pessoa física. Escolha um banco que atenda empresas."
                    : "Esse banco atende só empresas. Escolha um banco que atenda pessoa física."
            });

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
            created = await polp.CreateConsentAsync(new CreatePolpConsentRequest(institutionId, document, UserId, operatorCpf), ct);
        }
        // 422 é a Polp recusando os dados (documento que o banco não aceita, banco só de empresa ou só
        // de pessoa física, CPF do operador ausente...), não uma falha dela: devolver 502 fazia a tela
        // dizer "serviço instável" e a pessoa tentar de novo sem nunca conseguir.
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            return BadRequest(new { message = ConsentRefusedMessage(document.Length == 14, (ex as PolpValidationException)?.Fields) });
        }
        catch (Exception ex) when (PolpFailure.IsProviderFailure(ex, ct))
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível iniciar a conexão com o banco." });
        }

        if (created.Id == Guid.Empty || created.InstitutionId != institutionId ||
            (created.ClientUserId is not null && created.ClientUserId != UserId.ToString()))
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível validar a conexão com o banco." });
        if (await RemovedPolpResources.IsRemovedAsync(db, created.Id, ct))
            return Conflict(new { message = "A autorização anterior está sendo revogada. Aguarde antes de conectar novamente." });

        // avoidDuplicates pode devolver um consentimento que já temos (mesmo id): reaproveita a
        // linha local em vez de violar o índice único.
        var localIntegration = await db.PolpIntegrations
            .FirstOrDefaultAsync(p => p.PolpIntegrationId == created.Id, ct);
        // Linha em DISCONNECT_PENDING está sendo apagada e o consentimento revogado: reativá-la
        // traria de volta uma conexão que o usuário mandou desfazer.
        if (localIntegration is not null
            && (localIntegration.UserId != UserId || localIntegration.InstitutionId != institutionId ||
                localIntegration.Status is "DISCONNECT_PENDING" or "REVOKED" or "REJECTED" or "EXPIRED"))
            return Conflict(new { message = "Não foi possível iniciar a conexão com o banco." });

        // avoidDuplicates devolve o consentimento antigo mesmo sem todos os produtos.
        if (!PolpService.HasAllRequestedProducts(created.Products)
            && created.Status is "AUTHORISED" or "AWAITING_AUTHORIZATION"
            && created.CreatedAt is { } consentCreatedAt && consentCreatedAt.ToUniversalTime() < DateTime.UtcNow.AddMinutes(-5))
        {
            if (created.ClientUserId != UserId.ToString())
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível validar a conexão com o banco." });
            try
            {
                var recreated = await polp.RecreateConsentAsync(created.Id, document.Length == 14, ct);
                if (recreated.Id != created.Id || recreated.InstitutionId != institutionId || recreated.ClientUserId != UserId.ToString())
                    return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível validar a conexão com o banco." });
                created = recreated;
            }
            catch (Exception ex) when (PolpFailure.IsProviderFailure(ex, ct))
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível iniciar a conexão com o banco." });
            }
        }

        var institutionName = created.Institution?.Name ?? localIntegration?.InstitutionName
            ?? await institutionNames.FindAsync(institutionId, ct);

        var reused = localIntegration is not null;
        if (localIntegration is null)
        {
            localIntegration = new PolpIntegration
            {
                UserId = UserId,
                PolpIntegrationId = created.Id,
                InstitutionId = institutionId,
                Status = created.Status,
                ExecutionStatus = created.ExecutionStatus,
                InstitutionName = institutionName,
                ConsentProductsJson = PolpConsentMetadataService.SerializeProducts(created.Products),
                UpdatedAt = DateTime.UtcNow,
                // Decidido aqui, antes de qualquer conta chegar: só um banco iniciado sem nenhum
                // outro banco com contas pode virar padrão sozinho (DefaultBankAccounts).
                AutoDefaultPending = documentType == DocumentTypes.Cnpj
                    && !await db.BankAccounts.AnyAsync(b => b.UserId == UserId && b.PolpIntegrationId != null, ct)
            };
            db.PolpIntegrations.Add(localIntegration);
        }
        else
        {
            db.Entry(localIntegration).State = EntityState.Detached;
        }

        db.ConsentRecords.Add(new ConsentRecord
        {
            UserId = UserId,
            Kind = ConsentKinds.BankConnection,
            Version = LegalDocuments.BankConnectionNoticeVersion,
            Reference = institutionId.ToString()
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (!reused)
        {
            // avoidDuplicates can return the same remote id to concurrent requests on
            // different nodes. The unique index wins; never create a second local binding.
            db.ChangeTracker.Clear();
            var existing = await db.PolpIntegrations.AsNoTracking()
                .SingleOrDefaultAsync(p => p.PolpIntegrationId == created.Id && p.UserId == UserId && p.InstitutionId == institutionId, ct);
            if (existing is null)
            {
                // A conta foi excluída enquanto a Polp criava o consentimento (a exclusão trava a
                // linha do usuário, então este INSERT falhou pela FK). Sem dono local ninguém o
                // reaproveita: em vez de deixá-lo ativo, entra na fila de revogação. Para um usuário
                // que ainda existe, o erro segue adiante e a próxima tentativa recebe o mesmo
                // consentimento pelo avoidDuplicates.
                if (!await db.Users.AnyAsync(u => u.Id == UserId, CancellationToken.None))
                {
                    await disconnectService.QueueOrphanConsentAsync(created.Id, CancellationToken.None);
                    return Unauthorized();
                }
                throw;
            }
            return Conflict(new { message = "Esta conexão já está em andamento. Acompanhe a autorização existente.", integrationId = existing.Id, status = existing.Status });
        }

        if (reused)
        {
            var now = DateTime.UtcNow;
            var updated = await db.PolpIntegrations
                .Where(p => p.Id == localIntegration.Id && p.UserId == UserId && p.InstitutionId == institutionId
                    && p.Status == localIntegration.Status && p.UpdatedAt == localIntegration.UpdatedAt)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(p => p.Status, created.Status)
                    .SetProperty(p => p.ExecutionStatus, created.ExecutionStatus)
                    .SetProperty(p => p.ConsentProductsJson, PolpConsentMetadataService.SerializeProducts(created.Products))
                    .SetProperty(p => p.InstitutionName, institutionName)
                    .SetProperty(p => p.UpdatedAt, now), ct);
            if (updated == 0)
                return Conflict(new { message = "Não foi possível iniciar a conexão com o banco." });
        }

        // A tombstoned provider id is never reused; delayed events must remain discarded.

        if (created.Status == "AUTHORISED" && !localIntegration.SyncedLocally)
            await EnqueueBackfillAsync(created.Id, localIntegration.Id, ct);

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
            .Select(b => new { b.Id, b.Name, b.Balance, b.BalanceUpdatedAt, b.BankCode, b.PolpIntegrationId, b.PolpAccountId, b.PolpAccountType, b.PolpAccountSubtype, b.Number, b.IsDefault })
            .ToListAsync(ct);

        // BankAccount.PolpIntegrationId guarda o id do consentimento na Polp, mas o frontend
        // precisa do Guid local (PolpIntegration.Id) pra desconectar. Busca o mapeamento de uma
        // vez em vez de uma query por conta.
        var localByRemoteId = await db.PolpIntegrations
            .Where(p => p.UserId == UserId)
            .ToDictionaryAsync(p => p.PolpIntegrationId, p => new { p.Id, p.LastSyncedAt, p.EnrichmentPending, p.Status, p.CreatedAt }, ct);

        var result = accounts.Select(a =>
        {
            var local = a.PolpIntegrationId.HasValue && localByRemoteId.TryGetValue(a.PolpIntegrationId.Value, out var info)
                ? info
                : null;

            var isCreditCard = CreditCardAccountDetector.LooksLikeCreditCard(a.PolpAccountType, a.PolpAccountSubtype);

            return new
            {
                a.Id,
                a.Name,
                a.Number,
                Balance = a.PolpAccountId.HasValue && isCreditCard ? (decimal?)null : a.Balance,
                balanceUpdatedAt = a.BalanceUpdatedAt,
                a.BankCode,
                a.PolpIntegrationId,
                localIntegrationId = local?.Id,
                lastSyncedAt = local?.LastSyncedAt,
                enrichmentPending = local?.EnrichmentPending == true,
                connectionStatus = local?.Status,
                connectedAt = local?.CreatedAt,
                // Só contas (não cartões) têm saldo que a Polp consegue consultar na hora.
                canSyncBalance = a.PolpAccountId.HasValue && !isCreditCard,
                // Tipo cru da Polp + a mesma decisão de "é cartão" que Cartões/Dashboard já usam
                // (CreditCardAccountDetector), pra o frontend não apresentar um cartão como se fosse
                // outra conta bancária nem reimplementar essa heurística do lado de lá. O tipo cru
                // também é o que separa conta corrente de poupança/investimento em Investimentos.
                accountType = a.PolpAccountType,
                accountSubtype = a.PolpAccountSubtype,
                isCreditCard,
                // Conexão revogada, expirada ou em desconexão deixa de valer como padrão sem
                // promover outra conta: a escolha seguinte é sempre do usuário.
                isDefault = a.IsDefault && !isCreditCard && local?.Status == "AUTHORISED"
            };
        });

        return Ok(result);
    }

    // {id} aqui é o Guid local salvo em PolpIntegration, não o id do consentimento na Polp.
    [HttpGet("integrations/{id:guid}")]
    public async Task<IActionResult> GetIntegrationStatus(Guid id, CancellationToken ct)
    {
        var local = await db.PolpIntegrations
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == UserId && p.Status != PolpDisconnectService.PendingStatus, ct);

        if (local is null) return NotFound();

        string? urlToAuthenticate = null;
        string? error = null;

        // O webhook "consents" mantém o status local em dia. Só consulta a Polp enquanto o
        // consentimento ainda não foi autorizado (é o que a tela de conexão acompanha), e no
        // máximo uma vez por janela.
        if (local.Status == "AWAITING_AUTHORIZATION" && DateTime.UtcNow - local.UpdatedAt > StatusRefreshWindow)
        {
            PolpConsentDto remote;
            string? institutionName;
            try
            {
                remote = await polp.GetConsentAsync(local.PolpIntegrationId, ct);
                PolpConsentMetadataService.ValidatePrimary(local, remote);
                institutionName = remote.Institution?.Name ?? local.InstitutionName
                    ?? await institutionNames.FindAsync(local.InstitutionId, ct);
            }
            catch (Exception ex) when (PolpFailure.IsProviderFailure(ex, ct) || ex is PolpPermanentException)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível consultar o status da conexão." });
            }

            // Condicional: a desconexão pode ter marcado DISCONNECT_PENDING durante a consulta.
            // A URL de autenticação vai direto pro navegador e não é gravada: é de uso único.
            var enrichmentPending = PolpSyncService.HasEnrichmentPending(remote.Flags);
            var now = DateTime.UtcNow;
            var updated = await db.PolpIntegrations
                .Where(p => p.Id == local.Id && p.UserId == UserId && p.Status == "AWAITING_AUTHORIZATION" && p.UpdatedAt == local.UpdatedAt)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(p => p.Status, remote.Status)
                    .SetProperty(p => p.ExecutionStatus, remote.ExecutionStatus)
                    .SetProperty(p => p.ConsentProductsJson, PolpConsentMetadataService.SerializeProducts(remote.Products))
                    .SetProperty(p => p.EnrichmentPending, enrichmentPending)
                    .SetProperty(p => p.InstitutionName, institutionName)
                    .SetProperty(p => p.UpdatedAt, now), ct);
            if (updated == 0) return NotFound();

            local.Status = remote.Status;
            local.ExecutionStatus = remote.ExecutionStatus;
            urlToAuthenticate = remote.UrlToAuthenticate;
            error = remote.Status == "REJECTED" ? "A autorização foi recusada pelo banco." : null;

            if (local.Status == "AUTHORISED" && !local.SyncedLocally)
                await EnqueueBackfillAsync(local.PolpIntegrationId, local.Id, ct);
            else if (local.Status == "AUTHORISED")
            {
                await inbox.EnqueueAsync(new(PolpInternalEvents.ConsentMetadata, "consents", local.PolpIntegrationId.ToString("D"), null), ct);
                if (await InvestmentHolder.AllowsAsync(db, local.UserId, ct))
                    await inbox.EnqueueAsync(new(PolpInternalEvents.InvestmentDiscovery, "integrations", local.Id.ToString("D"), null), ct);
            }
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
            "REVOKED" => "revoked",
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

    [HttpPost("accounts/{id:guid}/balance-sync")]
    public async Task<IActionResult> SyncBalance(Guid id, CancellationToken ct)
    {
        var account = await db.BankAccounts
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == UserId && b.IsActive, ct);

        if (account?.PolpAccountId is not Guid remoteId || CreditCardAccountDetector.LooksLikeCreditCard(account))
            return NotFound();
        if (!await db.PolpIntegrations.AnyAsync(p => p.UserId == UserId && p.PolpIntegrationId == account.PolpIntegrationId && p.Status == "AUTHORISED", ct))
            return NotFound();

        if (await PolpBalanceSyncLimiter.TryReserveAsync(db, account.Id, DateTime.UtcNow, ct) is { } retryAfter)
            return BalanceSyncTooSoon(retryAfter);

        PolpAccountDto remote;
        try
        {
            remote = await polp.SyncBalanceAsync(remoteId, ct);
        }
        catch (Exception ex) when (PolpFailure.IsProviderFailure(ex, ct))
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível atualizar o saldo agora." });
        }

        if (remote.Balance?.AvailableAmount?.Value is not null)
        {
            if (remote.Id != remoteId || remote.ConsentId != account.PolpIntegrationId)
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível validar o saldo recebido." });
            if (!await PersistBalanceAsync(account, remote, ct)) return NotFound();
            await db.Entry(account).ReloadAsync(ct);
        }
        if (remote.Balance?.HasReservedBalance is { } hasReserved)
        {
            if (remote.Id != remoteId || remote.ConsentId != account.PolpIntegrationId)
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível validar o saldo recebido." });
            var updated = await db.BankAccounts.Where(b => b.Id == account.Id && b.UserId == UserId
                && db.PolpIntegrations.Any(p => p.UserId == UserId && p.PolpIntegrationId == b.PolpIntegrationId && p.Status == "AUTHORISED"))
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.HasReservedBalance, hasReserved), ct);
            if (updated > 0 && hasReserved)
                await inbox.EnqueueAsync(new(PolpInternalEvents.ReservedBalances, "accounts", remoteId.ToString(), null, PolpInboxPriority.Low), ct);
        }

        return Ok(new { balance = account.Balance });
    }

    // Saldo de todas as contas (não cartões) de uma conexão. Uma chamada por conta, em série, com a
    // mesma janela por conta da rota acima: o limite de 30 req/min da Polp é global do cliente.
    [HttpPost("integrations/{id:guid}/balance-sync")]
    public async Task<IActionResult> SyncConnectionBalance(Guid id, CancellationToken ct)
    {
        var integration = await db.PolpIntegrations
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == UserId && p.Status == "AUTHORISED", ct);
        if (integration is null) return NotFound();

        var accounts = (await db.BankAccounts
                .Where(b => b.UserId == UserId && b.IsActive && b.PolpIntegrationId == integration.PolpIntegrationId && b.PolpAccountId != null)
                .ToListAsync(ct))
            .Where(b => !CreditCardAccountDetector.LooksLikeCreditCard(b))
            .ToList();
        if (accounts.Count == 0) return NotFound();

        var due = new List<BankAccount>();
        var shortestWait = PolpBalanceSyncLimiter.AccountWindow;
        foreach (var account in accounts)
        {
            if (await PolpBalanceSyncLimiter.TryReserveAsync(db, account.Id, DateTime.UtcNow, ct) is not { } wait) due.Add(account);
            else if (wait < shortestWait) shortestWait = wait;
        }
        if (due.Count == 0) return BalanceSyncTooSoon(shortestWait);

        var failed = 0;
        foreach (var account in due)
        {
            try
            {
                var remote = await polp.SyncBalanceAsync(account.PolpAccountId!.Value, ct);
                if (remote.Id != account.PolpAccountId || remote.ConsentId != integration.PolpIntegrationId)
                {
                    failed++;
                    continue;
                }
                if (remote.Balance?.AvailableAmount?.Value is not null)
                {
                    if (!await PersistBalanceAsync(account, remote, ct)) failed++;
                }
                if (remote.Balance?.HasReservedBalance is { } hasReserved)
                {
                    var updated = await db.BankAccounts.Where(b => b.Id == account.Id && b.UserId == UserId
                        && db.PolpIntegrations.Any(p => p.Id == integration.Id && p.UserId == UserId && p.Status == "AUTHORISED"))
                        .ExecuteUpdateAsync(s => s.SetProperty(b => b.HasReservedBalance, hasReserved), ct);
                    if (updated > 0 && hasReserved)
                        await inbox.EnqueueAsync(new(PolpInternalEvents.ReservedBalances, "accounts", remote.Id.ToString(), null, PolpInboxPriority.Low), ct);
                }
            }
            catch (Exception ex) when (PolpFailure.IsProviderFailure(ex, ct))
            {
                failed++;
            }
        }

        if (failed == due.Count)
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Não foi possível atualizar o saldo agora." });

        return Ok(new { updated = due.Count - failed, failed, skipped = accounts.Count - due.Count });
    }

    private static readonly IntegrationLock FallbackBalanceHistoryLock = new();

    private async Task<bool> PersistBalanceAsync(BankAccount source, PolpAccountDto remote, CancellationToken ct)
    {
        var integration = await db.PolpIntegrations.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == UserId && p.PolpIntegrationId == source.PolpIntegrationId
                && p.Status == "AUTHORISED", ct);
        if (integration is null) return false;
        var config = HttpContext.RequestServices.GetService<IConfiguration>() ?? new ConfigurationBuilder().Build();
        var clock = HttpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
        return await (balanceHistoryLock ?? FallbackBalanceHistoryLock).RunAsync(db, integration.Id, async () =>
        {
            // Revalidar dentro do mesmo lock usado por sync/desconexão; a chamada remota já terminou.
            var account = await db.BankAccounts.FirstOrDefaultAsync(b => b.Id == source.Id && b.UserId == UserId
                && b.IsActive && b.PolpAccountId == remote.Id && b.PolpIntegrationId == remote.ConsentId
                && db.PolpIntegrations.Any(p => p.Id == integration.Id && p.UserId == UserId && p.Status == "AUTHORISED"), ct);
            if (account is null) return false;
            await db.Entry(account).ReloadAsync(ct);
            var documentType = await db.Users.Where(u => u.Id == UserId).Select(u => u.DocumentType).FirstAsync(ct);
            await BankBalanceHistory.ApplyAsync(db, account, remote, documentType, config, clock.GetUtcNow(), ct);
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    private ObjectResult BalanceSyncTooSoon(TimeSpan retryAfter) =>
        StatusCode(StatusCodes.Status429TooManyRequests, new
        {
            message = "O saldo foi atualizado há pouco. Tente novamente em instantes.",
            retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds)
        });

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

    // Sandbox (e consentimento reaproveitado) já nasce AUTHORISED e a Polp pode não mandar o
    // webhook "consents": o backfill vai para a inbox e o worker tenta até as contas existirem.
    private async Task EnqueueBackfillAsync(Guid consentId, Guid integrationId, CancellationToken ct)
    {
        await inbox.EnqueueAsync(new(PolpInternalEvents.Backfill, "consents", consentId.ToString("D"), null), ct);
        // Independent inbox items: an account/card failure must not prevent investment discovery.
        await inbox.EnqueueAsync(new(PolpInternalEvents.ConsentMetadata, "consents", consentId.ToString("D"), null), ct);
        if (await InvestmentHolder.AllowsIntegrationAsync(db, integrationId, ct))
            await inbox.EnqueueAsync(new(PolpInternalEvents.InvestmentDiscovery, "integrations", integrationId.ToString("D"), null), ct);
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

    // Só o nome do campo recusado orienta a mensagem; o texto da Polp nunca chega ao navegador.
    private static string ConsentRefusedMessage(bool business, IReadOnlyList<string>? fields)
    {
        var refused = fields ?? [];
        if (business && refused.Contains("cnpj"))
            return "A Polp não aceitou o CNPJ para esse banco. Confira o número e se o banco escolhido atende empresas.";
        if (refused.Contains("cpf"))
            return business
                ? "A Polp não aceitou o CPF de quem opera a conta da empresa. Confira o número informado."
                : "A Polp não aceitou o CPF para esse banco. Confira o número e se o banco escolhido atende pessoa física.";
        if (refused.Contains("consent"))
            return "Não foi possível criar uma nova autorização bancária agora. Tente novamente mais tarde.";
        if (refused.Contains("institution_id"))
            return "Esse banco não está disponível para conexão no momento. Escolha outro banco ou tente mais tarde.";
        return business
            ? "A Polp não aceitou esses dados para esse banco. Confira o CNPJ, o CPF de quem opera a conta e se o banco escolhido atende empresas."
            : "A Polp não aceitou esses dados para esse banco. Confira o CPF e se o banco escolhido atende pessoa física.";
    }

    // A logo vem de servidores dos bancos, informada pela Polp. Só https absoluto chega ao navegador:
    // http viraria conteúdo misto e qualquer outro esquema não é imagem. O resto cai nas iniciais.
    private static string? SafeLogoUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;

    private static string BuildInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "??";
        if (parts.Length == 1) return parts[0][..Math.Min(2, parts[0].Length)];
        return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
    }
}

// OperatorDocument: CPF de quem opera a conta, exigido pela Polp só quando Document é um CNPJ.
public record PolpConnectDto(string ConnectorId, string? Document, string? ConsentVersion, string? OperatorDocument = null);
