using ControlFinance.API.Controllers;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlFinance.API.Tests;

public class UserIsolationPolpTests
{
    private const string Cpf = "52998224725";

    private static (PolpController Controller, FakePolpProxy Polp) Build(TestDb t)
    {
        var (service, fake) = FakePolpProxy.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = t.Controller(db => new PolpController(
            db, service,
            new InstitutionNameResolver(service, cache, NullLogger<InstitutionNameResolver>.Instance),
            t.Encryption,
            new PolpDisconnectService(db, service, new IntegrationLock(), NullLogger<PolpDisconnectService>.Instance),
            new RecordingInbox(),
            cache));
        return (controller, fake);
    }

    private static void AssertUntouched(TestDb t, OtherUser other)
    {
        using var db = t.NewContext();
        var integration = db.PolpIntegrations.Single(p => p.Id == other.Integration.Id);
        Assert.Equal(other.User.Id, integration.UserId);
        Assert.Equal("AUTHORISED", integration.Status);
        var accounts = db.BankAccounts.Where(b => b.UserId == other.User.Id).ToList();
        Assert.Equal(3, accounts.Count);
        Assert.All(accounts, a => Assert.Equal(OtherUser.Balance, a.Balance));
        Assert.True(db.Transactions.Any(x => x.Id == other.Transaction.Id && x.UserId == other.User.Id));
    }

    [Fact]
    public async Task Lista_de_contas_conectadas_so_traz_contas_do_usuario()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        var mine = t.AddAccount("Minha conta");
        var (controller, polp) = Build(t);

        var result = await controller.GetIntegrations(default);

        var json = Http.Json(result);
        Assert.Contains(mine.Id.ToString(), json);
        Assert.DoesNotContain(other.Account.Id.ToString(), json);
        Assert.DoesNotContain(other.Card.Id.ToString(), json);
        Assert.DoesNotContain(other.ManualAccount.Id.ToString(), json);
        Assert.DoesNotContain(other.Integration.Id.ToString(), json);
        Assert.DoesNotContain(OtherUser.AccountName, json);
        Assert.Empty(polp.Calls);
    }

    [Fact]
    public async Task Status_de_conexao_alheia_responde_404_sem_chamar_a_Polp()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        var (controller, polp) = Build(t);

        Assert.Equal(404, Http.Status(await controller.GetIntegrationStatus(other.Integration.Id, default)));
        Assert.Empty(polp.Calls);
    }

    [Fact]
    public async Task Desconectar_conexao_alheia_responde_404_e_nao_apaga_nada()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        var (controller, polp) = Build(t);

        Assert.Equal(404, Http.Status(await controller.Disconnect(other.Integration.Id, default)));

        Assert.Empty(polp.Calls);
        AssertUntouched(t, other);
    }

    [Fact]
    public async Task Atualizar_saldo_de_conta_ou_conexao_alheia_responde_404_sem_chamar_a_Polp()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        var (controller, polp) = Build(t);

        Assert.Equal(404, Http.Status(await controller.SyncBalance(other.Account.Id, default)));
        Assert.Equal(404, Http.Status(await controller.SyncConnectionBalance(other.Integration.Id, default)));

        Assert.Empty(polp.Calls);
        AssertUntouched(t, other);
    }

    [Fact]
    public async Task Connect_que_recebe_da_Polp_um_consentimento_de_outro_usuario_e_recusado()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        t.User.DocumentHash = t.Encryption.ComputeLookupHash(Cpf);
        t.Db.SaveChanges();
        var (controller, polp) = Build(t);
        polp.Consent = new PolpConsentDto
        {
            Id = other.Integration.PolpIntegrationId,
            InstitutionId = other.Integration.InstitutionId,
            Status = "AUTHORISED",
        };

        var result = await controller.Connect(
            new PolpConnectDto(other.Integration.InstitutionId.ToString(), Cpf, LegalDocuments.BankConnectionNoticeVersion), default);

        Assert.Equal(409, Http.Status(result));
        Assert.DoesNotContain(other.Integration.Id.ToString(), Http.Json(result));
        await using var db = t.NewContext();
        Assert.False(await db.PolpIntegrations.AnyAsync(p => p.UserId == t.User.Id));
        Assert.False(await db.ConsentRecords.AnyAsync(c => c.UserId == t.User.Id));
        AssertUntouched(t, other);
    }
}

public class UserIsolationTransactionTests
{
    private static TransactionsController Controller(TestDb t) => t.Controller(db => new TransactionsController(db));

    private static string MonthFrom() => new DateTime(Dates.Today.Year, Dates.Today.Month, 1).AddMonths(-1).ToString("yyyy-MM-dd");
    private static string MonthTo() => new DateTime(Dates.Today.Year, Dates.Today.Month, 1).AddMonths(2).ToString("yyyy-MM-dd");

    private static CreateTransactionDto Dto(Guid? accountId, Guid? categoryId, string name = "Editada") =>
        new(name, null, "Expense", "Paid", 10m, Dates.Today, accountId, categoryId);

