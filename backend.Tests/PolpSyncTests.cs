using System.Net;
using System.Text;
using ControlFinance.API.Controllers;
using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlFinance.API.Tests;

public class PolpBackfillTests
{
    [Fact]
    public async Task Falha_em_uma_conta_sobe_e_nada_parcial_e_gravado()
    {
        using var t = new TestDb();
        var integration = SyncHarness.AddIntegration(t);
        var polp = new ScriptedPolp();
        var ok = ScriptedPolp.Account();
        var broken = ScriptedPolp.Account();
        polp.Accounts.AddRange([ok, broken]);
        polp.AccountTransactions[ok.Id] = [ScriptedPolp.AccountTx("2026-10-01T12:00:00.000Z", "10.00")];
        polp.Failing.Add(broken.Id);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            SyncHarness.Processor(t, polp).Process(PolpInternalEvents.Backfill, integration.PolpIntegrationId));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);

        await using var db = t.NewContext();
        var saved = db.PolpIntegrations.Single();
        Assert.False(saved.SyncedLocally);
        Assert.Null(saved.LastSyncedAt);
        Assert.Empty(db.BankAccounts);
        Assert.Empty(db.Transactions);
    }

    [Fact]
    public async Task Falha_nas_faturas_de_um_cartao_tambem_impede_o_backfill()
    {
        using var t = new TestDb();
        var integration = SyncHarness.AddIntegration(t);
        var polp = new ScriptedPolp();
        polp.Accounts.Add(ScriptedPolp.Account());
        var card = ScriptedPolp.Card(null, "1234");
        polp.Cards.Add(card);
        var failingBills = new ScriptedPolpBillsFailure(polp, card.Id);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            SyncHarness.Processor(t, failingBills).Process(PolpInternalEvents.Backfill, integration.PolpIntegrationId));

        await using var db = t.NewContext();
        Assert.False(db.PolpIntegrations.Single().SyncedLocally);
        Assert.Empty(db.BankAccounts);
    }

    [Fact]
    public async Task Tudo_ok_marca_sincronizado_e_grava_LastSyncedAt_na_mesma_operacao()
    {
        using var t = new TestDb();
        t.Clock.UtcNow = new DateTimeOffset(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);
        var integration = SyncHarness.AddIntegration(t);
        var polp = new ScriptedPolp();
        var account = ScriptedPolp.Account(balance: "321.00");
        polp.Accounts.Add(account);
        polp.AccountTransactions[account.Id] =
        [
            ScriptedPolp.AccountTx("2026-10-01T12:00:00.000Z", "10.00"),
            ScriptedPolp.AccountTx("2026-10-02T12:00:00.000Z", "20.00", credit: true)
        ];

        await SyncHarness.Processor(t, polp).Process(PolpInternalEvents.Backfill, integration.PolpIntegrationId);

        await using var db = t.NewContext();
        var saved = db.PolpIntegrations.Single();
        Assert.True(saved.SyncedLocally);
        Assert.Equal(t.Clock.UtcNow.Value.UtcDateTime, saved.LastSyncedAt);
        Assert.Equal(321m, db.BankAccounts.Single().Balance);
        Assert.Equal(2, db.Transactions.Count());

        // Já sincronizado: um segundo backfill não chama a Polp.
        polp.Calls.Clear();
        await SyncHarness.Processor(t, polp).Process(PolpInternalEvents.Backfill, integration.PolpIntegrationId);
        Assert.Empty(polp.Calls);
    }

    [Fact]
    public async Task Sem_contas_ainda_pede_nova_tentativa()
    {
        using var t = new TestDb();
        var integration = SyncHarness.AddIntegration(t);

        await Assert.ThrowsAsync<PolpResourceNotReadyException>(() =>
            SyncHarness.Processor(t, new ScriptedPolp()).Process(PolpInternalEvents.Backfill, integration.PolpIntegrationId));

        var nullPolp = new NullPolpService();
        await Assert.ThrowsAsync<PolpResourceNotReadyException>(() =>
            SyncHarness.Processor(t, nullPolp).Process(PolpInternalEvents.Backfill, integration.PolpIntegrationId));

        await using var db = t.NewContext();
        Assert.False(db.PolpIntegrations.Single().SyncedLocally);
    }

    [Fact]
    public async Task Numero_do_cartao_e_o_plastico_mais_usado_nas_transacoes()
    {
        using var t = new TestDb();
        var integration = SyncHarness.AddIntegration(t);
        var polp = new ScriptedPolp();
        var card = ScriptedPolp.Card(null, "5555000011112222", "4444000033339999");
        polp.Cards.Add(card);
        polp.CardTransactions[card.Id] =
        [
            ScriptedPolp.CardTx("2026-10-01T12:00:00.000Z", "1.00", plastic: "5555000011112222"),
            ScriptedPolp.CardTx("2026-10-02T12:00:00.000Z", "1.00", plastic: "4444000033339999"),
            ScriptedPolp.CardTx("2026-10-03T12:00:00.000Z", "1.00", plastic: "4444000033339999"),
            ScriptedPolp.CardTx("2026-10-04T12:00:00.000Z", "1.00", plastic: "4444000033339999"),
        ];

        await SyncHarness.Processor(t, polp).Process(PolpInternalEvents.Backfill, integration.PolpIntegrationId);

        await using var db = t.NewContext();
        Assert.Equal(AccountNumberMask.Mask("4444000033339999"), db.BankAccounts.Single().Number);
    }

    [Fact]
    public async Task Cartao_sem_transacoes_usa_o_primeiro_plastico_e_aviso_parcial_nao_troca_o_numero()
    {
        using var t = new TestDb();
        var integration = SyncHarness.AddIntegration(t, synced: true);
        var polp = new ScriptedPolp();
        var card = ScriptedPolp.Card(null, "5555000011112222", "4444000033339999");
        polp.Cards.Add(card);
        var processor = SyncHarness.Processor(t, polp);

        await processor.Process("credit_cards", integration.PolpIntegrationId);
        await using (var db = t.NewContext())
            Assert.Equal(AccountNumberMask.Mask("5555000011112222"), db.BankAccounts.Single().Number);

        polp.Cards[0].PaymentMethods.Reverse();
        await processor.Process("credit_cards", integration.PolpIntegrationId);
        await using (var db = t.NewContext())
            Assert.Equal(AccountNumberMask.Mask("5555000011112222"), db.BankAccounts.Single().Number);
    }

    private sealed class ScriptedPolpBillsFailure(ScriptedPolp inner, Guid cardId) : IPolpService
    {
        public Task<List<PolpBillDto>> GetBillsAsync(Guid creditCardId, PolpQueryWindow? window = null, CancellationToken ct = default) =>
            creditCardId == cardId
                ? throw new HttpRequestException("Falha simulada (HTTP 500).", null, HttpStatusCode.InternalServerError)
                : inner.GetBillsAsync(creditCardId, window, ct);

        public Task<List<PolpInstitutionDto>> GetInstitutionsAsync(CancellationToken ct = default) => inner.GetInstitutionsAsync(ct);
        public Task<PolpConsentDto> CreateConsentAsync(CreatePolpConsentRequest request, CancellationToken ct = default) => inner.CreateConsentAsync(request, ct);
        public Task<PolpConsentDto> GetConsentAsync(Guid consentId, CancellationToken ct = default) => inner.GetConsentAsync(consentId, ct);
        public Task<List<PolpAccountDto>> GetAccountsAsync(Guid consentId, PolpQueryWindow? window = null, CancellationToken ct = default) => inner.GetAccountsAsync(consentId, window, ct);
        public Task<List<PolpCreditCardDto>> GetCreditCardsAsync(Guid consentId, PolpQueryWindow? window = null, CancellationToken ct = default) => inner.GetCreditCardsAsync(consentId, window, ct);
        public Task<List<PolpAccountTransactionDto>> GetAccountTransactionsAsync(Guid accountId, PolpQueryWindow? window = null, CancellationToken ct = default) => inner.GetAccountTransactionsAsync(accountId, window, ct);
        public Task<List<PolpCardTransactionDto>> GetCardTransactionsAsync(Guid creditCardId, PolpQueryWindow? window = null, CancellationToken ct = default) => inner.GetCardTransactionsAsync(creditCardId, window, ct);
        public Task<PolpAccountDto> SyncBalanceAsync(Guid accountId, CancellationToken ct = default) => inner.SyncBalanceAsync(accountId, ct);
        public Task DeleteConsentAsync(Guid consentId, CancellationToken ct = default) => inner.DeleteConsentAsync(consentId, ct);
    }
}

