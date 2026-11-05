using System.Security.Cryptography;
using System.Text;
using ControlFinance.API.Controllers;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace ControlFinance.API.Tests;

public class PolpWebhookInboxTests
{
    private static readonly string Token = TestEncryption.RandomSecret();
    private static readonly string SigningSecret = TestEncryption.RandomSecret();
    private static readonly string ConsentId = Guid.NewGuid().ToString("D");
    private static readonly string Body =
        $$"""{"event":"accounts","resource":"consents","resource_id":"{{ConsentId.ToUpperInvariant()}}","query_parameters":"fromCreatedAt=2026-11-01T00:00:00"}""";

    private sealed class ThrowingInbox : IPolpInbox
    {
        public Task<bool> EnqueueAsync(PolpInboxEvent evt, CancellationToken ct) =>
            throw new InvalidOperationException("banco fora");
    }

    private static IConfiguration Config(bool? requireSignature = null, string? signingSecret = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Polp:WebhookToken"] = Token,
            ["Polp:WebhookSigningSecret"] = signingSecret ?? SigningSecret,
        };
        if (requireSignature.HasValue)
            settings["Polp:WebhookRequireSignature"] = requireSignature.Value ? "true" : "false";
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static string Sign(string body, string secret) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private static PolpWebhookController Controller(IConfiguration config, IPolpInbox inbox, string body, string? signature)
    {
        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        if (signature is not null) http.Request.Headers[PolpWebhookController.SignatureHeader] = signature;
        return new PolpWebhookController(config, inbox, new ListLogger<PolpWebhookController>())
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private static Task<IActionResult> Receive(InboxHarness h, string token = "", string? body = null, string? signature = "", IConfiguration? config = null)
    {
        body ??= Body;
        if (signature == "") signature = Sign(body, SigningSecret);
        return Controller(config ?? Config(), h.Inbox, body, signature).Receive(token == "" ? Token : token, default);
    }

    private static int Status(IActionResult result) => result switch
    {
        IStatusCodeActionResult s => s.StatusCode ?? 200,
        _ => throw new InvalidOperationException()
    };

    [Fact]
    public async Task Token_invalido_404_sem_gravar()
    {
        using var h = new InboxHarness();
        Assert.Equal(404, Status(await Receive(h, token: TestEncryption.RandomSecret())));
        Assert.Empty(h.Rows());
    }

    [Fact]
    public async Task Assinatura_ausente_ou_errada_401_com_RequireSignature()
    {
        using var h = new InboxHarness();
        Assert.Equal(401, Status(await Receive(h, signature: null, config: Config(requireSignature: true))));
        Assert.Equal(401, Status(await Receive(h, signature: Sign(Body, TestEncryption.RandomSecret()))));
        Assert.Equal(401, Status(await Receive(h, signature: Sign(Body + " ", SigningSecret))));
        Assert.Empty(h.Rows());
    }

    [Fact]
    public async Task Assinatura_obrigatoria_por_padrao()
    {
        using var h = new InboxHarness();
        Assert.Equal(401, Status(await Receive(h, signature: null)));
        Assert.Equal(503, Status(await Receive(h, signature: null, config: Config(signingSecret: ""))));
    }

    [Fact]
    public async Task Sem_RequireSignature_aceita_sem_assinatura_e_ainda_exige_token()
    {
        using var h = new InboxHarness();
        var config = Config(requireSignature: false, signingSecret: "");

        Assert.Equal(404, Status(await Receive(h, token: TestEncryption.RandomSecret(), signature: null, config: config)));
        Assert.Empty(h.Rows());

        Assert.Equal(200, Status(await Receive(h, signature: null, config: config)));
        Assert.Single(h.Rows());
    }

    [Theory]
    [InlineData("nao e json")]
    [InlineData("""{"event":"accounts","resource_id":"nao-e-guid"}""")]
    [InlineData("""{"event":"accounts","resource_id":123}""")]
    [InlineData("null")]
    public async Task Corpo_invalido_400(string body)
    {
        using var h = new InboxHarness();
        Assert.Equal(400, Status(await Receive(h, body: body)));
        Assert.Empty(h.Rows());
    }

    [Fact]
    public async Task Campo_maior_que_o_limite_400()
    {
        using var h = new InboxHarness();
        var longQuery = new string('a', PolpWebhookEvent.QueryParametersMaxLength + 1);
        var body = $$"""{"event":"accounts","resource":"consents","resource_id":"{{ConsentId}}","query_parameters":"{{longQuery}}"}""";
        Assert.Equal(400, Status(await Receive(h, body: body)));
        var longEvent = new string('e', PolpWebhookEvent.EventMaxLength + 1);
        Assert.Equal(400, Status(await Receive(h, body: $$"""{"event":"{{longEvent}}"}""")));
        Assert.Empty(h.Rows());
    }

    [Fact]
    public async Task Evento_valido_grava_uma_linha_Pending_e_nao_processa_no_request()
    {
        using var h = new InboxHarness();

        Assert.Equal(200, Status(await Receive(h)));

        var row = h.Single();
        Assert.Equal(PolpWebhookEventStatus.Pending, row.Status);
        Assert.Equal("accounts", row.Event);
        Assert.Equal("consents", row.Resource);
        Assert.Equal(ConsentId, row.ResourceId);
        Assert.Equal("fromCreatedAt=2026-11-01T00:00:00", row.QueryParameters);
        await Task.Delay(50);
        Assert.Empty(h.Handler.Handled);
    }

    [Fact]
    public async Task Duplicata_pendente_responde_200_sem_segunda_linha_e_atualiza_ReceivedAt()
    {
        using var h = new InboxHarness();
        Assert.Equal(200, Status(await Receive(h)));

        h.Now = InboxHarness.Start.AddSeconds(10);
        var sameResourceLowercase = Body.Replace(ConsentId.ToUpperInvariant(), ConsentId);
        Assert.Equal(200, Status(await Receive(h, body: sameResourceLowercase)));

        var row = h.Single();
        Assert.Equal(InboxHarness.Start.AddSeconds(10), row.ReceivedAt);
    }

    [Fact]
    public async Task Falha_ao_gravar_responde_500()
    {
        using var h = new InboxHarness();
        var result = await Controller(Config(), new ThrowingInbox(), Body, Sign(Body, SigningSecret)).Receive(Token, default);
        Assert.Equal(500, Status(result));
    }

    [Fact]
    public async Task Aviso_sem_event_responde_200_sem_gravar()
    {
        using var h = new InboxHarness();
        Assert.Equal(200, Status(await Receive(h, body: """{"resource":"consents"}""")));
        Assert.Empty(h.Rows());
    }

    [Fact]
    public async Task Limpeza_apaga_Done_com_mais_de_30_dias_e_Dead_com_mais_de_90()
    {
        using var h = new InboxHarness();
        var now = InboxHarness.Start;
        PolpWebhookEvent Row(PolpWebhookEventStatus status, int daysAgo) => new()
        {
            Event = "accounts", DedupKey = Guid.NewGuid().ToString("N"), Status = status,
            NextAttemptAt = now.AddDays(-daysAgo), ReceivedAt = now.AddDays(-daysAgo),
            ProcessedAt = status is PolpWebhookEventStatus.Done or PolpWebhookEventStatus.Dead ? now.AddDays(-daysAgo) : null
        };
        var oldDone = Row(PolpWebhookEventStatus.Done, 31);
        var recentDone = Row(PolpWebhookEventStatus.Done, 29);
        var oldDead = Row(PolpWebhookEventStatus.Dead, 91);
        var recentDead = Row(PolpWebhookEventStatus.Dead, 40);
        var oldPending = Row(PolpWebhookEventStatus.Pending, 100);
        var oldProcessing = Row(PolpWebhookEventStatus.Processing, 100);
        foreach (var r in new[] { oldDone, recentDone, oldDead, recentDead, oldPending, oldProcessing }) h.Insert(r);

        var jobLock = new FakeJobLock();
        using var db = h.NewContext();
        Assert.Equal(2, await PolpInboxCleanupService.RunRoundAsync(jobLock, db, now, default));

        var left = h.Rows().Select(r => r.Id).ToHashSet();
        Assert.Equal(new HashSet<Guid> { recentDone.Id, recentDead.Id, oldPending.Id, oldProcessing.Id }, left);
        Assert.False(jobLock.IsHeld(JobLockKeys.PolpInboxCleanup));
    }

    [Fact]
    public async Task Limpeza_nao_roda_sem_o_lock()
    {
        using var h = new InboxHarness();
        h.Insert(new PolpWebhookEvent
        {
            Event = "accounts", DedupKey = "x", Status = PolpWebhookEventStatus.Done,
            NextAttemptAt = InboxHarness.Start.AddDays(-60), ReceivedAt = InboxHarness.Start.AddDays(-60),
            ProcessedAt = InboxHarness.Start.AddDays(-60)
        });
        var jobLock = new FakeJobLock();
        await using var held = await jobLock.TryAcquireAsync(JobLockKeys.PolpInboxCleanup, default);

        using var db = h.NewContext();
        Assert.Equal(0, await PolpInboxCleanupService.RunRoundAsync(jobLock, db, InboxHarness.Start, default));
        Assert.Single(h.Rows());
    }

    private static IConfiguration SecretsConfig(bool? requireSignature, string signingSecret)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=cf",
            ["JwtSettings:SecretKey"] = TestEncryption.RandomSecret(),
            ["Encryption:Key"] = TestEncryption.RandomSecret(),
            ["Polp:BaseUrl"] = "https://api.polp.com.br/api/v2",
            ["Polp:ClientId"] = "client-id",
            ["Polp:ClientSecret"] = "client-secret",
            ["Polp:WebhookToken"] = TestEncryption.RandomSecret(),
            ["Polp:WebhookSigningSecret"] = signingSecret,
        };
        if (requireSignature.HasValue)
            settings["Polp:WebhookRequireSignature"] = requireSignature.Value ? "true" : "false";
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    [Fact]
    public void ConfigSecrets_exige_signing_secret_so_com_RequireSignature()
    {
        var missingRequired = ConfigSecrets.Validate(SecretsConfig(true, ""), isDevelopment: false);
        Assert.Contains(missingRequired, e => e.Contains("Polp:WebhookSigningSecret"));

        var missingDefault = ConfigSecrets.Validate(SecretsConfig(null, ""), isDevelopment: false);
        Assert.Contains(missingDefault, e => e.Contains("Polp:WebhookSigningSecret"));

        Assert.Empty(ConfigSecrets.Validate(SecretsConfig(false, ""), isDevelopment: false));
        Assert.Empty(ConfigSecrets.Validate(SecretsConfig(true, TestEncryption.RandomSecret()), isDevelopment: false));
    }
}
