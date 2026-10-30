using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ControlFinance.API.Models;
using Microsoft.IdentityModel.Tokens;

namespace ControlFinance.API.Services;

public record IssuedToken(string Jwt, string CsrfToken, DateTime ExpiresAt);

public interface ITokenService
{
    IssuedToken GenerateToken(User user);
}

public static class SessionClaims
{
    /// <summary>Versão de sessão do usuário no momento da emissão (User.TokenVersion).</summary>
    public const string TokenVersion = "tv";

    /// <summary>Token anti-CSRF amarrado a esta sessão. Ver CsrfProtection.</summary>
    public const string Csrf = "csrf";
}

public class TokenService : ITokenService
{
    private readonly IConfiguration _config;

    public TokenService(IConfiguration config)
    {
        _config = config;
    }

    public IssuedToken GenerateToken(User user)
    {
        var jwtSettings = _config.GetSection("JwtSettings");
        var secretKey   = jwtSettings["SecretKey"]!;
        var issuer      = jwtSettings["Issuer"]!;
        var audience    = jwtSettings["Audience"]!;
        var expiresInHours = int.TryParse(jwtSettings["ExpiresInHours"], out var h) ? h : 8;

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var csrf  = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var expiresAt = DateTime.UtcNow.AddHours(expiresInHours);

        // Só o necessário pra autenticar: id, identificador do token, versão de sessão e o
        // segredo anti-CSRF. E-mail saiu do token (nenhuma rota lia daqui) — JWT é só assinado,
        // não cifrado, e qualquer cópia dele carregava o e-mail junto.
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(SessionClaims.TokenVersion, user.TokenVersion.ToString()),
            new Claim(SessionClaims.Csrf, csrf),
        };

        var token = new JwtSecurityToken(
            issuer:             issuer,
            audience:           audience,
            claims:             claims,
            expires:            expiresAt,
            signingCredentials: creds
        );

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), csrf, expiresAt);
    }
}