public class PolpServicePaginationTests
{
    private sealed class EndlessCursor : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            var body = "{\"data\":[{\"id\":\"" + Guid.NewGuid() + "\"}],\"meta\":{\"next_cursor\":\"c" + Requests + "\"}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task Esgotar_o_teto_de_paginas_com_cursor_e_erro_e_nao_lista_truncada()
    {
        var handler = new EndlessCursor();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Polp:BaseUrl"] = "https://polp.invalid/api/v2/sandbox",
            ["Polp:ClientId"] = "id",
            ["Polp:ClientSecret"] = "secret",
        }).Build();
        var service = new PolpService(new HttpClient(handler), config, NullLogger<PolpService>.Instance);

        await Assert.ThrowsAsync<PolpPermanentException>(() => service.GetAccountTransactionsAsync(Guid.NewGuid()));
        Assert.Equal(PolpService.MaxPages, handler.Requests);
    }
}

public class PolpDisconnectRaceTests
{
    private static PolpDisconnectService Disconnect(AppDbContext db, IPolpService polp) =>
        new(db, polp, new IntegrationLock(), NullLogger<PolpDisconnectService>.Instance);

    [Fact]
    public async Task Integracao_pendente_nunca_volta_a_AUTHORISED()
    {
        using var t = new TestDb();
        var integration = SyncHarness.AddIntegration(t, status: PolpDisconnectService.PendingStatus);
        var polp = new ScriptedPolp();
        var inbox = new RecordingInbox();

        await SyncHarness.Processor(t, polp, inbox).Process("consents", integration.PolpIntegrationId);

        await using var db = t.NewContext();
        Assert.Equal(PolpDisconnectService.PendingStatus, db.PolpIntegrations.Single().Status);
        Assert.Empty(inbox.Events);
    }

