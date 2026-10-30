namespace ControlFinance.API.Services;

// Cookie da sessão. httpOnly: nenhum script da página (nem um XSS) consegue ler o JWT; antes ele
// ficava no localStorage, acessível a qualquer JavaScript da origem.
//
// SameSite vem de configuração (Auth:CookieSameSite): "Lax" quando frontend e API estão no mesmo
// site (ex.: app.dominio.com.br e api.dominio.com.br — subdomínios do mesmo domínio registrável
// contam como mesmo site), que é o cenário recomendado. "None" só se estiverem em domínios
// diferentes; nesse caso o cookie vira "de terceiro" e navegadores que bloqueiam cookies de
// terceiros (Safari, e o Chrome conforme configuração) vão impedir o login. Ver relatório.
public static class SessionCookie
{
    public const string Name = "access_token";

    public static void Append(HttpContext context, IConfiguration config, string jwt, DateTime expiresAt)
    {
        context.Response.Cookies.Append(Name, jwt, BuildOptions(context, config, expiresAt));
    }

    public static void Delete(HttpContext context, IConfiguration config)
    {
        context.Response.Cookies.Delete(Name, BuildOptions(context, config, null));
    }

    private static CookieOptions BuildOptions(HttpContext context, IConfiguration config, DateTime? expiresAt)
    {
        var sameSite = Enum.TryParse<SameSiteMode>(config["Auth:CookieSameSite"], true, out var parsed)
            ? parsed
            : SameSiteMode.Lax;
        var isDevelopment = context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();

        return new CookieOptions
        {
            HttpOnly = true,
            // Fora de desenvolvimento, sempre Secure (SameSite=None também exige Secure).
            Secure = !isDevelopment || context.Request.IsHttps || sameSite == SameSiteMode.None,
            SameSite = sameSite,
            Path = "/api",
            Expires = expiresAt,
            IsEssential = true,
        };
    }
}
