using System.Net;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlFinance.API.Tests.Support;

// IPolpService roteirizado: devolve o que o teste montou, registra cada chamada (com a janela) e
// permite falhar ou rodar um gancho no meio da fase de leitura.
public sealed class ScriptedPolp : IPolpService
{
    public PolpConsentDto Consent { get; set; } = new() { Status = "AUTHORISED", ExecutionStatus = "SUCCESS" };
    public List<PolpAccountDto> Accounts { get; } = [];
    public List<PolpCreditCardDto> Cards { get; } = [];
    public Dictionary<Guid, List<PolpBillDto>> Bills { get; } = [];
    public Dictionary<Guid, List<PolpAccountTransactionDto>> AccountTransactions { get; } = [];
    public Dictionary<Guid, List<PolpCardTransactionDto>> CardTransactions { get; } = [];
    public HashSet<Guid> Failing { get; } = [];
    public List<(string Op, Guid Id, string? Window)> Calls { get; } = [];
    public bool DeleteFails { get; set; } = true;

    /// <summary>Roda uma vez, na primeira leitura de transações ou do consentimento.</summary>
    public Func<Task>? OnRead { get; set; }

    private async Task Read(string op, Guid id, PolpQueryWindow? window)
    {
        Calls.Add((op, id, window?.Query));
        if (OnRead is { } hook)
        {
            OnRead = null;
            await hook();
        }
        if (Failing.Contains(id))
            throw new HttpRequestException("Falha simulada (HTTP 503).", null, HttpStatusCode.ServiceUnavailable);
    }

    public Task<List<PolpInstitutionDto>> GetInstitutionsAsync(CancellationToken ct = default) => Task.FromResult(new List<PolpInstitutionDto>());

    public Task<PolpConsentDto> CreateConsentAsync(CreatePolpConsentRequest request, CancellationToken ct = default) => throw new NotSupportedException();

    public async Task<PolpConsentDto> GetConsentAsync(Guid consentId, CancellationToken ct = default)
    {
        await Read("consent", consentId, null);
        return Consent;
    }

    public Task<List<PolpAccountDto>> GetAccountsAsync(Guid consentId, PolpQueryWindow? window = null, CancellationToken ct = default)
    {
        Calls.Add(("accounts", consentId, window?.Query));
        return Task.FromResult(Accounts.ToList());
    }

    public Task<List<PolpCreditCardDto>> GetCreditCardsAsync(Guid consentId, PolpQueryWindow? window = null, CancellationToken ct = default)
    {
        Calls.Add(("cards", consentId, window?.Query));
        return Task.FromResult(Cards.ToList());
    }

    public async Task<List<PolpBillDto>> GetBillsAsync(Guid creditCardId, PolpQueryWindow? window = null, CancellationToken ct = default)
    {
        await Read("bills", creditCardId, window);
        return Bills.GetValueOrDefault(creditCardId)?.ToList() ?? [];
    }

    public async Task<List<PolpAccountTransactionDto>> GetAccountTransactionsAsync(Guid accountId, PolpQueryWindow? window = null, CancellationToken ct = default)
    {
        await Read("account-transactions", accountId, window);
        return AccountTransactions.GetValueOrDefault(accountId)?.ToList() ?? [];
    }

    public async Task<List<PolpCardTransactionDto>> GetCardTransactionsAsync(Guid creditCardId, PolpQueryWindow? window = null, CancellationToken ct = default)
    {
        await Read("card-transactions", creditCardId, window);
        return CardTransactions.GetValueOrDefault(creditCardId)?.ToList() ?? [];
    }