    [Fact]
    public async Task Desconexao_durante_a_leitura_do_consentimento_vence_o_refresh()
    {
        using var t = new TestDb();
        var integration = SyncHarness.AddIntegration(t, status: "AWAITING_AUTHORIZATION");
        var polp = new ScriptedPolp();
        var inbox = new RecordingInbox();
        polp.OnRead = async () =>
        {
            await using var other = t.NewContext();
            var local = other.PolpIntegrations.Single();
            await Disconnect(other, polp).DisconnectAsync(local, default);
        };

        await SyncHarness.Processor(t, polp, inbox).Process("consents", integration.PolpIntegrationId);

        await using var db = t.NewContext();
        Assert.Equal(PolpDisconnectService.PendingStatus, db.PolpIntegrations.Single().Status);
        Assert.Empty(inbox.Events);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Sync_que_leu_antes_da_desconexao_nao_grava_nada_depois(bool providerAccepts)
    {
        using var t = new TestDb();
        var integration = SyncHarness.AddIntegration(t);
        var polp = new ScriptedPolp { DeleteFails = !providerAccepts };
        var account = ScriptedPolp.Account();
        polp.Accounts.Add(account);
        polp.AccountTransactions[account.Id] = [ScriptedPolp.AccountTx("2026-10-01T12:00:00.000Z", "10.00")];
        polp.OnRead = async () =>
        {
            await using var other = t.NewContext();
            await Disconnect(other, polp).DisconnectAsync(other.PolpIntegrations.Single(), default);
        };

        await SyncHarness.Processor(t, polp).Process(PolpInternalEvents.Backfill, integration.PolpIntegrationId);

        await using var db = t.NewContext();
        Assert.Empty(db.BankAccounts);
        Assert.Empty(db.Transactions);
        if (providerAccepts) Assert.Empty(db.PolpIntegrations);
        else Assert.Equal(PolpDisconnectService.PendingStatus, db.PolpIntegrations.Single().Status);
    }

    [Fact]
    public async Task Evento_de_cartoes_que_leu_antes_da_desconexao_nao_recria_cartao()
    {
        using var t = new TestDb();
        var integration = SyncHarness.AddIntegration(t, synced: true);
        var polp = new ScriptedPolp();
        var card = ScriptedPolp.Card(null, "1234");
        polp.Cards.Add(card);
        polp.CardTransactions[card.Id] = [ScriptedPolp.CardTx("2026-10-01T12:00:00.000Z", "10.00")];
        polp.OnRead = async () =>
        {
            await using var other = t.NewContext();
            await Disconnect(other, polp).DisconnectAsync(other.PolpIntegrations.Single(), default);
        };

        await SyncHarness.Processor(t, polp).Process("credit_cards", integration.PolpIntegrationId);

        await using var db = t.NewContext();
        Assert.Empty(db.BankAccounts);
        Assert.Empty(db.Transactions);
        var saved = db.PolpIntegrations.Single();
        Assert.Equal(PolpDisconnectService.PendingStatus, saved.Status);
        Assert.Null(saved.LastSyncedAt);
    }

    [Fact]
    public async Task Desconexao_apaga_os_dados_e_retry_pendente_tambem_passa_pelo_lock()
    {
        using var t = new TestDb();
        var integration = SyncHarness.AddIntegration(t, synced: true);
        var account = SyncHarness.AddLinked(t, integration, Guid.NewGuid(), card: false);
        t.AddTransaction(account, TransactionType.Expense, 10m, new DateTime(2026, 10, 1));
        var polp = new ScriptedPolp();
        var recording = new RecordingLock();

        await using (var db = t.NewContext())
        {
            var service = new PolpDisconnectService(db, polp, recording, NullLogger<PolpDisconnectService>.Instance);
            Assert.Equal(RemoteDeletionOutcome.Pending, await service.DisconnectAsync(db.PolpIntegrations.Single(), default));
            await service.RetryPendingAsync(default);
        }

        Assert.Equal([integration.Id, integration.Id], recording.Keys);
        await using var check = t.NewContext();
        Assert.Empty(check.BankAccounts);
        Assert.Empty(check.Transactions);
        Assert.Equal(PolpDisconnectService.PendingStatus, check.PolpIntegrations.Single().Status);
    }

    private sealed class RecordingLock : IIntegrationLock
    {
        private readonly IntegrationLock _inner = new();
        public List<Guid> Keys { get; } = [];

        public Task<T> RunAsync<T>(AppDbContext db, Guid localIntegrationId, Func<Task<T>> work, CancellationToken ct)
        {
            Keys.Add(localIntegrationId);
            return _inner.RunAsync(db, localIntegrationId, work, ct);
        }
    }
}

public class IntegrationLockTests
{
    [Fact]
    public async Task Mesma_integracao_serializa_e_integracoes_diferentes_nao()
    {
        using var a = new TestDb();
        using var b = new TestDb();
        var integrationLock = new IntegrationLock();
        var id = Guid.NewGuid();

        var release = new TaskCompletionSource();
        var firstEntered = new TaskCompletionSource();
        var secondEntered = new TaskCompletionSource();

        var first = integrationLock.RunAsync(a.Db, id, async () => { firstEntered.SetResult(); await release.Task; return 1; }, default);
        await firstEntered.Task;
        var second = integrationLock.RunAsync(b.Db, id, () => { secondEntered.SetResult(); return Task.FromResult(2); }, default);

        await Task.Delay(200);
        Assert.False(secondEntered.Task.IsCompleted);
        release.SetResult();
        Assert.Equal(1, await first);
        Assert.Equal(2, await second);

        var bothInside = new TaskCompletionSource();
        var otherEntered = new TaskCompletionSource();
        var held = integrationLock.RunAsync(a.Db, Guid.NewGuid(), async () => { await otherEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); bothInside.SetResult(); return 0; }, default);
        await integrationLock.RunAsync(b.Db, Guid.NewGuid(), () => { otherEntered.SetResult(); return Task.FromResult(0); }, default);
        await held;
        Assert.True(bothInside.Task.IsCompleted);
    }

