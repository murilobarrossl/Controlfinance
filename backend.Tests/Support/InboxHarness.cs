using System.Collections.Concurrent;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ControlFinance.API.Tests.Support;

public sealed class ListLogger<T> : ILogger<T>
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Enqueue((logLevel, formatter(state, exception)));
}

public sealed class FakeEventHandler : IPolpEventHandler
{
    public Func<PolpInboxEvent, CancellationToken, Task> OnHandle { get; set; } = (_, _) => Task.CompletedTask;
    public ConcurrentQueue<PolpInboxEvent> Handled { get; } = new();

    public async Task HandleAsync(PolpInboxEvent evt, CancellationToken ct)
    {
        Handled.Enqueue(evt);
        await OnHandle(evt, ct);
    }
}

// Escopos de DI de verdade sobre o SQLite em memória do TestDb: inbox e worker abrem contextos
// próprios, como em produção.
public sealed class InboxHarness : IDisposable
{
    public static readonly DateTime Start = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private readonly ServiceProvider _services;

    public TestDb T { get; }
    public FakeEventHandler Handler { get; } = new();
    public ListLogger<PolpInboxWorker> WorkerLog { get; } = new();
    public IServiceScopeFactory Scopes => _services.GetRequiredService<IServiceScopeFactory>();
    public PolpInbox Inbox { get; }
    public PolpInboxWorker Worker { get; }

    public InboxHarness()
    {
        T = new TestDb();
        T.Clock.UtcNow = Start;
        _services = new ServiceCollection()
            .AddScoped(_ => T.NewContext())
            .AddSingleton<IPolpEventHandler>(Handler)
            .BuildServiceProvider(validateScopes: true);
        Inbox = new PolpInbox(Scopes, T.Clock);
        Worker = new PolpInboxWorker(Scopes, T.Clock, WorkerLog);
    }

    public DateTime Now
    {
        get => T.Clock.GetUtcNow().UtcDateTime;
        set => T.Clock.UtcNow = new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    public List<PolpWebhookEvent> Rows()
    {
        using var db = T.NewContext();
        return db.PolpWebhookEvents.AsNoTracking().OrderBy(e => e.ReceivedAt).ToList();
    }

    public PolpWebhookEvent Single() => Assert.Single(Rows());

    public void Insert(PolpWebhookEvent row)
    {
        using var db = T.NewContext();
        db.PolpWebhookEvents.Add(row);
        db.SaveChanges();
    }

    public AppDbContext NewContext() => T.NewContext();

    public void Dispose()
    {
        _services.Dispose();
        T.Dispose();
    }
}
