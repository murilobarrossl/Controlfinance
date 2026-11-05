using ControlFinance.API.Controllers;
using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlFinance.API.Tests;

public class PolpRemovedResourceTests
{
    private sealed record Connection(PolpIntegration Integration, Guid AccountId, Guid CardId);

    private static Connection AddConnection(TestDb t)
    {
        var integration = SyncHarness.AddIntegration(t, synced: true);
        var accountId = Guid.NewGuid();
        var cardId = Guid.NewGuid();
        var account = SyncHarness.AddLinked(t, integration, accountId, card: false);
        SyncHarness.AddLinked(t, integration, cardId, card: true);
        t.AddTransaction(account, TransactionType.Expense, 10m, new DateTime(2026, 10, 1));
        return new Connection(integration, accountId, cardId);
    }

    private static PolpDisconnectService Disconnect(AppDbContext db, IPolpService polp) =>
        new(db, polp, new IntegrationLock(), NullLogger<PolpDisconnectService>.Instance);

    private static HashSet<Guid> Markers(TestDb t)
    {
        using var db = t.NewContext();
        return db.RemovedPolpResources.Select(r => r.PolpId).ToHashSet();
    }

    // Worker de verdade sobre o processor de verdade: o que importa é o desfecho na inbox
    // (Done sem retentativa) e o que vai para o log.
    private static (InboxHarness H, ListLogger<PolpWebhookProcessor> Log, ScriptedPolp Polp) Pipeline()
    {
        var h = new InboxHarness();
        var log = new ListLogger<PolpWebhookProcessor>();
        var polp = new ScriptedPolp { DeleteFails = false };
        var processor = SyncHarness.Processor(h.T, polp, logger: log);
        h.Handler.OnHandle = (evt, ct) => processor.ProcessAsync(evt, ct);
        return (h, log, polp);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Desconexao_grava_consentimento_contas_e_cartoes(bool providerAccepts)
    {
        using var t = new TestDb();
        var c = AddConnection(t);
        var untouched = SyncHarness.AddIntegration(t, synced: true);
        SyncHarness.AddLinked(t, untouched, Guid.NewGuid(), card: false);

        await using (var db = t.NewContext())
        {
            var service = Disconnect(db, new ScriptedPolp { DeleteFails = !providerAccepts });
            await service.DisconnectAsync(db.PolpIntegrations.Single(p => p.Id == c.Integration.Id), default);
            if (!providerAccepts) await service.RetryPendingAsync(default);
        }

        Assert.Equal(new HashSet<Guid> { c.Integration.PolpIntegrationId, c.AccountId, c.CardId }, Markers(t));
    }

    [Fact]
    public async Task Exclusao_da_conta_grava_os_ids_na_mesma_operacao_e_o_marcador_sobrevive()
    {
        using var t = new TestDb();
        t.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword("senha-forte");
        t.Db.SaveChanges();
        var c = AddConnection(t);
        t.AddAccount("Manual");

        var polp = new ScriptedPolp { DeleteFails = true };
        var controller = t.Controller(db => new AccountController(
            db, Disconnect(db, polp), new SessionValidator(db, new MemoryCache(new MemoryCacheOptions())),
            new RateLimitService(), new ConfigurationBuilder().Build()));
        controller.HttpContext.RequestServices = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new TestHostEnvironment())
            .BuildServiceProvider();

        Assert.IsType<OkObjectResult>(await controller.DeleteAccount(new ConfirmPasswordDto { Password = "senha-forte" }, default));

        await using var check = t.NewContext();
        Assert.Empty(check.Users);
        Assert.Empty(check.BankAccounts);
        Assert.Equal(new HashSet<Guid> { c.Integration.PolpIntegrationId, c.AccountId, c.CardId }, Markers(t));
    }

