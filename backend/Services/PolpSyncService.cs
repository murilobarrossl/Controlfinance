using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ControlFinance.API.Services;

// Puxa contas, cartões, faturas e transações da Polp (API v2) e grava no banco. Toda operação tem
// duas fases: leitura na Polp fora de qualquer transação (pode levar dezenas de segundos) e
// gravação curta sob IIntegrationLock, que relê a integração e não grava nada se ela foi
// desconectada nesse meio-tempo. Falha em qualquer conta, cartão ou fatura sobe como exceção e o
// evento da inbox é reprocessado: nada parcial é commitado.
public class PolpSyncService(
    AppDbContext db, IPolpService polp, IConfiguration config, InstitutionNameResolver institutionNames,
    IIntegrationLock integrationLock, TimeProvider time, ILogger<PolpSyncService>? logger = null, IPolpInbox? inbox = null,
    IEncryptionService? encryption = null)
{
    public const string AuthorisedStatus = "AUTHORISED";
    // 4: relê o histórico para preencher documento (partie_cnpj_cpf) e conta completa da contraparte.
    // 5: relê para marcar se o CNPJ da contraparte veio do banco (CounterpartyCnpjFromBank).
    // 6: relê para marcar pagamentos de tributo (PolpTaxPayment, TaxAuthority).
    public const int TransactionDetailsVersion = 6;
    public static readonly TimeSpan ReconcileWindow = TimeSpan.FromDays(7);

    private const string CardBillPaymentType = "PAGAMENTO_FATURA";
    private const string FutureSentinel = "0001-01-01";

    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById(UserClock.DefaultTimeZoneId);

    public static bool HasEnrichmentPending(IEnumerable<string>? flags) =>
        flags?.Any(f => f is "ERROR_PROCESSING_CATEGORIES" or "ERROR_PROCESSING_COUNTERPARTIES") ?? false;

    /// <summary>Grava status, execution_status, enriquecimento pendente e nome da instituição.
    /// Devolve true se o consentimento está autorizado e ainda falta o backfill.</summary>
    public async Task<bool> RefreshConsentAsync(PolpIntegration local, CancellationToken ct)
    {
        var remote = await polp.GetConsentAsync(local.PolpIntegrationId, ct);
        PolpConsentMetadataService.ValidatePrimary(local, remote);
        if (string.IsNullOrWhiteSpace(remote.Status))
            throw new InvalidOperationException("Consentimento sem status na resposta da Polp.");

        var remoteName = remote.Institution?.Name;
        var institutionName = !string.IsNullOrWhiteSpace(remoteName)
            ? remoteName
            : local.InstitutionName ?? await institutionNames.FindAsync(local.InstitutionId, ct);
        var enrichmentPending = HasEnrichmentPending(remote.Flags);
        var now = time.GetUtcNow().UtcDateTime;

        return await integrationLock.RunAsync(db, local.Id, async () =>
        {
            var current = await db.PolpIntegrations.AsNoTracking().FirstOrDefaultAsync(p => p.Id == local.Id, ct);
            if (current is null || current.Status is "DISCONNECT_PENDING" or "REVOKED" or "REJECTED" or "EXPIRED") return false;
            if (current.Status == AuthorisedStatus && remote.Status == "AWAITING_AUTHORIZATION") return false;
            if (current.UpdatedAt != local.UpdatedAt) return false;

            // Condicional mesmo sob o lock: a desconexão marca DISCONNECT_PENDING fora dele.
            var updated = await db.PolpIntegrations
                .Where(p => p.Id == local.Id && p.Status != PolpDisconnectService.PendingStatus)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, remote.Status)
                    .SetProperty(p => p.ExecutionStatus, remote.ExecutionStatus)
                    .SetProperty(p => p.ConsentProductsJson, PolpConsentMetadataService.SerializeProducts(remote.Products))
                    .SetProperty(p => p.EnrichmentPending, enrichmentPending)
                    .SetProperty(p => p.InstitutionName, institutionName)
                    .SetProperty(p => p.UpdatedAt, now), ct);
            if (updated > 0 && PolpAuthorizationEnd.IsEnded(remote.Status))
                await PolpAuthorizationEnd.SuspendAsync(db, now, ct, local.Id);

            return updated > 0 && remote.Status == AuthorisedStatus && !current.SyncedLocally;
        }, ct);
    }

    /// <summary>Backfill: contas, cartões, faturas e todo o histórico. Só marca SyncedLocally se
    /// tudo foi lido e gravado.</summary>
    public async Task BackfillAsync(PolpIntegration local, CancellationToken ct)
    {
        if (local.SyncedLocally || local.Status == PolpDisconnectService.PendingStatus) return;
        if (local.Status != AuthorisedStatus)
        {
            if (local.Status is "REJECTED" or "EXPIRED") return;
            throw new PolpResourceNotReadyException("Consentimento ainda não autorizado localmente.");
        }

        var plan = new Plan(local) { Backfill = true, FullTransactionHistory = true };
        plan.Accounts.AddRange(await polp.GetAccountsAsync(local.PolpIntegrationId, null, ct));
        plan.Cards.AddRange(await polp.GetCreditCardsAsync(local.PolpIntegrationId, null, ct));

        // O consentimento chega AUTHORISED antes de a Polp gerar as contas (no sandbox, ~2 s
        // depois): lista vazia aqui é "cedo demais", não "banco sem contas".
        if (plan.Accounts.Count == 0 && plan.Cards.Count == 0)
            throw new PolpResourceNotReadyException("A Polp ainda não gerou as contas do consentimento.");

        foreach (var account in plan.Accounts) await FetchTransactionsAsync(plan, account.Id, isCard: false, null, ct);
        foreach (var card in plan.Cards) await FetchTransactionsAsync(plan, card.Id, isCard: true, null, ct);

        await CommitAsync(plan, ct);
    }

    /// <summary>Eventos accounts / credit_cards. Conta ou cartão que aparece pela primeira vez
    /// vem com histórico completo (e faturas, se for cartão): nenhum outro evento o traria.</summary>
    public async Task SyncAccountsAndCardsAsync(PolpIntegration local, PolpQueryWindow? window, CancellationToken ct)
    {
        if (local.Status != AuthorisedStatus) return;
        var plan = new Plan(local);
        plan.Accounts.AddRange(await polp.GetAccountsAsync(local.PolpIntegrationId, window, ct));
        plan.Cards.AddRange(await polp.GetCreditCardsAsync(local.PolpIntegrationId, window, ct));
        await FetchNewResourcesAsync(plan, ct);
        await CommitAsync(plan, ct);
    }

    /// <summary>Eventos bills / accounts.transactions / credit_cards.transactions de uma conta
    /// ou cartão já conhecido.</summary>
    public async Task SyncResourceAsync(
        PolpIntegration local, BankAccount account, bool transactions, PolpQueryWindow? window, CancellationToken ct)
    {
        if (local.Status != AuthorisedStatus || account.UserId != local.UserId
            || account.PolpIntegrationId != local.PolpIntegrationId || account.PolpAccountId is not Guid remoteId) return;
        var isCard = CreditCardAccountDetector.LooksLikeCreditCard(account);
        var plan = new Plan(local);

        if (transactions)
            await FetchTransactionsAsync(plan, remoteId, isCard, window, ct);
        else if (isCard)
        {
            var bills = await polp.GetBillsAsync(remoteId, null, ct);
            if (bills.Any(b => b.CreditCardId != remoteId)) throw new PolpPermanentException("Fatura de outro recurso na resposta da Polp.");
            plan.Bills[remoteId] = bills;
            // Pagamento de fatura muda o limite sem garantia de aviso credit_cards.
            var cards = await polp.GetCreditCardsAsync(local.PolpIntegrationId, null, ct);
            if (cards.Any(c => c.ConsentId != local.PolpIntegrationId)) throw new PolpPermanentException("Cartão de outro consentimento na resposta da Polp.");
            plan.Cards.AddRange(cards.Where(c => c.Id == remoteId));
        }
        else
            return;

        await CommitAsync(plan, ct);
    }

    // Linhas importadas antes de existirem os campos de detalhe só os recebem relendo o histórico.
    public async Task RefreshTransactionDetailsAsync(PolpIntegration local, CancellationToken ct)
    {
        if (local.Status != AuthorisedStatus || !local.SyncedLocally || local.TransactionDetailsVersion >= TransactionDetailsVersion) return;

        var plan = new Plan(local) { FullTransactionHistory = true };
        var known = await db.BankAccounts.AsNoTracking()
            .Where(b => b.UserId == local.UserId && b.PolpIntegrationId == local.PolpIntegrationId && b.PolpAccountId != null && b.IsActive)
            .ToListAsync(ct);
        foreach (var account in known)
            await FetchTransactionsAsync(plan, account.PolpAccountId!.Value, CreditCardAccountDetector.LooksLikeCreditCard(account), null, ct);

        await CommitAsync(plan, ct);
    }

    /// <summary>Rede de segurança para avisos perdidos: tudo que mudou nos últimos 7 dias.</summary>
    public async Task ReconcileAsync(PolpIntegration local, CancellationToken ct)
    {
        if (local.Status == PolpDisconnectService.PendingStatus || local.Status != AuthorisedStatus) return;
        if (!local.SyncedLocally)
        {
            await BackfillAsync(local, ct);
            return;
        }

        var window = PolpQueryWindow.ChangedSince(time.GetUtcNow() - ReconcileWindow);
        var plan = new Plan(local);
        plan.Accounts.AddRange(await polp.GetAccountsAsync(local.PolpIntegrationId, window, ct));
        plan.Cards.AddRange(await polp.GetCreditCardsAsync(local.PolpIntegrationId, window, ct));
        await FetchMissingCardIdentitiesAsync(plan, ct);
        await FetchNewResourcesAsync(plan, ct);

        var known = await db.BankAccounts.AsNoTracking()
            .Where(b => b.UserId == local.UserId && b.PolpIntegrationId == local.PolpIntegrationId && b.PolpAccountId != null)
            .ToListAsync(ct);
        foreach (var account in known)
        {
            var remoteId = account.PolpAccountId!.Value;
            if (plan.Transactions.ContainsKey(remoteId)) continue;
            await FetchTransactionsAsync(plan, remoteId, CreditCardAccountDetector.LooksLikeCreditCard(account), window, ct);
        }

        await CommitAsync(plan, ct);
    }

    // Legacy cards may never occur in the delta window again. Read their identity from the
    // existing card-list endpoint, without importing limits, bills or transactions from it.
    private async Task FetchMissingCardIdentitiesAsync(Plan plan, CancellationToken ct)
    {
        var alreadyRead = plan.Cards.Select(c => c.Id).ToList();
        var missing = await db.BankAccounts.AsNoTracking()
            .Where(b => b.UserId == plan.Integration.UserId && b.IsActive
                && b.PolpIntegrationId == plan.Integration.PolpIntegrationId
                && b.PolpAccountType == CreditCardAccountDetector.CardType
                && b.PolpOriginalName == null && b.PolpAccountId != null
                && !alreadyRead.Contains(b.PolpAccountId.Value))
            .Select(b => b.PolpAccountId!.Value)
            .ToListAsync(ct);
        if (missing.Count == 0) return;
        List<PolpCreditCardDto> identities;
        try
        {
            identities = await polp.GetCreditCardsAsync(plan.Integration.PolpIntegrationId, null, ct);
        }
        catch (Exception ex) when (PolpFailure.IsProviderFailure(ex, ct) || ex is PolpPermanentException)
        {
            // Optional visual enrichment must never stop the existing financial pipeline.
            // Real cancellation is not caught. Retry on a future reconciliation.
            logger?.LogWarning("Identidade visual Polp adiada por {FailureType}; sincronização financeira continua",
                ex.GetType().Name);
            return;
        }
        foreach (var remote in identities)
        {
            if (missing.Contains(remote.Id))
                plan.CardIdentities[remote.Id] = new CardIdentity(remote.Name, remote.CreditCardNetwork, remote.ProductType);
        }
    }

    // Faturas sempre sem janela: a fatura atual depende da última fechada, que uma janela pode
    // deixar de fora. São poucas por cartão.
    private async Task FetchTransactionsAsync(Plan plan, Guid remoteId, bool isCard, PolpQueryWindow? window, CancellationToken ct)
    {
        List<RemoteTransaction> items;
        if (isCard)
        {
            var rows = await polp.GetCardTransactionsAsync(remoteId, window, ct);
            if (rows.Any(t => t.CreditCardId != remoteId)) throw new PolpPermanentException("Transação de outro recurso na resposta da Polp.");
            items = rows.Select(RemoteTransaction.From).ToList();
        }
        else
        {
            var rows = await polp.GetAccountTransactionsAsync(remoteId, window, ct);
            if (rows.Any(t => t.AccountId != remoteId)) throw new PolpPermanentException("Transação de outro recurso na resposta da Polp.");
            items = rows.Select(RemoteTransaction.From).ToList();
        }
        plan.Transactions[remoteId] = new TransactionBatch(items, Complete: window is null);
        if (isCard)
        {
            var bills = await polp.GetBillsAsync(remoteId, null, ct);
            if (bills.Any(b => b.CreditCardId != remoteId)) throw new PolpPermanentException("Fatura de outro recurso na resposta da Polp.");
            plan.Bills[remoteId] = bills;
            try
            {
                plan.Recurrings[remoteId] = await polp.GetCardRecurringsAsync(remoteId, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested
                && ex is HttpRequestException or IOException or TaskCanceledException or JsonException or PolpPermanentException)
            {
                // Enriquecimento opcional: uma falha nunca interrompe a importação financeira.
                // Mantém o snapshot anterior, cuja data continua explícita. Nenhum dado bancário no log.
                logger?.LogWarning("Recorrências do cartão não atualizadas: {ErrorType}", ex.GetType().Name);
            }
        }
    }

    private async Task FetchNewResourcesAsync(Plan plan, CancellationToken ct)
    {
        var remoteIds = plan.Accounts.Select(a => a.Id).Concat(plan.Cards.Select(c => c.Id)).ToList();
        var known = (await db.BankAccounts.AsNoTracking()
                .Where(b => b.UserId == plan.Integration.UserId && b.PolpAccountId != null && remoteIds.Contains(b.PolpAccountId.Value))
                .Select(b => b.PolpAccountId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

        foreach (var account in plan.Accounts.Where(a => !known.Contains(a.Id)))
            await FetchTransactionsAsync(plan, account.Id, isCard: false, null, ct);
        foreach (var card in plan.Cards.Where(c => !known.Contains(c.Id)))
            await FetchTransactionsAsync(plan, card.Id, isCard: true, null, ct);
    }

    private async Task CommitAsync(Plan plan, CancellationToken ct)
    {
        if (plan.Accounts.Any(a => a.ConsentId != plan.Integration.PolpIntegrationId || a.Id == Guid.Empty)
            || plan.Cards.Any(c => c.ConsentId != plan.Integration.PolpIntegrationId || c.Id == Guid.Empty))
            throw new PolpPermanentException("Recurso de outro consentimento na resposta da Polp.");
        var userId = plan.Integration.UserId;
        var categoryIds = await PrepareCategoriesAsync(plan, ct);
        var bankName = plan.Integration.InstitutionName
            ?? await institutionNames.FindAsync(plan.Integration.InstitutionId, ct)
            ?? UnresolvedBankName;
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.TimeZone, u.DocumentType })
            .FirstAsync(ct);

        // Tudo que for lido sob o lock tem de vir do banco, não de uma cópia rastreada antes dele.
        db.ChangeTracker.Clear();

        try
        {
            await integrationLock.RunAsync(db, plan.Integration.Id,
                () => ApplyAsync(plan, categoryIds, bankName, user.TimeZone, user.DocumentType, ct), ct);
        }
        catch (IntegrationGoneException)
        {
            // Desconectada durante a gravação: o rollback já descartou tudo.
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
        // Só conexões marcadas no Connect pagam essa verificação; a marca cai assim que a empresa
        // tem conta padrão.
        if (plan.Integration.AutoDefaultPending)
        {
            // Os dados do banco já foram gravados: uma falha aqui não pode marcar o sync como falho.
            try
            {
                await DefaultBankAccounts.AssignPendingAsync(db, userId, plan.Integration.Id, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger?.LogWarning("Conta padrão automática não definida: {Error}", ex.GetType().Name);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
        if (inbox is not null)
            foreach (var account in plan.Accounts.Where(a => a.Balance?.HasReservedBalance == true))
                await inbox.EnqueueAsync(new(PolpInternalEvents.ReservedBalances, "accounts", account.Id.ToString(), null, PolpInboxPriority.Low), ct);
    }

    private async Task<bool> ApplyAsync(
        Plan plan, Dictionary<string, Guid> categoryIds, string bankName, string? timeZoneId, string? documentType,
        CancellationToken ct)
    {
        var current = await db.PolpIntegrations.AsNoTracking().FirstOrDefaultAsync(p => p.Id == plan.Integration.Id, ct);
        if (current is null || current.Status != AuthorisedStatus || current.UserId != plan.Integration.UserId
            || current.InstitutionId != plan.Integration.InstitutionId || current.PolpIntegrationId != plan.Integration.PolpIntegrationId) return false;
        if (plan.Backfill && (current.SyncedLocally || current.Status != AuthorisedStatus)) return false;

        var userId = current.UserId;
        var now = time.GetUtcNow();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(UserClock.Normalize(timeZoneId));
        var today = UserClock.TodayIn(timeZoneId, now);

        var remoteIds = plan.Accounts.Select(a => a.Id)
            .Concat(plan.Cards.Select(c => c.Id))
            .Concat(plan.Transactions.Keys)
            .Concat(plan.Bills.Keys)
            .Distinct()
            .ToList();
        var byRemote = (await db.BankAccounts
                .Where(b => b.UserId == userId && b.PolpIntegrationId == current.PolpIntegrationId && b.PolpAccountId != null && remoteIds.Contains(b.PolpAccountId.Value))
                .OrderBy(b => b.CreatedAt)
                .ToListAsync(ct))
            .GroupBy(b => b.PolpAccountId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var remote in plan.Accounts)
        {
            if (!byRemote.TryGetValue(remote.Id, out var account))
            {
                account = NewAccount(current, remote.Id, $"{bankName} · {AccountKindLabel(remote.Type)}", documentType);
                byRemote[remote.Id] = account;
            }
            else if (bankName != UnresolvedBankName && IsGeneratedAccountName(account.Name, bankName))
                account.Name = $"{bankName} · {AccountKindLabel(remote.Type)}";

            // Saldo null = ainda não sincronizado na Polp: mantém o que já temos em vez de zerar.
            await BankBalanceHistory.ApplyAsync(db, account, remote, documentType, config, now, ct);
            if (remote.Balance?.HasReservedBalance is { } hasReserved) account.HasReservedBalance = hasReserved;
            account.PolpAccountType = remote.Type;
            account.PolpAccountSubtype = remote.Subtype;
            account.Number = AccountNumberMask.Mask($"{remote.Number}{remote.CheckDigit}");
        }

        foreach (var remote in plan.Cards)
        {
            if (!byRemote.TryGetValue(remote.Id, out var card))
            {
                card = NewAccount(current, remote.Id, remote.Name ?? $"{bankName} · Cartão de crédito", documentType);
                byRemote[remote.Id] = card;
            }

            // A v2 não separa conta de cartão por "type": o cartão é um recurso próprio, então
            // o tipo é fixado aqui para o resto do app (CreditCardAccountDetector) reconhecê-lo.
            card.PolpAccountType = CreditCardAccountDetector.CardType;
            // Provider identity must follow the current response, never the editable nickname.
            // Missing/blank identity clears stale evidence rather than retaining an old model.
            card.PolpOriginalName = string.IsNullOrWhiteSpace(remote.Name) ? null : remote.Name;
            card.ProductType = remote.ProductType;
            card.Brand = remote.CreditCardNetwork;
            card.Number = CardNumber(remote, plan.Transactions.GetValueOrDefault(remote.Id), card.Number);

            ApplyLimit(card, remote.Limits);
        }

        // Identity-only enrichment of existing cards: no creation, reconnection or financial
        // fields. Apply under the same integration lock/transaction and disconnect guard.
        if (plan.CardIdentities.Count > 0)
        {
            var identityIds = plan.CardIdentities.Keys.ToList();
            var identityTargets = await db.BankAccounts
                .Where(b => b.UserId == userId && b.IsActive
                    && b.PolpIntegrationId == current.PolpIntegrationId
                    && b.PolpOriginalName == null && b.PolpAccountId != null
                    && identityIds.Contains(b.PolpAccountId.Value))
                .ToListAsync(ct);
            foreach (var target in identityTargets)
            {
                var identity = plan.CardIdentities[target.PolpAccountId!.Value];
                target.PolpOriginalName = string.IsNullOrWhiteSpace(identity.Name) ? null : identity.Name;
                target.Brand = identity.Network;
                target.ProductType = identity.ProductType;
            }
        }

        var hadBills = new Dictionary<Guid, bool>();
        foreach (var (remoteId, bills) in plan.Bills)
        {
            if (!byRemote.TryGetValue(remoteId, out var card)) continue;
            hadBills[remoteId] = card.ClosingDay.HasValue;
            ApplyBillDays(card, bills);
        }

        foreach (var (remoteId, recurrings) in plan.Recurrings)
        {
            if (!byRemote.TryGetValue(remoteId, out var card)) continue;
            card.PolpRecurringsJson = recurrings is null ? null : JsonSerializer.Serialize(recurrings);
            card.PolpRecurringsSyncedAt = now.UtcDateTime;
        }

        await UpsertTransactionsAsync(plan, byRemote, categoryIds, zone, ct);
        await db.SaveChangesAsync(ct);
        await RemovedPolpResources.ClearAsync(db, byRemote.Keys, ct);

        foreach (var (remoteId, bills) in plan.Bills)
        {
            if (!byRemote.TryGetValue(remoteId, out var card)) continue;
            await ApplyCurrentInvoiceAsync(card, bills, today, hadBills[remoteId], ct);
        }
        await db.SaveChangesAsync(ct);

        var syncedAt = now.UtcDateTime;
        var synced = current.SyncedLocally || plan.Backfill;
        var detailsVersion = plan.FullTransactionHistory ? TransactionDetailsVersion : current.TransactionDetailsVersion;
        var updated = await db.PolpIntegrations
            .Where(p => p.Id == current.Id && p.Status != PolpDisconnectService.PendingStatus)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.SyncedLocally, synced)
                .SetProperty(p => p.TransactionDetailsVersion, detailsVersion)
                .SetProperty(p => p.LastSyncedAt, syncedAt)
                .SetProperty(p => p.UpdatedAt, syncedAt), ct);
        if (updated == 0) throw new IntegrationGoneException();

        return true;
    }

    private BankAccount NewAccount(PolpIntegration local, Guid remoteId, string name, string? documentType)
    {
        var account = new BankAccount
        {
            UserId = local.UserId,
            // O nome não é tocado nas atualizações seguintes: o usuário pode ter renomeado.
            Name = name,
            BankCode = local.InstitutionId.ToString(),
            PolpAccountId = remoteId,
            PolpIntegrationId = local.PolpIntegrationId,
            // Chute inicial do Radar de Recorrências (CNPJ = Business, CPF = Personal).
            Ownership = AccountOwnershipDefault.FromDocumentType(documentType)
        };
        db.BankAccounts.Add(account);
        return account;
    }

    // A doc não diz qual plástico é o do titular. Com o histórico completo, o mais usado nas
    // transações; senão mantém o que já foi gravado, para um aviso parcial (ex.: uma compra do
    // adicional) não trocar o número exibido.
    private static string? CardNumber(PolpCreditCardDto remote, TransactionBatch? batch, string? current)
    {
        if (batch is { Complete: true })
        {
            var mostUsed = batch.Items
                .Where(t => !string.IsNullOrWhiteSpace(t.IdentificationNumber))
                .GroupBy(t => t.IdentificationNumber!)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Key)
                .FirstOrDefault();
            if (mostUsed is not null) return AccountNumberMask.Mask(mostUsed);
        }

        return current ?? AccountNumberMask.Mask(remote.PaymentMethods.FirstOrDefault()?.IdentificationNumber);
    }

    // Os três valores vêm sempre da mesma linha: misturar com os antigos gera usado > limite que o banco nunca informou.
    private static void ApplyLimit(BankAccount card, List<PolpCardLimitDto> limits)
    {
        var limit = limits.FirstOrDefault(l => l.CreditLineLimitType == "LIMITE_CREDITO_TOTAL")
            ?? (limits.Count == 1 ? limits[0] : null);
        if (limit is null)
        {
            if (limits.Count > 1) card.CreditLimit = card.UsedLimit = card.AvailableLimit = null;
            return;
        }
        if (limit.LimitAmount?.Value is null && limit.UsedAmount?.Value is null && limit.AvailableAmount?.Value is null) return;
        card.CreditLimit = limit.LimitAmount?.Value;
        card.UsedLimit = limit.UsedAmount?.Value;
        card.AvailableLimit = limit.AvailableAmount?.Value;
    }

    private static void ApplyBillDays(BankAccount card, List<PolpBillDto> bills)
    {
        var last = ClosedBills(bills).OrderByDescending(b => b.Closing).FirstOrDefault();
        if (last is null) return;
        card.ClosingDay = last.Closing.Day;
        card.DueDay = last.Due.Day;
    }

    // Fatura atual. /bills só traz faturas fechadas. Fatura paga nunca vira "atual" nem "atrasada".
    // A doc liga cada payments[] à "fatura que fechar em seguida", mas no sandbox o pagamento do
    // ciclo vem na própria fatura que ele quita (data = vencimento, valor = total). Por isso conta
    // qualquer payments[] datado depois do fechamento, em qualquer fatura. PAGAMENTO_FATURA nas
    // transações é a outra fonte; usa a maior das duas porque o mesmo pagamento pode estar nas duas.
    private async Task ApplyCurrentInvoiceAsync(BankAccount card, List<PolpBillDto> bills, DateTime today, bool hadBills, CancellationToken ct)
    {
        var closed = ClosedBills(bills).ToList();
        var transactions = (await db.Transactions
                .Where(t => t.BankAccountId == card.Id && t.Description != null)
                .ToListAsync(ct))
            .Where(t => IsPolpId(t.Description))
            .ToList();

        foreach (var bill in closed.Where(b => b.Due >= today && b.Total.HasValue).OrderBy(b => b.Due))
        {
            if (PaidAfter(bill.Closing, closed, transactions) >= bill.Total!.Value) continue;

            card.CurrentInvoiceAmount = bill.Total.Value;
            card.InvoiceDueDate = bill.Due;
            return;
        }

        var lastClosed = closed.OrderByDescending(b => b.Closing).FirstOrDefault();

        // Depois do próximo fechamento o saldo já passou pra fatura seguinte.
        if (lastClosed is { Total: not null } && lastClosed.Due < today && today <= lastClosed.Closing.AddMonths(1))
        {
            if (PaidAfter(lastClosed.Closing, closed, transactions) < (lastClosed.Minimum ?? lastClosed.Total.Value))
            {
                card.CurrentInvoiceAmount = lastClosed.Total.Value;
                card.InvoiceDueDate = lastClosed.Due;
                return;
            }
        }

        // O cartão já teve fatura e agora a lista veio sem nenhuma: resposta incoerente; somar o
        // histórico inteiro como "fatura aberta" seria um valor errado.
        if (lastClosed is null && hadBills) return;

        card.CurrentInvoiceAmount = transactions
            .Where(t => t.Status != TransactionStatus.Pending
                && t.PolpMovementKind != PolpMovementKind.CardBillPayment
                && (lastClosed is null || t.DueDate > lastClosed.Closing))
            .Sum(t => t.Type == TransactionType.Expense ? t.Amount : -t.Amount);
        card.InvoiceDueDate = lastClosed is null ? null : NextDueOnOrAfter(lastClosed.Due, today);
    }

    private static decimal PaidAfter(DateTime closing, List<ClosedBill> bills, List<Transaction> transactions)
    {
        var fromTransactions = transactions
            .Where(t => t.PolpMovementKind == PolpMovementKind.CardBillPayment && t.DueDate > closing)
            .Sum(t => t.Type == TransactionType.Income ? t.Amount : -t.Amount);
        var fromBills = bills.SelectMany(b => b.Payments).Where(p => p.Date > closing).Sum(p => p.Amount);
        return Math.Max(fromTransactions, fromBills);
    }

    private static DateTime NextDueOnOrAfter(DateTime lastDue, DateTime today)
    {
        for (var months = 1; ; months++)
        {
            var month = lastDue.AddMonths(months);
            var day = Math.Min(lastDue.Day, DateTime.DaysInMonth(month.Year, month.Month));
            var due = new DateTime(month.Year, month.Month, day, 0, 0, 0, DateTimeKind.Utc);
            if (due >= today) return due;
        }
    }

    private sealed record BillPayment(DateTime Date, decimal Amount);

    private sealed record ClosedBill(DateTime Due, DateTime Closing, decimal? Total, decimal? Minimum, List<BillPayment> Payments);

    // Datas de fatura são do calendário do banco (Brasília), não do fuso do usuário.
    private static IEnumerable<ClosedBill> ClosedBills(List<PolpBillDto> bills) =>
        bills
            .Select(b => (due: ToCivilDate(b.DueDate, Brasilia), closing: ToCivilDate(b.BillClosingDate, Brasilia), total: b.BillTotalAmount?.Value, minimum: b.BillMinimumAmount?.Value, payments: BillPayments(b)))
            .Where(b => b.due.HasValue && b.closing.HasValue)
            .Select(b => new ClosedBill(b.due!.Value, b.closing!.Value, b.total, b.minimum, b.payments));

    private static List<BillPayment> BillPayments(PolpBillDto bill) =>
        bill.Payments
            .Select(p => (date: ToCivilDate(p.PaymentDate, Brasilia),
                amount: decimal.TryParse(p.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null))
            .Where(p => p.date.HasValue && p.amount is > 0)
            .Select(p => new BillPayment(p.date!.Value, p.amount!.Value))
            .ToList();

    // partie_cnpj_cpf vem do próprio banco e prevalece sobre o CNPJ do enriquecimento (que pode ser de
    // matriz/filial ou de outra origem): é ele que identifica pagamentos. CPF só como hash. Documento
    // inválido é ignorado.
    private void ApplyCounterpartyDocument(Transaction t, RemoteTransaction rt, Guid userId, bool hashCpf)
    {
        var digits = PayeeKeys.Digits(rt.CounterpartyDocument);
        if (digits.Length == 14 && DocumentValidator.IsValidCnpj(digits))
        {
            t.CounterpartyCnpj = digits;
            t.CounterpartyCnpjFromBank = true;
            t.CounterpartyCpfHash = null;
        }
        else if (digits.Length == 11 && hashCpf && DocumentValidator.IsValidCpf(digits))
            t.CounterpartyCpfHash = encryption!.ComputeLookupHash(EmployeeKeys.CpfLookupInput(userId, digits));

        // Conta completa só como hash: identifica pagamento a funcionário sem documento na linha (ver SupplierPaymentMatcher).
        if (hashCpf && PayeeKeys.AccountLookupInput(userId, rt.CounterpartyBankCode, rt.CounterpartyBranch,
                rt.CounterpartyNumber is null || rt.CounterpartyCheckDigit is null ? null : rt.CounterpartyNumber + rt.CounterpartyCheckDigit) is { } account)
            t.CounterpartyAccountHash = encryption!.ComputeLookupHash(account);
    }

    private async Task UpsertTransactionsAsync(
        Plan plan, Dictionary<Guid, BankAccount> byRemote, Dictionary<string, Guid> categoryIds, TimeZoneInfo zone, CancellationToken ct)
    {
        var targets = plan.Transactions
            .Where(kv => byRemote.ContainsKey(kv.Key))
            .Select(kv => (account: byRemote[kv.Key], batch: kv.Value))
            .ToList();
        if (targets.Count == 0) return;

        var accountIds = targets.Select(t => t.account.Id).ToList();
        var userId = plan.Integration.UserId;

        // Só linhas cujo Description é id da Polp: lançamentos manuais na mesma conta ficam de
        // fora. GroupBy porque nada impede duas linhas com a mesma chave; atualiza a mais antiga.
        var existingByKey = (await db.Transactions
                .Where(t => t.UserId == userId && t.BankAccountId != null && accountIds.Contains(t.BankAccountId.Value) && t.Description != null)
                .ToListAsync(ct))
            .Where(t => IsPolpId(t.Description))
            .GroupBy(t => (t.BankAccountId!.Value, t.Description!))
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.CreatedAt).First());

        // Prazo máximo de histórico (opt-in, DataRetention:SyncedHistoryMonths): o que é mais
        // antigo nem entra no banco (minimização na origem).
        var historyCutoff = DataRetentionPolicy.SyncedHistoryCutoff(config, time.GetUtcNow().UtcDateTime);
        Guid? fallbackCategoryId = categoryIds.TryGetValue(UncategorizedFallbackName, out var fid) ? fid : null;
        // CPF da contraparte só vira hash (HMAC por empresa, o mesmo de Employee.CpfHash) e só para
        // login CNPJ, que precisa dele para reconhecer pagamento a funcionário. Nunca é guardado em claro.
        var hashCounterpartyCpf = encryption is not null
            && await db.Users.AnyAsync(u => u.Id == userId && u.DocumentType == DocumentTypes.Cnpj, ct);

        foreach (var (account, batch) in targets)
        {
            foreach (var rt in batch.Items)
            {
                var polpTransactionId = rt.Id.ToString();
                existingByKey.TryGetValue((account.Id, polpTransactionId), out var existing);

                var dueDate = ResolveDueDate(rt, account, existing, zone);
                if (historyCutoff.HasValue && dueDate < historyCutoff.Value && !rt.IsFuture)
                    continue;

                var paidAt = rt.IsFuture ? (DateTime?)null : dueDate;
                var status = rt.IsFuture ? TransactionStatus.Pending : TransactionStatus.Paid;
                var categoryName = rt.CategoryName;
                Guid? categoryId = categoryIds.TryGetValue(categoryName, out var cid) ? cid : fallbackCategoryId;

                if (existing is not null)
                {
                    // A Polp corrige valor/status depois (pré-autorização confirmada com outro
                    // valor, parcela futura que entra na fatura).
                    if (rt.Amount is { } amount) existing.Amount = amount;
                    if (rt.IsCredit is { } credit) existing.Type = credit ? TransactionType.Income : TransactionType.Expense;
                    existing.Status = status;
                    existing.DueDate = dueDate;
                    existing.PaidAt = paidAt;
                    existing.PolpMovementKind = rt.MovementKind(existing.PolpMovementKind);
                    existing.PolpTaxPayment = rt.TaxPayment(existing.PolpTaxPayment);
                    rt.ApplyBankDetails(existing);
                    ApplyCounterpartyDocument(existing, rt, userId, hashCounterpartyCpf);
                    existing.TaxAuthority = TaxPaymentDetection.AuthorityFor(existing);

                    // Enriquecimento tardio: só completa o que ainda é automático; renomeação ou
                    // recategorização feita no app nunca é sobrescrita.
                    if (rt.EnrichedName is not null && existing.Name == rt.RawName)
                        existing.Name = rt.EnrichedName;
                    if (categoryName != UncategorizedFallbackName && categoryIds.ContainsKey(categoryName)
                        && (existing.CategoryId is null || existing.CategoryId == fallbackCategoryId))
                        existing.CategoryId = categoryId;
                    continue;
                }

                if (rt.Amount is not { } newAmount || rt.IsCredit is not { } newCredit)
                {
                    logger?.LogWarning("Transação da Polp sem valor ou sem tipo: não importada até vir completa.");
                    continue;
                }

                var transaction = new Transaction
                {
                    UserId = account.UserId,
                    BankAccountId = account.Id,
                    CategoryId = categoryId,
                    Name = rt.EnrichedName ?? rt.RawName,
                    Description = polpTransactionId, // id da Polp: chave do upsert
                    Type = newCredit ? TransactionType.Income : TransactionType.Expense,
                    Status = status,
                    Amount = newAmount,
                    DueDate = dueDate,
                    PolpMovementKind = rt.MovementKind(PolpMovementKind.None),
                    PolpTaxPayment = rt.TaxPayment(false),
                    PaidAt = paidAt
                };
                rt.ApplyBankDetails(transaction);
                ApplyCounterpartyDocument(transaction, rt, userId, hashCounterpartyCpf);
                transaction.TaxAuthority = TaxPaymentDetection.AuthorityFor(transaction);
                db.Transactions.Add(transaction);
                existingByKey[(account.Id, polpTransactionId)] = transaction;
            }
        }
    }

    // Dia civil do usuário à meia-noite UTC, como o lançamento manual. Nunca "agora": uma data
    // inventada mudaria a cada sync e jogaria o lançamento no mês errado.
    private static DateTime ResolveDueDate(RemoteTransaction rt, BankAccount account, Transaction? existing, TimeZoneInfo zone)
    {
        if (rt.IsFuture && ParseYearMonth(rt.BillForecastDate) is { } forecast)
        {
            var day = Math.Min(account.DueDay ?? 1, DateTime.DaysInMonth(forecast.Year, forecast.Month));
            return new DateTime(forecast.Year, forecast.Month, day, 0, 0, 0, DateTimeKind.Utc);
        }

        if (ToCivilDate(rt.DateTime, zone) is { } date) return date;
        if (ToCivilDate(rt.BillPostDate, zone) is { } posted) return posted;
        if (existing is not null) return existing.DueDate;

        throw new PolpPermanentException($"Transação {rt.Id} da Polp sem data válida.");
    }

    /// <summary>Texto com Z/offset é convertido para o fuso dado; sem offset é horário de
    /// Brasília (como a Polp interpreta); só a data ("AAAA-MM-DD") é usada como está.</summary>
    internal static DateTime? ToCivilDate(string? text, TimeZoneInfo zone)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
            return dateOnly.Year < 1900 ? null : DateTime.SpecifyKind(dateOnly, DateTimeKind.Utc);

        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            || parsed.Year < 1900)
            return null;

        var utc = parsed.Kind == DateTimeKind.Unspecified
            ? TimeZoneInfo.ConvertTimeToUtc(parsed, Brasilia)
            : parsed.ToUniversalTime();
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, zone).Date, DateTimeKind.Utc);
    }

    private static (int Year, int Month)? ParseYearMonth(string? text) =>
        DateTime.TryParseExact(text, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.Year >= 1900
            ? (d.Year, d.Month)
            : null;

    private static bool IsPolpId(string? description) => Guid.TryParse(description, out _);

    private sealed class Plan(PolpIntegration integration)
    {
        public PolpIntegration Integration { get; } = integration;
        public Dictionary<Guid, List<PolpCardRecurringDto>?> Recurrings { get; } = [];
        public bool Backfill { get; init; }
        public bool FullTransactionHistory { get; init; }
        public List<PolpAccountDto> Accounts { get; } = [];
        public List<PolpCreditCardDto> Cards { get; } = [];
        public Dictionary<Guid, CardIdentity> CardIdentities { get; } = [];
        public Dictionary<Guid, List<PolpBillDto>> Bills { get; } = [];
        public Dictionary<Guid, TransactionBatch> Transactions { get; } = [];
    }

    private sealed record CardIdentity(string? Name, string? Network, string? ProductType);

    // Complete = histórico inteiro, sem janela.
    private sealed record TransactionBatch(List<RemoteTransaction> Items, bool Complete);

    private sealed class IntegrationGoneException : Exception;

    // Visão comum de transação de conta e de cartão, só com o que o upsert precisa.
    private sealed record RemoteTransaction(
        Guid Id, string RawName, string? EnrichedName, decimal? Amount, bool? IsCredit, bool IsFuture,
        string? DateTime, string? CategoryRef, bool IsCardBillPayment,
        string? BillPostDate = null, string? BillForecastDate = null, string? IdentificationNumber = null)
    {
        public string? BankName { get; init; }
        public string? OperationType { get; init; }
        public string? CounterpartyBankCode { get; init; }
        public string? CounterpartyNumber { get; init; }
        public string? CounterpartyPersonType { get; init; }
        public string? CounterpartyDocument { get; init; }
        public string? CounterpartyBranch { get; init; }
        public string? CounterpartyCheckDigit { get; init; }
        public int? ChargeIdentificator { get; init; }
        public int? ChargeNumber { get; init; }
        public PolpMoney? OriginalAmount { get; init; }
        public bool IsCard { get; init; }
        public PolpCounterpartyDto? Counterparty { get; init; }

        public void ApplyBankDetails(Transaction t)
        {
            t.BankDateTime = ToBankInstant(DateTime);
            t.BankOperationType = SafeCode(OperationType, 40);
            t.BankDescription = string.IsNullOrWhiteSpace(BankName) ? null : BankName.Trim();
            // Enriquecimento chega depois e pode faltar num webhook seguinte: não apaga o que já veio.
            if (Counterparty is { } cp && (!string.IsNullOrWhiteSpace(cp.Name) || !string.IsNullOrWhiteSpace(cp.Alias)))
            {
                t.CounterpartyName = string.IsNullOrWhiteSpace(cp.Name) ? null : cp.Name.Trim();
                t.CounterpartyAlias = string.IsNullOrWhiteSpace(cp.Alias) ? null : cp.Alias.Trim();
                // CNPJ que veio do banco é prova e o enriquecimento nunca o troca nem o rebaixa: uma releitura sem
                // partie_cnpj_cpf faria o pagamento de imposto sumir. Só CNPJ (cadastro público de empresa); um CPF
                // de terceiro nunca é guardado.
                if (!t.CounterpartyCnpjFromBank)
                    t.CounterpartyCnpj = cp.TaxId is { Length: 14 } taxId && taxId.All(char.IsAsciiDigit) ? taxId : null;
            }
            t.CounterpartyPersonType = CounterpartyPersonType is "PESSOA_NATURAL" or "PESSOA_JURIDICA" ? CounterpartyPersonType : null;
            t.CounterpartyBankCode = CounterpartyBankCode is { Length: > 0 and <= 8 } code && code.All(char.IsAsciiDigit) ? code : null;
            t.CounterpartyAccount = AccountNumberMask.Mask(CounterpartyNumber);
            t.CardNumber = IsCard ? AccountNumberMask.Mask(IdentificationNumber) : null;

            var validInstallment = ChargeNumber is > 1 && ChargeIdentificator >= 1 && ChargeIdentificator <= ChargeNumber;
            t.InstallmentNumber = validInstallment ? ChargeIdentificator : null;
            t.InstallmentTotal = validInstallment ? ChargeNumber : null;

            var currency = SafeCode(OriginalAmount?.Currency, 3);
            var foreign = IsCard && currency is { Length: 3 } && currency != "BRL" && OriginalAmount?.Value is not null;
            t.OriginalAmount = foreign ? Math.Abs(OriginalAmount!.Value!.Value) : null;
            t.OriginalCurrency = foreign ? currency : null;

            t.BillMonth = IsCard && ParseYearMonth(BillForecastDate) is { } m ? $"{m.Year:D4}-{m.Month:D2}" : null;
        }

        public string CategoryName => IsCardBillPayment
            ? TransferDetection.CardBillPaymentCategoryName
            : PolpCategoryMap.ToCategoryName(CategoryRef);

        // PAGAMENTO_FATURA é sinal do próprio lançamento, não do enriquecimento: nunca rebaixado.
        // Sem category_ref ainda, mantém a marcação existente.
        public PolpMovementKind MovementKind(PolpMovementKind existing) =>
            IsCardBillPayment ? PolpMovementKind.CardBillPayment
            : CategoryRef is null ? existing
            : TransferDetection.ToMovementKind(CategoryRef);

        public bool TaxPayment(bool existing) =>
            CategoryRef is null ? existing : TaxPaymentDetection.IsTaxPaymentClassification(CategoryRef);

        // Campo ausente fica nulo, nunca 0 nem "débito": um valor ou tipo inventado sobrescreveria o que o banco
        // já tinha informado.
        private static decimal? AbsOrNull(decimal? value) => value is { } v ? Math.Abs(v) : null;
        private static bool? CreditFlag(string? type) =>
            string.Equals(type, "CREDITO", StringComparison.OrdinalIgnoreCase) ? true
            : string.Equals(type, "DEBITO", StringComparison.OrdinalIgnoreCase) ? false
            : null;

        public static RemoteTransaction From(PolpAccountTransactionDto t) => new(
            t.Id,
            t.TransactionName ?? "Transação",
            t.Counterparty?.Alias ?? t.Counterparty?.Name,
            AbsOrNull(t.TransactionAmount?.Value),
            CreditFlag(t.CreditDebitType),
            false,
            t.TransactionDateTime,
            t.CategoryRef,
            false)
        {
            BankName = t.TransactionName,
            OperationType = t.Type,
            Counterparty = t.Counterparty,
            CounterpartyBankCode = t.PartieCompeCode?.Trim(),
            CounterpartyNumber = t.PartieNumber,
            CounterpartyPersonType = t.PartiePersonType,
            CounterpartyDocument = t.PartieCnpjCpf,
            CounterpartyBranch = t.PartieBranchCode,
            CounterpartyCheckDigit = t.PartieCheckDigit,
        };

        public static RemoteTransaction From(PolpCardTransactionDto t) => new(
            t.Id,
            t.TransactionName ?? "Transação",
            t.Counterparty?.Alias ?? t.Counterparty?.Name,
            AbsOrNull(t.BrazilianAmount?.Value),
            CreditFlag(t.CreditDebitType),
            // Sem fatura e com a data-sentinela: lançamento futuro (parcela ou agendado).
            t.BillId is null && (t.BillPostDate?.StartsWith(FutureSentinel, StringComparison.Ordinal) ?? true),
            t.TransactionDateTime,
            t.CategoryRef,
            string.Equals(t.TransactionType, CardBillPaymentType, StringComparison.OrdinalIgnoreCase),
            t.BillPostDate,
            t.BillForecastDate,
            t.IdentificationNumber)
        {
            BankName = t.TransactionName,
            OperationType = t.TransactionType,
            Counterparty = t.Counterparty,
            ChargeIdentificator = t.ChargeIdentificator,
            ChargeNumber = t.ChargeNumber,
            OriginalAmount = t.OriginalAmount,
            IsCard = true,
        };
    }

    private static string? SafeCode(string? value, int maxLength)
    {
        var code = value?.Trim().ToUpperInvariant();
        return code is { Length: > 0 } && code.Length <= maxLength && code.All(c => char.IsAsciiLetter(c) || c == '_')
            ? code
            : null;
    }

    // Só data, ou meia-noite exata no relógio do próprio texto, é tratada como "sem horário".
    internal static DateTime? ToBankInstant(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var tIndex = text.IndexOfAny(['T', ' ']);
        if (tIndex < 0) return null;

        var timePart = text[(tIndex + 1)..];
        var hasOffset = timePart.Contains('Z') || timePart.Contains('z') || timePart.Contains('+') || timePart.Contains('-');

        if (hasOffset)
        {
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)
                || dto.Year < 1900 || dto.TimeOfDay == TimeSpan.Zero)
                return null;
            return dto.UtcDateTime;
        }

        if (!System.DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            || local.Year < 1900 || local.TimeOfDay == TimeSpan.Zero)
            return null;
        return System.DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeToUtc(System.DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Brasilia),
            DateTimeKind.Utc);
    }

    // Categorias são criadas antes do lock, cada uma salva isolada: no Postgres uma violação de
    // unicidade (corrida com outro sync) abortaria a transação do lock inteira. Só as que o upsert
    // vai usar, para não criar categorias vazias na lista do usuário.
    private async Task<Dictionary<string, Guid>> PrepareCategoriesAsync(Plan plan, CancellationToken ct)
    {
        var userId = plan.Integration.UserId;
        await EnsureDefaultCategoriesAsync(userId, ct);

        var categoriesByName = (await db.Categories
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync(ct))
            .GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => g.First());
        if (plan.Transactions.Count == 0) return categoriesByName.ToDictionary(kv => kv.Key, kv => kv.Value.Id);

        var remoteIds = plan.Transactions.Keys.ToList();
        var localIds = await db.BankAccounts.AsNoTracking()
            .Where(b => b.UserId == userId && b.PolpAccountId != null && remoteIds.Contains(b.PolpAccountId.Value))
            .Select(b => new { b.Id, Remote = b.PolpAccountId!.Value })
            .ToListAsync(ct);
        var accountIds = localIds.Select(a => a.Id).ToList();
        var existing = (await db.Transactions.AsNoTracking()
                .Where(t => t.UserId == userId && t.BankAccountId != null && accountIds.Contains(t.BankAccountId.Value) && t.Description != null)
                .Select(t => new { AccountId = t.BankAccountId!.Value, t.Description, t.CategoryId })
                .ToListAsync(ct))
            .GroupBy(t => (t.AccountId, t.Description!))
            .ToDictionary(g => g.Key, g => g.First().CategoryId);
        var fallbackId = categoriesByName.GetValueOrDefault(UncategorizedFallbackName)?.Id;

        var needed = new HashSet<string>();
        foreach (var (remoteId, batch) in plan.Transactions)
        {
            var localId = localIds.FirstOrDefault(a => a.Remote == remoteId)?.Id;
            foreach (var rt in batch.Items)
            {
                var name = rt.CategoryName;
                if (localId is not Guid id || !existing.TryGetValue((id, rt.Id.ToString()), out var categoryId))
                {
                    needed.Add(name);
                    needed.Add(UncategorizedFallbackName);
                }
                else if (name != UncategorizedFallbackName && (categoryId is null || categoryId == fallbackId))
                {
                    needed.Add(name);
                }
            }
        }

        foreach (var name in needed)
            await ResolveCategoryIdAsync(categoriesByName, name, null, userId, ct);
        await db.SaveChangesAsync(ct);

        return categoriesByName.ToDictionary(kv => kv.Key, kv => kv.Value.Id);
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

    private const string UnresolvedBankName = "Banco";
    private static readonly string[] AccountKindLabels = ["Conta corrente", "Poupança", "Conta de pagamento"];

    private static string AccountKindLabel(string type) => type switch
    {
        "CONTA_POUPANCA" => "Poupança",
        "CONTA_PAGAMENTO_PRE_PAGA" => "Conta de pagamento",
        _ => "Conta corrente"
    };

    private static bool IsGeneratedAccountName(string name, string bankName) =>
        AccountKindLabels.Any(k => name == $"{UnresolvedBankName} · {k}" || name == $"{bankName} · {k}");

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
    // transação. "Outros" é uma Category normal, então o usuário pode renomeá-la ou mover a
    // transação pra outra categoria como qualquer outra.
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
            // Salva na hora: com a constraint de unicidade (UserId, Name), a corrida com outro
            // sync criando essa mesma categoria só pode ser detectada e tratada aqui, isolada.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Perdeu a corrida: descarta a tentativa (senão ela fica presa no change tracker e
            // quebra o próximo save) e usa a que já existe.
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
