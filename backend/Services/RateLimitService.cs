using System.Collections.Concurrent;

namespace ControlFinance.API.Services;

// Bloqueia tanto por identificador (email/CPF) quanto por IP — só por identificador permite
// que alguém tente senha em N contas diferentes do mesmo IP sem nunca ser bloqueado.
// ConcurrentDictionary porque esse serviço é singleton e atende requisições concorrentes.
public class RateLimitService
{
    private readonly ConcurrentDictionary<string, (int attempts, DateTime lastAttempt)> _byIdentifier = new();
    private readonly ConcurrentDictionary<string, (int attempts, DateTime lastAttempt)> _byIp = new();

    private const int MaxAttemptsPerIdentifier = 5;
    private const int MaxAttemptsPerIp = 20;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public bool IsBlocked(string identifier, string ipAddress) =>
        IsBlocked(_byIdentifier, identifier, MaxAttemptsPerIdentifier) ||
        IsBlocked(_byIp, ipAddress, MaxAttemptsPerIp);

    public void RegisterFailure(string identifier, string ipAddress)
    {
        RegisterFailure(_byIdentifier, identifier);
        RegisterFailure(_byIp, ipAddress);
    }

    public void RegisterSuccess(string identifier, string ipAddress)
    {
        _byIdentifier.TryRemove(identifier, out _);
        _byIp.TryRemove(ipAddress, out _);
    }

    public int RemainingAttempts(string identifier)
    {
        if (!_byIdentifier.TryGetValue(identifier, out var entry)) return MaxAttemptsPerIdentifier;
        return Math.Max(0, MaxAttemptsPerIdentifier - entry.attempts);
    }

    private static bool IsBlocked(ConcurrentDictionary<string, (int attempts, DateTime lastAttempt)> store, string key, int maxAttempts)
    {
        if (!store.TryGetValue(key, out var entry)) return false;

        if (DateTime.UtcNow - entry.lastAttempt > Window)
        {
            store.TryRemove(key, out _);
            return false;
        }

        return entry.attempts >= maxAttempts;
    }

    private static void RegisterFailure(ConcurrentDictionary<string, (int attempts, DateTime lastAttempt)> store, string key)
    {
        store.AddOrUpdate(
            key,
            _ => (1, DateTime.UtcNow),
            (_, entry) => (entry.attempts + 1, DateTime.UtcNow));
    }
}
