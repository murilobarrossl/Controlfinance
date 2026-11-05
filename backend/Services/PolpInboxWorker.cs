using System.Net;
using ControlFinance.API.Data;

namespace ControlFinance.API.Services;

public interface IPolpEventHandler
{
    Task HandleAsync(PolpInboxEvent evt, CancellationToken ct);
}

// Um evento por vez por instância: o rate limit da Polp é global para o app (240 req/min), então
// paralelizar aqui só trocaria eventos processados por 429.
public sealed class PolpInboxWorker(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<PolpInboxWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RenewEvery = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(30);
    public const int MaxAttempts = 8;
    public const int MaxErrorLength = 500;

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromHours(1),
        TimeSpan.FromHours(6), TimeSpan.FromHours(6), TimeSpan.FromHours(6),
    ];

    public static TimeSpan RetryDelay(int attempts) => RetryDelays[Math.Clamp(attempts, 1, RetryDelays.Length) - 1];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan? delay = null;
            try
            {
                if (!await ProcessNextAsync(stoppingToken)) delay = PollInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError("Falha no worker da inbox da Polp ({ErrorType})", ex.GetType().Name);
                delay = ErrorDelay;
            }

            if (delay is { } wait)
            {
                try { await Task.Delay(wait, clock, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    /// <summary>Processa no máximo um evento. Devolve false se não havia nenhum disponível.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken stoppingToken)
    {
        ClaimedPolpEvent? claim;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            claim = (await PolpInboxStore.ClaimAsync(db, PolpInboxTime.Now(clock), Lease, 1, MaxAttempts, stoppingToken)).FirstOrDefault();
        }
        if (claim is null) return false;

        if (claim.Dead)
        {
            logger.LogError("Evento {EventId} da Polp ({Event}) descartado após {Attempts} tentativa(s): {Error}",
                claim.Id, claim.Event.Event, claim.Attempts, PolpInboxStore.LeaseExpiredError);
            return true;
        }

        using var renewal = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeat = KeepLeaseAsync(claim, renewal.Token);

        Exception? failure = null;
        try
        {
            using var scope = scopes.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<IPolpEventHandler>();
            await handler.HandleAsync(claim.Event, stoppingToken);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        await renewal.CancelAsync();
        var lockedUntil = await heartbeat;

        // Finalização com CancellationToken.None: no desligamento o evento ainda precisa voltar
        // para a fila em vez de ficar preso em Processing até o lease vencer.
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await FinishAsync(db, claim, lockedUntil, failure, stoppingToken);
        }

        return true;
    }

    private async Task FinishAsync(AppDbContext db, ClaimedPolpEvent claim, DateTime lockedUntil, Exception? failure, CancellationToken stoppingToken)
    {
        var now = PolpInboxTime.Now(clock);
        var ct = CancellationToken.None;
        bool owned;

        if (failure is null)
        {
            var outcome = await PolpInboxStore.CompleteAsync(db, claim, lockedUntil, now, ct);
            owned = outcome != PolpInboxCompletion.LeaseLost;
        }
        else if (failure is OperationCanceledException && stoppingToken.IsCancellationRequested)
        {
            owned = await PolpInboxStore.ReleaseAsync(db, claim, lockedUntil, now, ct);
        }
        else
        {
            var attempts = claim.Attempts + 1;
            var error = Describe(failure);

            if (IsPermanent(failure) || attempts >= MaxAttempts)
            {
                owned = await PolpInboxStore.DeadAsync(db, claim, lockedUntil, attempts, now, error, ct);
                logger.LogError("Evento {EventId} da Polp ({Event}) descartado após {Attempts} tentativa(s): {Error}",
                    claim.Id, claim.Event.Event, attempts, error);
            }
            else
            {
                owned = await PolpInboxStore.RetryAsync(db, claim, lockedUntil, attempts, now + RetryDelay(attempts), error, ct);
                logger.LogWarning("Evento {EventId} da Polp ({Event}) falhou na tentativa {Attempts}: {Error}",
                    claim.Id, claim.Event.Event, attempts, error);
            }
        }

        if (!owned)
            logger.LogWarning("Evento {EventId} da Polp: lease perdido antes de finalizar; outra execução assumiu", claim.Id);
    }

    private async Task<DateTime> KeepLeaseAsync(ClaimedPolpEvent claim, CancellationToken stop)
    {
        var lockedUntil = claim.LockedUntil;
        try
        {
            while (true)
            {
                await Task.Delay(RenewEvery, clock, stop);
                try
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var renewed = await PolpInboxStore.RenewAsync(db, claim, lockedUntil, PolpInboxTime.Now(clock) + Lease, stop);
                    if (renewed is null) return lockedUntil;
                    lockedUntil = renewed.Value;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Falha ao renovar o lease do evento {EventId} da Polp ({ErrorType})", claim.Id, ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return lockedUntil;
        }
    }

    public static bool IsPermanent(Exception ex) =>
        ex is PolpPermanentException
        || ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound };

    // Só tipo, status HTTP e mensagens escritas por nós: mensagem de exceção de terceiros pode
    // trazer trecho do corpo da Polp ou valor de coluna.
    public static string Describe(Exception ex)
    {
        var text = ex switch
        {
            PolpPermanentException or PolpResourceNotReadyException => $"{ex.GetType().Name}: {ex.Message}",
            HttpRequestException { StatusCode: { } status } => $"{ex.GetType().Name} HTTP {(int)status}",
            _ => ex.GetType().Name
        };
        return text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];
    }
}
