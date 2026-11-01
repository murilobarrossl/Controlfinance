using System.Net;
using ControlFinance.API.Controllers;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlFinance.API.Tests;

public class UserTimeZoneTests
{
    // Offsets fixos escritos à mão (o Brasil não tem horário de verão desde 2019): o teste confere
    // a base de fusos do sistema em vez de recalcular o esperado com ela.
    public static TheoryData<string, int, int, int, int, int, int, int> DueDates()
    {
        var zones = new (string Id, int Offset)[]
        {
            ("America/Sao_Paulo", -3), ("America/Manaus", -4), ("America/Rio_Branco", -5), ("America/Noronha", -2),
        };
        var dates = new (int Y, int M, int D, int NextY, int NextM, int NextD)[]
        {
            (2026, 12, 31, 2027, 1, 31),
            (2026, 10, 31, 2026, 11, 30),
            (2027, 2, 28, 2027, 3, 28),
            (2026, 11, 15, 2026, 12, 15),
        };
        var data = new TheoryData<string, int, int, int, int, int, int, int>();
        foreach (var z in zones)
            foreach (var d in dates)
                data.Add(z.Id, z.Offset, d.Y, d.M, d.D, d.NextY, d.NextM, d.NextD);
        return data;
    }

    public static TheoryData<string, int, int, int, int, int, int, int> MonthTurnDueDates()
    {
        var data = new TheoryData<string, int, int, int, int, int, int, int>();
        foreach (var row in DueDates())
            if ((int)row[4] != 15) data.Add((string)row[0], (int)row[1], (int)row[2], (int)row[3], (int)row[4], (int)row[5], (int)row[6], (int)row[7]);
        return data;
    }

