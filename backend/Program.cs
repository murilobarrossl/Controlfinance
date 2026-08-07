using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using ControlFinance.API.Data;
using ControlFinance.API.Migrations;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddScoped<PolpSyncService>();
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddSingleton<RateLimitService>();
builder.Services.AddHttpClient<EmailService>();
builder.Services.AddHostedService<ScheduledEmailService>();

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

    // Precisa saber se a migration do Radar de Recorrências ainda não tinha rodado nessa base
    // ANTES de aplicar (Migrate() abaixo já marca ela como aplicada) — é o gatilho do backfill
    // logo depois. Sem essa checagem, toda vez que o app subisse ele tentaria rodar de novo o
    // backfill em cima de linhas que o usuário já classificou manualmente como "conta mista"
    // (Mixed), sobrescrevendo a escolha dele com o chute. Lido via reflection (o [Migration] da
    // classe gerada) em vez de string literal: já testei isso quebrando silenciosamente uma vez,
    // ao regenerar a migration com outro timestamp e esquecer de atualizar o literal à mão.
    var recurrenceRadarMigrationId = typeof(AddRecurrenceRadarSupport)
        .GetCustomAttribute<MigrationAttribute>()!.Id;
    var recurrenceRadarMigrationIsPending = (await db.Database.GetPendingMigrationsAsync())
        .Contains(recurrenceRadarMigrationId);

    db.Database.Migrate();

    if (recurrenceRadarMigrationIsPending)
        await BackfillAccountOwnershipAsync(db);
}

// Chute inicial de pessoal/empresa/mista (ver AccountOwnershipDefault) pras contas que já
// existiam antes da coluna Ownership existir (a coluna nasce com todas as linhas em Personal,
// valor default do enum, então não precisa filtrar quem "ainda não tem" — é sempre todo mundo).
// Roda uma única vez, no exato momento em que a migration acima é aplicada — nunca mais depois
// disso, então uma conta marcada como Mixed pelo usuário fica assim pra sempre, mesmo em
// reinícios futuros do app.
static async Task BackfillAccountOwnershipAsync(AppDbContext db)
{
    var accounts = await db.BankAccounts.Include(b => b.User).ToListAsync();

    foreach (var account in accounts)
        account.Ownership = AccountOwnershipDefault.FromDocument(account.User.Document);

    if (accounts.Count > 0)
        await db.SaveChangesAsync();
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
app.MapControllers();

app.Run();
