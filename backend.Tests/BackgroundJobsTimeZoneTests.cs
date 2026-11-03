using System.Net;
using System.Text.Json;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlFinance.API.Tests;

internal sealed class RecordingEmail : HttpMessageHandler
{
    public List<(string To, string Subject, string Html)> Sent { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        var root = json.RootElement;
        Sent.Add((root.GetProperty("to")[0].GetString()!, root.GetProperty("subject").GetString()!, root.GetProperty("html").GetString()!));
        return new HttpResponseMessage(HttpStatusCode.OK);
    }

    public EmailService Service() => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Resend:ApiKey"] = "teste" }).Build(),
        new HttpClient(this));
}

internal static class LocalTime
{
    public static DateTimeOffset At(DateTime wallClock, int offsetHours) =>
        new DateTimeOffset(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified), TimeSpan.FromHours(offsetHours)).ToUniversalTime();

    public static DateTime Day(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    public static TestDb NewDb(string zone, bool monthlySummary = false)
    {
        var t = new TestDb();
        t.User.TimeZone = zone;
        t.User.MonthlySummaryEnabled = monthlySummary;
        t.Db.SaveChanges();
        return t;
    }
}

public class DueReminderTimeZoneTests
{
    private const string Reminder = "Você tem contas perto do vencimento";

    public static TheoryData<string, int, int, int, int, int, int, int> DueDates() => UserTimeZoneTests.DueDates();

    private static Task Run(TestDb t, RecordingEmail email, DateTime wallClock, int offset) =>
        ScheduledEmailService.RunOnceAsync(t.Db, email.Service(), LocalTime.At(wallClock, offset), NullLogger.Instance, default);

    [Theory]
    [MemberData(nameof(DueDates))]
    public async Task Janela_vai_de_hoje_ate_3_dias_pelo_calendario_local_incluindo_quem_vence_hoje(
        string zone, int offset, int y, int m, int d, int ny, int nm, int nd)
    {
        var due = LocalTime.Day(y, m, d);
        using var t = LocalTime.NewDb(zone);
        var account = t.AddAccount();
        var yesterday = t.AddTransaction(account, TransactionType.Expense, 10m, due.AddDays(-1), "ontem", TransactionStatus.Pending);
        var today = t.AddTransaction(account, TransactionType.Expense, 20m, due, "hoje", TransactionStatus.Pending);
        var in3 = t.AddTransaction(account, TransactionType.Expense, 30m, due.AddDays(3), "em3", TransactionStatus.Pending);
        var in4 = t.AddTransaction(account, TransactionType.Expense, 40m, due.AddDays(4), "em4", TransactionStatus.Pending);
        var email = new RecordingEmail();

        await Run(t, email, due.AddHours(8), offset);

        var sent = Assert.Single(email.Sent);
        Assert.Equal(t.User.Email, sent.To);
        Assert.Equal(Reminder, sent.Subject);
        Assert.Contains("2 contas pendentes", sent.Html);
        Assert.Contains(due.ToString("dd/MM/yyyy"), sent.Html);
        Assert.Null(yesterday.ReminderSentAt);
        Assert.NotNull(today.ReminderSentAt);
        Assert.NotNull(in3.ReminderSentAt);
        Assert.Null(in4.ReminderSentAt);
        Assert.Equal(DateOnly.FromDateTime(due), t.User.LastDueReminderLocalDate);
    }

    [Theory]
    [MemberData(nameof(DueDates))]
    public async Task Nao_envia_antes_das_8h_locais(
        string zone, int offset, int y, int m, int d, int ny, int nm, int nd)
    {
        var due = LocalTime.Day(y, m, d);
        using var t = LocalTime.NewDb(zone);
        t.AddTransaction(t.AddAccount(), TransactionType.Expense, 20m, due, "hoje", TransactionStatus.Pending);
        var email = new RecordingEmail();

        await Run(t, email, due, offset);
        await Run(t, email, due.AddHours(8).AddSeconds(-1), offset);
        Assert.Empty(email.Sent);

        await Run(t, email, due.AddHours(8), offset);
        Assert.Single(email.Sent);
    }