    private static DateTimeOffset Local(DateTime wallClock, int offsetHours) =>
        new DateTimeOffset(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified), TimeSpan.FromHours(offsetHours)).ToUniversalTime();

    private static DateTime Day(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<decimal> UsedLimit(TestDb t)
    {
        await using var db = t.NewContext();
        return (await db.CreditCards.SingleAsync()).UsedLimit;
    }

    private static TestDb NewDb(string zone)
    {
        var t = new TestDb();
        t.User.TimeZone = zone;
        t.Db.SaveChanges();
        return t;
    }

    [Theory]
    [MemberData(nameof(DueDates))]
    public void Hoje_do_usuario_vira_exatamente_na_meia_noite_do_fuso_dele(
        string zone, int offset, int y, int m, int d, int ny, int nm, int nd)
    {
        var due = Day(y, m, d);

        Assert.Equal(due.AddDays(-1), UserClock.TodayIn(zone, Local(due.AddSeconds(-1), offset)));
        Assert.Equal(due, UserClock.TodayIn(zone, Local(due, offset)));
        Assert.Equal(due, UserClock.TodayIn(zone, Local(due.AddDays(1).AddSeconds(-1), offset)));
        Assert.Equal(due.AddDays(1), UserClock.TodayIn(zone, Local(due.AddDays(1), offset)));
        Assert.Equal(DateTimeKind.Utc, UserClock.TodayIn(zone, Local(due, offset)).Kind);
    }

    [Theory]
    [MemberData(nameof(DueDates))]
    public void Status_vencido_so_aparece_depois_da_meia_noite_do_fuso(
        string zone, int offset, int y, int m, int d, int ny, int nm, int nd)
    {
        var due = Day(y, m, d);
        string StatusAt(DateTime wallClock) =>
            TransactionDto.EffectiveStatus(TransactionStatus.Pending, due, UserClock.TodayIn(zone, Local(wallClock, offset)));

        Assert.Equal("Pending", StatusAt(due.AddSeconds(-1)));
        Assert.Equal("Pending", StatusAt(due));
        Assert.Equal("Pending", StatusAt(due.AddDays(1).AddSeconds(-1)));
        Assert.Equal("Overdue", StatusAt(due.AddDays(1)));
    }

    [Theory]
    [MemberData(nameof(DueDates))]
    public async Task Listagem_e_filtro_de_status_usam_o_fuso_do_usuario(
        string zone, int offset, int y, int m, int d, int ny, int nm, int nd)
    {
        var due = Day(y, m, d);
        using var t = NewDb(zone);
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 123.45m, due, "Conta", TransactionStatus.Pending);
        var controller = t.Controller(db => new TransactionsController(db));

        async Task<List<TransactionDto>> List(string? status) =>
            Results.OkValue<List<TransactionDto>>(await controller.GetAll(status, null, null));

        t.Clock.UtcNow = Local(due.AddDays(1).AddSeconds(-1), offset);
        Assert.Equal("Pending", Assert.Single(await List(null)).Status);
        Assert.Single(await List("Pending"));
        Assert.Empty(await List("Overdue"));

        t.Clock.UtcNow = Local(due.AddDays(1), offset);
        var overdue = Assert.Single(await List(null));
        Assert.Equal("Overdue", overdue.Status);
        Assert.Equal(due, overdue.DueDate);
        Assert.Equal(123.45m, overdue.Amount);
        Assert.Empty(await List("Pending"));
        Assert.Single(await List("Overdue"));
    }

    [Theory]
    [MemberData(nameof(DueDates))]
    public async Task Parcela_so_avanca_depois_da_meia_noite_do_fuso(
        string zone, int offset, int y, int m, int d, int ny, int nm, int nd)
    {
        var due = Day(y, m, d);

        using (var t = NewDb(zone))
        {
            var card = t.AddCard(5000m, 1200m);
            t.AddInstallment(card, null, 100m, 1, 12, due, dueDay: d);
            t.Clock.UtcNow = Local(due.AddDays(1).AddSeconds(-1), offset);

            var dto = Assert.Single(Results.OkValue<IEnumerable<InstallmentDto>>(
                await t.Controller(db => new InstallmentsController(db)).GetAll()));

            Assert.Equal(1, dto.CurrentInstallment);
            Assert.Equal(due, dto.NextDueDate);
            Assert.Equal(1200.00m, await UsedLimit(t));
        }

        using (var t = NewDb(zone))
        {
            var card = t.AddCard(5000m, 1200m);
            t.AddInstallment(card, null, 100m, 1, 12, due, dueDay: d);
            t.Clock.UtcNow = Local(due.AddDays(1), offset);

            var dto = Assert.Single(Results.OkValue<IEnumerable<InstallmentDto>>(
                await t.Controller(db => new InstallmentsController(db)).GetAll()));

            Assert.Equal(2, dto.CurrentInstallment);
            Assert.Equal(Day(ny, nm, nd), dto.NextDueDate);
            Assert.Equal(1100.00m, await UsedLimit(t));
        }
    }

    [Theory]
    [MemberData(nameof(DueDates))]
    public async Task Vencimento_do_cartao_manual_vira_na_meia_noite_do_fuso(
        string zone, int offset, int y, int m, int d, int ny, int nm, int nd)
    {
        var due = Day(y, m, d);
        using var t = NewDb(zone);
        t.AddCard(5000m, 0m, dueDay: d);
        var controller = t.Controller(db => new CreditCardsController(db));

        async Task<DateTime?> InvoiceDueAt(DateTime wallClock)
        {
            t.Clock.UtcNow = Local(wallClock, offset);
            return Assert.Single(Results.OkValue<List<RecognizedCardDto>>(await controller.GetSummary(default))).InvoiceDueDate;
        }

        Assert.Equal(due, await InvoiceDueAt(due));
        Assert.Equal(due, await InvoiceDueAt(due.AddDays(1).AddSeconds(-1)));
        Assert.Equal(Day(ny, nm, nd), await InvoiceDueAt(due.AddDays(1)));
    }

    [Theory]
    [MemberData(nameof(MonthTurnDueDates))]
    public async Task Mes_do_dashboard_vira_na_meia_noite_do_fuso(
        string zone, int offset, int y, int m, int d, int ny, int nm, int nd)
    {
        var due = Day(y, m, d);
        using var t = NewDb(zone);
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 10.10m, due);
        t.AddTransaction(account, TransactionType.Expense, 20.20m, due.AddDays(1));
        var controller = t.Controller(db => new DashboardController(db));

        async Task<decimal> ExpenseAt(DateTime wallClock)
        {
            t.Clock.UtcNow = Local(wallClock, offset);
            return Results.OkValue<DashboardSummaryDto>(await controller.GetSummary(null, default)).TotalExpense;
        }

        Assert.Equal(10.10m, await ExpenseAt(due.AddDays(1).AddSeconds(-1)));
        Assert.Equal(20.20m, await ExpenseAt(due.AddDays(1)));
    }

    [Fact]
    public async Task Mesmo_instante_na_virada_do_ano_da_status_diferente_por_fuso()
    {
        var due = Day(2026, 12, 31);
        var instant = new DateTimeOffset(2027, 1, 1, 3, 30, 0, TimeSpan.Zero);
        var expected = new Dictionary<string, string>
        {
            ["America/Noronha"] = "Overdue",
            ["America/Sao_Paulo"] = "Overdue",
            ["America/Manaus"] = "Pending",
            ["America/Rio_Branco"] = "Pending",
        };

        foreach (var (zone, status) in expected)
        {
            using var t = NewDb(zone);
            var account = t.AddAccount();
            t.AddTransaction(account, TransactionType.Expense, 50m, due, "Conta", TransactionStatus.Pending);
            t.Clock.UtcNow = instant;

            var result = await t.Controller(db => new TransactionsController(db)).GetAll(null, null, null);

            Assert.Equal(status, Assert.Single(Results.OkValue<List<TransactionDto>>(result)).Status);
        }
    }

    [Theory]
    [InlineData("America/Sao_Paulo", "America/Sao_Paulo")]
    [InlineData("America/Manaus", "America/Manaus")]
    [InlineData("America/Rio_Branco", "America/Rio_Branco")]
    [InlineData("America/Noronha", "America/Noronha")]
    [InlineData("Europe/Lisbon", "Europe/Lisbon")]
    [InlineData("UTC", "UTC")]
    [InlineData(null, "America/Sao_Paulo")]
    [InlineData("", "America/Sao_Paulo")]
    [InlineData("   ", "America/Sao_Paulo")]
    [InlineData("America/Atlantida", "America/Sao_Paulo")]
    [InlineData("E. South America Standard Time", "America/Sao_Paulo")]
    [InlineData("SA Western Standard Time", "America/Sao_Paulo")]
    [InlineData("../../etc/passwd", "America/Sao_Paulo")]
    [InlineData("America/Manaus ", "America/Sao_Paulo")]
    [InlineData("-03:00", "America/Sao_Paulo")]
    public void Fuso_invalido_ou_ausente_cai_em_Sao_Paulo(string? input, string expected)
    {
        Assert.Equal(expected, UserClock.Normalize(input));
    }

    [Fact]
    public void Fuso_gravado_invalido_calcula_o_hoje_de_Sao_Paulo()
    {
        var instant = new DateTimeOffset(2027, 1, 1, 2, 30, 0, TimeSpan.Zero);

        Assert.Equal(Day(2026, 12, 31), UserClock.TodayIn("Nao/Existe", instant));
        Assert.Equal(Day(2026, 12, 31), UserClock.TodayIn(null, instant));
        Assert.Equal(Day(2027, 1, 1), UserClock.TodayIn("America/Noronha", instant));
    }
}

