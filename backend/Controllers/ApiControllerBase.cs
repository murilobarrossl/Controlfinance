using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace ControlFinance.API.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    // Lê o "sub" explicitamente em vez de depender só do mapeamento implícito que o handler
    // JWT faz de "sub" para ClaimTypes.NameIdentifier: esse mapeamento some se algum dia
    // MapInboundClaims = false for configurado (comum ao migrar para JsonWebTokenHandler), e
    // toda rota autenticada quebraria de uma vez com NullReferenceException.
    protected Guid UserId
    {
        get
        {
            var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(sub, out var userId))
                throw new InvalidOperationException("Token autenticado sem claim 'sub' válida.");

            return userId;
        }
    }
}
