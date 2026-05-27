namespace ControlFinance.API.Services;

public class RateLimitService
{
    private readonly Dictionary<string, (int attempts, DateTime lastAttempt)> _attempts = new();
    private const int MaxAttempts = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public bool IsBlocked(string identifier)
    {
        if (!_attempts.TryGetValue(identifier, out var entry)) return false;
        if (DateTime.UtcNow - entry.lastAttempt > Window)
        {
            _attempts.Remove(identifier);
            return false;
        }
        return entry.attempts >= MaxAttempts;
    }

    public void RegisterFailure(string identifier)
    {
        if (_attempts.TryGetValue(identifier, out var entry))
            _attempts[identifier] = (entry.attempts + 1, DateTime.UtcNow);
        else
            _attempts[identifier] = (1, DateTime.UtcNow);
    }

    public void RegisterSuccess(string identifier) => _attempts.Remove(identifier);

    public int RemainingAttempts(string identifier)
    {
        if (!_attempts.TryGetValue(identifier, out var entry)) return MaxAttempts;
        return Math.Max(0, MaxAttempts - entry.attempts);
    }
}
