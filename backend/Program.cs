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

// Fora de Development, segredo ausente ou placeholder impede a subida: senão a API sobe usando um
// valor público do repositório como chave.
var secretErrors = ConfigSecrets.Validate(builder.Configuration);
if (secretErrors.Count > 0)
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Configuração de segredos inválida: " + string.Join("; ", secretErrors));

    foreach (var error in secretErrors)
        Console.Error.WriteLine($"ATENÇÃO: {error}");
}

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
builder.Services.AddScoped<PolpDisconnectService>();
builder.Services.AddScoped<PolpWebhookProcessor>();
builder.Services.AddScoped<InstitutionNameResolver>();
builder.Services.AddScoped<SessionValidator>();
builder.Services.AddScoped<AccessLogService>();
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddSingleton<RateLimitService>();
builder.Services.AddHttpClient<EmailService>();
builder.Services.AddHostedService<ScheduledEmailService>();
builder.Services.AddHostedService<PendingDisconnectRetryService>();
builder.Services.AddHostedService<DataRetentionService>();

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

        options.Events = new JwtBearerEvents
        {
            // A sessão do navegador vem no cookie httpOnly (SessionCookie), não no cabeçalho:
            // sem Authorization, lê do cookie. Cabeçalho Authorization continua aceito (Swagger,
            // clientes de API), e requisições assim não passam pela checagem de CSRF.
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token)
                    && !context.Request.Headers.ContainsKey("Authorization")
                    && context.Request.Cookies.TryGetValue(SessionCookie.Name, out var cookieToken))
                {
                    context.Token = cookieToken;
                }
                return Task.CompletedTask;
            },

            // Um JWT válido na assinatura ainda pode ter sido encerrado: logout (revogação por
            // JTI), "sair de todos os dispositivos" (versão de sessão) ou conta excluída/inativa.
            OnTokenValidated = async context =>
            {
                var jti = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);
                var sub = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                    ?? context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var tvClaim = context.Principal?.FindFirstValue(SessionClaims.TokenVersion);

                if (string.IsNullOrEmpty(jti) || !Guid.TryParse(sub, out var userId) || !int.TryParse(tvClaim, out var tokenVersion))
                {
                    context.Fail("Token incompleto.");
                    return;
                }

                // Escrita e exportação nunca usam o cache (ver SessionValidator).
                var request = context.HttpContext.Request;
                var bypassCache = !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
                    || request.Path.StartsWithSegments("/api/account/export");

                var revocation = context.HttpContext.RequestServices.GetRequiredService<ITokenRevocationService>();
                if (await revocation.IsRevokedAsync(jti, bypassCache))
                {
                    context.Fail("Token revogado.");
                    return;
                }

                var sessions = context.HttpContext.RequestServices.GetRequiredService<SessionValidator>();
                if (!await sessions.IsValidAsync(userId, tokenVersion, bypassCache))
                    context.Fail("Sessão encerrada.");
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
// Plano com a Polp pode ficar sem renovar (credenciais ausentes do .env/appsettings) sem que isso
// derrube a aplicação: sem essa checagem, PolpService.ctor lança assim que o ASP.NET tenta montar
// PolpController pra QUALQUER ação dele, inclusive as que só leem do banco (ex.: listar contas já
// sincronizadas pro seletor lateral). NullPolpService devolve listas vazias pras leituras e recusa
// com uma mensagem clara as ações que exigiriam falar de verdade com a Polp (conectar banco novo,
// sincronizar) — reversível: voltando a preencher as duas chaves, a integração real volta sozinha.
if (ConfigSecrets.IsPolpEnabled(builder.Configuration))
{
    builder.Services.AddHttpClient<IPolpService, PolpService>();

    // O Sandbox da Polp (/api/v2/sandbox) gera dados fictícios e nunca conecta ao banco de
    // verdade: em produção, um usuário "conectaria" um banco e veria contas inventadas. Não
    // bloqueia (a configuração de produção é do operador), mas deixa o alerta explícito no log.
    var polpBaseUrl = builder.Configuration["Polp:BaseUrl"] ?? string.Empty;
    if (!builder.Environment.IsDevelopment() && polpBaseUrl.Contains("/sandbox", StringComparison.OrdinalIgnoreCase))
        Console.Error.WriteLine("ATENÇÃO: Polp:BaseUrl aponta para o Sandbox da Polp fora de Development.");
}
else
{
    builder.Services.AddScoped<IPolpService, NullPolpService>();
}
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
        Description  = "Token JWT (opcional: o navegador usa o cookie httpOnly de sessão). Bearer {seu_token}"
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
    // ANTES de aplicar (Migrate() abaixo já marca ela como aplicada). É o gatilho do backfill
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

    // Antes do backfill do Radar: ele lê User.DocumentType, que é preenchido aqui.
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var encryption = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
    await PrivacyBackfill.RunAsync(db, encryption, app.Configuration, startupLogger);
    await InstitutionNameResolver.BackfillAsync(db, scope.ServiceProvider.GetRequiredService<InstitutionNameResolver>());
    // Opt-in (Encryption:RotateOnStartup): regrava colunas cifradas com a chave ativa.
    await EncryptionKeyRotation.RunAsync(db, app.Configuration, startupLogger);

    if (recurrenceRadarMigrationIsPending)
        await BackfillAccountOwnershipAsync(db);
}

// Chute inicial de pessoal/empresa/mista (ver AccountOwnershipDefault) pras contas que já
// existiam antes da coluna Ownership existir (a coluna nasce com todas as linhas em Personal,
// valor default do enum, então não precisa filtrar quem "ainda não tem": é sempre todo mundo).
// Roda uma única vez, no exato momento em que a migration acima é aplicada. Nunca mais depois
// disso, então uma conta marcada como Mixed pelo usuário fica assim pra sempre, mesmo em
// reinícios futuros do app.
static async Task BackfillAccountOwnershipAsync(AppDbContext db)
{
    var accounts = await db.BankAccounts.Include(b => b.User).ToListAsync();

    foreach (var account in accounts)
        account.Ownership = AccountOwnershipDefault.FromDocumentType(account.User.DocumentType);

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

    if (context.Request.Path.StartsWithSegments("/api"))
    {
        // Respostas da API trazem saldo, extrato e dados de cadastro: nada disso deve ficar em
        // cache de navegador, proxy ou CDN.
        context.Response.Headers.Append("Cache-Control", "no-store");
        // A API só devolve JSON, nunca HTML/script: uma CSP fechada não quebra nada e impede
        // que uma resposta seja interpretada como página. (Swagger fica fora do /api.)
        context.Response.Headers.Append("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");
    }

    await next();
});

app.UseCors("FrontendPolicy");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseCsrfProtection();
app.UseAuthorization();
app.MapControllers();

app.Run();
