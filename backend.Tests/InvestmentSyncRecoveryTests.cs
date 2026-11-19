using System.Text.Json;
using ControlFinance.API.Controllers;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Services.Investments;
using ControlFinance.API.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlFinance.API.Tests;

// Synthetic, isolated fixtures. Added for regression coverage; not executed in this task.
public class InvestmentSyncRecoveryTests
{
    [Fact]
    public async Task Backfill_failure_does_not_prevent_independent_investment_jobs()
    {
        using var t = new TestDb();
        var parent = SyncHarness.AddIntegration(t);
        var polp = new ScriptedPolp(); // No account/card resources yet: backfill must retry.
        var inbox = new RecordingInbox();
        var integrationLock = new IntegrationLock();
        var config = new ConfigurationBuilder().Build();
        var imports = new PolpInvestmentImportService(t.Db, polp, integrationLock, inbox,
            new InvestmentImportStore(t.Db), t.Clock, config);
        var metadata = new PolpConsentMetadataService(t.Db, polp, integrationLock, t.Clock, inbox);
        var processor = new PolpWebhookProcessor(t.Db, SyncHarness.Sync(t, polp), polp, inbox,
            NullLogger<PolpWebhookProcessor>.Instance, metadata, imports);

        await Assert.ThrowsAsync<PolpResourceNotReadyException>(() => processor.Process(PolpInternalEvents.Backfill, parent.PolpIntegrationId));

        Assert.Contains(inbox.Events, e => e.Event == PolpInternalEvents.InvestmentDiscovery && e.ResourceId == parent.Id.ToString());
        Assert.Contains(inbox.Events, e => e.Event == PolpInternalEvents.ConsentMetadata && e.ResourceId == parent.PolpIntegrationId.ToString());
        Assert.Empty(t.Db.ImportedInvestments);
    }

    [Fact]
    public async Task Recently_completed_discovery_still_finds_new_reserved_accounts()
    {
        using var t = new TestDb();
        var parent = SyncHarness.AddIntegration(t);
        parent.ConsentProductsJson = "[\"ACCOUNT\",\"CREDIT_CARD_ACCOUNT\",\"INVESTMENTS\"]";
        var account = SyncHarness.AddLinked(t, parent, Guid.NewGuid(), false);
        account.HasReservedBalance = true;
        t.Db.InvestmentSyncStates.Add(new InvestmentSyncState
        {
            UserId = parent.UserId, IntegrationId = parent.Id, SourceConsentId = parent.PolpIntegrationId,
            Kind = "discovery", Family = "all", Status = "Complete", StartedAt = t.Clock.GetUtcNow().UtcDateTime
        });
        await t.Db.SaveChangesAsync();
        var inbox = new RecordingInbox();
        var service = new PolpInvestmentImportService(t.Db, new ScriptedPolp(), new IntegrationLock(), inbox,
            new InvestmentImportStore(t.Db), t.Clock, new ConfigurationBuilder().Build());

        await service.DiscoverAsync(parent.Id, default);

        Assert.Contains(await t.Db.InvestmentSyncStates.AsNoTracking().ToListAsync(), s => s.UserId == parent.UserId
            && s.IntegrationId == parent.Id && s.SourceConsentId == parent.PolpIntegrationId
            && s.Kind == "reserved" && s.ParentExternalId == account.PolpAccountId!.Value.ToString());
        Assert.Empty(t.Db.ImportedReservedBalances); // No fabricated financial data.
    }

    [Fact]
    public async Task Failed_metadata_read_can_be_retried_without_false_success_from_throttle()
    {
        using var t = new TestDb();
        var parent = SyncHarness.AddIntegration(t);
        var polp = new ScriptedPolp
        {
            Consent = new PolpConsentDto { Id = parent.PolpIntegrationId, InstitutionId = parent.InstitutionId,
                ClientUserId = parent.UserId.ToString(), Status = "AUTHORISED", Products = ["ACCOUNT"] }
        };
        polp.Failing.Add(parent.PolpIntegrationId);
        var service = new PolpConsentMetadataService(t.Db, polp, new IntegrationLock(), t.Clock);
        await Assert.ThrowsAsync<HttpRequestException>(() => service.RefreshAsync(parent, default));
        polp.Failing.Clear();

        await service.RefreshAsync(parent, default);

        Assert.Equal(2, polp.Calls.Count(c => c.Op == "consent"));
        var current = await t.Db.PolpIntegrations.AsNoTracking().SingleAsync(p => p.Id == parent.Id);
        Assert.Equal("[\"ACCOUNT\"]", current.ConsentProductsJson);
        Assert.False(PolpConsentMetadataService.HasInvestmentsJson(current.ConsentProductsJson));
    }

    [Fact]
    public async Task Dead_discovery_is_an_error_and_never_a_confirmed_empty_portfolio()
    {
        using var t = new TestDb();
        var parent = SyncHarness.AddIntegration(t);
        parent.ConsentProductsJson = "[\"ACCOUNT\",\"INVESTMENTS\"]";
        t.Db.PolpWebhookEvents.Add(new PolpWebhookEvent { Event = PolpInternalEvents.InvestmentDiscovery,
            ResourceId = parent.Id.ToString(), Status = PolpWebhookEventStatus.Dead, ReceivedAt = t.Clock.GetUtcNow().UtcDateTime });
        await t.Db.SaveChangesAsync();
        var controller = t.Controller(db => new InvestmentsController(db, t.Clock));

        var result = Assert.IsType<OkObjectResult>(await controller.Connections(null, default));
        var connection = JsonSerializer.SerializeToElement(result.Value).GetProperty("data")[0];
        Assert.Equal("temporary_error", connection.GetProperty("status").GetString());
        Assert.Equal("temporary_error", connection.GetProperty("reservedStatus").GetString());
    }
}
