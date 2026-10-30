using ControlFinance.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ControlFinance.API.Services;

// Checagem por requisição de que a sessão ainda vale: usuário existe, está ativo e o token foi
// emitido na versão de sessão atual (User.TokenVersion). É o que faz "sair de todos os
// dispositivos" e "excluir conta" derrubarem sessões abertas em outros aparelhos, em vez de
// elas continuarem válidas até o JWT expirar.
public class SessionValidator(AppDbContext db, IMemoryCache cache)
{
    // Cache curto só pra leituras: requisições que alteram dados ou exportam a conta pedem
    // bypassCache e sempre consultam o banco. Assim, depois de "sair de todos os aparelhos" ou de
    // excluir a conta, nenhuma instância aceita escrita/exportação com a sessão antiga; leituras
    // em outras instâncias podem passar por até 10 s (antes, 30 s para tudo).
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);

    private static string Key(Guid userId) => $"session-state:{userId}";

    public async Task<bool> IsValidAsync(Guid userId, int tokenVersion, bool bypassCache = false)
    {
        SessionState? state;
        if (bypassCache)
        {
            state = await LoadAsync(userId);
            cache.Set(Key(userId), state, CacheTtl);
        }
        else
        {
            state = await cache.GetOrCreateAsync(Key(userId), async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheTtl;
                return await LoadAsync(userId);
            });
        }

        return state is { IsActive: true } && state.TokenVersion == tokenVersion;
    }

    public void Invalidate(Guid userId) => cache.Remove(Key(userId));

    private Task<SessionState?> LoadAsync(Guid userId) =>
        db.Users
            .Where(u => u.Id == userId)
            .Select(u => new SessionState(u.IsActive, u.TokenVersion))
            .FirstOrDefaultAsync();

    private record SessionState(bool IsActive, int TokenVersion);
}
