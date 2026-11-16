using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ControlFinance.API.Services;

// Na v2 o consentimento traz só institution_id, sem o nome: ele vem da lista pública de
// instituições, que muda raramente e por isso fica em cache. O tipo (PERSONAL/BUSINESS/BOTH) vem
// na mesma lista e é usado pra recusar, antes de chamar a Polp, a conexão de um titular num banco
// que não atende o tipo de documento dele.
public class InstitutionNameResolver(IPolpService polp, IMemoryCache cache, ILogger<InstitutionNameResolver> logger)
{
    private const string CacheKey = "polp:institutions-by-id";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private sealed record Entry(string Name, string? Type);

    public async Task<string?> FindAsync(Guid institutionId, CancellationToken ct)
    {
        var entries = await GetEntriesAsync(ct);
        return entries?.GetValueOrDefault(institutionId)?.Name;
    }

    /// <summary>Tipo informado pela Polp, ou nulo se a instituição ou a lista não estiverem disponíveis.</summary>
    public async Task<string?> FindTypeAsync(Guid institutionId, CancellationToken ct)
    {
        var entries = await GetEntriesAsync(ct);
        return entries?.GetValueOrDefault(institutionId)?.Type;
    }

    public static async Task BackfillAsync(AppDbContext db, InstitutionNameResolver resolver, CancellationToken ct = default)
    {
        var missing = await db.PolpIntegrations.Where(p => p.InstitutionName == null).ToListAsync(ct);
        if (missing.Count == 0) return;

        foreach (var integration in missing)
            integration.InstitutionName = await resolver.FindAsync(integration.InstitutionId, ct);

        await db.SaveChangesAsync(ct);
    }

    private async Task<Dictionary<Guid, Entry>?> GetEntriesAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out Dictionary<Guid, Entry>? cached)) return cached;

        try
        {
            var entries = (await polp.GetInstitutionsAsync(ct))
                .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                .GroupBy(i => i.Id)
                .ToDictionary(g => g.Key, g => new Entry(g.First().Name, g.First().Type));

            if (entries.Count > 0) cache.Set(CacheKey, entries, CacheDuration);
            return entries;
        }
        catch (Exception ex) when (PolpFailure.IsProviderFailure(ex, ct))
        {
            logger.LogWarning("Não foi possível carregar a lista de instituições da Polp");
            return null;
        }
    }
}
