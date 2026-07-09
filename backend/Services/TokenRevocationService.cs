using ControlFinance.API.Data;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public interface ITokenRevocationService
{
    Task RevokeAsync(string jti, DateTime expiresAt);
    Task<bool> IsRevokedAsync(string jti);
}

public class TokenRevocationService : ITokenRevocationService
{
    private readonly AppDbContext _db;

    public TokenRevocationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task RevokeAsync(string jti, DateTime expiresAt)
    {
        // Aproveita a escrita pra descartar entradas já expiradas (evita crescimento indefinido
        // da tabela) — o próprio ValidateLifetime do JWT já rejeitaria esses tokens de qualquer forma.
        var stale = _db.RevokedTokens.Where(t => t.ExpiresAt < DateTime.UtcNow);
        _db.RevokedTokens.RemoveRange(stale);

        if (!await _db.RevokedTokens.AnyAsync(t => t.Jti == jti))
            _db.RevokedTokens.Add(new RevokedToken { Jti = jti, ExpiresAt = expiresAt });

        await _db.SaveChangesAsync();
    }

    public Task<bool> IsRevokedAsync(string jti) =>
        _db.RevokedTokens.AnyAsync(t => t.Jti == jti);
}
