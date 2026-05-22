using System.Collections.Concurrent;

namespace ControlFinance.API.Services;

public interface IRateLimitService
{
    /// <summary>
    /// Retorna true se a tentativa é permitida.
    /// Retorna false (bloqueado) se o limite foi atingido.
    /// </summary>
    bool AllowAttempt(string key);

    /// <summary>Reseta o contador após login bem-sucedido.</summary>
    void Reset(string key);

    /// <summary>Segundos restantes de bloqueio, ou 0 se liberado.</summary>
    int GetLockoutSeconds(string key);
}

public class RateLimitService : IRateLimitService
{
    // Configurações
    private const int MaxAttempts    = 5;     // tentativas antes de bloquear
    private const int WindowSeconds  = 300;   // janela de 5 minutos
    private const int LockoutSeconds = 900;   // 15 minutos bloqueado

    private record AttemptRecord(int Count, DateTime WindowStart, DateTime? LockedUntil);

    private readonly ConcurrentDictionary<string, AttemptRecord> _store = new();

    public bool AllowAttempt(string key)
    {
        var now = DateTime.UtcNow;
        var record = _store.GetOrAdd(key, _ => new AttemptRecord(0, now, null));

        // Ainda está bloqueado?
        if (record.LockedUntil.HasValue && now < record.LockedUntil.Value)
            return false;

        // Janela expirou — reseta
        if ((now - record.WindowStart).TotalSeconds > WindowSeconds)
            record = new AttemptRecord(0, now, null);

        var newCount = record.Count + 1;

        if (newCount > MaxAttempts)
        {
            // Bloqueia
            _store[key] = record with { Count = newCount, LockedUntil = now.AddSeconds(LockoutSeconds) };
            return false;
        }

        _store[key] = record with { Count = newCount };
        return true;
    }

    public void Reset(string key) =>
        _store.TryRemove(key, out _);

    public int GetLockoutSeconds(string key)
    {
        if (!_store.TryGetValue(key, out var record)) return 0;
        if (!record.LockedUntil.HasValue) return 0;

        var remaining = (int)(record.LockedUntil.Value - DateTime.UtcNow).TotalSeconds;
        return remaining > 0 ? remaining : 0;
    }
}
