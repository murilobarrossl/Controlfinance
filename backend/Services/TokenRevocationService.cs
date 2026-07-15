using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ControlFinance.API.Services;

public interface ITokenRevocationService
{
    Task RevokeAsync(string jti, DateTime expiresAt);
    Task<bool> IsRevokedAsync(string jti);
}

public class TokenRevocationService : ITokenRevocationService
{
    // TTL curto porque essa checagem roda em toda requisição autenticada (OnTokenValidated):
    // sem cache seria um SELECT a mais em 100% do tráfego. Curto o bastante pra um logout se
    // propagar rápido para outras instâncias, que não recebem o Set direto do RevokeAsync abaixo.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;

    public TokenRevocationService(AppDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task RevokeAsync(string jti, DateTime expiresAt)
    {
        // Aproveita a escrita pra descartar entradas já expiradas (evita crescimento indefinido
        // da tabela); o próprio ValidateLifetime do JWT já rejeitaria esses tokens de qualquer forma.
        await _db.RevokedTokens.Where(t => t.ExpiresAt < DateTime.UtcNow).ExecuteDeleteAsync();

        if (!await _db.RevokedTokens.AnyAsync(t => t.Jti == jti))
            _db.RevokedTokens.Add(new RevokedToken { Jti = jti, ExpiresAt = expiresAt });

        await _db.SaveChangesAsync();

        // Marca no cache imediatamente: sem isso, um IsRevokedAsync que acabou de cachear
        // "não revogado" para esse jti continuaria aceitando o token até o cache expirar.
        _cache.Set(CacheKey(jti), true, CacheTtl);
    }

    public async Task<bool> IsRevokedAsync(string jti)
    {
        if (_cache.TryGetValue(CacheKey(jti), out bool cached))
            return cached;

        var revoked = await _db.RevokedTokens.AnyAsync(t => t.Jti == jti);
        _cache.Set(CacheKey(jti), revoked, CacheTtl);
        return revoked;
    }

    private static string CacheKey(string jti) => $"revoked-token:{jti}";
}
