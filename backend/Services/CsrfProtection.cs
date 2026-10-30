using System.Security.Cryptography;
using System.Text;

namespace ControlFinance.API.Services;

// Com a sessão num cookie, o navegador manda esse cookie sozinho em qualquer requisição pra API,
// inclusive as disparadas por outro site. Pra uma requisição que altera algo (não GET/HEAD/
// OPTIONS) autenticada pelo cookie, exige o cabeçalho X-CSRF-Token igual ao segredo "csrf"
// gravado dentro do próprio JWT. O frontend recebe esse valor no corpo de /auth/login,
// /auth/register e /auth/me (respostas que outro site não consegue ler, por causa do CORS) e o
// guarda só em memória.
//
// Não usa o padrão "cookie legível + cabeçalho" (double-submit) porque frontend e API podem estar
// em origens diferentes: o JavaScript do frontend não consegue ler cookie da origem da API.
// Requisições com cabeçalho Authorization (Swagger, clientes de API) não carregam cookie
// ambiente e não podem ser forjadas por outro site, então ficam de fora.
public static class CsrfProtection
{
    public const string HeaderName = "X-CSRF-Token";

    public static IApplicationBuilder UseCsrfProtection(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var method = context.Request.Method;
        var isSafe = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
        var usesCookieSession = context.User.Identity?.IsAuthenticated == true
            && !context.Request.Headers.ContainsKey("Authorization");

        if (!isSafe && usesCookieSession)
        {
            var expected = context.User.FindFirst(SessionClaims.Csrf)?.Value;
            var received = context.Request.Headers[HeaderName].ToString();

            if (string.IsNullOrEmpty(expected) || !FixedTimeEquals(expected, received))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { message = "Sessão inválida. Recarregue a página e tente novamente." });
                return;
            }
        }

        await next();
    });

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
