using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
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
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlFinance.API.Tests;

// Polp que registra cada revogação pedida e falha só para os ids escolhidos.
public class RevocationPolp : DispatchProxy
{
    public ConcurrentQueue<Guid> Revoked { get; } = new();
    public HashSet<Guid> Failing { get; } = [];
    public PolpConsentDto? Consent { get; set; }
    public Action? DuringCreate { get; set; }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case nameof(IPolpService.DeleteConsentAsync):
                var id = (Guid)args![0]!;
                if (Failing.Contains(id))
                    return Task.FromException(new HttpRequestException("Falha simulada (HTTP 503).", null, HttpStatusCode.ServiceUnavailable));
                Revoked.Enqueue(id);
                return Task.CompletedTask;
            case nameof(IPolpService.CreateConsentAsync):
                DuringCreate?.Invoke();
                Consent!.InstitutionId = ((CreatePolpConsentRequest)args![0]!).InstitutionId;
                return Task.FromResult(Consent);
            case nameof(IPolpService.GetInstitutionsAsync):
                return Task.FromResult(new List<PolpInstitutionDto>());
            default:
                throw new NotSupportedException(method.Name);
        }
    }

    public static (IPolpService Service, RevocationPolp Fake) Create()
    {
        var service = Create<IPolpService, RevocationPolp>();
        return (service, (RevocationPolp)(object)service);
    }
}

public class AccountDeletionRevocationTests
{
    private const string Password = "senha-forte";

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "ControlFinance.API.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static PolpDisconnectService Disconnect(AppDbContext db, IPolpService polp) =>
        new(db, polp, new IntegrationLock(), NullLogger<PolpDisconnectService>.Instance);

