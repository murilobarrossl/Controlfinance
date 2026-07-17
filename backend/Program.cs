using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ControlFinance.API.Data;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Usado tanto pros cookies de sessão/CSRF quanto pra config já existente (Hsts, Swagger):
// localhost em portas diferentes é "same-site" e funciona sem HTTPS local; em produção, o
// frontend e a API ficam em subdomínios diferentes, então precisa de SameSite=None + Secure.
var isDevelopment = builder.Environment.IsDevelopment();

// Segredos reais ficam em appsettings.Local.json (gitignorado) ou em variáveis de
// ambiente: appsettings.json versionado no git só tem placeholders.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// ──────────────────────────────────────────
//  BANCO DE DADOS
// ──────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ──────────────────────────────────────────
//  SERVIÇOS DA APLICAÇÃO
// ──────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenRevocationService, TokenRevocationService>();
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddSingleton<RateLimitService>();
builder.Services.AddHttpClient<EmailService>();
builder.Services.AddHostedService<ScheduledEmailService>();

// Guarda o key ring (usado pra assinar/criptografar os tokens CSRF) no Postgres: sem isso,
// cada reinício/redeploy gera uma chave nova e invalida todo token CSRF já emitido, e duas
// instâncias rodando ao mesmo tempo rejeitariam o token uma da outra.
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("ControlFinance");

// CSRF via double-submit cookie: o cookie httpOnly interno do IAntiforgery fica só com o
// framework; o par que o JS lê e ecoa no header (XSRF-TOKEN) é emitido manualmente no
// AuthController. Precisa do mesmo SameSite/Secure do cookie de sessão, senão a validação
// falha sempre em produção.
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.SameSite = isDevelopment ? SameSiteMode.Lax : SameSiteMode.None;
    options.Cookie.SecurePolicy = isDevelopment ? CookieSecurePolicy.None : CookieSecurePolicy.Always;
});

// ──────────────────────────────────────────
//  JWT
// ──────────────────────────────────────────
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey   = jwtSettings["SecretKey"]!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtSettings["Issuer"],
            ValidAudience            = jwtSettings["Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
            ClockSkew                = TimeSpan.Zero
        };

        // Rejeita tokens revogados (logout) mesmo que ainda não tenham expirado:
        // JWT é stateless por padrão, então essa checagem extra é o que torna o logout real.
        options.Events = new JwtBearerEvents
        {
            // Cookie httpOnly é o caminho principal agora; o header Authorization continua
            // funcionando também (Swagger, chamadas de sessões antigas ainda com token salvo).
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token) && context.Request.Cookies.TryGetValue("access_token", out var cookieToken))
                {
                    context.Token = cookieToken;
                }
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var jti = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);
                if (string.IsNullOrEmpty(jti))
                {
                    context.Fail("Token sem identificador.");
                    return;
                }

                var revocation = context.HttpContext.RequestServices.GetRequiredService<ITokenRevocationService>();
                if (await revocation.IsRevokedAsync(jti))
                    context.Fail("Token revogado.");
            }
        };
    });

builder.Services.AddAuthorization();

// ──────────────────────────────────────────
//  CORS
// ──────────────────────────────────────────
// Origens liberadas vêm de config (Cors:AllowedOrigins). Em produção, adicionar o domínio
// real do frontend em appsettings.Local.json ou variável de ambiente, sem tocar no código.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://localhost:3000"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// ──────────────────────────────────────────
//  CONTROLLERS + SWAGGER
// ──────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient<IPolpService, PolpService>();
builder.Services.AddSwaggerGen(c =>

{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title   = "Control Finance API",
        Version = "v1"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name         = "Authorization",
        Type         = SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = ParameterLocation.Header,
        Description  = "Informe o token JWT: Bearer {seu_token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ──────────────────────────────────────────
//  PIPELINE
// ──────────────────────────────────────────
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// Confia nos headers X-Forwarded-* do proxy (Cloudflare/DigitalOcean) na frente da aplicação:
// sem isso, UseHttpsRedirection/UseHsts não enxergam que a conexão já chegou em HTTPS na borda
// e podem causar loop de redirecionamento. Além disso, o RemoteIpAddress usado pelo rate limit
// por IP ficaria sempre igual ao IP do proxy para todo mundo.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
// Por padrão, o ASP.NET Core só aceita esses headers vindos de loopback; atrás do proxy do
// DigitalOcean/Cloudflare eles chegam de um IP externo e seriam descartados silenciosamente.
// Limpar as listas faz confiar em qualquer proxy, seguro aqui porque só o proxy da borda fala
// diretamente com esta aplicação.
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseHttpsRedirection();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Captura qualquer exceção não tratada antes que ela vaze stack trace na resposta:
// precisa vir cedo no pipeline pra cobrir os middlewares seguintes também.
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        if (feature?.Error is { } ex)
            logger.LogError(ex, "Exceção não tratada em {Path}", context.Request.Path);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { message = "Ocorreu um erro inesperado. Tente novamente." });
    });
});

// Headers de segurança básicos: ASP.NET Core não adiciona nada disso por padrão.
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

app.UseCors("FrontendPolicy");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

// Com o cookie de sessão em SameSite=None (necessário pra funcionar entre subdomínios
// diferentes), o navegador manda o cookie sozinho em requisições forjadas de outro site: é
// exatamente isso que o SameSite normalmente bloquearia. Essa checagem de CSRF é o que fecha
// esse buraco. Requisições com header Authorization não são alvo de CSRF (um site atacante não
// consegue anexar esse header, só o navegador manda cookie ambiente sozinho), então ficam de
// fora, e é isso que mantém o Swagger funcionando sem nenhuma mudança nele.
app.Use(async (context, next) =>
{
    var request = context.Request;
    var isSafeMethod = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method);
    var isPreAuthEndpoint = request.Path.StartsWithSegments("/api/auth/login") || request.Path.StartsWithSegments("/api/auth/register");
    var hasAuthorizationHeader = request.Headers.ContainsKey("Authorization");

    if (!isSafeMethod && !isPreAuthEndpoint && !hasAuthorizationHeader)
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message = "Token CSRF inválido ou ausente." });
            return;
        }
    }

    await next();
});

app.MapControllers();

app.Run();