    [Fact]
    public async Task Excecao_no_trabalho_desfaz_tudo_e_libera_o_lock()
    {
        using var t = new TestDb();
        var integrationLock = new IntegrationLock();
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() => integrationLock.RunAsync<int>(t.Db, id, async () =>
        {
            t.Db.BankAccounts.Add(new BankAccount { UserId = t.User.Id, Name = "Temporária" });
            await t.Db.SaveChangesAsync();
            throw new InvalidOperationException("falha");
        }, default));

        t.Db.ChangeTracker.Clear();
        Assert.Empty(t.Db.BankAccounts);
        Assert.Equal(7, await integrationLock.RunAsync(t.Db, id, () => Task.FromResult(7), default));
    }

    [Fact]
    public void Chave_do_advisory_lock_e_estavel()
    {
        var id = Guid.Parse("8c1f6a52-2d0e-4c55-9b0a-6f0e3c1d2b4a");
        Assert.Equal(IntegrationLock.ObjectId(id), IntegrationLock.ObjectId(Guid.Parse(id.ToString())));
    }
}

public class PolpEventRoutingTests
{
    [Theory]
    [InlineData("bills")]
    [InlineData("accounts.transactions")]
    [InlineData("credit_cards.transactions")]
    public async Task Conta_ou_cartao_desconhecido_pede_nova_tentativa(string evt)
    {
        using var t = new TestDb();
        SyncHarness.AddIntegration(t, synced: true);

        await Assert.ThrowsAsync<PolpResourceNotReadyException>(() =>
            SyncHarness.Processor(t, new ScriptedPolp()).Process(evt, Guid.NewGuid()));
    }

