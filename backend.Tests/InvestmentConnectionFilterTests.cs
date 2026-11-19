using System.Text.Json;
using ControlFinance.API.Controllers;
using ControlFinance.API.Models;
using ControlFinance.API.Services.Investments;
using ControlFinance.API.Tests.Support;
using Microsoft.AspNetCore.Mvc;

namespace ControlFinance.API.Tests;

public class InvestmentConnectionFilterTests
{
    private static InvestmentsController Controller(TestDb t) => t.Controller(db => new InvestmentsController(db,
        t.Clock));

    [Fact]
    public async Task Connection_filter_rejects_other_users_in_both_catalogs()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        var controller = Controller(t);
        Assert.IsType<NotFoundResult>(await controller.List(null, 1, default, other.Integration.Id));
        Assert.IsType<NotFoundResult>(await controller.Reserves(null, 1, default, other.Integration.Id));
    }

    [Fact]
    public async Task Connection_filter_runs_before_pagination_and_needs_no_bank_account()
    {
        using var t = new TestDb();
        var first = SyncHarness.AddIntegration(t);
        var second = SyncHarness.AddIntegration(t);
        var at = t.Clock.GetUtcNow().UtcDateTime;
        t.Db.ImportedInvestments.Add(new ImportedInvestment { UserId = t.User.Id, IntegrationId = first.Id,
            SourceConsentId = first.PolpIntegrationId, Family = "funds", ExternalId = Guid.NewGuid().ToString(),
            FirstObservedAt = at.AddDays(-1), LastObservedAt = at });
        var selected = new ImportedInvestment { UserId = t.User.Id, IntegrationId = second.Id,
            SourceConsentId = second.PolpIntegrationId, Family = "funds", ExternalId = Guid.NewGuid().ToString(),
            FirstObservedAt = at, LastObservedAt = at };
        t.Db.ImportedInvestments.Add(selected);
        await t.Db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await Controller(t).List(null, 1, default, second.Id));
        var json = JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal(selected.Id, json.GetProperty("data")[0].GetProperty("Id").GetGuid());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("nextCursor").ValueKind);
        Assert.Empty(t.Db.BankAccounts);
    }
}
