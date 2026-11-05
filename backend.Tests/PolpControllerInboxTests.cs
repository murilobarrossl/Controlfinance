using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using ControlFinance.API.Controllers;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ControlFinance.API.Tests;

// Proxy em vez de classe concreta: o controller só usa CreateConsent/GetConsent, e qualquer outra
// chamada à Polp (ex.: um sync disparado no request) falha o teste.
public class FakePolpProxy : DispatchProxy
{
    public ConcurrentQueue<string> Calls { get; } = new();
    public PolpConsentDto? Consent { get; set; }
    public Action? DuringGetConsent { get; set; }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        Calls.Enqueue(method!.Name);
        if (method.Name == nameof(IPolpService.GetConsentAsync)) DuringGetConsent?.Invoke();
        return method.Name switch
        {
            nameof(IPolpService.CreateConsentAsync) or nameof(IPolpService.GetConsentAsync) => Task.FromResult(Consent!),
            _ => throw new NotSupportedException(method.Name)
        };
    }

    public static (IPolpService Service, FakePolpProxy Fake) Create()
    {
        var service = Create<IPolpService, FakePolpProxy>();
        return (service, (FakePolpProxy)(object)service);
    }
}

public class PolpControllerInboxTests
{
    private const string Cpf = "52998224725";

    private static (PolpController Controller, FakePolpProxy Polp) Build(InboxHarness h)
    {
        var (service, fake) = FakePolpProxy.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = h.T.Controller(db => new PolpController(
            db, service,
            new InstitutionNameResolver(service, cache, new ListLogger<InstitutionNameResolver>()),
            h.T.Encryption,
            null!,
            h.Inbox,
            cache));
        return (controller, fake);
    }

    private static PolpConsentDto Consent(Guid id, string status) => new()
    {
        Id = id,
        Status = status,
        ExecutionStatus = "AWAITING_RESOURCES",
        Institution = new PolpConsentInstitutionDto { Name = "Banco Teste" }
    };

