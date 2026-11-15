using System.Text;
using ControlFinance.API.Controllers;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlFinance.API.Tests;

public class UserIsolationAccountsAndCategoriesTests
{
    private static BankAccountsController Accounts(TestDb t) => t.Controller(db => new BankAccountsController(db));
    private static CategoriesController Categories(TestDb t) => t.Controller(db => new CategoriesController(db));

    [Fact]
    public async Task Contas_alheias_nao_sao_listadas_lidas_editadas_nem_apagadas()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        var mine = t.AddAccount("Minha conta");
        var id = other.ManualAccount.Id;

        var list = Http.Json(await Accounts(t).GetAll());
        Assert.Contains(mine.Id.ToString(), list);
        Assert.DoesNotContain(other.Account.Id.ToString(), list);
        Assert.DoesNotContain(id.ToString(), list);

        Assert.Equal(404, Http.Status(await Accounts(t).GetById(id)));
        Assert.Equal(404, Http.Status(await Accounts(t).SetOwnership(id, new SetAccountOwnershipDto("Business"))));
        Assert.Equal(404, Http.Status(await Accounts(t).Delete(id, default)));
        Assert.Equal(404, Http.Status(await Accounts(t).Delete(other.Account.Id, default)));

        await using var db = t.NewContext();
        var saved = db.BankAccounts.Single(b => b.Id == id);
        Assert.Equal(other.User.Id, saved.UserId);
        Assert.Equal("Conta manual da outra pessoa", saved.Name);
        Assert.Equal(OtherUser.Balance, saved.Balance);
        Assert.Equal(AccountOwnership.Personal, saved.Ownership);
        Assert.Equal(3, db.BankAccounts.Count(b => b.UserId == other.User.Id));
        Assert.True(db.Transactions.Any(x => x.Id == other.Transaction.Id));
    }

    [Fact]
    public async Task Categorias_alheias_nao_sao_listadas_editadas_nem_apagadas()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        var id = other.Category.Id;

        Assert.DoesNotContain(id.ToString(), Http.Json(await Categories(t).GetAll()));
        Assert.Equal(404, Http.Status(await Categories(t).Update(id, new CreateCategoryDto("Tomada", "#000000"))));
        Assert.Equal(404, Http.Status(await Categories(t).Delete(id)));

        await using var db = t.NewContext();
        var saved = db.Categories.Single(c => c.Id == id);
        Assert.Equal(other.User.Id, saved.UserId);
        Assert.Equal("Categoria da outra pessoa", saved.Name);
    }

    [Fact]
    public async Task Nome_de_categoria_alheia_nao_bloqueia_nem_revela_categoria_com_o_mesmo_nome()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);

        Assert.Equal(201, Http.Status(await Categories(t).Create(new CreateCategoryDto(other.Category.Name, null))));

        await using var db = t.NewContext();
        Assert.Equal(1, db.Categories.Count(c => c.UserId == t.User.Id && c.Name == other.Category.Name));
        Assert.Equal(1, db.Categories.Count(c => c.UserId == other.User.Id && c.Name == other.Category.Name));
    }
}

public class UserIsolationCardsTests
{
    private static CreditCardsController Cards(TestDb t) => t.Controller(db => new CreditCardsController(db));

    [Fact]
    public async Task Cartoes_alheios_nao_aparecem_em_listas_nem_no_resumo()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        var myCard = t.AddAccount("Meu cartao", type: "CREDIT_CARD");

        var json = Http.Json(await Cards(t).GetSummary(default));
        Assert.Contains(myCard.Id.ToString(), json);
        Assert.DoesNotContain(other.Card.Id.ToString(), json);
        Assert.DoesNotContain("da outra pessoa", json);
    }

}

public class UserIsolationRecurrencesAndPlanningTests
{
    private static RecurrencesController Recurrences(TestDb t) => t.Controller(db => new RecurrencesController(db, t.Encryption));
    private static PlanningController Planning(TestDb t) => t.Controller(db => new PlanningController(db));

    [Fact]
    public async Task Recorrencias_nao_mostram_nem_aceitam_conta_alheia()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        for (var i = 0; i < 4; i++)
            t.Db.Transactions.Add(new Transaction
            {
                UserId = other.User.Id, BankAccountId = other.Account.Id, Name = "Assinatura alheia",
                Type = TransactionType.Expense, Status = TransactionStatus.Paid, Amount = 39.9m,
                DueDate = Dates.Today.AddMonths(-i)
            });
        var mine = t.AddAccount("Minha conta");
        for (var i = 0; i < 4; i++)
            t.AddTransaction(mine, TransactionType.Expense, 29.9m, Dates.Today.AddMonths(-i), "Assinatura minha");
        t.Db.SaveChanges();

        var json = Http.Json(await Recurrences(t).GetAll());
        Assert.Contains(mine.Id.ToString(), json);
        Assert.DoesNotContain("Assinatura alheia", json);
        Assert.DoesNotContain(other.Account.Id.ToString(), json);