    private static AccountController Account(TestDb t, IPolpService polp)
    {
        var controller = t.Controller(db => new AccountController(
            db, Disconnect(db, polp), new SessionValidator(db, new MemoryCache(new MemoryCacheOptions())),
            new RateLimitService(), new ConfigurationBuilder().Build()));
        controller.ControllerContext.HttpContext.RequestServices = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new TestHostEnvironment())
            .BuildServiceProvider();
        return controller;
    }

    private static TestDb WithPassword()
    {
        var t = new TestDb();
        t.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password);
        t.Db.SaveChanges();
        return t;
    }

    private static async Task<string> Delete(TestDb t, IPolpService polp)
    {
        var result = await Account(t, polp).DeleteAccount(new ConfirmPasswordDto { Password = Password }, default);
        return Http.Json(Assert.IsType<OkObjectResult>(result));
    }

    [Fact]
    public async Task Excluir_a_conta_revoga_todos_os_consentimentos_do_usuario_em_qualquer_status_e_nenhum_de_outro()
    {
        using var t = WithPassword();
        var other = OtherUser.Add(t);
        var consents = new[] { "AUTHORISED", "AWAITING_AUTHORIZATION", "EXPIRED", PolpDisconnectService.PendingStatus }
            .Select(status => SyncHarness.AddIntegration(t, status).PolpIntegrationId).ToHashSet();
        var (polp, fake) = RevocationPolp.Create();

        var json = await Delete(t, polp);

        Assert.Equal(consents, fake.Revoked.ToHashSet());
        Assert.DoesNotContain(other.Integration.PolpIntegrationId, fake.Revoked);
        Assert.Contains("\"requested\"", json);
        await using var db = t.NewContext();
        Assert.Empty(db.PendingProviderDeletions);
        Assert.False(await db.Users.AnyAsync(u => u.Id == t.User.Id));
        Assert.True(await db.PolpIntegrations.AnyAsync(p => p.Id == other.Integration.Id && p.Status == "AUTHORISED"));
    }

    [Fact]
    public async Task Revogacao_sem_confirmacao_fica_na_fila_e_so_sai_quando_a_Polp_confirma()
    {
        using var t = WithPassword();
        var confirmed = SyncHarness.AddIntegration(t).PolpIntegrationId;
        var unconfirmed = SyncHarness.AddIntegration(t).PolpIntegrationId;
        var (polp, fake) = RevocationPolp.Create();
        fake.Failing.Add(unconfirmed);

        var json = await Delete(t, polp);

        Assert.Contains("\"pending\"", json);
        Assert.Equal([confirmed], fake.Revoked.ToArray());
        await using (var db = t.NewContext())
        {
            Assert.False(await db.Users.AnyAsync(u => u.Id == t.User.Id));
            Assert.Equal([unconfirmed], await db.PendingProviderDeletions.Select(x => x.PolpIntegrationId).ToListAsync());
        }

        // Polp ainda fora: o pedido continua na fila, com a tentativa registrada.
        await using (var db = t.NewContext())
            await Disconnect(db, polp).RetryPendingAsync(default);
        await using (var db = t.NewContext())
        {
            var item = await db.PendingProviderDeletions.SingleAsync();
            Assert.NotNull(item.LastAttemptAt);
        }

        // Polp confirmou: sai da fila.
        fake.Failing.Clear();
        await using (var db = t.NewContext())
            await Disconnect(db, polp).RetryPendingAsync(default);
        await using (var db = t.NewContext())
            Assert.Empty(db.PendingProviderDeletions);
        Assert.Contains(unconfirmed, fake.Revoked);
    }

    [Fact]
    public async Task Integracao_gravada_depois_do_pedido_de_revogacao_tambem_entra_na_fila()
    {
        using var t = WithPassword();
        var first = SyncHarness.AddIntegration(t).PolpIntegrationId;
        var (polp, fake) = RevocationPolp.Create();

        await using var db = t.NewContext();
        var service = Disconnect(db, polp);
        var revocation = await service.RequestProviderDeletionForAccountAsync(t.User.Id, default);
        // Connect concorrente grava uma integração nova entre a revogação e a transação da exclusão.
        var late = SyncHarness.AddIntegration(t).PolpIntegrationId;

        var pending = await service.QueueUnconfirmedProviderDeletionsAsync(t.User.Id, revocation.Confirmed, default);

        Assert.Equal(1, pending);
        Assert.Equal([first], fake.Revoked.ToArray());
        Assert.Equal([late], await db.PendingProviderDeletions.Select(x => x.PolpIntegrationId).ToListAsync());
    }

    [Fact]
    public async Task Consentimento_criado_para_conta_excluida_durante_a_conexao_entra_na_fila_de_revogacao()
    {
        const string Cpf = "52998224725";
        using var t = new TestDb();
        t.User.DocumentHash = t.Encryption.ComputeLookupHash(Cpf);
        t.Db.SaveChanges();
        var consentId = Guid.NewGuid();
        var (polp, fake) = RevocationPolp.Create();
        fake.Consent = new PolpConsentDto { Id = consentId, Status = "AWAITING_AUTHORIZATION", ClientUserId = t.User.Id.ToString() };
        // A exclusão da conta termina enquanto a Polp cria o consentimento.
        fake.DuringCreate = () =>
        {
            using var other = t.NewContext();
            other.Users.Where(u => u.Id == t.User.Id).ExecuteDelete();
        };
        var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = t.Controller(db => new PolpController(
            db, polp, new InstitutionNameResolver(polp, cache, NullLogger<InstitutionNameResolver>.Instance),
            t.Encryption, Disconnect(db, polp), new RecordingInbox(), cache));

        var result = await controller.Connect(
            new PolpConnectDto(Guid.NewGuid().ToString(), Cpf, LegalDocuments.BankConnectionNoticeVersion), default);

        Assert.Equal(401, Http.Status(result));
        await using var check = t.NewContext();
        Assert.Equal([consentId], await check.PendingProviderDeletions.Select(x => x.PolpIntegrationId).ToListAsync());
        Assert.True(await RemovedPolpResources.IsRemovedAsync(check, consentId, default));
        Assert.Empty(check.PolpIntegrations);
    }

    [Fact]
    public async Task Fila_nao_duplica_o_mesmo_consentimento()
    {
        using var t = new TestDb();
        var consentId = Guid.NewGuid();
        var (polp, _) = RevocationPolp.Create();

        await using var db = t.NewContext();
        await Disconnect(db, polp).QueueOrphanConsentAsync(consentId, default);
        await Disconnect(db, polp).QueueOrphanConsentAsync(consentId, default);

        Assert.Single(db.PendingProviderDeletions);
    }
}