    [Fact]
    public async Task Eventos_de_recursos_removidos_terminam_Done_com_LogInformation_sem_erro_nem_retentativa()
    {
        var (h, log, polp) = Pipeline();
        using var _ = h;
        var c = AddConnection(h.T);
        await using (var db = h.NewContext())
            await Disconnect(db, polp).DisconnectAsync(db.PolpIntegrations.Single(), default);
        polp.Calls.Clear();

        var consent = c.Integration.PolpIntegrationId.ToString("D");
        PolpInboxEvent[] events =
        [
            new("consents", "consents", consent, null),
            new("accounts", "consents", consent, null),
            new("credit_cards", "consents", consent, null),
            new(PolpInternalEvents.Backfill, "consents", consent, null),
            new(PolpInternalEvents.Reconcile, "consents", consent, null, PolpInboxPriority.Low),
            new("accounts.transactions", "accounts", c.AccountId.ToString("D"), null),
            new("credit_cards.transactions", "credit_cards", c.CardId.ToString("D"), null),
            new("bills", "credit_cards", c.CardId.ToString("D"), null),
        ];
        foreach (var evt in events) await h.Inbox.EnqueueAsync(evt, default);

        while (await h.Worker.ProcessNextAsync(default)) { }

        Assert.All(h.Rows(), r =>
        {
            Assert.Equal(PolpWebhookEventStatus.Done, r.Status);
            Assert.Equal(0, r.Attempts);
            Assert.Null(r.LastError);
        });
        Assert.Empty(polp.Calls);
        Assert.DoesNotContain(h.WorkerLog.Entries, e => e.Level >= LogLevel.Warning);
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Equal(events.Length, log.Entries.Count(e => e.Level == LogLevel.Information));
        Assert.All(log.Entries, e => Assert.DoesNotContain("Banco Teste", e.Message));
    }

    [Theory]
    [InlineData("consents", false)]
    [InlineData(PolpInternalEvents.Reconcile, false)]
    [InlineData("bills", true)]
    [InlineData("accounts.transactions", true)]
    public async Task Recurso_nunca_visto_continua_com_retentativa(string evt, bool byResource)
    {
        var (h, log, _) = Pipeline();
        using var _h = h;
        // Outro id removido não pode afetar este.
        await using (var db = h.NewContext())
            await RemovedPolpResources.MarkAsync(db, [Guid.NewGuid()], InboxHarness.Start, default);

        await h.Inbox.EnqueueAsync(new PolpInboxEvent(evt, byResource ? "accounts" : "consents", Guid.NewGuid().ToString("D"), null), default);
        Assert.True(await h.Worker.ProcessNextAsync(default));

        var row = h.Single();
        Assert.Equal(PolpWebhookEventStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.StartsWith(nameof(PolpResourceNotReadyException), row.LastError);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Sync_que_recria_conta_removida_limpa_o_marcador_e_o_evento_seguinte_e_processado()
    {
        using var t = new TestDb();
        var integration = SyncHarness.AddIntegration(t, synced: true);
        var accountId = Guid.NewGuid();
        var unrelated = Guid.NewGuid();
        await using (var db = t.NewContext())
            await RemovedPolpResources.MarkAsync(db, [accountId, unrelated], InboxHarness.Start, default);
        var polp = new ScriptedPolp();
        polp.Accounts.Add(ScriptedPolp.Account(accountId));
        var processor = SyncHarness.Processor(t, polp);

        await processor.Process("accounts", integration.PolpIntegrationId);

        Assert.Equal(new HashSet<Guid> { unrelated }, Markers(t));
        polp.Calls.Clear();
        await processor.Process("accounts.transactions", accountId);
        Assert.Contains(polp.Calls, c => c is ("account-transactions", var id, _) && id == accountId);
    }

    [Fact]
    public async Task Marcar_de_novo_e_idempotente_e_renova_a_data()
    {
        using var t = new TestDb();
        var id = Guid.NewGuid();
        await using (var db = t.NewContext())
        {
            await RemovedPolpResources.MarkAsync(db, [id, id], InboxHarness.Start, default);
            await RemovedPolpResources.MarkAsync(db, [id], InboxHarness.Start.AddDays(2), default);
        }

        await using var check = t.NewContext();
        Assert.Equal(InboxHarness.Start.AddDays(2), Assert.Single(check.RemovedPolpResources).RemovedAt);
    }

    [Fact]
    public async Task Limpeza_apaga_marcadores_com_mais_de_30_dias()
    {
        using var h = new InboxHarness();
        var old = Guid.NewGuid();
        var recent = Guid.NewGuid();
        await using (var db = h.NewContext())
        {
            await RemovedPolpResources.MarkAsync(db, [old], InboxHarness.Start.AddDays(-31), default);
            await RemovedPolpResources.MarkAsync(db, [recent], InboxHarness.Start.AddDays(-29), default);
        }

        await using (var db = h.NewContext())
            await PolpInboxCleanupService.RunRoundAsync(new FakeJobLock(), db, InboxHarness.Start, default);

        Assert.Equal(new HashSet<Guid> { recent }, Markers(h.T));
    }

    [Fact]
    public void Insercao_no_Postgres_ignora_duplicata()
    {
        var sql = RemovedPolpResources.PostgresMarkSql([Guid.NewGuid()], InboxHarness.Start).Format;
        Assert.Contains("ON CONFLICT (\"PolpId\") DO UPDATE", sql);
        Assert.Contains("unnest(", sql);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "testes";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
