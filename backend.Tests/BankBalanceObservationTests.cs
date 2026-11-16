using System.Reflection;
using System.Text.Json;
using ControlFinance.API.Controllers;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Results = ControlFinance.API.Tests.Support.Results;

namespace ControlFinance.API.Tests;

public class BankBalanceObservationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-16T18:00:00Z");
    private static readonly IConfiguration Config = new ConfigurationBuilder().Build();

    [Fact]
    public void Migracao_adiciona_somente_a_data_e_a_tabela_de_observacoes_sem_backfill_inventado()
    {
        using var db = new ControlFinance.API.Data.AppDbContext(
            new DbContextOptionsBuilder<ControlFinance.API.Data.AppDbContext>().UseNpgsql("Host=127.0.0.1;Database=design_only").Options,
            TestEncryption.Create());
        Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript("20261116171916_RemoveSupplierStatus", "20261116201457_AddBankBalanceObservations");
        Assert.Contains("BankBalanceObservations", sql);
        Assert.Contains("BalanceUpdatedAt", sql);
        Assert.DoesNotContain("UPDATE \"BankAccounts\"", sql);
        Assert.DoesNotContain("DELETE FROM", sql);
    }

    private static (PolpIntegration Integration, BankAccount Account, PolpAccountDto Remote) Setup(TestDb t)
    {
        t.User.DocumentType = DocumentTypes.Cnpj;
        t.Clock.UtcNow = Now;
        t.Db.SaveChanges();
        var integration = SyncHarness.AddIntegration(t, synced: true);
        var remote = ScriptedPolp.Account();
        remote.ConsentId = integration.PolpIntegrationId;
        remote.Balance!.UpdateDateTime = "2026-11-15T14:30:00-03:00";
        return (integration, SyncHarness.AddLinked(t, integration, remote.Id, false), remote);
    }

    private static async Task Apply(TestDb t, BankAccount account, PolpAccountDto remote, IConfiguration? config = null)
    {
        await BankBalanceHistory.ApplyAsync(t.Db, account, remote, t.User.DocumentType, config ?? Config, Now, default);
        await t.Db.SaveChangesAsync();
    }

    private static Task<Microsoft.AspNetCore.Mvc.IActionResult> Read(TestDb t, Guid accountId,
        string from = "2026-11-01", string to = "2026-12-01", string bucket = "day") =>
        t.Controller(db => new CashFlowController(db)).BalanceHistory(accountId, from, to, bucket);

    [Fact]
    public void Contrato_desserializa_a_data_do_saldo_sem_usar_updated_at_do_recurso()
    {
        var dto = JsonSerializer.Deserialize<PolpAccountDto>("""
            {"updated_at":"2026-11-16T18:00:00Z","balance":{"update_date_time":"2026-11-15T14:30:00-03:00","available_amount":{"amount":"100.00","currency":"BRL"}}}
            """);
        Assert.Equal(DateTime.Parse("2026-11-15T17:30:00Z").ToUniversalTime(), BankBalanceHistory.BankTimestamp(dto!.Balance, Now));
    }

    [Fact]
    public async Task Reenvio_corrige_o_mesmo_instante_e_resposta_antiga_nao_regride_o_saldo_atual()
    {
        using var t = new TestDb();
        var (_, account, remote) = Setup(t);
        await Apply(t, account, remote);
        remote.Balance!.AvailableAmount = ScriptedPolp.Money("200.00");
        await Apply(t, account, remote);
        Assert.Single(t.Db.BankBalanceObservations);
        Assert.Equal(200m, t.Db.BankBalanceObservations.Single().Balance);
        remote.Balance.UpdateDateTime = "2026-11-14T12:00:00Z";
        remote.Balance.AvailableAmount = ScriptedPolp.Money("50.00");
        await Apply(t, account, remote);
        Assert.Equal(2, t.Db.BankBalanceObservations.Count());
        Assert.Equal(200m, account.Balance);
        Assert.Equal(DateTime.Parse("2026-11-15T17:30:00Z").ToUniversalTime(), account.BalanceUpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2026-11-15T14:00:00")]
    [InlineData("2026-11-15")]
    [InlineData("2026-11-17T00:00:00Z")]
    [InlineData("0001-01-01T00:00:00Z")]
    [InlineData("invalid")]
    public async Task Data_ausente_ambigua_invalida_ou_futura_nao_vira_observacao(string? timestamp)
    {
        using var t = new TestDb();
        var (_, account, remote) = Setup(t);
        remote.Balance!.UpdateDateTime = timestamp;
        await Apply(t, account, remote);
        Assert.Empty(t.Db.BankBalanceObservations);
        Assert.Null(account.BalanceUpdatedAt);
    }

    [Fact]
    public async Task CPF_nao_passa_a_coletar_historico_e_mantem_o_saldo_existente()
    {
        using var t = new TestDb();
        var (_, account, remote) = Setup(t);
        t.User.DocumentType = DocumentTypes.Cpf;
        await Apply(t, account, remote);
        Assert.Equal(100m, account.Balance);
        Assert.Empty(t.Db.BankBalanceObservations);
        Assert.Null(account.BalanceUpdatedAt);
    }

    [Fact]
    public async Task Retencao_e_moeda_impedem_observacoes_fora_da_base_permitida()
    {
        using var t = new TestDb();
        var (_, account, remote) = Setup(t);
        remote.Balance!.UpdateDateTime = "2025-01-01T12:00:00Z";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataRetention:SyncedHistoryMonths"] = "3" }).Build();
        await Apply(t, account, remote, config);
        remote.Balance.UpdateDateTime = "2026-11-15T12:00:00Z";
        remote.Balance.AvailableAmount!.Currency = "USD";
        await Apply(t, account, remote);
        Assert.Empty(t.Db.BankBalanceObservations);
    }

    [Fact]
    public async Task Webhook_e_reconciliacao_preservam_as_observacoes_na_mesma_transacao_do_sync()
    {
        using var t = new TestDb();
        var (integration, _, remote) = Setup(t);
        var provider = new ScriptedPolp();
        provider.Accounts.Add(remote);
        await SyncHarness.Processor(t, provider).Process("accounts", integration.PolpIntegrationId);
        Assert.Single(t.Db.BankBalanceObservations.AsNoTracking());
        remote.Balance!.UpdateDateTime = "2026-11-16T12:00:00Z";
        await SyncHarness.Sync(t, provider).ReconcileAsync(integration, default);
        Assert.Equal(2, t.Db.BankBalanceObservations.AsNoTracking().Count());
    }

    [Fact]
    public async Task API_exibe_observacoes_reais_sem_preencher_lacunas_e_respeita_o_fuso_e_fim_exclusivo()
    {
        using var t = new TestDb();
        var (_, account, remote) = Setup(t);
        t.User.TimeZone = "America/Recife";
        remote.Balance!.UpdateDateTime = "2026-11-01T01:00:00Z"; // 31/out no fuso do titular
        await Apply(t, account, remote);
        remote.Balance.UpdateDateTime = "2026-11-03T10:00:00Z";
        await Apply(t, account, remote);
        remote.Balance.UpdateDateTime = "2026-11-03T20:00:00Z";
        await Apply(t, account, remote);
        remote.Balance.UpdateDateTime = "2026-11-09T12:00:00Z";
        await Apply(t, account, remote);
        var history = Results.OkValue<BalanceHistoryDto>(await Read(t, account.Id, to: "2026-11-09"));
        Assert.True(history.Available);
        var point = Assert.Single(history.Points);
        Assert.Equal(new DateOnly(2026, 11, 3), point.Date);
        Assert.Equal(DateTime.Parse("2026-11-03T20:00:00Z").ToUniversalTime(), point.ObservedAt);
        Assert.Equal(new DateOnly(2026, 10, 31), history.HistoryStart);
        var monthly = Results.OkValue<BalanceHistoryDto>(await Read(t, account.Id, bucket: "month"));
        Assert.Equal(new DateOnly(2026, 11, 9), Assert.Single(monthly.Points).Date);
        var outside = Results.OkValue<BalanceHistoryDto>(await Read(t, account.Id, "2026-10-01", "2026-10-02"));
        Assert.False(outside.Available);
        Assert.Equal("outsideHistory", outside.Reason);
    }

    [Fact]
    public async Task Observacoes_sao_cifradas_e_apagadas_junto_com_a_conta()
    {
        using var t = new TestDb();
        var (_, account, remote) = Setup(t);
        await Apply(t, account, remote);
        using var command = t.Db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT BalanceEncrypted FROM BankBalanceObservations";
        var raw = (string)(await command.ExecuteScalarAsync())!;
        Assert.DoesNotContain("100.00", raw);
        Assert.NotEqual("100", raw);
        await t.Db.BankAccounts.Where(x => x.Id == account.Id).ExecuteDeleteAsync();
        Assert.Empty(t.Db.BankBalanceObservations.AsNoTracking());
    }

    [Fact]
    public async Task Historico_de_outra_empresa_nao_contamina_a_serie_da_conta_selecionada()
    {
        using var t = new TestDb();
        var (_, account, remote) = Setup(t);
        await Apply(t, account, remote);
        var other = OtherUser.Add(t);
        t.Db.BankBalanceObservations.Add(new BankBalanceObservation
        {
            UserId = other.User.Id, BankAccountId = other.Account.Id,
            ObservedAt = DateTime.Parse("2026-11-15T12:00:00Z").ToUniversalTime(), Balance = 999999m
        });
        await t.Db.SaveChangesAsync();
        var history = Results.OkValue<BalanceHistoryDto>(await Read(t, account.Id));
        Assert.Equal(100m, Assert.Single(history.Points).Balance);
        Assert.Equal(404, Http.Status(await Read(t, other.Account.Id)));
    }

    public class BalanceProvider : DispatchProxy
    {
        public PolpAccountDto Remote { get; set; } = null!;
        public Action? OnRead { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name != nameof(IPolpService.SyncBalanceAsync)) throw new NotSupportedException();
            OnRead?.Invoke();
            return Task.FromResult(Remote);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Atualizacao_sob_demanda_de_conta_e_conexao_tambem_preserva_a_data(bool connection)
    {
        using var t = new TestDb();
        var (integration, account, remote) = Setup(t);
        var provider = DispatchProxy.Create<IPolpService, BalanceProvider>();
        ((BalanceProvider)(object)provider).Remote = remote;
        var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = t.Controller(db => new PolpController(db, provider,
            new InstitutionNameResolver(provider, cache, NullLogger<InstitutionNameResolver>.Instance), t.Encryption, null!, new RecordingInbox(), cache));
        var result = connection ? await controller.SyncConnectionBalance(integration.Id, default) : await controller.SyncBalance(account.Id, default);
        Assert.Equal(200, Http.Status(result));
        Assert.Single(t.Db.BankBalanceObservations.AsNoTracking());
        Assert.NotNull((await t.Db.BankAccounts.AsNoTracking().SingleAsync()).BalanceUpdatedAt);
    }

    [Fact]
    public async Task Desconexao_durante_leitura_remota_impede_gravar_saldo_e_observacao()
    {
        using var t = new TestDb();
        var (integration, account, remote) = Setup(t);
        var provider = DispatchProxy.Create<IPolpService, BalanceProvider>();
        var script = (BalanceProvider)(object)provider;
        script.Remote = remote;
        script.OnRead = () =>
        {
            integration.Status = PolpDisconnectService.PendingStatus;
            t.Db.SaveChanges();
        };
        var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = t.Controller(db => new PolpController(db, provider,
            new InstitutionNameResolver(provider, cache, NullLogger<InstitutionNameResolver>.Instance), t.Encryption, null!, new RecordingInbox(), cache));
        Assert.Equal(404, Http.Status(await controller.SyncBalance(account.Id, default)));
        Assert.Empty(t.Db.BankBalanceObservations.AsNoTracking());
        Assert.Null((await t.Db.BankAccounts.AsNoTracking().SingleAsync()).Balance);
    }
}
