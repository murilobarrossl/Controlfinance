using System.Net;
using System.Text.Json;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ControlFinance.API.Tests;

public class PolpInboxWorkerTests
{
    private static readonly PolpInboxEvent Accounts =
        new("accounts", "consents", Guid.NewGuid().ToString("D"), "fromCreatedAt=2026-11-01T00:00:00");

    private static readonly TimeSpan[] ExpectedDelays =
    [
        TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromHours(1),
        TimeSpan.FromHours(6), TimeSpan.FromHours(6), TimeSpan.FromHours(6),
    ];

    [Fact]
    public async Task Enqueue_grava_uma_linha_pendente_com_dedup_key_sha256()
    {
        using var h = new InboxHarness();

        Assert.True(await h.Inbox.EnqueueAsync(Accounts, default));

        var row = h.Single();
        Assert.Equal(PolpWebhookEventStatus.Pending, row.Status);
        Assert.Equal(Accounts.Event, row.Event);
        Assert.Equal(Accounts.ResourceId, row.ResourceId);
        Assert.Equal(Accounts.QueryParameters, row.QueryParameters);
        Assert.Equal(0, row.Attempts);
        Assert.Equal(InboxHarness.Start, row.NextAttemptAt);
        Assert.Equal(InboxHarness.Start, row.ReceivedAt);
        Assert.Matches("^[0-9a-f]{64}$", row.DedupKey);
        Assert.Equal(PolpInbox.DedupKey(Accounts), row.DedupKey);
        Assert.NotEqual(PolpInbox.DedupKey(Accounts with { QueryParameters = null }), row.DedupKey);
    }

    [Fact]
    public async Task Enqueue_duplicado_pendente_nao_cria_linha_e_atualiza_ReceivedAt()
    {
        using var h = new InboxHarness();
        await h.Inbox.EnqueueAsync(Accounts, default);

        h.Now = InboxHarness.Start.AddSeconds(3);
        Assert.False(await h.Inbox.EnqueueAsync(Accounts, default));

        var row = h.Single();
        Assert.Equal(InboxHarness.Start.AddSeconds(3), row.ReceivedAt);
        Assert.Equal(InboxHarness.Start, row.NextAttemptAt);
    }

    [Fact]
    public async Task Enqueue_igual_a_um_ja_concluido_cria_linha_nova()
    {
        using var h = new InboxHarness();
        await h.Inbox.EnqueueAsync(Accounts, default);
        Assert.True(await h.Worker.ProcessNextAsync(default));

        h.Now = InboxHarness.Start.AddMinutes(1);
        Assert.True(await h.Inbox.EnqueueAsync(Accounts, default));

        var rows = h.Rows();
        Assert.Equal(2, rows.Count);
        Assert.Equal(PolpWebhookEventStatus.Done, rows[0].Status);
        Assert.Equal(PolpWebhookEventStatus.Pending, rows[1].Status);
    }

    [Fact]
    public async Task Sucesso_marca_Done_com_ProcessedAt()
    {
        using var h = new InboxHarness();
        await h.Inbox.EnqueueAsync(Accounts, default);
        h.Now = InboxHarness.Start.AddSeconds(1);

        Assert.True(await h.Worker.ProcessNextAsync(default));
        Assert.False(await h.Worker.ProcessNextAsync(default));

        Assert.Equal(Accounts, Assert.Single(h.Handler.Handled));
        var row = h.Single();
        Assert.Equal(PolpWebhookEventStatus.Done, row.Status);
        Assert.Equal(InboxHarness.Start.AddSeconds(1), row.ProcessedAt);
        Assert.Null(row.LockedUntil);
        Assert.Null(row.LastError);
    }

    [Fact]
    public async Task Evento_fora_de_escopo_vira_Done_pelo_processor()
    {
        using var h = new InboxHarness();
        var loans = new PolpInboxEvent("loans", "consents", Guid.NewGuid().ToString("D"), null);
        await h.Inbox.EnqueueAsync(loans, default);

        Assert.True(await h.Worker.ProcessNextAsync(default));

        Assert.Equal(loans, Assert.Single(h.Handler.Handled));
        Assert.Equal(PolpWebhookEventStatus.Done, h.Single().Status);
    }

