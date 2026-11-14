using ControlFinance.API.Controllers;
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

public class PlanningDataTests
{
    private const string Password = "senha-correta";

    private static PlanningController Planning(TestDb t) => t.Controller(db => new PlanningController(db));

    // A exclusão termina apagando o cookie de sessão, que consulta o ambiente (Secure fora de
    // desenvolvimento): o HttpContext do teste precisa ter IHostEnvironment registrado.
    private static AccountController Account(TestDb t)
    {
        var controller = t.Controller(db => new AccountController(
            db,
            new PolpDisconnectService(db, new NullPolpService(), new IntegrationLock(), NullLogger<PolpDisconnectService>.Instance),
            new SessionValidator(db, new MemoryCache(new MemoryCacheOptions())),
            new RateLimitService(),
            new ConfigurationBuilder().Build()));
        controller.ControllerContext.HttpContext.RequestServices = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new TestHostEnvironment())
            .BuildServiceProvider();
        return controller;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "ControlFinance.API.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static User AddStranger(TestDb t)
    {
        var stranger = new User { Name = "Outro", Email = $"{Guid.NewGuid():N}@x.com", DocumentHash = Guid.NewGuid().ToString("N"), PasswordHash = "x" };
        t.Db.Users.Add(stranger);
        t.Db.EmergencyReserves.Add(new EmergencyReserve { UserId = stranger.Id });
        t.Db.SavingsGoals.Add(new SavingsGoal { UserId = stranger.Id, Name = "Meta do outro", TargetAmount = 5000m });
        t.Db.SaveChanges();
        return stranger;
    }

    private static ImportedInvestment AddInvestment(TestDb t, string name = "CDB Liquidez")
    {
        var integration = SyncHarness.AddIntegration(t);
        var at = t.Clock.GetUtcNow().UtcDateTime;
        var investment = new ImportedInvestment { UserId = t.User.Id, IntegrationId = integration.Id, SourceConsentId = integration.PolpIntegrationId,
            Family = "bank-fixed-incomes", ExternalId = Guid.NewGuid().ToString(), Name = name, FirstObservedAt = at, LastObservedAt = at };
        t.Db.ImportedInvestments.Add(investment);
        t.Db.SaveChanges();
        return investment;
    }

    private static Guid NewGoal(TestDb t, string name = "Viagem") =>
        Results.OkValue<SavingsGoalDto>(Planning(t).CreateGoal(new CreateSavingsGoalDto(name, 8000m, null), default).Result).Id;

    [Fact]
    public async Task Excluir_a_conta_apaga_reserva_metas_e_vinculos_do_usuario_e_preserva_os_de_outro()
    {
        using var t = new TestDb();
        t.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password);
        t.Db.SaveChanges();
        var stranger = AddStranger(t);
        var investment = AddInvestment(t);

        await Planning(t).SetImportedReserve(new AllocationChangeDto(investment.Id, null, true), default);
        await Planning(t).CreateGoal(new CreateSavingsGoalDto("Viagem", 8000m, new DateOnly(2027, 12, 1)), default);

        var result = await Account(t).DeleteAccount(new ConfirmPasswordDto { Password = Password }, default);

        Assert.IsType<OkObjectResult>(result);
        await using var db = t.NewContext();
        Assert.False(await db.Users.AnyAsync(u => u.Id == t.User.Id));
        Assert.False(await db.EmergencyReserves.AnyAsync(r => r.UserId == t.User.Id));
        Assert.False(await db.SavingsGoals.AnyAsync(g => g.UserId == t.User.Id));
        Assert.False(await db.PlanningAllocations.AnyAsync(a => a.UserId == t.User.Id));

