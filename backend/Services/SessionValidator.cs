using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ControlFinance.API.Services;

// Checagem por requisição de que a sessão ainda vale: usuário existe, está ativo e o token foi
// emitido na versão de sessão atual (User.TokenVersion). É o que faz "sair de todos os
// dispositivos" e "excluir conta" derrubarem sessões abertas em outros aparelhos, em vez de
// elas continuarem válidas até o JWT expirar.
public class SessionValidator(AppDbContext db, IMemoryCache cache, IServiceScopeFactory? scopes = null)
{
    // Cache curto só pra leituras: requisições que alteram dados ou exportam a conta pedem
    // bypassCache e sempre consultam o banco. Assim, depois de "sair de todos os aparelhos" ou de
    // excluir a conta, nenhuma instância aceita escrita/exportação com a sessão antiga; leituras
    // em outras instâncias podem passar por até 10 s (antes, 30 s para tudo).
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);

    private static readonly SharedDbLoad<Guid, SessionState?> Loads = new();

    // Incrementada por Invalidate. Uma leitura direta (bypassCache) que começou antes de um "sair de todos os
    // aparelhos" terminaria depois dele e gravaria no cache o estado antigo, aceito por leituras por até 10 s;
    // com a geração ela só grava se nada foi invalidado no meio.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, long> Generations = new();

    private static string Key(Guid userId) => $"session-state:{userId}";

    public async Task<bool> IsValidAsync(Guid userId, int tokenVersion, bool bypassCache = false, CancellationToken ct = default)
    {
        SessionState? state;
        if (bypassCache)
        {
            var generation = Generations.GetValueOrDefault(userId);
            state = await LoadAsync(userId, ct);
            if (Generations.GetValueOrDefault(userId) == generation)
                cache.Set(Key(userId), state, CacheTtl);
        }
        else if (!cache.TryGetValue(Key(userId), out state))
        {
            state = scopes is null
                ? await LoadAsync(userId, ct)
                : await Loads.GetAsync(userId, scopes, shared => Load(shared, userId, CancellationToken.None),
                    loaded => cache.Set(Key(userId), loaded, CacheTtl), ct);
            if (scopes is null) cache.Set(Key(userId), state, CacheTtl);
        }

        return state is { IsActive: true } && state.TokenVersion == tokenVersion;
    }

    public void Invalidate(Guid userId)
    {
        Generations.AddOrUpdate(userId, 1, (_, current) => current + 1);
        Loads.Forget(userId);
        cache.Remove(Key(userId));
    }

    private Task<SessionState?> LoadAsync(Guid userId, CancellationToken ct) => Load(db, userId, ct);

    private static Task<SessionState?> Load(AppDbContext db, Guid userId, CancellationToken ct) =>
        db.Users
            .Where(u => u.Id == userId)
            .Select(u => new SessionState(u.IsActive, u.TokenVersion))
            .FirstOrDefaultAsync(ct);

    public sealed record SessionState(bool IsActive, int TokenVersion);
}
