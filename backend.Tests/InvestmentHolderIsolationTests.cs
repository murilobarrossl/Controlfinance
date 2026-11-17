using System.Text.Json;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Services.Investments;
using ControlFinance.API.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlFinance.API.Tests;

public class InvestmentHolderIsolationTests
{
    private static PolpInvestmentImportService Service(TestDb t, IPolpService polp, IPolpInbox inbox) =>
        new(t.Db, polp, new IntegrationLock(), inbox, new InvestmentImportStore(t.Db), t.Clock, new ConfigurationBuilder().Build());

    private static PolpIntegration InvestmentIntegration(TestDb t, string documentType)
    {
        t.User.DocumentType = documentType;
        var parent = SyncHarness.AddIntegration(t);
        parent.ConsentProductsJson = "[\"ACCOUNT\",\"CREDIT_CARD_ACCOUNT\",\"INVESTMENTS\"]";
        t.Db.SaveChanges();
        return parent;
    }

    [Fact]
    public async Task Cnpj_discovery_does_not_claim_or_request_anything()
    {
        using var t = new TestDb();
        var parent = InvestmentIntegration(t, DocumentTypes.Cnpj);
        var account = SyncHarness.AddLinked(t, parent, Guid.NewGuid(), false);
        account.HasReservedBalance = true;
        await t.Db.SaveChangesAsync();
        var inbox = new RecordingInbox();

        await Service(t, new ScriptedPolp(), inbox).DiscoverAsync(parent.Id, default);

        Assert.Empty(await t.Db.InvestmentSyncStates.ToListAsync());
        Assert.Empty(inbox.Events);
    }

    [Fact]
    public async Task Cpf_discovery_still_runs()
    {
        using var t = new TestDb();
        var parent = InvestmentIntegration(t, DocumentTypes.Cpf);
        var inbox = new RecordingInbox();

        await Service(t, new ScriptedPolp(), inbox).DiscoverAsync(parent.Id, default);

        var states = await t.Db.InvestmentSyncStates.AsNoTracking().ToListAsync();
        Assert.Contains(states, s => s.Kind == "discovery" && s.Status == "Complete");
        Assert.Contains(states, s => s.Kind == "list");
        Assert.Contains(inbox.Events, e => e.Event == PolpInternalEvents.InvestmentPage);
    }

    [Fact]
    public async Task Pending_cnpj_page_is_blocked_without_calling_polp()
    {
        using var t = new TestDb();
        var parent = InvestmentIntegration(t, DocumentTypes.Cnpj);
        var scope = new InvestmentSyncState { UserId = parent.UserId, IntegrationId = parent.Id, SourceConsentId = parent.PolpIntegrationId,
            Family = "funds", Kind = "list", Status = "Pending", RunId = Guid.NewGuid() };
        t.Db.InvestmentSyncStates.Add(scope);
        await t.Db.SaveChangesAsync();
        var polp = new ScriptedPolp();

        await Service(t, polp, new RecordingInbox()).ProcessPageAsync(scope.Id, default, scope.RunId, 0);

        var current = await t.Db.InvestmentSyncStates.AsNoTracking().SingleAsync(s => s.Id == scope.Id);
        Assert.Equal("Blocked", current.Status);
        Assert.Equal("holder_not_supported", current.ErrorCode);
        Assert.Empty(polp.Calls);
    }

    [Fact]
    public async Task Webhook_does_not_queue_discovery_for_cnpj()
    {
        using var t = new TestDb();
        var parent = InvestmentIntegration(t, DocumentTypes.Cnpj);
        var polp = new ScriptedPolp();
        var inbox = new RecordingInbox();
        var processor = new PolpWebhookProcessor(t.Db, SyncHarness.Sync(t, polp), polp, inbox,
            NullLogger<PolpWebhookProcessor>.Instance, null, Service(t, polp, inbox));

        await Assert.ThrowsAsync<PolpResourceNotReadyException>(() => processor.Process(PolpInternalEvents.Backfill, parent.PolpIntegrationId));

        Assert.DoesNotContain(inbox.Events, e => e.Event == PolpInternalEvents.InvestmentDiscovery);
    }

    [Fact]
    public async Task Reconciliation_does_not_queue_discovery_for_cnpj()
    {
        using var t = new TestDb();
        var parent = InvestmentIntegration(t, DocumentTypes.Cnpj);
        var inbox = new RecordingInbox();
        var reconciliation = ReconciliationFor(t, inbox);

        await reconciliation.RunOnceAsync(default);

        Assert.Contains(inbox.Events, e => e.Event == PolpInternalEvents.ConsentMetadata);
        Assert.DoesNotContain(inbox.Events, e => e.Event == PolpInternalEvents.InvestmentDiscovery);
    }

    [Fact]
    public async Task Reconciliation_still_queues_discovery_for_cpf()
    {
        using var t = new TestDb();
        var parent = InvestmentIntegration(t, DocumentTypes.Cpf);
        var inbox = new RecordingInbox();

        await ReconciliationFor(t, inbox).RunOnceAsync(default);

        Assert.Contains(inbox.Events, e => e.Event == PolpInternalEvents.InvestmentDiscovery && e.ResourceId == parent.Id.ToString());
    }

    [Fact]
    public async Task Failed_discovery_releases_its_marker_for_the_next_attempt()
    {
        using var t = new TestDb();
        var parent = InvestmentIntegration(t, DocumentTypes.Cpf);
        var inbox = new FailingInbox(new RecordingInbox());

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(t, new ScriptedPolp(), inbox).DiscoverAsync(parent.Id, default));

        var marker = await t.Db.InvestmentSyncStates.AsNoTracking().SingleAsync(s => s.Kind == "discovery");
        Assert.Equal("Error", marker.Status);