    [Theory]
    [MemberData(nameof(DueDates))]
    public async Task Nao_reenvia_no_mesmo_dia_local_e_volta_a_enviar_depois_da_meia_noite_local(
        string zone, int offset, int y, int m, int d, int ny, int nm, int nd)
    {
        var due = LocalTime.Day(y, m, d);
        using var t = LocalTime.NewDb(zone);
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 20m, due, "hoje", TransactionStatus.Pending);
        var email = new RecordingEmail();

        await Run(t, email, due.AddHours(8), offset);
        Assert.Single(email.Sent);

        t.AddTransaction(account, TransactionType.Expense, 25m, due.AddDays(1), "amanha", TransactionStatus.Pending);
        await Run(t, email, due.AddHours(9), offset);
        await Run(t, email, due.AddDays(1).AddSeconds(-1), offset);
        await Run(t, email, due.AddDays(1), offset);
        Assert.Single(email.Sent);

        await Run(t, email, due.AddDays(1).AddHours(8), offset);
        Assert.Equal(2, email.Sent.Count);
        Assert.Contains("1 conta pendente", email.Sent[1].Html);
        Assert.Contains(due.AddDays(1).ToString("dd/MM/yyyy"), email.Sent[1].Html);
        Assert.Equal(DateOnly.FromDateTime(due.AddDays(1)), t.User.LastDueReminderLocalDate);
    }

    [Theory]
    [MemberData(nameof(DueDates))]
    public async Task Conta_entra_na_janela_so_quando_vira_o_dia_local(
        string zone, int offset, int y, int m, int d, int ny, int nm, int nd)
    {
        var due = LocalTime.Day(y, m, d);
        using var t = LocalTime.NewDb(zone);
        t.AddTransaction(t.AddAccount(), TransactionType.Expense, 20m, due, "conta", TransactionStatus.Pending);
        var email = new RecordingEmail();

        await Run(t, email, due.AddDays(-4).AddHours(8), offset);
        await Run(t, email, due.AddDays(-3).AddSeconds(-1), offset);
        Assert.Empty(email.Sent);

        await Run(t, email, due.AddDays(-3).AddHours(8), offset);
        Assert.Contains(due.ToString("dd/MM/yyyy"), Assert.Single(email.Sent).Html);
    }

    [Fact]
    public async Task Mesmo_instante_envia_so_onde_ja_sao_8h_locais()
    {
        var due = LocalTime.Day(2027, 1, 1);
        var instant = new DateTimeOffset(2027, 1, 1, 11, 30, 0, TimeSpan.Zero);
        var expected = new Dictionary<string, int>
        {
            ["America/Noronha"] = 1,
            ["America/Sao_Paulo"] = 1,
            ["America/Manaus"] = 0,
            ["America/Rio_Branco"] = 0,
        };

        foreach (var (zone, count) in expected)
        {
            using var t = LocalTime.NewDb(zone);
            t.AddTransaction(t.AddAccount(), TransactionType.Expense, 20m, due, "conta", TransactionStatus.Pending);
            var email = new RecordingEmail();

            await ScheduledEmailService.RunOnceAsync(t.Db, email.Service(), instant, NullLogger.Instance, default);

            Assert.Equal(count, email.Sent.Count);
        }
    }
}

public class MonthlySummaryTimeZoneTests
{
    private const string Notice = "Seu mês fechou: veja o resumo no Control Finance";

    public static TheoryData<string, int, int, int> Months()
    {
        var data = new TheoryData<string, int, int, int>();
        foreach (var (zone, offset) in new[] { ("America/Sao_Paulo", -3), ("America/Manaus", -4), ("America/Rio_Branco", -5), ("America/Noronha", -2) })
            foreach (var (y, m) in new[] { (2026, 12), (2026, 10), (2027, 2) })
                data.Add(zone, offset, y, m);
        return data;
    }

    private static Task Run(TestDb t, RecordingEmail email, DateTime wallClock, int offset) =>
        ScheduledEmailService.RunOnceAsync(t.Db, email.Service(), LocalTime.At(wallClock, offset), NullLogger.Instance, default);