    private static JsonElement Json(IActionResult result) =>
        JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value);

    private static PolpIntegration AddIntegration(InboxHarness h, string status, bool synced, bool enrichmentPending = false,
        string? executionStatus = "SUCCESS", DateTime? updatedAt = null)
    {
        var integration = new PolpIntegration
        {
            UserId = h.T.User.Id, PolpIntegrationId = Guid.NewGuid(), InstitutionId = Guid.NewGuid(),
            InstitutionName = "Banco Teste", Status = status, ExecutionStatus = executionStatus,
            SyncedLocally = synced, EnrichmentPending = enrichmentPending,
            UpdatedAt = updatedAt ?? DateTime.UtcNow
        };
        h.T.Db.PolpIntegrations.Add(integration);
        h.T.Db.SaveChanges();
        return integration;
    }

    [Fact]
    public async Task Connect_com_consentimento_AUTHORISED_enfileira_internal_backfill_sem_sincronizar_no_request()
    {
        using var h = new InboxHarness();
        h.T.User.DocumentHash = h.T.Encryption.ComputeLookupHash(Cpf);
        h.T.Db.SaveChanges();
        var (controller, polp) = Build(h);
        var consentId = Guid.NewGuid();
        polp.Consent = Consent(consentId, "AUTHORISED");

        var result = await controller.Connect(
            new PolpConnectDto(Guid.NewGuid().ToString(), Cpf, LegalDocuments.BankConnectionNoticeVersion), default);

        var json = Json(result);
        Assert.Equal("AUTHORISED", json.GetProperty("status").GetString());
        Assert.Equal(consentId, json.GetProperty("polpIntegrationId").GetGuid());
        Assert.True(json.TryGetProperty("integrationId", out _));
        Assert.True(json.TryGetProperty("urlToAuthenticate", out _));

        var row = h.Single();
        Assert.Equal(PolpInternalEvents.Backfill, row.Event);
        Assert.Equal("consents", row.Resource);
        Assert.Equal(consentId.ToString("D"), row.ResourceId);
        Assert.Null(row.QueryParameters);
        Assert.Equal(PolpWebhookEventStatus.Pending, row.Status);

        await Task.Delay(50);
        Assert.Equal([nameof(IPolpService.CreateConsentAsync)], polp.Calls.ToArray());
    }

    [Fact]
    public async Task Connect_aguardando_autorizacao_nao_enfileira()
    {
        using var h = new InboxHarness();
        h.T.User.DocumentHash = h.T.Encryption.ComputeLookupHash(Cpf);
        h.T.Db.SaveChanges();
        var (controller, polp) = Build(h);
        polp.Consent = Consent(Guid.NewGuid(), "AWAITING_AUTHORIZATION");

        Json(await controller.Connect(
            new PolpConnectDto(Guid.NewGuid().ToString(), Cpf, LegalDocuments.BankConnectionNoticeVersion), default));

        Assert.Empty(h.Rows());
    }

    [Fact]
    public async Task Status_que_vira_AUTHORISED_enfileira_backfill()
    {
        using var h = new InboxHarness();
        var integration = AddIntegration(h, "AWAITING_AUTHORIZATION", synced: false, updatedAt: DateTime.UtcNow.AddMinutes(-1));
        var (controller, polp) = Build(h);
        polp.Consent = Consent(integration.PolpIntegrationId, "AUTHORISED");

        var json = Json(await controller.GetIntegrationStatus(integration.Id, default));

        Assert.Equal("syncing", json.GetProperty("status").GetString());
        var row = h.Single();
        Assert.Equal(PolpInternalEvents.Backfill, row.Event);
        Assert.Equal(integration.PolpIntegrationId.ToString("D"), row.ResourceId);
    }

    [Fact]
    public async Task GetIntegrations_nao_dispara_sync_e_enrichmentPending_vem_da_coluna()
    {
        using var h = new InboxHarness();
        var pendingEnrichment = AddIntegration(h, "AUTHORISED", synced: true, enrichmentPending: true, executionStatus: "SUCCESS");
        var partialButEnriched = AddIntegration(h, "AUTHORISED", synced: true, enrichmentPending: false, executionStatus: "PARTIAL_SUCCESS");
        var neverSynced = AddIntegration(h, "AUTHORISED", synced: false);
        var a = h.T.AddAccount("Conta A", polpAccountId: Guid.NewGuid());
        var b = h.T.AddAccount("Conta B", polpAccountId: Guid.NewGuid());
        var c = h.T.AddAccount("Conta C", polpAccountId: Guid.NewGuid());
        a.PolpIntegrationId = pendingEnrichment.PolpIntegrationId;
        b.PolpIntegrationId = partialButEnriched.PolpIntegrationId;
        c.PolpIntegrationId = neverSynced.PolpIntegrationId;
        h.T.Db.SaveChanges();
        var (controller, polp) = Build(h);

        var json = Json(await controller.GetIntegrations(default));

        var byId = json.EnumerateArray().ToDictionary(e => e.GetProperty("Id").GetGuid());
        Assert.True(byId[a.Id].GetProperty("enrichmentPending").GetBoolean());
        Assert.False(byId[b.Id].GetProperty("enrichmentPending").GetBoolean());
        Assert.False(byId[c.Id].GetProperty("enrichmentPending").GetBoolean());
        Assert.Equal(pendingEnrichment.Id, byId[a.Id].GetProperty("localIntegrationId").GetGuid());

        await Task.Delay(50);
        Assert.Empty(h.Rows());
        Assert.Empty(polp.Calls);
    }

    private static void MarkDisconnectPending(InboxHarness h, Guid integrationId)
    {
        using var other = h.NewContext();
        var row = other.PolpIntegrations.Single(p => p.Id == integrationId);
        row.Status = PolpDisconnectService.PendingStatus;
        other.SaveChanges();
    }

    private static PolpIntegration Reload(InboxHarness h, Guid integrationId)
    {
        using var db = h.NewContext();
        return db.PolpIntegrations.AsNoTracking().Single(p => p.Id == integrationId);
    }

    [Fact]
    public async Task Desconexao_durante_a_consulta_de_status_nao_ressuscita_a_integracao()
    {
        using var h = new InboxHarness();
        var integration = AddIntegration(h, "AWAITING_AUTHORIZATION", synced: false, updatedAt: DateTime.UtcNow.AddMinutes(-1));
        var (controller, polp) = Build(h);
        polp.Consent = Consent(integration.PolpIntegrationId, "AUTHORISED");
        polp.DuringGetConsent = () => MarkDisconnectPending(h, integration.Id);

        var result = await controller.GetIntegrationStatus(integration.Id, default);

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(PolpDisconnectService.PendingStatus, Reload(h, integration.Id).Status);
        Assert.Empty(h.Rows());
    }

    [Fact]
    public async Task Consulta_de_status_grava_enrichmentPending_das_flags()
    {
        using var h = new InboxHarness();
        var integration = AddIntegration(h, "AWAITING_AUTHORIZATION", synced: false, updatedAt: DateTime.UtcNow.AddMinutes(-1));
        var (controller, polp) = Build(h);
        polp.Consent = Consent(integration.PolpIntegrationId, "AWAITING_AUTHORIZATION");
        polp.Consent.Flags = ["ERROR_PROCESSING_CATEGORIES"];

        var json = Json(await controller.GetIntegrationStatus(integration.Id, default));

        Assert.Equal("waiting_user_input", json.GetProperty("status").GetString());
        Assert.True(Reload(h, integration.Id).EnrichmentPending);
        Assert.Empty(h.Rows());
    }

    [Fact]
    public async Task Connect_sobre_linha_DISCONNECT_PENDING_responde_409_e_nao_reativa()
    {
        using var h = new InboxHarness();
        h.T.User.DocumentHash = h.T.Encryption.ComputeLookupHash(Cpf);
        h.T.Db.SaveChanges();
        var integration = AddIntegration(h, PolpDisconnectService.PendingStatus, synced: false);
        var (controller, polp) = Build(h);
        polp.Consent = Consent(integration.PolpIntegrationId, "AUTHORISED");

        var result = await controller.Connect(
            new PolpConnectDto(integration.InstitutionId.ToString(), Cpf, LegalDocuments.BankConnectionNoticeVersion), default);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var body = JsonSerializer.SerializeToElement(conflict.Value);
        Assert.Equal("Não foi possível iniciar a conexão com o banco.", body.GetProperty("message").GetString());
        Assert.Single(body.EnumerateObject());
        Assert.Equal(PolpDisconnectService.PendingStatus, Reload(h, integration.Id).Status);
        Assert.Empty(h.Rows());
    }

    [Fact]
    public async Task Connect_reaproveita_linha_ativa_e_atualiza_status()
    {
        using var h = new InboxHarness();
        h.T.User.DocumentHash = h.T.Encryption.ComputeLookupHash(Cpf);
        h.T.Db.SaveChanges();
        var integration = AddIntegration(h, "AWAITING_AUTHORIZATION", synced: false, updatedAt: DateTime.UtcNow.AddHours(-2));
        var (controller, polp) = Build(h);
        polp.Consent = Consent(integration.PolpIntegrationId, "AUTHORISED");

        var json = Json(await controller.Connect(
            new PolpConnectDto(integration.InstitutionId.ToString(), Cpf, LegalDocuments.BankConnectionNoticeVersion), default));

        Assert.Equal(integration.Id, json.GetProperty("integrationId").GetGuid());
        Assert.Equal("AUTHORISED", Reload(h, integration.Id).Status);
        Assert.Equal(PolpInternalEvents.Backfill, h.Single().Event);
        using var db = h.NewContext();
        Assert.Single(db.ConsentRecords.Where(c => c.UserId == h.T.User.Id));
    }

    [Fact]
    public async Task Reconexao_do_mesmo_consentimento_limpa_o_marcador_e_o_evento_seguinte_e_processado()
    {
        using var h = new InboxHarness();
        h.T.User.DocumentHash = h.T.Encryption.ComputeLookupHash(Cpf);
        h.T.Db.SaveChanges();
        var consentId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        using (var db = h.NewContext())
            await RemovedPolpResources.MarkAsync(db, [consentId, otherId], InboxHarness.Start, default);
        var (controller, polp) = Build(h);
        polp.Consent = Consent(consentId, "AUTHORISED");

        Json(await controller.Connect(
            new PolpConnectDto(Guid.NewGuid().ToString(), Cpf, LegalDocuments.BankConnectionNoticeVersion), default));

        using (var db = h.NewContext())
            Assert.Equal([otherId], db.RemovedPolpResources.Select(r => r.PolpId).ToList());

        var scripted = new ScriptedPolp();
        await SyncHarness.Processor(h.T, scripted).Process("consents", consentId);
        Assert.Contains(scripted.Calls, c => c is ("consent", var id, _) && id == consentId);
    }
}