        Assert.True(await db.EmergencyReserves.AnyAsync(r => r.UserId == stranger.Id));
        var strangerGoal = await db.SavingsGoals.SingleAsync(g => g.UserId == stranger.Id);
        Assert.Equal("Meta do outro", strangerGoal.Name);
    }

    [Fact]
    public async Task Senha_errada_nao_apaga_reserva_nem_metas()
    {
        using var t = new TestDb();
        t.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password);
        t.Db.SaveChanges();
        var investment = AddInvestment(t);
        await Planning(t).SetImportedReserve(new AllocationChangeDto(investment.Id, null, true), default);
        await Planning(t).CreateGoal(new CreateSavingsGoalDto("Viagem", 8000m, null), default);

        var result = await Account(t).DeleteAccount(new ConfirmPasswordDto { Password = "errada" }, default);

        Assert.IsType<BadRequestObjectResult>(result);
        await using var db = t.NewContext();
        Assert.True(await db.PlanningAllocations.AnyAsync(a => a.UserId == t.User.Id));
        Assert.Equal(1, await db.SavingsGoals.CountAsync(g => g.UserId == t.User.Id));
    }

    [Fact]
    public async Task Meta_de_outro_usuario_nao_pode_ser_apagada_nem_vinculada_nem_listada()
    {
        using var t = new TestDb();
        var stranger = AddStranger(t);
        var strangerGoalId = t.Db.SavingsGoals.Single(g => g.UserId == stranger.Id).Id;
        var investment = AddInvestment(t);

        Assert.IsType<NotFoundObjectResult>(await Planning(t).DeleteGoal(strangerGoalId, default));
        Assert.IsType<NotFoundObjectResult>(await Planning(t).SetGoalAllocation(strangerGoalId, new AllocationChangeDto(investment.Id, null, true), default));

        var planning = Results.OkValue<PlanningDto>(await Planning(t).Get(default));
        Assert.Empty(planning.Reserve.InvestmentIds);
        Assert.Empty(planning.Goals);

        await using var db = t.NewContext();
        Assert.False(await db.PlanningAllocations.AnyAsync());
    }

    [Fact]
    public async Task Investimento_de_outro_usuario_nao_entra_na_reserva_nem_em_meta()
    {
        using var t = new TestDb();
        var other = OtherUser.Add(t);
        var at = t.Clock.GetUtcNow().UtcDateTime;
        var foreign = new ImportedInvestment { UserId = other.User.Id, IntegrationId = other.Integration.Id, SourceConsentId = other.Integration.PolpIntegrationId,
            Family = "funds", ExternalId = Guid.NewGuid().ToString(), FirstObservedAt = at, LastObservedAt = at };
        t.Db.ImportedInvestments.Add(foreign);
        t.Db.SaveChanges();
        var goalId = NewGoal(t);

        Assert.IsType<NotFoundObjectResult>(await Planning(t).SetImportedReserve(new AllocationChangeDto(foreign.Id, null, true), default));
        Assert.IsType<NotFoundObjectResult>(await Planning(t).SetGoalAllocation(goalId, new AllocationChangeDto(foreign.Id, null, true), default));
        await using var db = t.NewContext();
        Assert.False(await db.PlanningAllocations.AnyAsync());
    }

    [Fact]
    public async Task Um_investimento_conta_para_um_destino_so()
    {
        using var t = new TestDb();
        var investment = AddInvestment(t);
        var trip = NewGoal(t, "Viagem");
        var car = NewGoal(t, "Carro");

        var linked = Results.OkValue<PlanningDto>(await Planning(t).SetGoalAllocation(trip, new AllocationChangeDto(investment.Id, null, true), default));
        Assert.Equal([investment.Id], linked.Goals.Single(g => g.Id == trip).InvestmentIds);

        Assert.IsType<ConflictObjectResult>(await Planning(t).SetGoalAllocation(car, new AllocationChangeDto(investment.Id, null, true), default));
        Assert.IsType<ConflictObjectResult>(await Planning(t).SetImportedReserve(new AllocationChangeDto(investment.Id, null, true), default));

        var released = Results.OkValue<PlanningDto>(await Planning(t).SetGoalAllocation(trip, new AllocationChangeDto(investment.Id, null, false), default));
        Assert.Empty(released.Goals.Single(g => g.Id == trip).InvestmentIds);
        var reserve = Results.OkValue<PlanningDto>(await Planning(t).SetImportedReserve(new AllocationChangeDto(investment.Id, null, true), default));
        Assert.Equal([investment.Id], reserve.Reserve.InvestmentIds);
    }

    [Fact]
    public async Task Reserva_aceita_varios_investimentos()
    {
        using var t = new TestDb();
        var first = AddInvestment(t, "CDB A");
        var second = AddInvestment(t, "CDB B");

        await Planning(t).SetImportedReserve(new AllocationChangeDto(first.Id, null, true), default);
        var planning = Results.OkValue<PlanningDto>(await Planning(t).SetImportedReserve(new AllocationChangeDto(second.Id, null, true), default));

        Assert.Equal([first.Id, second.Id], planning.Reserve.InvestmentIds);
    }

    [Fact]
    public async Task Reserva_automatica_por_nome_so_vai_para_meta_depois_de_sair_da_reserva()
    {
        using var t = new TestDb();
        var auto = AddInvestment(t, "Reserva de Emergência");
        var goalId = NewGoal(t);

        Assert.IsType<ConflictObjectResult>(await Planning(t).SetGoalAllocation(goalId, new AllocationChangeDto(auto.Id, null, true), default));

        var removed = Results.OkValue<PlanningDto>(await Planning(t).SetImportedReserve(new AllocationChangeDto(auto.Id, null, false), default));
        Assert.Contains(auto.Id, removed.Reserve.ExcludedAutoIds);
        var linked = Results.OkValue<PlanningDto>(await Planning(t).SetGoalAllocation(goalId, new AllocationChangeDto(auto.Id, null, true), default));
        Assert.Equal([auto.Id], linked.Goals.Single().InvestmentIds);
    }

    [Fact]
    public async Task Apagar_a_meta_libera_os_investimentos_vinculados()
    {
        using var t = new TestDb();
        var investment = AddInvestment(t);
        var goalId = NewGoal(t);
        await Planning(t).SetGoalAllocation(goalId, new AllocationChangeDto(investment.Id, null, true), default);

        await Planning(t).DeleteGoal(goalId, default);

        await using var db = t.NewContext();
        Assert.False(await db.PlanningAllocations.AnyAsync());
    }

    [Fact]
    public async Task Conta_bancaria_nao_pode_mais_virar_reserva()
    {
        using var t = new TestDb();
        var account = t.AddAccount("Conta corrente");

        Assert.IsType<BadRequestObjectResult>(await Planning(t).SetReserveAccount(new SetReserveAccountDto(account.Id), default));
    }

    [Fact]
    public async Task Nome_e_valor_alvo_ficam_cifrados_no_banco()
    {
        using var t = new TestDb();
        await Planning(t).CreateGoal(new CreateSavingsGoalDto("Tratamento dentário", 8000m, null), default);

        List<string> stored;
        await using (var command = t.Db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = """SELECT g."NameEncrypted", g."TargetAmountEncrypted" FROM "SavingsGoals" g""";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            stored = Enumerable.Range(0, 2).Select(reader.GetString).ToList();
        }

        Assert.DoesNotContain(stored, v => v.Contains("Tratamento") || v.Contains("8000"));

        await using var db = t.NewContext();
        var goal = await db.SavingsGoals.SingleAsync();
        Assert.Equal("Tratamento dentário", goal.Name);
        Assert.Equal(8000m, goal.TargetAmount);
    }

    [Fact]
    public async Task Importacao_do_navegador_nao_sobrescreve_e_pode_ser_repetida_sem_duplicar()
    {
        using var t = new TestDb();
        var stranger = AddStranger(t);
        var strangerGoalId = t.Db.SavingsGoals.Single(g => g.UserId == stranger.Id).Id;

        var localGoalId = Guid.NewGuid();
        var dto = new ImportLegacyPlanningDto([
            new LegacyGoalDto(localGoalId, "Viagem", 8000m, null, null),
            new LegacyGoalDto(strangerGoalId, "Colisão", 10m, null, null),
        ]);

        var first = Results.OkValue<ImportLegacyPlanningResultDto>(await Planning(t).ImportLegacy(dto, default));
        var second = Results.OkValue<ImportLegacyPlanningResultDto>(await Planning(t).ImportLegacy(dto, default));

        Assert.Equal([localGoalId], first.StoredGoalIds);
        Assert.Equal([localGoalId], second.StoredGoalIds);
        Assert.Single(second.Planning.Goals);
        Assert.Empty(second.Planning.Goals[0].InvestmentIds);

        await using var db = t.NewContext();
        var strangerGoal = await db.SavingsGoals.SingleAsync(g => g.Id == strangerGoalId);
        Assert.Equal(stranger.Id, strangerGoal.UserId);
        Assert.Equal("Meta do outro", strangerGoal.Name);
    }
}
