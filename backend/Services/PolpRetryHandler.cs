using System.Net;

namespace ControlFinance.API.Services;

// O limite de requisições da Polp é global por x-api-client (compartilhado por todos os usuários),
// então 429 e 5xx passageiros são esperados. Só GET é repetido: repetir um POST /consents que
// chegou a ser processado criaria outro consentimento e consumiria a cota regulatória do CPF.
public sealed class PolpRetryHandler : DelegatingHandler
{
    public const int MaxAttempts = 3;
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<double> _random;
    private readonly TimeSpan _attemptTimeout;
    private readonly TimeProvider _time;
    private readonly ILogger<PolpRetryHandler>? _logger;

    public PolpRetryHandler(ILogger<PolpRetryHandler> logger)
        : this(Task.Delay, Random.Shared.NextDouble, AttemptTimeout, TimeProvider.System, logger)
    {
    }

    internal PolpRetryHandler(Func<TimeSpan, CancellationToken, Task> delay, Func<double> random,
        TimeSpan attemptTimeout, TimeProvider time, ILogger<PolpRetryHandler>? logger = null)
    {
        _delay = delay;
        _random = random;
        _attemptTimeout = attemptTimeout;
        _time = time;
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var retryable = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;

        for (var attempt = 1; ; attempt++)
        {
            var isLast = !retryable || attempt >= MaxAttempts;
            HttpResponseMessage response;

            using (var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                attemptCts.CancelAfter(_attemptTimeout);
                try
                {
                    response = await base.SendAsync(request, attemptCts.Token);
                }
                catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    if (isLast)
                        throw new TaskCanceledException($"A Polp não respondeu em {_attemptTimeout.TotalSeconds:0} s.", ex);

                    _logger?.LogWarning("Polp API: tempo esgotado na tentativa {Attempt} de {Method}", attempt, request.Method);
                    await _delay(Backoff(attempt), cancellationToken);
                    continue;
                }
            }

            if (isLast || !IsTransient(response.StatusCode))
                return response;

            var wait = Backoff(attempt);
            if (RetryAfter(response) is { } retryAfter)
            {
                if (retryAfter > MaxRetryAfter)
                    return response;
                wait = retryAfter;
            }

            _logger?.LogWarning("Polp API: HTTP {Status} na tentativa {Attempt} de {Method}",
                (int)response.StatusCode, attempt, request.Method);
            response.Dispose();
            await _delay(wait, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private TimeSpan Backoff(int attempt) =>
        TimeSpan.FromMilliseconds(_random() * BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (header?.Date is { } date)
        {
            var wait = date - _time.GetUtcNow();
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }
        return null;
    }
}
