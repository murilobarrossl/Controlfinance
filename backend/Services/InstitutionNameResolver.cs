using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ControlFinance.API.Services;

// Na v2 o consentimento traz só institution_id, sem o nome: ele vem da lista pública de
// instituições, que muda raramente e por isso fica em cache.
public class InstitutionNameResolver(IPolpService polp, IMemoryCache cache, ILogger<InstitutionNameResolver> logger)
{
    private const string CacheKey = "polp:institution-names";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    public async Task<string?> FindAsync(Guid institutionId, CancellationToken ct)
    {
        var names = await GetNamesAsync(ct);
        return names?.GetValueOrDefault(institutionId);
    }

    public static async Task BackfillAsync(AppDbContext db, InstitutionNameResolver resolver, CancellationToken ct = default)
    {
        var missing = await db.PolpIntegrations.Where(p => p.InstitutionName == null).ToListAsync(ct);
        if (missing.Count == 0) return;

        foreach (var integration in missing)
            integration.InstitutionName = await resolver.FindAsync(integration.InstitutionId, ct);

        await db.SaveChangesAsync(ct);
    }

    private async Task<Dictionary<Guid, string>?> GetNamesAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out Dictionary<Guid, string>? cached)) return cached;

        try
        {
            var names = (await polp.GetInstitutionsAsync(ct))
                .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                .GroupBy(i => i.Id)
                .ToDictionary(g => g.Key, g => g.First().Name);

            if (names.Count > 0) cache.Set(CacheKey, names, CacheDuration);
            return names;
        }
        catch (HttpRequestException)
        {
            logger.LogWarning("Não foi possível carregar a lista de instituições da Polp");
            return null;
        }
    }
}