    [Theory]
    [InlineData("consents")]
    [InlineData("accounts")]
    [InlineData("credit_cards")]
    [InlineData(PolpInternalEvents.Backfill)]
    [InlineData(PolpInternalEvents.Reconcile)]
    public async Task Consentimento_desconhecido_pede_nova_tentativa(string evt)
    {
        using var t = new TestDb();

        await Assert.ThrowsAsync<PolpResourceNotReadyException>(() =>
            SyncHarness.Processor(t, new ScriptedPolp()).Process(evt, Guid.NewGuid()));
    }

    [Theory]
    [InlineData("bills")]
    [InlineData("credit_cards.transactions")]
    [InlineData("accounts")]
    [InlineData(PolpInternalEvents.Reconcile)]
    public async Task Integracao_pendente_termina_sem_chamar_a_Polp_nem_gravar(string evt)
    {
        using var t = new TestDb();
        var integration = SyncHarness.AddIntegration(t, status: PolpDisconnectService.PendingStatus, synced: true);
        var cardId = Guid.NewGuid();
        SyncHarness.AddLinked(t, integration, cardId, card: true);
        var polp = new ScriptedPolp();
        polp.Cards.Add(ScriptedPolp.Card(cardId));

        var resource = evt is "bills" or "credit_cards.transactions" ? cardId : integration.PolpIntegrationId;
        await SyncHarness.Processor(t, polp).Process(evt, resource);

        Assert.Empty(polp.Calls);
        await using var db = t.NewContext();
        Assert.Null(db.PolpIntegrations.Single().LastSyncedAt);
    }

    [Theory]
    [InlineData("loans")]
    [InlineData("bank_fixed_incomes.transactions")]
    [InlineData("evento_que_nao_existe")]
    public async Task Fora_do_escopo_termina_sem_chamada(string evt)
    {
        using var t = new TestDb();
        var polp = new ScriptedPolp();

        await SyncHarness.Processor(t, polp).Process(evt, Guid.NewGuid());

        Assert.Empty(polp.Calls);
    }

    [Fact]
    public async Task resource_id_invalido_e_permanente()
    {
        using var t = new TestDb();
        await Assert.ThrowsAsync<PolpPermanentException>(() =>
            SyncHarness.Processor(t, new ScriptedPolp()).ProcessAsync(new PolpInboxEvent("bills", "credit-cards", "nao-e-guid", null), default));
    }