    [Theory]
    [MemberData(nameof(Months))]
    public async Task Nao_sai_antes_de_o_mes_fechar_e_das_8h_do_dia_1_locais(string zone, int offset, int y, int m)
    {
        var month = LocalTime.Day(y, m, 1);
        var next = month.AddMonths(1);
        using var t = LocalTime.NewDb(zone, monthlySummary: true);
        t.AddTransaction(t.AddAccount(), TransactionType.Expense, 10m, month.AddDays(9));
        var email = new RecordingEmail();

        await Run(t, email, next.AddSeconds(-1), offset);
        await Run(t, email, next, offset);
        await Run(t, email, next.AddHours(8).AddSeconds(-1), offset);
        Assert.Empty(email.Sent);

        await Run(t, email, next.AddHours(8), offset);
        var sent = Assert.Single(email.Sent);
        Assert.Equal(Notice, sent.Subject);
        Assert.Equal(DateOnly.FromDateTime(month), t.User.LastMonthlySummaryMonth);
    }

    [Theory]
    [MemberData(nameof(Months))]
    public async Task Sai_uma_unica_vez_por_mes(string zone, int offset, int y, int m)
    {
        var month = LocalTime.Day(y, m, 1);
        var next = month.AddMonths(1);
        using var t = LocalTime.NewDb(zone, monthlySummary: true);
        var account = t.AddAccount();
        t.AddTransaction(account, TransactionType.Expense, 10m, month.AddDays(9));
        var email = new RecordingEmail();

        await Run(t, email, next.AddHours(8), offset);
        await Run(t, email, next.AddHours(9), offset);
        await Run(t, email, next.AddDays(1).AddSeconds(-1), offset);
        await Run(t, email, next.AddDays(1).AddHours(8), offset);
        await Run(t, email, next.AddMonths(1).AddSeconds(-1), offset);
        Assert.Single(email.Sent);

        t.AddTransaction(account, TransactionType.Expense, 10m, next.AddDays(4));
        await Run(t, email, next.AddMonths(1).AddHours(8), offset);
        Assert.Equal(2, email.Sent.Count);
        Assert.Equal(DateOnly.FromDateTime(next), t.User.LastMonthlySummaryMonth);
    }

