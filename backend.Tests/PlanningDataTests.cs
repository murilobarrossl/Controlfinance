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
        t.Db.EmergencyReserves.Add(new EmergencyReserve { UserId = stranger.Id, CurrentAmount = 900m });
        t.Db.SavingsGoals.Add(new SavingsGoal { UserId = stranger.Id, Name = "Meta do outro", TargetAmount = 5000m, CurrentAmount = 100m });
        t.Db.SaveChanges();
        return stranger;
    }

    [Fact]
    public async Task Excluir_a_conta_apaga_reserva_e_metas_do_usuario_e_preserva_as_de_outro()
    {
        using var t = new TestDb();
        t.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password);
        t.Db.SaveChanges();
        var stranger = AddStranger(t);

        await Planning(t).AddToReserve(new ReserveAmountDto(1500m), default);
        await Planning(t).CreateGoal(new CreateSavingsGoalDto("Viagem", 8000m, 2000m, new DateOnly(2027, 12, 1)), default);
        await Planning(t).CreateGoal(new CreateSavingsGoalDto("Notebook", 6000m, 0m, null), default);

        var result = await Account(t).DeleteAccount(new ConfirmPasswordDto { Password = Password }, default);

        Assert.IsType<OkObjectResult>(result);
        await using var db = t.NewContext();
        Assert.False(await db.Users.AnyAsync(u => u.Id == t.User.Id));
        Assert.False(await db.EmergencyReserves.AnyAsync(r => r.UserId == t.User.Id));
        Assert.False(await db.SavingsGoals.AnyAsync(g => g.UserId == t.User.Id));

        var strangerReserve = await db.EmergencyReserves.SingleAsync(r => r.UserId == stranger.Id);
        Assert.Equal(900m, strangerReserve.CurrentAmount);
        var strangerGoal = await db.SavingsGoals.SingleAsync(g => g.UserId == stranger.Id);
        Assert.Equal("Meta do outro", strangerGoal.Name);
    }

    [Fact]
    public async Task Senha_errada_nao_apaga_reserva_nem_metas()
    {
        using var t = new TestDb();
        t.User.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password);
        t.Db.SaveChanges();
        await Planning(t).AddToReserve(new ReserveAmountDto(300m), default);
        await Planning(t).CreateGoal(new CreateSavingsGoalDto("Viagem", 8000m, 0m, null), default);

        var result = await Account(t).DeleteAccount(new ConfirmPasswordDto { Password = "errada" }, default);

        Assert.IsType<BadRequestObjectResult>(result);
        await using var db = t.NewContext();
        Assert.True(await db.EmergencyReserves.AnyAsync(r => r.UserId == t.User.Id));
        Assert.Equal(1, await db.SavingsGoals.CountAsync(g => g.UserId == t.User.Id));
    }

    [Fact]
    public async Task Meta_de_outro_usuario_nao_pode_ser_apagada_nem_alterada_nem_listada()
    {
        using var t = new TestDb();
        var stranger = AddStranger(t);
        var strangerGoalId = t.Db.SavingsGoals.Single(g => g.UserId == stranger.Id).Id;

        Assert.IsType<NotFoundObjectResult>(await Planning(t).DeleteGoal(strangerGoalId, default));
        Assert.IsType<NotFoundObjectResult>(await Planning(t).UpdateGoalProgress(strangerGoalId, new UpdateGoalProgressDto(1m), default));

        var planning = Results.OkValue<PlanningDto>(await Planning(t).Get(default));
        Assert.Null(planning.Reserve);
        Assert.Empty(planning.Goals);

        await using var db = t.NewContext();
        var untouched = await db.SavingsGoals.SingleAsync(g => g.Id == strangerGoalId);
        Assert.Equal(100m, untouched.CurrentAmount);
    }

    [Fact]
    public async Task Nome_e_valores_ficam_cifrados_no_banco()
    {
        using var t = new TestDb();
        await Planning(t).AddToReserve(new ReserveAmountDto(1234.56m), default);
        await Planning(t).CreateGoal(new CreateSavingsGoalDto("Tratamento dentário", 8000m, 2500m, null), default);

        List<string> stored;
        await using (var command = t.Db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = """
                SELECT g."NameEncrypted", g."TargetAmountEncrypted", g."CurrentAmountEncrypted", r."CurrentAmountEncrypted"
                FROM "SavingsGoals" g, "EmergencyReserves" r
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            stored = Enumerable.Range(0, 4).Select(reader.GetString).ToList();
        }

        Assert.DoesNotContain(stored, v => v.Contains("Tratamento") || v.Contains("8000") || v.Contains("2500") || v.Contains("1234"));

        await using var db = t.NewContext();
        var goal = await db.SavingsGoals.SingleAsync();
        Assert.Equal("Tratamento dentário", goal.Name);
        Assert.Equal(8000m, goal.TargetAmount);
        Assert.Equal(1234.56m, (await db.EmergencyReserves.SingleAsync()).CurrentAmount);
    }

    [Fact]
    public async Task Importacao_do_navegador_nao_sobrescreve_e_pode_ser_repetida_sem_duplicar()
    {
        using var t = new TestDb();
        var stranger = AddStranger(t);
        var strangerGoalId = t.Db.SavingsGoals.Single(g => g.UserId == stranger.Id).Id;
        await Planning(t).AddToReserve(new ReserveAmountDto(500m), default);

        var localGoalId = Guid.NewGuid();
        var dto = new ImportLegacyPlanningDto(700m, [
            new LegacyGoalDto(localGoalId, "Viagem", 8000m, 1000m, null, null),
            new LegacyGoalDto(strangerGoalId, "Colisão", 10m, 0m, null, null),
        ]);

        var first = Results.OkValue<ImportLegacyPlanningResultDto>(await Planning(t).ImportLegacy(dto, default));
        var second = Results.OkValue<ImportLegacyPlanningResultDto>(await Planning(t).ImportLegacy(dto, default));

        Assert.False(first.ReserveImported);
        Assert.Equal(500m, first.Planning.Reserve!.CurrentAmount);
        Assert.Equal([localGoalId], first.StoredGoalIds);
        Assert.Equal([localGoalId], second.StoredGoalIds);
        Assert.Single(second.Planning.Goals);

        await using var db = t.NewContext();
        var strangerGoal = await db.SavingsGoals.SingleAsync(g => g.Id == strangerGoalId);
        Assert.Equal(stranger.Id, strangerGoal.UserId);
        Assert.Equal("Meta do outro", strangerGoal.Name);
    }
}