    [Fact]
    public async Task Cartao_novo_num_evento_credit_cards_traz_faturas_e_transacoes_completas()
    {
        using var t = new TestDb();
        t.Clock.UtcNow = new DateTimeOffset(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);
        var integration = SyncHarness.AddIntegration(t, synced: true);
        var existingAccountId = Guid.NewGuid();
        SyncHarness.AddLinked(t, integration, existingAccountId, card: false);
        var polp = new ScriptedPolp();
        polp.Accounts.Add(ScriptedPolp.Account(existingAccountId));
        var card = ScriptedPolp.Card(null, "1234123412341234");
        polp.Cards.Add(card);
        polp.Bills[card.Id] = [ScriptedPolp.Bill("2026-10-28", "2026-11-10", "300.00")];
        polp.CardTransactions[card.Id] = [ScriptedPolp.CardTx("2026-10-20T15:00:00.000Z", "300.00")];
        const string query = "fromUpdatedAt=2026-11-04T10:00:00";

        await SyncHarness.Processor(t, polp).Process("credit_cards", integration.PolpIntegrationId, query);

        Assert.Contains(polp.Calls, c => c is ("bills", var id, null) && id == card.Id);
        Assert.Contains(polp.Calls, c => c is ("card-transactions", var id, null) && id == card.Id);
        Assert.DoesNotContain(polp.Calls, c => c.Op == "account-transactions");

        await using var db = t.NewContext();
        var savedCard = db.BankAccounts.Single(b => b.PolpAccountId == card.Id);
        Assert.Equal(CreditCardAccountDetector.CardType, savedCard.PolpAccountType);
        Assert.Equal("GOLD", savedCard.ProductType);
        Assert.Equal(300m, savedCard.CurrentInvoiceAmount);
        Assert.Equal(new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc), savedCard.InvoiceDueDate);
        Assert.Single(db.Transactions.Where(x => x.BankAccountId == savedCard.Id));
        Assert.NotNull(db.PolpIntegrations.Single().LastSyncedAt);
    }

    [Fact]
    public async Task Flags_de_erro_de_enriquecimento_marcam_EnrichmentPending_e_recurso_indisponivel_nao()
    {
        using var t = new TestDb();
        var integration = SyncHarness.AddIntegration(t, status: "AWAITING_AUTHORIZATION");
        var polp = new ScriptedPolp();
        var inbox = new RecordingInbox();
        var processor = SyncHarness.Processor(t, polp, inbox);

        polp.Consent = new PolpConsentDto { Status = "AUTHORISED", ExecutionStatus = "PARTIAL_SUCCESS", Flags = ["ERROR_PROCESSING_CATEGORIES"] };
        await processor.Process("consents", integration.PolpIntegrationId);
        await using (var db = t.NewContext())
        {
            var saved = db.PolpIntegrations.Single();
            Assert.True(saved.EnrichmentPending);
            Assert.Equal("AUTHORISED", saved.Status);
            Assert.Equal("PARTIAL_SUCCESS", saved.ExecutionStatus);
        }
        var backfill = Assert.Single(inbox.Events);
        Assert.Equal(new PolpInboxEvent(PolpInternalEvents.Backfill, "consents", integration.PolpIntegrationId.ToString(), null), backfill);

        polp.Consent = new PolpConsentDto { Status = "AUTHORISED", ExecutionStatus = "PARTIAL_SUCCESS", Flags = ["PARTIALLY_UNAVAILABLE_RESOURCES"] };
        await processor.Process("consents", integration.PolpIntegrationId);
        await using (var db = t.NewContext())
            Assert.False(db.PolpIntegrations.Single().EnrichmentPending);

        Assert.True(PolpSyncService.HasEnrichmentPending(["X", "ERROR_PROCESSING_COUNTERPARTIES"]));
        Assert.False(PolpSyncService.HasEnrichmentPending(null));
    }
}

public class PolpQueryWindowTests
{
    private static string Decode(PolpQueryWindow? w) => Uri.UnescapeDataString(Assert.IsType<PolpQueryWindow>(w).Query);

    [Theory]
    [InlineData("fromCreatedAt=2026-07-17T16:00:00Z")]
    [InlineData("fromCreatedAt=2026-07-17T16:00:00.000Z")]
    [InlineData("fromCreatedAt=2026-07-17T16%3A00%3A00%2B00%3A00")]
    [InlineData("fromCreatedAt=2026-07-17T13:00:00")]
    [InlineData("fromCreatedAt=2026-07-17T13:00:00.000000")]
    public void Emitida_em_horario_de_Brasilia_sem_sufixo(string raw)
    {
        Assert.Equal("fromCreatedAt=2026-07-17T13:00:00.000000", Decode(PolpQueryWindow.TryParse(raw)));
    }

    [Fact]
    public void Campos_desconhecidos_e_datas_invalidas_sao_descartados()
    {
        Assert.Null(PolpQueryWindow.TryParse("cursor=abc&fromCreatedAt=ontem"));
        Assert.Equal("toUpdatedAt=2026-07-17T21:00:00.000000",
            Decode(PolpQueryWindow.TryParse("x=1&toUpdatedAt=2026-07-18T00:00:00Z")));
    }

