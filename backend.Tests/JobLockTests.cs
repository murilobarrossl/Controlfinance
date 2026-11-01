using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlFinance.API.Tests;

public class JobLockTests
{
    // Segura o primeiro envio até o teste liberar: garante que a segunda rodada começa enquanto a
    // primeira ainda está no meio, com o lock na mão.
    private sealed class GatedEmail : HttpMessageHandler
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public TaskCompletionSource FirstSendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<(string To, string Subject)> Sent { get; } = new();

        public void Release() => _release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Sent.Enqueue((json.RootElement.GetProperty("to")[0].GetString()!, json.RootElement.GetProperty("subject").GetString()!));
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstSendStarted.TrySetResult();
                await _release.Task;
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        public EmailService Service() => new(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Resend:ApiKey"] = "teste" }).Build(),
            new HttpClient(this));
    }

    private static readonly DateTime Due = LocalTime.Day(2026, 12, 1);
    private static readonly DateTimeOffset NineAmSaoPaulo = LocalTime.At(Due.AddHours(9), -3);

    private static User AddUser(TestDb t, string email)
    {
        var user = new User
        {
            Name = "Outro", Email = email, DocumentHash = Guid.NewGuid().ToString("N"), PasswordHash = "x",
            TimeZone = "America/Sao_Paulo", MonthlySummaryEnabled = true,
        };
        t.Db.Users.Add(user);
        t.Db.SaveChanges();
        return user;
    }

    private static void AddDueAndLastMonth(TestDb t, Guid userId)
    {
        t.Db.Transactions.AddRange(
            new Transaction { UserId = userId, Name = "conta", Type = TransactionType.Expense, Status = TransactionStatus.Pending, Amount = 10m, DueDate = Due },
            new Transaction { UserId = userId, Name = "mes passado", Type = TransactionType.Expense, Status = TransactionStatus.Paid, Amount = 20m, DueDate = Due.AddDays(-15) });
        t.Db.SaveChanges();
    }

    [Fact]
    public async Task Duas_rodadas_simultaneas_so_uma_envia_e_cada_email_sai_uma_unica_vez()
    {
        using var t = LocalTime.NewDb("America/Sao_Paulo", monthlySummary: true);
        AddDueAndLastMonth(t, t.User.Id);
        var other = AddUser(t, "outro@teste.com");
        AddDueAndLastMonth(t, other.Id);
        var jobLock = new FakeJobLock();
        var email = new GatedEmail();

        var first = ScheduledEmailService.RunRoundAsync(jobLock, t.Db, email.Service(), NineAmSaoPaulo, NullLogger.Instance, default);
        await email.FirstSendStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await using (var secondDb = t.NewContext())
        {
            var second = await ScheduledEmailService.RunRoundAsync(jobLock, secondDb, email.Service(), NineAmSaoPaulo, NullLogger.Instance, default);
            Assert.False(second);
        }

        email.Release();
        Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(jobLock.IsHeld(JobLockKeys.ScheduledEmail));

        await using (var laterDb = t.NewContext())
            Assert.True(await ScheduledEmailService.RunRoundAsync(jobLock, laterDb, email.Service(), NineAmSaoPaulo.AddHours(1), NullLogger.Instance, default));

        const string reminder = "Você tem contas perto do vencimento";
        const string notice = "Seu mês fechou: veja o resumo no Control Finance";
        Assert.Equal(
            new[] { (t.User.Email, reminder), (t.User.Email, notice), ("outro@teste.com", reminder), ("outro@teste.com", notice) }.Order(),
            email.Sent.Order());
    }

    [Fact]
    public async Task Rodada_que_lanca_excecao_libera_o_lock()
    {
        using var t = LocalTime.NewDb("America/Sao_Paulo");
        AddDueAndLastMonth(t, t.User.Id);
        var jobLock = new FakeJobLock();
        var email = new RecordingEmail();
        var broken = t.NewContext();
        broken.Dispose();

        await Assert.ThrowsAnyAsync<ObjectDisposedException>(() =>
            ScheduledEmailService.RunRoundAsync(jobLock, broken, email.Service(), NineAmSaoPaulo, NullLogger.Instance, default));

        Assert.False(jobLock.IsHeld(JobLockKeys.ScheduledEmail));
        Assert.True(await ScheduledEmailService.RunRoundAsync(jobLock, t.Db, email.Service(), NineAmSaoPaulo, NullLogger.Instance, default));
        Assert.Single(email.Sent);
    }

    [Theory]
    [InlineData(JobLockKeys.ScheduledEmail)]
    [InlineData(JobLockKeys.PendingDisconnectRetry)]
    [InlineData(JobLockKeys.DataRetention)]
    public async Task RunExclusive_libera_o_lock_quando_a_rodada_falha(long key)
    {
        var jobLock = new FakeJobLock();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            jobLock.RunExclusiveAsync(key, () => throw new InvalidOperationException("falhou"), default));

        Assert.False(jobLock.IsHeld(key));
        Assert.True(await jobLock.RunExclusiveAsync(key, () => Task.CompletedTask, default));
    }

    [Fact]
    public async Task Rodada_sem_o_lock_nao_envia_nem_grava_nada()
    {
        using var t = LocalTime.NewDb("America/Sao_Paulo", monthlySummary: true);
        AddDueAndLastMonth(t, t.User.Id);
        var jobLock = new FakeJobLock();
        var email = new RecordingEmail();
        var held = await jobLock.TryAcquireAsync(JobLockKeys.ScheduledEmail, default);
        Assert.NotNull(held);

        Assert.False(await ScheduledEmailService.RunRoundAsync(jobLock, t.Db, email.Service(), NineAmSaoPaulo, NullLogger.Instance, default));

        Assert.Empty(email.Sent);
        Assert.False(t.Db.ChangeTracker.HasChanges());
        await using (var db = t.NewContext())
        {
            var user = db.Users.Single(u => u.Id == t.User.Id);
            Assert.Null(user.LastDueReminderLocalDate);
            Assert.Null(user.LastMonthlySummaryMonth);
            Assert.Null(user.LastMonthlySummarySentAt);
            Assert.All(db.Transactions, tr => Assert.Null(tr.ReminderSentAt));
        }

        await held!.DisposeAsync();
        Assert.True(await ScheduledEmailService.RunRoundAsync(jobLock, t.Db, email.Service(), NineAmSaoPaulo, NullLogger.Instance, default));
        Assert.Equal(2, email.Sent.Count);
    }

    [Fact]
    public async Task Cada_job_tem_sua_chave_e_uma_nao_bloqueia_a_outra()
    {
        long[] keys = [JobLockKeys.ScheduledEmail, JobLockKeys.PendingDisconnectRetry, JobLockKeys.DataRetention];
        Assert.Equal(keys.Length, keys.Distinct().Count());

        var jobLock = new FakeJobLock();
        var handles = new List<IAsyncDisposable>();
        foreach (var key in keys)
            handles.Add(Assert.IsAssignableFrom<IAsyncDisposable>(await jobLock.TryAcquireAsync(key, default)));
        foreach (var key in keys)
            Assert.Null(await jobLock.TryAcquireAsync(key, default));
        foreach (var handle in handles)
            await handle.DisposeAsync();
    }
}
