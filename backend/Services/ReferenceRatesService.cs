using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace ControlFinance.API.Services;

public record ReferenceRate(decimal AnnualPercent, DateOnly Date);

public record ReferenceRatesDto(ReferenceRate? SelicTarget, ReferenceRate? SelicEffective, ReferenceRate? Cdi, string Source);

// Séries públicas do SGS do Banco Central. Selic meta (432) define a regra legal da poupança;
// Selic efetiva (1178) é o indexador do Tesouro Selic; CDI (4389) é a base de caixinhas e CDBs.
// São séries distintas de propósito: tratar Selic e CDI como a mesma taxa distorce as projeções.
// Nenhum dado do usuário sai nessa chamada. Sem resposta válida, a taxa volta nula e a tela não
// projeta: não existe valor padrão embutido.
public class ReferenceRatesService(HttpClient http, IMemoryCache cache, ILogger<ReferenceRatesService> logger)
{
    public const string SourceLabel = "Banco Central do Brasil (SGS)";
    private const string CacheKey = "reference-rates";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(10);

    public async Task<ReferenceRatesDto> GetAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out ReferenceRatesDto? cached) && cached is not null)
            return cached;

        var selicTarget = FetchLatestAsync(432, ct);
        var selicEffective = FetchLatestAsync(1178, ct);
        var cdi = FetchLatestAsync(4389, ct);
        await Task.WhenAll(selicTarget, selicEffective, cdi);

        var result = new ReferenceRatesDto(selicTarget.Result, selicEffective.Result, cdi.Result, SourceLabel);
        var complete = result.SelicTarget is not null && result.SelicEffective is not null && result.Cdi is not null;
        cache.Set(CacheKey, result, complete ? CacheDuration : FailureCacheDuration);
        return result;
    }

    private async Task<ReferenceRate?> FetchLatestAsync(int series, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync($"dados/serie/bcdata.sgs.{series}/dados/ultimos/1?formato=json", ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("SGS série {Series} respondeu {Status}", series, (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;

            var last = doc.RootElement[doc.RootElement.GetArrayLength() - 1];
            if (!DateOnly.TryParseExact(last.GetProperty("data").GetString(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !decimal.TryParse(last.GetProperty("valor").GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                || value is <= 0 or > 100)
                return null;

            if (DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - date.DayNumber > MaxAge.Days)
            {
                logger.LogWarning("SGS série {Series} sem valor recente (último em {Date})", series, date);
                return null;
            }

            return new ReferenceRate(value, date);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            if (ct.IsCancellationRequested) throw;
            logger.LogWarning("SGS série {Series} indisponível: {Error}", series, ex.GetType().Name);
            return null;
        }
    }
}