    [Fact]
    public void Janela_da_reconciliacao_usa_criado_ou_alterado_desde_em_Brasilia()
    {
        var window = PolpQueryWindow.ChangedSince(new DateTimeOffset(2026, 10, 28, 15, 0, 0, TimeSpan.Zero));
        Assert.Equal("fromCreatedAt=2026-10-28T12:00:00.000000&fromUpdatedAt=2026-10-28T12:00:00.000000", Decode(window));
    }
}

public class PolpReconciliationTests
{
    private static (PolpReconciliationService Service, RecordingInbox Inbox, FakeJobLock Lock) Build(TestDb t, int? intervalMinutes = null)
    {
        var inbox = new RecordingInbox();
        var jobLock = new FakeJobLock();
        var provider = new ServiceCollection()
            .AddScoped(_ => t.NewContext())
            .AddSingleton<IPolpInbox>(inbox)
            .BuildServiceProvider();
        var settings = new Dictionary<string, string?>();
        if (intervalMinutes.HasValue) settings["Polp:ReconciliationIntervalMinutes"] = intervalMinutes.Value.ToString();
        var service = new PolpReconciliationService(provider.GetRequiredService<IServiceScopeFactory>(), jobLock,
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), t.Clock, NullLogger<PolpReconciliationService>.Instance);
        return (service, inbox, jobLock);
    }

    private static readonly DateTimeOffset Midnight = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Intervalo_1_enfileira_todas_com_prioridade_baixa_reconcile_para_sincronizadas_backfill_para_incompletas_e_nada_para_pendentes()
    {
        using var t = new TestDb();
        var synced = SyncHarness.AddIntegration(t, synced: true);
        var incomplete = SyncHarness.AddIntegration(t);
        SyncHarness.AddIntegration(t, status: PolpDisconnectService.PendingStatus, synced: true);
        SyncHarness.AddIntegration(t, status: "AWAITING_AUTHORIZATION");
        var (service, inbox, jobLock) = Build(t, intervalMinutes: 1);

        Assert.True(await service.RunOnceAsync(default));

        Assert.Equal(2, inbox.Events.Count);
        Assert.Contains(new PolpInboxEvent(PolpInternalEvents.Reconcile, "consents", synced.PolpIntegrationId.ToString(), null, PolpInboxPriority.Low), inbox.Events);
        Assert.Contains(new PolpInboxEvent(PolpInternalEvents.Backfill, "consents", incomplete.PolpIntegrationId.ToString(), null, PolpInboxPriority.Low), inbox.Events);
        Assert.False(jobLock.IsHeld(JobLockKeys.PolpReconciliation));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(15, 15, 1)]
    [InlineData(100, 15, 6)]
    [InlineData(1440, 15, 96)]
    [InlineData(0, 1, 1)]
    public void Tick_e_slots_derivam_do_intervalo(int interval, int tickMinutes, int slots)
    {
        var schedule = ReconciliationSchedule.FromMinutes(interval);
        Assert.Equal(TimeSpan.FromMinutes(tickMinutes), schedule.Tick);
        Assert.Equal(slots, schedule.Slots);
    }

    [Fact]
    public void Bucket_e_rodada_sao_estaveis()
    {
        var schedule = ReconciliationSchedule.FromMinutes(1440);
        var id = Guid.Parse("8c1f6a52-2d0e-4c55-9b0a-6f0e3c1d2b4a");
        Assert.Equal(schedule.Bucket(id), ReconciliationSchedule.FromMinutes(1440).Bucket(Guid.Parse(id.ToString())));
        Assert.Equal(0, schedule.Round(Midnight));
        Assert.Equal(1, schedule.Round(Midnight.AddMinutes(15)));
        Assert.Equal(1, schedule.Round(Midnight.AddMinutes(29)));
        Assert.Equal(0, schedule.Round(Midnight.AddDays(1)));
        Assert.Equal(Midnight.AddMinutes(30), schedule.NextTickAt(Midnight.AddMinutes(15)));
    }

    [Fact]
    public async Task Intervalo_1440_em_96_rodadas_enfileira_cada_integracao_exatamente_uma_vez()
    {
        using var t = new TestDb();
        var active = Enumerable.Range(0, 60).Select(i => SyncHarness.AddIntegration(t, synced: i % 3 != 0)).ToList();
        var pending = SyncHarness.AddIntegration(t, status: PolpDisconnectService.PendingStatus, synced: true);
        var (service, inbox, _) = Build(t, intervalMinutes: 1440);

        var perRound = new List<int>();
        for (var round = 0; round < 96; round++)
        {
            t.Clock.UtcNow = Midnight.AddMinutes(15 * round).AddSeconds(1);
            var before = inbox.Events.Count;
            Assert.True(await service.RunOnceAsync(default));
            perRound.Add(inbox.Events.Count - before);
        }

        var counts = inbox.Events.GroupBy(e => e.ResourceId).ToDictionary(g => g.Key!, g => g.Count());
        Assert.Equal(active.Select(a => a.PolpIntegrationId.ToString()).OrderBy(x => x), counts.Keys.OrderBy(x => x));
        Assert.All(counts.Values, c => Assert.Equal(1, c));
        Assert.DoesNotContain(pending.PolpIntegrationId.ToString(), counts.Keys);
        Assert.All(inbox.Events, e => Assert.Equal(PolpInboxPriority.Low, e.Priority));
        Assert.True(perRound.Max() < active.Count);
    }

    [Fact]
    public async Task Rodada_isolada_enfileira_so_a_fracao_do_seu_slot()
    {
        using var t = new TestDb();
        var active = Enumerable.Range(0, 60).Select(_ => SyncHarness.AddIntegration(t, synced: true)).ToList();
        var (service, inbox, _) = Build(t, intervalMinutes: 1440);
        var round = service.Schedule.Bucket(active[0].Id);
        t.Clock.UtcNow = Midnight.AddDays(3).AddMinutes(15 * round).AddMinutes(7);

        Assert.True(await service.RunOnceAsync(default));

        var expected = active.Where(a => service.Schedule.Bucket(a.Id) == round).Select(a => a.PolpIntegrationId.ToString()).OrderBy(x => x).ToList();
        Assert.Contains(active[0].PolpIntegrationId.ToString(), expected);
        Assert.True(expected.Count < active.Count);
        Assert.Equal(expected, inbox.Events.Select(e => e.ResourceId!).OrderBy(x => x));
    }

    [Fact]
    public async Task Outra_instancia_com_o_lock_impede_a_rodada()
    {
        using var t = new TestDb();
        SyncHarness.AddIntegration(t, synced: true);
        var (service, inbox, jobLock) = Build(t, intervalMinutes: 1);

        await using (await jobLock.TryAcquireAsync(JobLockKeys.PolpReconciliation, default))
            Assert.False(await service.RunOnceAsync(default));

        Assert.Empty(inbox.Events);
    }

    [Fact]
    public async Task Reconcile_usa_janela_de_7_dias_em_Brasilia_sem_Z_para_contas_cartoes_e_transacoes()
    {
        using var t = new TestDb();
        t.Clock.UtcNow = new DateTimeOffset(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);
        var integration = SyncHarness.AddIntegration(t, synced: true);
        var accountId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        SyncHarness.AddLinked(t, integration, accountId, card: false);
        SyncHarness.AddLinked(t, integration, cardId, card: true);
        var polp = new ScriptedPolp();
        polp.AccountTransactions[accountId] = [ScriptedPolp.AccountTx("2026-11-01T12:00:00.000Z", "15.00")];

        await SyncHarness.Processor(t, polp).Process(PolpInternalEvents.Reconcile, integration.PolpIntegrationId);

        const string expected = "fromCreatedAt=2026-10-28T12%3A00%3A00.000000&fromUpdatedAt=2026-10-28T12%3A00%3A00.000000";
        Assert.Contains(polp.Calls, c => c is ("accounts", _, expected));
        Assert.Contains(polp.Calls, c => c is ("cards", _, expected));
        Assert.Contains(polp.Calls, c => c is ("account-transactions", var id, expected) && id == accountId);
        Assert.Contains(polp.Calls, c => c is ("card-transactions", var id, expected) && id == cardId);
        Assert.Contains(polp.Calls, c => c is ("bills", var id, null) && id == cardId);

        await using var db = t.NewContext();
        Assert.Single(db.Transactions);
        Assert.Equal(t.Clock.UtcNow.Value.UtcDateTime, db.PolpIntegrations.Single().LastSyncedAt);
    }
}