public class AuthTimeZoneTests
{
    private const string Password = "senha1234";

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class FakeTokens : ITokenService
    {
        public IssuedToken GenerateToken(User user) => new("jwt", "csrf", DateTime.UtcNow.AddHours(1));
    }

    private static AuthService Service(TestDb t, RateLimitService rateLimit)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Resend:ApiKey"] = "teste" })
            .Build();
        return new AuthService(t.Db, new FakeTokens(), new EmailService(config, new HttpClient(new OkHandler())),
            rateLimit, t.Encryption, NullLogger<AuthService>.Instance);
    }

    private static RegisterRequestDto Register(string? timeZone) => new()
    {
        Name = "Joana",
        Email = "joana@teste.com",
        Document = "52998224725",
        Password = Password,
        AcceptedTermsVersion = LegalDocuments.TermsVersion,
        AcceptedPrivacyVersion = LegalDocuments.PrivacyVersion,
        ConfirmsAdult = true,
        TimeZone = timeZone,
    };

    private static async Task<string> StoredTimeZone(TestDb t)
    {
        await using var db = t.NewContext();
        return await db.Users.Where(u => u.Email == "joana@teste.com").Select(u => u.TimeZone).SingleAsync();
    }

    [Theory]
    [InlineData("America/Rio_Branco", "America/Rio_Branco")]
    [InlineData("America/Noronha", "America/Noronha")]
    [InlineData(null, "America/Sao_Paulo")]
    [InlineData("Fuso/Inventado", "America/Sao_Paulo")]
    [InlineData("E. South America Standard Time", "America/Sao_Paulo")]
    public async Task Cadastro_grava_o_fuso_do_navegador_ou_Sao_Paulo(string? sent, string expected)
    {
        using var t = new TestDb();
        using var rateLimit = new RateLimitService();

        var (success, _, _, _) = await Service(t, rateLimit).RegisterAsync(Register(sent), "127.0.0.1");

        Assert.True(success);
        Assert.Equal(expected, await StoredTimeZone(t));
    }

    [Fact]
    public async Task Login_atualiza_o_fuso_quando_muda_e_ignora_ausente_ou_invalido()
    {
        using var t = new TestDb();
        using var rateLimit = new RateLimitService();
        var service = Service(t, rateLimit);
        Assert.True((await service.RegisterAsync(Register("America/Sao_Paulo"), "127.0.0.1")).Success);

        async Task Login(string? timeZone) =>
            Assert.True((await service.LoginAsync(
                new LoginRequestDto { Identifier = "joana@teste.com", Password = Password, TimeZone = timeZone }, "127.0.0.1")).Success);

        await Login("America/Manaus");
        Assert.Equal("America/Manaus", await StoredTimeZone(t));

        await Login(null);
        Assert.Equal("America/Manaus", await StoredTimeZone(t));

        await Login("Fuso/Inventado");
        Assert.Equal("America/Manaus", await StoredTimeZone(t));

        await Login("SA Western Standard Time");
        Assert.Equal("America/Manaus", await StoredTimeZone(t));

        await Login("America/Rio_Branco");
        Assert.Equal("America/Rio_Branco", await StoredTimeZone(t));
    }
}