    [Theory]
    [MemberData(nameof(Months))]
    public async Task Dia_1_sem_execucao_e_recuperado_no_dia_seguinte(string zone, int offset, int y, int m)
    {
        var month = LocalTime.Day(y, m, 1);
        var next = month.AddMonths(1);
        using var t = LocalTime.NewDb(zone, monthlySummary: true);
        t.AddTransaction(t.AddAccount(), TransactionType.Expense, 10m, month.AddDays(9));
        var email = new RecordingEmail();

        await Run(t, email, next.AddDays(1).AddMinutes(30), offset);
        Assert.Single(email.Sent);

        await Run(t, email, next.AddDays(1).AddHours(8), offset);
        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task Quem_ja_recebeu_pela_versao_anterior_nao_recebe_de_novo_no_mesmo_mes()
    {
        using var t = LocalTime.NewDb("America/Sao_Paulo", monthlySummary: true);
        t.AddTransaction(t.AddAccount(), TransactionType.Expense, 10m, LocalTime.Day(2026, 10, 10));
        t.User.LastMonthlySummarySentAt = new DateTime(2026, 11, 1, 1, 0, 0, DateTimeKind.Utc);
        t.Db.SaveChanges();
        var email = new RecordingEmail();

        await Run(t, email, LocalTime.Day(2026, 11, 1).AddHours(9), -3);
        await Run(t, email, LocalTime.Day(2026, 11, 20).AddHours(9), -3);

        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task Mes_sem_transacao_nao_gera_aviso_nem_depois_de_importacao_atrasada()
    {
        using var t = LocalTime.NewDb("America/Manaus", monthlySummary: true);
        var account = t.AddAccount();
        var email = new RecordingEmail();

        await Run(t, email, LocalTime.Day(2027, 1, 1).AddHours(8), -4);
        t.AddTransaction(account, TransactionType.Expense, 10m, LocalTime.Day(2026, 12, 20));
        await Run(t, email, LocalTime.Day(2027, 1, 15).AddHours(8), -4);

        Assert.Empty(email.Sent);
        Assert.Equal(new DateOnly(2026, 12, 1), t.User.LastMonthlySummaryMonth);
    }
}

public class CardBillTimeZoneTests
{
    public static TheoryData<string, int, int, int, int, int, int, int> DueDates() => UserTimeZoneTests.DueDates();

    private sealed class FakePolp(List<PolpBillDto> bills) : IPolpService
    {
        public Task<List<PolpBillDto>> GetBillsAsync(Guid creditCardId, PolpQueryWindow? window = null, CancellationToken ct = default) =>
            Task.FromResult(bills);

        public Task<List<PolpInstitutionDto>> GetInstitutionsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PolpConsentDto> CreateConsentAsync(CreatePolpConsentRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PolpConsentDto> GetConsentAsync(Guid consentId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<PolpAccountDto>> GetAccountsAsync(Guid consentId, PolpQueryWindow? window = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<PolpCreditCardDto>> GetCreditCardsAsync(Guid consentId, PolpQueryWindow? window = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<PolpAccountTransactionDto>> GetAccountTransactionsAsync(Guid accountId, PolpQueryWindow? window = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<List<PolpCardTransactionDto>> GetCardTransactionsAsync(Guid creditCardId, PolpQueryWindow? window = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PolpAccountDto> SyncBalanceAsync(Guid accountId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteConsentAsync(Guid consentId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private static PolpBillDto Bill(DateTime due, string total) => new()
    {
        Id = Guid.NewGuid(),
        DueDate = due.ToString("yyyy-MM-dd"),
        BillClosingDate = due.AddDays(-7).ToString("yyyy-MM-dd"),
        BillTotalAmount = new PolpMoney { Amount = total, Currency = "BRL" },
    };

    [Theory]
    [MemberData(nameof(DueDates))]
    public async Task Fatura_atual_so_passa_para_a_proxima_na_meia_noite_local(
        string zone, int offset, int y, int m, int d, int ny, int nm, int nd)
    {
        var due = LocalTime.Day(y, m, d);
        var nextDue = LocalTime.Day(ny, nm, nd);
        using var t = LocalTime.NewDb(zone);
        var card = t.AddAccount(type: CreditCardAccountDetector.CardType, polpAccountId: Guid.NewGuid());
        var polp = new FakePolp([Bill(nextDue, "200.20"), Bill(due, "100.10")]);
        var sync = new PolpSyncService(t.Db, polp, new ConfigurationBuilder().Build(),
            new InstitutionNameResolver(polp, new MemoryCache(new MemoryCacheOptions()), NullLogger<InstitutionNameResolver>.Instance),
            t.Clock);

        t.Clock.UtcNow = LocalTime.At(due.AddDays(1).AddSeconds(-1), offset);
        await sync.SyncBillsAsync(card, null, default);
        Assert.Equal(due, card.InvoiceDueDate);
        Assert.Equal(100.10m, card.CurrentInvoiceAmount);
        Assert.Equal(d, card.DueDay);
        Assert.Equal(due.AddDays(-7).Day, card.ClosingDay);

        t.Clock.UtcNow = LocalTime.At(due.AddDays(1), offset);
        await sync.SyncBillsAsync(card, null, default);
        Assert.Equal(nextDue, card.InvoiceDueDate);
        Assert.Equal(200.20m, card.CurrentInvoiceAmount);
        Assert.Equal(nd, card.DueDay);
        Assert.Equal(nextDue.AddDays(-7).Day, card.ClosingDay);

        await using var other = t.NewContext();
        var saved = other.BankAccounts.Single(a => a.Id == card.Id);
        Assert.Equal(nextDue, saved.InvoiceDueDate);
        Assert.Equal(200.20m, saved.CurrentInvoiceAmount);
    }
}