    [Fact]
    public async Task Evento_com_NextAttemptAt_no_futuro_nao_e_pego()
    {
        using var h = new InboxHarness();
        await h.Inbox.EnqueueAsync(Accounts, default);
        h.Now = InboxHarness.Start.AddSeconds(-1);

        Assert.False(await h.Worker.ProcessNextAsync(default));
        Assert.Empty(h.Handler.Handled);
    }

    public static TheoryData<string> Transient => new() { "500", "429", "timeout", "notready", "json", "db" };

    private static Exception Make(string kind) => kind switch
    {
        "500" => new HttpRequestException("Falha ao listar contas na Polp (HTTP 500).", null, HttpStatusCode.InternalServerError),
        "429" => new HttpRequestException("Falha ao listar contas na Polp (HTTP 429).", null, HttpStatusCode.TooManyRequests),
        "timeout" => new TaskCanceledException("timeout"),
        "notready" => new PolpResourceNotReadyException("conta ainda não sincronizada"),
        "json" => new JsonException("valor 'R$ 1.234,56' de Fulano inválido"),
        "db" => new DbUpdateException("violação com valor sensível"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [Theory]
    [MemberData(nameof(Transient))]
    public async Task Falha_transitoria_volta_para_Pending_com_30s(string kind)
    {
        using var h = new InboxHarness();
        await h.Inbox.EnqueueAsync(Accounts, default);
        h.Handler.OnHandle = (_, _) => throw Make(kind);

        Assert.True(await h.Worker.ProcessNextAsync(default));

        var row = h.Single();
        Assert.Equal(PolpWebhookEventStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(InboxHarness.Start.AddSeconds(30), row.NextAttemptAt);
        Assert.Null(row.LockedUntil);
        Assert.NotNull(row.LastError);
        Assert.DoesNotContain("R$", row.LastError);
        Assert.DoesNotContain("Fulano", row.LastError);
        Assert.DoesNotContain("sensível", row.LastError);
    }

    [Fact]
    public async Task LastError_tem_tipo_e_status_http()
    {
        using var h = new InboxHarness();
        await h.Inbox.EnqueueAsync(Accounts, default);
        h.Handler.OnHandle = (_, _) => throw Make("429");

        await h.Worker.ProcessNextAsync(default);

        Assert.Equal("HttpRequestException HTTP 429", h.Single().LastError);
        Assert.Equal("JsonException", PolpInboxWorker.Describe(Make("json")));
        Assert.Equal(PolpInboxWorker.MaxErrorLength,
            PolpInboxWorker.Describe(new PolpPermanentException(new string('x', 2000))).Length);
    }

    [Fact]
    public async Task Retentativas_seguem_a_escala_e_a_oitava_falha_vira_Dead_com_LogError()
    {
        using var h = new InboxHarness();
        await h.Inbox.EnqueueAsync(Accounts, default);
        h.Handler.OnHandle = (_, _) => throw Make("500");

        for (var attempt = 1; attempt <= 7; attempt++)
        {
            var before = h.Now;
            Assert.True(await h.Worker.ProcessNextAsync(default));

            var row = h.Single();
            Assert.Equal(PolpWebhookEventStatus.Pending, row.Status);
            Assert.Equal(attempt, row.Attempts);
            Assert.Equal(before + ExpectedDelays[attempt - 1], row.NextAttemptAt);

            h.Now = before + ExpectedDelays[attempt - 1] - TimeSpan.FromSeconds(1);
            Assert.False(await h.Worker.ProcessNextAsync(default));
            h.Now = row.NextAttemptAt;
        }

        Assert.DoesNotContain(h.WorkerLog.Entries, e => e.Level == LogLevel.Error);
        Assert.True(await h.Worker.ProcessNextAsync(default));

        var dead = h.Single();
        Assert.Equal(PolpWebhookEventStatus.Dead, dead.Status);
        Assert.Equal(8, dead.Attempts);
        Assert.Equal(h.Now, dead.ProcessedAt);
        Assert.Null(dead.LockedUntil);
        Assert.Single(h.WorkerLog.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal(8, h.Handler.Handled.Count);
        Assert.False(await h.Worker.ProcessNextAsync(default));
    }

    public static TheoryData<string> Permanent => new() { "404", "permanent" };

    [Theory]
    [MemberData(nameof(Permanent))]
    public async Task Erro_permanente_vira_Dead_na_hora(string kind)
    {
        using var h = new InboxHarness();
        await h.Inbox.EnqueueAsync(Accounts, default);
        h.Handler.OnHandle = (_, _) => throw (kind == "404"
            ? new HttpRequestException("Falha ao listar contas na Polp (HTTP 404).", null, HttpStatusCode.NotFound)
            : new PolpPermanentException("data inválida na transação"));

        Assert.True(await h.Worker.ProcessNextAsync(default));

        var row = h.Single();
        Assert.Equal(PolpWebhookEventStatus.Dead, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Single(h.WorkerLog.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Lease_vencido_e_retomado()
    {
        using var h = new InboxHarness();
        h.Insert(new PolpWebhookEvent
        {
            Event = Accounts.Event, Resource = Accounts.Resource, ResourceId = Accounts.ResourceId,
            DedupKey = PolpInbox.DedupKey(Accounts), Status = PolpWebhookEventStatus.Processing,
            Attempts = 2, NextAttemptAt = InboxHarness.Start.AddMinutes(-20),
            ReceivedAt = InboxHarness.Start.AddMinutes(-20), LockedUntil = InboxHarness.Start.AddSeconds(5)
        });

        Assert.False(await h.Worker.ProcessNextAsync(default));

        h.Now = InboxHarness.Start.AddSeconds(6);
        Assert.True(await h.Worker.ProcessNextAsync(default));

        Assert.Single(h.Handler.Handled);
        var row = h.Single();
        Assert.Equal(PolpWebhookEventStatus.Done, row.Status);
        Assert.Equal(3, row.Attempts);
    }

    [Fact]
    public async Task Lease_vencido_retomado_conta_tentativa_e_falha_seguinte_usa_a_escala()
    {
        using var h = new InboxHarness();
        h.Insert(ExpiredLease(attempts: 0));
        h.Handler.OnHandle = (_, _) => throw Make("500");

        Assert.True(await h.Worker.ProcessNextAsync(default));

        var row = h.Single();
        Assert.Equal(PolpWebhookEventStatus.Pending, row.Status);
        Assert.Equal(2, row.Attempts);
        Assert.Equal(InboxHarness.Start + ExpectedDelays[1], row.NextAttemptAt);
    }

    [Fact]
    public async Task Lease_vencido_retomado_marca_LastError_e_sucesso_limpa()
    {
        using var h = new InboxHarness();
        h.Insert(ExpiredLease(attempts: 0));
        string? errorDuringProcessing = null;
        h.Handler.OnHandle = (_, _) =>
        {
            errorDuringProcessing = h.Single().LastError;
            Assert.Equal(1, h.Single().Attempts);
            Assert.Equal(PolpWebhookEventStatus.Processing, h.Single().Status);
            return Task.CompletedTask;
        };

        Assert.True(await h.Worker.ProcessNextAsync(default));

        Assert.Equal(PolpInboxStore.LeaseExpiredError, errorDuringProcessing);
        Assert.Null(h.Single().LastError);
    }

    [Fact]
    public async Task Oitavo_vencimento_de_lease_vira_Dead_com_LogError_sem_processar()
    {
        using var h = new InboxHarness();
        h.Insert(ExpiredLease(attempts: PolpInboxWorker.MaxAttempts - 1));

        Assert.True(await h.Worker.ProcessNextAsync(default));

        Assert.Empty(h.Handler.Handled);
        var row = h.Single();
        Assert.Equal(PolpWebhookEventStatus.Dead, row.Status);
        Assert.Equal(PolpInboxWorker.MaxAttempts, row.Attempts);
        Assert.Equal(InboxHarness.Start, row.ProcessedAt);
        Assert.Null(row.LockedUntil);
        Assert.Equal(PolpInboxStore.LeaseExpiredError, row.LastError);
        Assert.Single(h.WorkerLog.Entries, e => e.Level == LogLevel.Error);
        Assert.False(await h.Worker.ProcessNextAsync(default));
    }

    [Fact]
    public async Task Vencimentos_sucessivos_de_lease_chegam_a_Dead_na_oitava_vez()
    {
        using var h = new InboxHarness();
        await h.Inbox.EnqueueAsync(Accounts, default);
        // Simula o processo caindo no meio: o lease nunca é finalizado.
        for (var expiry = 1; expiry < PolpInboxWorker.MaxAttempts; expiry++)
        {
            using (var db = h.NewContext())
            {
                var claimed = Assert.Single(await PolpInboxStore.ClaimAsync(db, h.Now, PolpInboxWorker.Lease, 1, PolpInboxWorker.MaxAttempts, default));
                Assert.False(claimed.Dead);
                Assert.Equal(expiry - 1, claimed.Attempts);
            }
            h.Now += PolpInboxWorker.Lease + TimeSpan.FromSeconds(1);
        }

        using (var db = h.NewContext())
        {
            var last = Assert.Single(await PolpInboxStore.ClaimAsync(db, h.Now, PolpInboxWorker.Lease, 1, PolpInboxWorker.MaxAttempts, default));
            Assert.Equal(PolpInboxWorker.MaxAttempts - 1, last.Attempts);
            Assert.False(last.Dead);
        }
        h.Now += PolpInboxWorker.Lease + TimeSpan.FromSeconds(1);

        Assert.True(await h.Worker.ProcessNextAsync(default));
        Assert.Empty(h.Handler.Handled);
        var row = h.Single();
        Assert.Equal(PolpWebhookEventStatus.Dead, row.Status);
        Assert.Equal(PolpInboxWorker.MaxAttempts, row.Attempts);
    }

    private static PolpWebhookEvent ExpiredLease(int attempts) => new()
    {
        Event = Accounts.Event, Resource = Accounts.Resource, ResourceId = Accounts.ResourceId,
        QueryParameters = Accounts.QueryParameters,
        DedupKey = PolpInbox.DedupKey(Accounts), Status = PolpWebhookEventStatus.Processing,
        Attempts = attempts, NextAttemptAt = InboxHarness.Start.AddMinutes(-20),
        ReceivedAt = InboxHarness.Start.AddMinutes(-20), LockedUntil = InboxHarness.Start.AddSeconds(-1)
    };

    [Fact]
    public async Task Aviso_igual_durante_o_processamento_volta_para_Pending_em_vez_de_Done()
    {
        using var h = new InboxHarness();
        await h.Inbox.EnqueueAsync(Accounts, default);
        var processing = true;
        h.Handler.OnHandle = async (evt, ct) =>
        {
            if (!processing) return;
            processing = false;
            h.Now = InboxHarness.Start.AddSeconds(2);
            Assert.False(await h.Inbox.EnqueueAsync(evt, ct));
            h.Now = InboxHarness.Start.AddSeconds(4);
        };

        Assert.True(await h.Worker.ProcessNextAsync(default));

        var row = h.Single();
        Assert.Equal(PolpWebhookEventStatus.Pending, row.Status);
        Assert.Equal(0, row.Attempts);
        Assert.Equal(InboxHarness.Start.AddSeconds(4), row.NextAttemptAt);
        Assert.Null(row.LockedUntil);

        Assert.True(await h.Worker.ProcessNextAsync(default));
        Assert.Equal(PolpWebhookEventStatus.Done, h.Single().Status);
        Assert.Equal(2, h.Handler.Handled.Count);
    }

    [Fact]
    public async Task Sem_posse_do_lease_nao_finaliza()
    {
        using var h = new InboxHarness();
        await h.Inbox.EnqueueAsync(Accounts, default);
        var otherLease = InboxHarness.Start.AddMinutes(30);
        h.Handler.OnHandle = async (_, ct) =>
        {
            await using var db = h.NewContext();
            await db.PolpWebhookEvents.ExecuteUpdateAsync(s => s.SetProperty(e => e.LockedUntil, otherLease), ct);
        };

        Assert.True(await h.Worker.ProcessNextAsync(default));

        var row = h.Single();
        Assert.Equal(PolpWebhookEventStatus.Processing, row.Status);
        Assert.Equal(otherLease, row.LockedUntil);
        Assert.Contains(h.WorkerLog.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("lease perdido"));
    }

    [Fact]
    public async Task Desligamento_devolve_o_evento_sem_contar_tentativa()
    {
        using var h = new InboxHarness();
        await h.Inbox.EnqueueAsync(Accounts, default);
        using var stopping = new CancellationTokenSource();
        h.Handler.OnHandle = async (_, ct) =>
        {
            await stopping.CancelAsync();
            ct.ThrowIfCancellationRequested();
        };

        Assert.True(await h.Worker.ProcessNextAsync(stopping.Token));

        var row = h.Single();
        Assert.Equal(PolpWebhookEventStatus.Pending, row.Status);
        Assert.Equal(0, row.Attempts);
        Assert.Null(row.LockedUntil);
        Assert.Null(row.LastError);
    }

    [Fact]
    public async Task Eventos_sao_processados_em_ordem_de_NextAttemptAt_um_por_vez()
    {
        using var h = new InboxHarness();
        var first = Accounts with { ResourceId = Guid.NewGuid().ToString("D") };
        await h.Inbox.EnqueueAsync(first, default);
        h.Now = InboxHarness.Start.AddSeconds(1);
        await h.Inbox.EnqueueAsync(Accounts, default);

        Assert.True(await h.Worker.ProcessNextAsync(default));
        Assert.Equal(first, Assert.Single(h.Handler.Handled));
        Assert.True(await h.Worker.ProcessNextAsync(default));
        Assert.Equal(2, h.Handler.Handled.Count);
    }

    [Fact]
    public async Task Alta_prioridade_e_processada_antes_de_qualquer_baixa_mesmo_mais_antiga()
    {
        using var h = new InboxHarness();
        var lows = Enumerable.Range(0, 3)
            .Select(_ => new PolpInboxEvent(PolpInternalEvents.Reconcile, "consents", Guid.NewGuid().ToString("D"), null, PolpInboxPriority.Low))
            .ToList();
        foreach (var low in lows) await h.Inbox.EnqueueAsync(low, default);
        h.Now = InboxHarness.Start.AddMinutes(1);
        var highs = Enumerable.Range(0, 3)
            .Select(_ => Accounts with { ResourceId = Guid.NewGuid().ToString("D") })
            .ToList();
        foreach (var high in highs) await h.Inbox.EnqueueAsync(high, default);

        for (var i = 0; i < 6; i++) Assert.True(await h.Worker.ProcessNextAsync(default));
        Assert.False(await h.Worker.ProcessNextAsync(default));

        var handled = h.Handler.Handled.ToList();
        Assert.Equal(highs, handled.Take(3));
        Assert.Equal(lows, handled.Skip(3));
        Assert.All(h.Rows(), r => Assert.Equal(PolpWebhookEventStatus.Done, r.Status));
    }

    [Fact]
    public async Task Baixa_prioridade_e_pega_quando_nenhuma_alta_esta_pronta()
    {
        using var h = new InboxHarness();
        var low = new PolpInboxEvent(PolpInternalEvents.Reconcile, "consents", Guid.NewGuid().ToString("D"), null, PolpInboxPriority.Low);
        await h.Inbox.EnqueueAsync(low, default);
        h.Insert(new PolpWebhookEvent
        {
            Event = "accounts", ResourceId = Guid.NewGuid().ToString("D"), DedupKey = "futuro",
            Priority = PolpInboxPriority.High, NextAttemptAt = InboxHarness.Start.AddMinutes(10), ReceivedAt = InboxHarness.Start
        });

        Assert.True(await h.Worker.ProcessNextAsync(default));
        Assert.Equal(low, Assert.Single(h.Handler.Handled));
    }

    [Fact]
    public async Task Dedupe_sobe_a_prioridade_e_nunca_rebaixa()
    {
        using var h = new InboxHarness();
        var low = new PolpInboxEvent(PolpInternalEvents.Backfill, "consents", Guid.NewGuid().ToString("D"), null, PolpInboxPriority.Low);
        var high = low with { Priority = PolpInboxPriority.High };
        Assert.Equal(PolpInbox.DedupKey(low), PolpInbox.DedupKey(high));

        Assert.True(await h.Inbox.EnqueueAsync(low, default));
        Assert.Equal(PolpInboxPriority.Low, h.Single().Priority);

        h.Now = InboxHarness.Start.AddSeconds(1);
        Assert.False(await h.Inbox.EnqueueAsync(high, default));
        Assert.Equal(PolpInboxPriority.High, h.Single().Priority);

        h.Now = InboxHarness.Start.AddSeconds(2);
        Assert.False(await h.Inbox.EnqueueAsync(low, default));
        var row = h.Single();
        Assert.Equal(PolpInboxPriority.High, row.Priority);
        Assert.Equal(InboxHarness.Start.AddSeconds(2), row.ReceivedAt);
    }

    [Fact]
    public async Task Dedupe_sobe_a_prioridade_de_evento_em_processamento()
    {
        using var h = new InboxHarness();
        var low = new PolpInboxEvent(PolpInternalEvents.Reconcile, "consents", Guid.NewGuid().ToString("D"), null, PolpInboxPriority.Low);
        await h.Inbox.EnqueueAsync(low, default);
        h.Handler.OnHandle = async (_, ct) =>
        {
            h.Now = InboxHarness.Start.AddSeconds(5);
            await h.Inbox.EnqueueAsync(low with { Priority = PolpInboxPriority.High }, ct);
        };

        Assert.True(await h.Worker.ProcessNextAsync(default));

        var row = h.Single();
        Assert.Equal(PolpWebhookEventStatus.Pending, row.Status);
        Assert.Equal(PolpInboxPriority.High, row.Priority);
    }

    [Fact]
    public void Claim_no_Postgres_usa_SKIP_LOCKED_e_no_SQLite_nao()
    {
        var npgsqlOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=naoconecta").Options;
        using var pg = new AppDbContext(npgsqlOptions, TestEncryption.Create());
        using var t = new TestDb();

        Assert.True(PolpInboxStore.UsesSkipLocked(pg));
        Assert.False(PolpInboxStore.UsesSkipLocked(t.Db));

        var sql = pg.PolpWebhookEvents
            .FromSql(PolpInboxStore.PostgresClaimSql(InboxHarness.Start, InboxHarness.Start.AddMinutes(5), 1, PolpInboxWorker.MaxAttempts))
            .AsNoTracking()
            .ToQueryString();

        Assert.Contains("FOR UPDATE SKIP LOCKED", sql);
        Assert.Contains("RETURNING e.*", sql);
        Assert.Contains("UPDATE \"PolpWebhookEvents\" AS e SET", sql);
        Assert.Matches(@"""Attempts"" = CASE WHEN e\.""Status"" = 'Processing' THEN e\.""Attempts"" \+ 1 ELSE e\.""Attempts"" END", sql);
        Assert.Matches(@"""Status"" = CASE WHEN e\.""Status"" = 'Processing' AND e\.""Attempts"" \+ 1 >= @p\d+ THEN 'Dead' ELSE 'Processing' END", sql);
        Assert.Matches(@"\(""Status"" = 'Pending' AND ""NextAttemptAt"" <= @p\d+\)", sql);
        Assert.Matches(@"\(""Status"" = 'Processing' AND ""LockedUntil"" < @p\d+\)", sql);
        Assert.Matches(@"LIMIT @p\d+", sql);
        Assert.Contains("ORDER BY \"Priority\", \"NextAttemptAt\"", sql);
        // Composição (SELECT por fora do UPDATE) quebraria o RETURNING no Postgres.
        Assert.DoesNotContain(") AS ", sql);
    }
}