        var result = await Recurrences(t).SetDecision(new SetRecurrenceDecisionDto(other.Account.Id, "Assinatura alheia", "Dismissed", null, null));
        Assert.Equal(400, Http.Status(result));
        await using var db = t.NewContext();
        Assert.False(await db.RecurrenceDecisions.AnyAsync());
    }

    [Fact]
    public async Task Reserva_e_taxa_de_rendimento_nao_aceitam_conta_alheia_nem_apagam_taxa_alheia()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        t.Db.InvestmentRateProfiles.Add(new InvestmentRateProfile
        {
            UserId = other.User.Id, BankAccountId = other.ManualAccount.Id, Kind = InvestmentRateKinds.CdiPercent, Value = 100m
        });
        t.Db.SaveChanges();

        Assert.Equal(400, Http.Status(await Planning(t).SetReserveAccount(new SetReserveAccountDto(other.Account.Id), default)));
        Assert.Equal(404, Http.Status(await Planning(t).SetInvestmentRate(other.ManualAccount.Id,
            new SetInvestmentRateDto(InvestmentRateKinds.CdiPercent, 110m, false), default)));
        await Planning(t).DeleteInvestmentRate(other.ManualAccount.Id, default);

        await using var db = t.NewContext();
        Assert.False(await db.EmergencyReserves.AnyAsync(r => r.BankAccountId == other.Account.Id));
        var profile = db.InvestmentRateProfiles.Single();
        Assert.Equal(other.User.Id, profile.UserId);
        Assert.Equal(100m, profile.Value);

        var planning = Http.Json(await Planning(t).Get(default));
        Assert.DoesNotContain(other.ManualAccount.Id.ToString(), planning);
    }
}

public class UserIsolationExportTests
{
    [Fact]
    public async Task Exportacao_de_dados_nao_traz_nada_de_outro_usuario()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        t.Db.SavingsGoals.Add(new SavingsGoal { UserId = other.User.Id, Name = "Meta da outra pessoa", TargetAmount = 1000m });
        t.Db.EmergencyReserves.Add(new EmergencyReserve { UserId = other.User.Id, BankAccountId = other.Account.Id });
        t.Db.ConsentRecords.Add(new ConsentRecord { UserId = other.User.Id, Kind = ConsentKinds.BankConnection, Version = "x", Reference = "alheia" });
        t.Db.SaveChanges();
        var mine = t.AddAccount("Minha conta");
        t.AddTransaction(mine, TransactionType.Expense, 12m, Dates.Today, "Minha compra");

        var controller = t.Controller(db => new AccountController(
            db,
            new PolpDisconnectService(db, new NullPolpService(), new IntegrationLock(), NullLogger<PolpDisconnectService>.Instance),
            new SessionValidator(db, new MemoryCache(new MemoryCacheOptions())),
            new RateLimitService(),
            new ConfigurationBuilder().Build()));
        var body = new MemoryStream();
        controller.ControllerContext.HttpContext.Response.Body = body;

        await controller.Export(default);
        await controller.ControllerContext.HttpContext.Response.BodyWriter.FlushAsync();

        var json = Encoding.UTF8.GetString(body.ToArray());
        Assert.Contains("Minha compra", json);
        foreach (var leak in new[]
        {
            "da outra pessoa", "Banco Alheio", "alheia", "777.77", "98765.43", "321.09",
            other.User.Id.ToString(), other.Account.Id.ToString(), other.Integration.Id.ToString(),
            other.Integration.PolpIntegrationId.ToString(), other.Transaction.Id.ToString(),
        })
            Assert.DoesNotContain(leak, json);
    }
}

public class UserIsolationSyncTests
{
    [Fact]
    public async Task Evento_de_contas_de_uma_conexao_so_cria_contas_para_o_dono_dela()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        var mine = SyncHarness.AddIntegration(t, synced: true);
        var polp = new ScriptedPolp();
        var remote = ScriptedPolp.Account();
        remote.ConsentId = other.Integration.PolpIntegrationId;
        polp.Accounts.Add(remote);

        await SyncHarness.Processor(t, polp).Process("accounts", other.Integration.PolpIntegrationId);

        await using var db = t.NewContext();
        var created = db.BankAccounts.Single(b => b.PolpAccountId == remote.Id);
        Assert.Equal(other.User.Id, created.UserId);
        Assert.Equal(other.Integration.PolpIntegrationId, created.PolpIntegrationId);
        Assert.False(await db.BankAccounts.AnyAsync(b => b.UserId == t.User.Id));
        Assert.Equal(mine.PolpIntegrationId, db.PolpIntegrations.Single(p => p.UserId == t.User.Id).PolpIntegrationId);
    }

    [Fact]
    public async Task Evento_de_transacoes_de_conta_presente_em_dois_usuarios_grava_cada_lado_com_o_proprio_dono()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        var remoteId = other.Account.PolpAccountId!.Value;
        var mineIntegration = SyncHarness.AddIntegration(t, synced: true);
        var mine = SyncHarness.AddLinked(t, mineIntegration, remoteId, card: false);
        var polp = new ScriptedPolp();
        var tx = ScriptedPolp.AccountTx(Dates.Today.ToString("yyyy-MM-dd") + "T12:00:00.000Z", "15.00");
        tx.AccountId = remoteId;
        polp.AccountTransactions[remoteId] = [tx];

        await SyncHarness.Processor(t, polp).Process("accounts.transactions", remoteId);

        await using var db = t.NewContext();
        var imported = db.Transactions.Where(x => x.Description == tx.Id.ToString()).ToList();
        Assert.Equal(2, imported.Count);
        Assert.Contains(imported, x => x.BankAccountId == mine.Id && x.UserId == t.User.Id);
        Assert.Contains(imported, x => x.BankAccountId == other.Account.Id && x.UserId == other.User.Id);
        Assert.All(db.Transactions.Include(x => x.BankAccount).Where(x => x.BankAccountId != null).ToList(),
            x => Assert.Equal(x.BankAccount!.UserId, x.UserId));
        Assert.All(db.Transactions.Include(x => x.Category).Where(x => x.CategoryId != null).ToList(),
            x => Assert.Equal(x.Category!.UserId, x.UserId));
    }
}