    private static void AssertTransactionUntouched(TestDb t, OtherUser other)
    {
        using var db = t.NewContext();
        var tx = db.Transactions.Single(x => x.Id == other.Transaction.Id);
        Assert.Equal(other.User.Id, tx.UserId);
        Assert.Equal(OtherUser.TransactionName, tx.Name);
        Assert.Equal(other.Account.Id, tx.BankAccountId);
        Assert.Equal(other.Category.Id, tx.CategoryId);
        Assert.Equal(321.09m, tx.Amount);
        Assert.False(tx.IsFixed);
    }

    [Fact]
    public async Task Editar_marcar_fixa_renomear_e_apagar_transacao_alheia_respondem_404()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        var mine = t.AddAccount();
        var id = other.Transaction.Id;

        Assert.Equal(404, Http.Status(await Controller(t).Update(id, Dto(mine.Id, null))));
        Assert.Equal(404, Http.Status(await Controller(t).SetFixed(id, new SetTransactionFixedDto(true))));
        Assert.Equal(404, Http.Status(await Controller(t).SetDetails(id, new SetTransactionDetailsDto("Renomeada", null))));
        Assert.Equal(404, Http.Status(await Controller(t).Delete(id)));

        AssertTransactionUntouched(t, other);
    }

    [Fact]
    public async Task Criar_transacao_com_conta_ou_categoria_alheia_e_recusado()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);

        Assert.Equal(400, Http.Status(await Controller(t).Create(Dto(other.Account.Id, null, "Nova"))));
        Assert.Equal(400, Http.Status(await Controller(t).Create(Dto(null, other.Category.Id, "Nova"))));

        await using var db = t.NewContext();
        Assert.False(await db.Transactions.AnyAsync(x => x.UserId == t.User.Id));
        Assert.Equal(1, await db.Transactions.CountAsync(x => x.BankAccountId == other.Account.Id));
    }

    [Fact]
    public async Task Mover_transacao_propria_para_conta_ou_categoria_alheia_e_recusado()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        var mine = t.AddAccount();
        var category = t.AddCategory("Minha");
        var own = t.AddTransaction(mine, TransactionType.Expense, 50m, Dates.Today, "Minha compra", category: category);

        Assert.Equal(400, Http.Status(await Controller(t).Update(own.Id, Dto(other.Account.Id, category.Id))));
        Assert.Equal(400, Http.Status(await Controller(t).Update(own.Id, Dto(mine.Id, other.Category.Id))));
        Assert.Equal(400, Http.Status(await Controller(t).SetDetails(own.Id, new SetTransactionDetailsDto("Minha compra", other.Category.Id))));

        await using var db = t.NewContext();
        var saved = db.Transactions.Single(x => x.Id == own.Id);
        Assert.Equal(mine.Id, saved.BankAccountId);
        Assert.Equal(category.Id, saved.CategoryId);
    }

    [Fact]
    public async Task Leituras_com_bankAccountId_alheio_nao_devolvem_dados_da_outra_pessoa()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        var id = other.Account.Id;

        var all = Results.OkValue<List<TransactionDto>>(await Controller(t).GetAll(null, null, id));
        var recent = Results.OkValue<List<TransactionDto>>(await Controller(t).GetRecent(id, 10, default));
        var report = Results.OkValue<TransactionReportDto>(await Controller(t).GetReport(null, null, null, id));
        var aggregates = Results.OkValue<TransactionAggregatesDto>(await Controller(t).GetAggregates(id, MonthFrom(), MonthTo(), default));
        var details = Results.OkValue<List<TransactionDto>>(
            await Controller(t).GetDetails(id, MonthFrom(), MonthTo(), null, null, null, default));

        Assert.Empty(all);
        Assert.Empty(recent);
        Assert.Empty(report.Items);
        Assert.Equal(0m, report.TotalExpense);
        Assert.Equal(0, aggregates.Count);
        Assert.Equal(0m, aggregates.TotalExpense);
        Assert.Empty(details);
    }

    [Fact]
    public async Task Leituras_sem_filtro_de_conta_nao_trazem_transacoes_alheias()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        var mine = t.AddAccount();
        t.AddTransaction(mine, TransactionType.Expense, 40m, Dates.Today, "Minha compra");

        foreach (var result in new[]
        {
            await Controller(t).GetAll(null, null, null),
            await Controller(t).GetReport(null, null, OtherUser.TransactionName),
            await Controller(t).GetReport(null, null, null),
            await Controller(t).GetAggregates(null, MonthFrom(), MonthTo(), default),
            await Controller(t).GetDetails(null, MonthFrom(), MonthTo(), "Expense", null, null, default),
        })
        {
            Assert.Equal(200, Http.Status(result));
            var json = Http.Json(result);
            Assert.DoesNotContain(OtherUser.TransactionName, json);
            Assert.DoesNotContain(other.Transaction.Id.ToString(), json);
            Assert.DoesNotContain("321.09", json);
        }
    }

    [Fact]
    public async Task Dashboard_com_conta_alheia_nao_mostra_saldo_nem_transacoes_da_outra_pessoa()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);

        var result = await t.Controller(db => new DashboardController(db)).GetSummary(other.Account.Id, default);

        var json = Http.Json(result);
        Assert.IsNotType<DashboardSummaryDto>((result as Microsoft.AspNetCore.Mvc.ObjectResult)?.Value);
        Assert.DoesNotContain(OtherUser.AccountName, json);
        Assert.DoesNotContain("98765.43", json);
        Assert.DoesNotContain(OtherUser.TransactionName, json);
    }
}