    public Task<PolpAccountDto> SyncBalanceAsync(Guid accountId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task DeleteConsentAsync(Guid consentId, CancellationToken ct = default) =>
        DeleteFails ? throw new HttpRequestException("Falha simulada (HTTP 503).", null, HttpStatusCode.ServiceUnavailable) : Task.CompletedTask;

    public static PolpAccountDto Account(Guid? id = null, string balance = "100.00") => new()
    {
        Id = id ?? Guid.NewGuid(), Type = "CONTA_DEPOSITO_A_VISTA", Number = "123456", CheckDigit = "7",
        Balance = new PolpBalanceDto { AvailableAmount = Money(balance) }
    };

    public static PolpCreditCardDto Card(Guid? id = null, params string[] plastics) => new()
    {
        Id = id ?? Guid.NewGuid(), Name = "Cartão Teste", CreditCardNetwork = "VISA", ProductType = "GOLD",
        PaymentMethods = plastics.Select(p => new PolpCardPaymentMethodDto { IdentificationNumber = p }).ToList(),
        Limits = [new PolpCardLimitDto { CreditLineLimitType = "LIMITE_CREDITO_TOTAL", LimitAmount = Money("5000.00"), UsedAmount = Money("100.00") }]
    };

    public static PolpAccountTransactionDto AccountTx(string date, string amount, bool credit = false, string? categoryRef = null, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), TransactionName = "LANCAMENTO", TransactionDateTime = date,
        CreditDebitType = credit ? "CREDITO" : "DEBITO", TransactionAmount = Money(amount), CategoryRef = categoryRef
    };

    public static PolpCardTransactionDto CardTx(string date, string amount, bool credit = false, string? type = null,
        string? billPostDate = null, Guid? billId = null, string? forecast = null, string? plastic = null, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), TransactionName = "COMPRA", TransactionDateTime = date,
        CreditDebitType = credit ? "CREDITO" : "DEBITO", TransactionType = type ?? "PAGAMENTO",
        BrazilianAmount = Money(amount), BillPostDate = billPostDate ?? date[..10], BillId = billId,
        BillForecastDate = forecast, IdentificationNumber = plastic
    };

    public static PolpBillDto Bill(string closing, string due, string total) => new()
    {
        Id = Guid.NewGuid(), BillClosingDate = closing, DueDate = due, BillTotalAmount = Money(total)
    };

    public static PolpMoney Money(string amount) => new() { Amount = amount, Currency = "BRL" };
}

public sealed class RecordingInbox : IPolpInbox
{
    public List<PolpInboxEvent> Events { get; } = [];

    public Task<bool> EnqueueAsync(PolpInboxEvent evt, CancellationToken ct)
    {
        Events.Add(evt);
        return Task.FromResult(true);
    }
}

public static class SyncHarness
{
    public static PolpSyncService Sync(TestDb t, IPolpService polp, IIntegrationLock? integrationLock = null, IConfiguration? config = null) =>
        new(t.Db, polp, config ?? new ConfigurationBuilder().Build(),
            new InstitutionNameResolver(polp, new MemoryCache(new MemoryCacheOptions()), NullLogger<InstitutionNameResolver>.Instance),
            integrationLock ?? new IntegrationLock(), t.Clock);

    public static PolpWebhookProcessor Processor(TestDb t, IPolpService polp, IPolpInbox? inbox = null, IIntegrationLock? integrationLock = null,
        ILogger<PolpWebhookProcessor>? logger = null) =>
        new(t.Db, Sync(t, polp, integrationLock), polp, inbox ?? new RecordingInbox(), logger ?? NullLogger<PolpWebhookProcessor>.Instance);

    public static PolpIntegration AddIntegration(TestDb t, string status = "AUTHORISED", bool synced = false)
    {
        var integration = new PolpIntegration
        {
            UserId = t.User.Id, PolpIntegrationId = Guid.NewGuid(), InstitutionId = Guid.NewGuid(),
            InstitutionName = "Banco Teste", Status = status, SyncedLocally = synced
        };
        t.Db.PolpIntegrations.Add(integration);
        t.Db.SaveChanges();
        return integration;
    }

    public static BankAccount AddLinked(TestDb t, PolpIntegration integration, Guid remoteId, bool card)
    {
        var account = new BankAccount
        {
            UserId = t.User.Id, Name = card ? "Cartão" : "Conta", PolpAccountId = remoteId,
            PolpIntegrationId = integration.PolpIntegrationId,
            PolpAccountType = card ? CreditCardAccountDetector.CardType : "CONTA_DEPOSITO_A_VISTA"
        };
        t.Db.BankAccounts.Add(account);
        t.Db.SaveChanges();
        return account;
    }

    public static Task Process(this PolpWebhookProcessor processor, string evt, Guid resourceId, string? query = null) =>
        processor.ProcessAsync(new PolpInboxEvent(evt, null, resourceId.ToString(), query), default);
}