        inbox.Throw = false;
        await Service(t, new ScriptedPolp(), inbox).DiscoverAsync(parent.Id, default);
        Assert.Equal("Complete", (await t.Db.InvestmentSyncStates.AsNoTracking().SingleAsync(s => s.Kind == "discovery")).Status);
    }

    [Fact]
    public async Task Second_schedule_read_updates_existing_rows_instead_of_failing()
    {
        using var t = new TestDb();
        var parent = InvestmentIntegration(t, DocumentTypes.Cpf);
        var polp = new SchedulePolp(new ScriptedPolp
        {
            Consent = new PolpConsentDto { Id = parent.PolpIntegrationId, InstitutionId = parent.InstitutionId, ClientUserId = parent.UserId.ToString(),
                Status = "AUTHORISED", ExecutionStatus = "SUCCESS", Products = ["ACCOUNT", "INVESTMENTS"] }
        }, parent.PolpIntegrationId);
        var service = Service(t, polp, new RecordingInbox());

        for (var round = 0; round < 2; round++)
        {
            var scope = await t.Db.InvestmentSyncStates.AsNoTracking().SingleOrDefaultAsync(s => s.Kind == "schedules");
            if (scope is null)
            {
                scope = new InvestmentSyncState { UserId = parent.UserId, IntegrationId = parent.Id, SourceConsentId = parent.PolpIntegrationId,
                    Family = "sync-schedules", Kind = "schedules", Status = "Pending", RunId = Guid.NewGuid() };
                t.Db.InvestmentSyncStates.Add(scope);
            }
            else
            {
                scope = await t.Db.InvestmentSyncStates.SingleAsync(s => s.Id == scope.Id);
                scope.Status = "Pending"; scope.RunId = Guid.NewGuid(); scope.PageNumber = 0; scope.Cursor = null; scope.SeenCursorsJson = "[]";
            }
            await t.Db.SaveChangesAsync();
            t.Db.ChangeTracker.Clear();

            await service.ProcessPageAsync(scope.Id, default, scope.RunId, 0);

            var current = await t.Db.InvestmentSyncStates.AsNoTracking().SingleAsync(s => s.Id == scope.Id);
            Assert.Equal("Complete", current.Status);
            var schedule = await t.Db.InvestmentSyncSchedules.AsNoTracking().SingleAsync();
            Assert.Equal(current.RunId, schedule.ObservedRunId);
        }
    }

    private static PolpReconciliationService ReconciliationFor(TestDb t, IPolpInbox inbox)
    {
        var provider = new ServiceCollection().AddScoped(_ => t.NewContext()).AddSingleton(inbox).BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Polp:ReconciliationIntervalMinutes"] = "1"
        }).Build();
        return new PolpReconciliationService(provider.GetRequiredService<IServiceScopeFactory>(), new FakeJobLock(), config, t.Clock,
            NullLogger<PolpReconciliationService>.Instance);
    }

    private sealed class FailingInbox(RecordingInbox inner) : IPolpInbox
    {
        public bool Throw { get; set; } = true;
        public Task<bool> EnqueueAsync(PolpInboxEvent evt, CancellationToken ct) =>
            Throw ? throw new InvalidOperationException("fila indisponível") : inner.EnqueueAsync(evt, ct);
    }

    private sealed class SchedulePolp(ScriptedPolp inner, Guid consentId) : IPolpService
    {
        public Task<PolpInvestmentPage> GetInvestmentSchedulePageAsync(Guid consent, string? cursor, CancellationToken ct = default) =>
            Task.FromResult(new PolpInvestmentPage([JsonSerializer.SerializeToElement(new
            {
                consent_id = consentId.ToString("D"), resource = "fund.transactions", resource_id = "fund-1",
                last_sync_at = "2026-11-16T10:00:00Z", next_sync_at = "2026-11-17T10:00:00Z"
            })], null));
        public Task<List<PolpInstitutionDto>> GetInstitutionsAsync(CancellationToken ct = default) => inner.GetInstitutionsAsync(ct);
        public Task<PolpConsentDto> CreateConsentAsync(CreatePolpConsentRequest request, CancellationToken ct = default) => inner.CreateConsentAsync(request, ct);
        public Task<PolpConsentDto> GetConsentAsync(Guid id, CancellationToken ct = default) => inner.GetConsentAsync(id, ct);
        public Task<List<PolpAccountDto>> GetAccountsAsync(Guid id, PolpQueryWindow? window = null, CancellationToken ct = default) => inner.GetAccountsAsync(id, window, ct);
        public Task<List<PolpCreditCardDto>> GetCreditCardsAsync(Guid id, PolpQueryWindow? window = null, CancellationToken ct = default) => inner.GetCreditCardsAsync(id, window, ct);
        public Task<List<PolpBillDto>> GetBillsAsync(Guid id, PolpQueryWindow? window = null, CancellationToken ct = default) => inner.GetBillsAsync(id, window, ct);
        public Task<List<PolpAccountTransactionDto>> GetAccountTransactionsAsync(Guid id, PolpQueryWindow? window = null, CancellationToken ct = default) => inner.GetAccountTransactionsAsync(id, window, ct);
        public Task<List<PolpCardTransactionDto>> GetCardTransactionsAsync(Guid id, PolpQueryWindow? window = null, CancellationToken ct = default) => inner.GetCardTransactionsAsync(id, window, ct);
        public Task<PolpAccountDto> SyncBalanceAsync(Guid id, CancellationToken ct = default) => inner.SyncBalanceAsync(id, ct);
        public Task DeleteConsentAsync(Guid id, CancellationToken ct = default) => inner.DeleteConsentAsync(id, ct);
    }
}
