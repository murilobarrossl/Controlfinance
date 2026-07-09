namespace ControlFinance.API.Models;

// Guarda o "jti" de tokens invalidados manualmente (logout) antes da expiração natural.
// JWT é stateless por padrão — sem isso, um token vazado continuaria válido até expirar.
public class RevokedToken
{
    public string Jti { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
