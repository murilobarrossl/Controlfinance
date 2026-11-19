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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Segredos reais ficam em appsettings.Local.json (gitignorado) ou em variáveis de
// ambiente: appsettings.json versionado no git só tem placeholders.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
// Overrides do ambiente/deploy e da linha de comando prevalecem sobre o arquivo local.
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddCommandLine(args);

// Fora de Development, segredo ausente ou placeholder impede a subida: senão a API sobe usando um
// valor público do repositório como chave.
var secretErrors = ConfigSecrets.Validate(builder.Configuration, builder.Environment.IsDevelopment());
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
// Centraliza timeout e limites do pool, preservando parâmetros explícitos da conexão.
builder.Configuration["ConnectionStrings:DefaultConnection"] = StartupDatabaseConnection.Configure(builder.Configuration);

builder.Services.AddSingleton<DatabaseConnectionDiagnostics>();
builder.Services.AddDbContext<AppDbContext>((services, options) =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
        .ReplaceService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator, ConnectedNpgsqlDatabaseCreator>()
        .AddInterceptors(services.GetRequiredService<DatabaseConnectionDiagnostics>()));

// ──────────────────────────────────────────
//  SERVIÇOS DA APLICAÇÃO
// ──────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenRevocationService, TokenRevocationService>();
builder.Services.AddScoped<PolpSyncService>();
builder.Services.AddScoped<PolpConsentMetadataService>();
builder.Services.AddScoped<ControlFinance.API.Services.Investments.InvestmentImportStore>();
builder.Services.AddScoped<ControlFinance.API.Services.Investments.PolpInvestmentImportService>();
builder.Services.AddSingleton<PolpApiCooldownStore>();
builder.Services.AddScoped<PolpDisconnectService>();
builder.Services.AddScoped<PolpLoanImportService>();
builder.Services.AddScoped<PolpWebhookProcessor>();
builder.Services.AddScoped<IPolpEventHandler, PolpWebhookProcessorHandler>();
builder.Services.AddScoped<IPolpInbox, PolpInbox>();
builder.Services.AddHostedService<PolpInboxWorker>();
builder.Services.AddHostedService<PolpInboxCleanupService>();
builder.Services.AddScoped<InstitutionNameResolver>();
builder.Services.AddScoped<SessionValidator>();
builder.Services.AddScoped<AccessLogService>();
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddSingleton<RateLimitService>();
builder.Services.AddInvestmentReadRateLimit(builder.Configuration);
builder.Services.AddUserOperationRateLimits();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IJobLock, PostgresJobLock>();
builder.Services.AddSingleton<IIntegrationLock, IntegrationLock>();
builder.Services.AddHostedService<PolpReconciliationService>();
builder.Services.AddHttpClient<EmailService>();
builder.Services.AddHttpClient<ReferenceRatesService>(c =>
{
    c.BaseAddress = new Uri("https://api.bcb.gov.br/");
    c.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddHostedService<ScheduledEmailService>();
builder.Services.AddHostedService<PendingDisconnectRetryService>();
builder.Services.AddHostedService<DataRetentionService>();
builder.Services.AddScoped<AccountDeletionService>();
builder.Services.AddHostedService<InactiveAccountService>();

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
            // A database outage is not an invalid credential. Let the error handler emit 503
            // instead of turning a provider exception into 401 and logging the user out.
            OnAuthenticationFailed = context =>
                DatabaseFailure.IsTransient(context.Exception)
                    || (context.Exception is OperationCanceledException && context.HttpContext.RequestAborted.IsCancellationRequested)
                    ? Task.FromException(context.Exception)
                    : Task.CompletedTask,
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

                // Cliente que desistiu (navegou, recarregou, StrictMode em dev) cancela RequestAborted: não é
                // falha de banco nem de credencial, então falha fechado sem exceção (o handler registraria
                // como erro). Falha real de banco continua lançando e vira 503 em OnAuthenticationFailed.
                try
                {
                    var revocation = context.HttpContext.RequestServices.GetRequiredService<ITokenRevocationService>();
                    if (await revocation.IsRevokedAsync(jti, bypassCache, context.HttpContext.RequestAborted))
                    {
                        context.Fail("Token revogado.");
                        return;
                    }

                    var sessions = context.HttpContext.RequestServices.GetRequiredService<SessionValidator>();
                    if (!await sessions.IsValidAsync(userId, tokenVersion, bypassCache, context.HttpContext.RequestAborted))
                    {
                        context.Fail("Sessão encerrada.");
                        return;
                    }

                    // Falha ao registrar a atividade sobe como ActivityNotRecordedException e vira 503: o
                    // usuário não é atendido sem atividade gravada, e a próxima requisição tenta de novo.
                    var services = context.HttpContext.RequestServices;
                    try
                    {
                        await UserActivity.TouchAsync(services.GetRequiredService<AppDbContext>(), services.GetRequiredService<IMemoryCache>(),
                            userId, DateTime.UtcNow, context.HttpContext.RequestAborted);
                    }
                    catch (ActivityNotRecordedException ex)
                    {
                        services.GetRequiredService<ILoggerFactory>().CreateLogger("UserActivity")
                            .LogWarning("Atividade do usuário não registrada ({ErrorType}); requisição recusada.", ex.InnerException?.GetType().Name);
                        throw;
                    }
                }
                catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
                {
                    context.Fail("Requisição cancelada pelo cliente.");
                }
            }
        };
    });

builder.Services.AddAuthorization();

// ──────────────────────────────────────────
//  CORS
// ──────────────────────────────────────────
// Origens liberadas vêm de config (Cors:AllowedOrigins). Em produção, adicionar o domínio
// real do frontend em appsettings.Local.json ou variável de ambiente, sem tocar no código.
// Fora de Development a lista não tem padrão: localhost ou http com credenciais liberadas não pode valer em
// produção. As origens de desenvolvimento ficam em appsettings.Development.json; arrays de configuração se
// mesclam por índice, então manter localhost no appsettings.json sobreviveria a um Cors__AllowedOrigins__0.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? (builder.Environment.IsDevelopment() ? ["http://localhost:5173", "http://localhost:3000"] : []);
if (!builder.Environment.IsDevelopment())
{
    var corsErrors = ConfigSecrets.ValidateCorsOrigins(allowedOrigins);
    if (corsErrors.Count > 0)
        throw new InvalidOperationException("Configuração de CORS inválida: " + string.Join("; ", corsErrors));
    if (builder.Configuration["AllowedHosts"] is null or "*")
        Console.Error.WriteLine("ATENÇÃO: AllowedHosts aceita qualquer host; configure os domínios da API.");
}

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
    builder.Services.AddTransient<PolpRetryHandler>();
    // O timeout de cada tentativa fica no PolpRetryHandler; este só não pode cortar antes do pior
    // caso dele (3 × 15 s + até 2 esperas de Retry-After de 10 s).
    builder.Services.AddHttpClient<IPolpService, PolpService>(c => c.Timeout = TimeSpan.FromSeconds(90))
        .AddHttpMessageHandler<PolpRetryHandler>();
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

// Diagnóstico explícito: não inicia o servidor/jobs e sai antes de migrations/backfills.
if (args.Contains("--check-database", StringComparer.Ordinal))
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.OpenConnectionAsync();
        await using var query = db.Database.GetDbConnection().CreateCommand();
        query.CommandText = "SELECT 1, ssl, version FROM pg_stat_ssl WHERE pid = pg_backend_pid()";
        await using var result = await query.ExecuteReaderAsync();
        if (!await result.ReadAsync() || result.GetInt32(0) != 1)
            throw new InvalidOperationException("Verificação da conexão sem resultado.");
        app.Logger.LogInformation("Conexão PostgreSQL confirmada pelo EF Core: SELECT 1 OK; TLS ativo: {Tls}; versão: {TlsVersion}. Nenhuma migration ou backfill executado.",
            result.GetBoolean(1), result.IsDBNull(2) ? "nenhuma" : result.GetString(2));
    }
    catch (Exception error)
    {
        app.Logger.LogError("Verificação PostgreSQL falhou: {Failure}. Nenhuma migration ou backfill executado.",
            DatabaseConnectionDiagnostics.Describe(error));
        Environment.ExitCode = 1;
    }
    finally
    {
        await app.DisposeAsync();
    }
    return;
}

if (ConfigSecrets.IsPolpEnabled(app.Configuration) && !ConfigSecrets.IsWebhookSignatureRequired(app.Configuration))
    app.Logger.LogWarning("Assinatura do webhook da Polp desativada (Polp:WebhookRequireSignature=false): só o token da URL protege o endpoint");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Mantém a conexão aberta durante a inicialização; a repetição cobre somente Open,
    // sem reexecutar alterações cujo resultado no servidor possa ser incerto.
    await StartupDatabaseConnection.OpenAsync(
        ct => db.Database.OpenConnectionAsync(ct), app.Logger, app.Lifetime.ApplicationStopping);

    // Uma instância por vez aplica migrations e backfills: o EF Core 8 não trava a aplicação, e duas
    // instâncias subindo juntas aplicariam a mesma migration (ou o backfill do Radar) em paralelo. A
    // outra espera e, ao entrar, já não encontra nada pendente. Trava de transação (IJobLock), que vale
    // também no pooler do Supabase; tem de vir antes de GetPendingMigrationsAsync.
    await using var startupLock = await AcquireStartupLockAsync(
        scope.ServiceProvider.GetRequiredService<IJobLock>(), app.Logger, app.Lifetime.ApplicationStopping);

    // Precisa saber se a migration do Radar de Recorrências ainda não tinha rodado nessa base
    // ANTES de aplicar (MigrateAsync abaixo já marca ela como aplicada). É o gatilho do backfill
    // logo depois. Sem essa checagem, toda vez que o app subisse ele tentaria rodar de novo o
    // backfill em cima de linhas que o usuário já classificou manualmente como "conta mista"
    // (Mixed), sobrescrevendo a escolha dele com o chute. Lido via reflection (o [Migration] da
    // classe gerada) em vez de string literal: já testei isso quebrando silenciosamente uma vez,
    // ao regenerar a migration com outro timestamp e esquecer de atualizar o literal à mão.
    var recurrenceRadarMigrationId = typeof(AddRecurrenceRadarSupport)
        .GetCustomAttribute<MigrationAttribute>()!.Id;
    var recurrenceRadarMigrationIsPending = (await db.Database.GetPendingMigrationsAsync(app.Lifetime.ApplicationStopping))
        .Contains(recurrenceRadarMigrationId);

    await db.Database.MigrateAsync(app.Lifetime.ApplicationStopping);

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
static async Task<IAsyncDisposable> AcquireStartupLockAsync(IJobLock jobLock, ILogger logger, CancellationToken ct)
{
    var waited = false;
    while (true)
    {
        if (await jobLock.TryAcquireAsync(JobLockKeys.StartupMigrations, ct) is { } handle) return handle;
        if (!waited) logger.LogInformation("Outra instância está aplicando migrations; aguardando.");
        waited = true;
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
    }
}

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
// Confiar em qualquer origem deixa quem alcança a aplicação sem passar pelo proxy forjar X-Forwarded-For:
// burla o limite por IP de login, cadastro e exclusão e grava IP falso no registro de acesso. Por isso só
// os proxies configurados (ForwardedHeaders:KnownProxies = IPs, KnownNetworks = CIDRs) são aceitos; sem
// nada configurado vale o padrão do ASP.NET (só loopback, que cobre o desenvolvimento local). Confiar em
// qualquer origem exige escolha explícita (TrustAllProxies) e só é seguro se a origem não for alcançável
// diretamente, como em plataformas cujo proxy de borda não tem IP fixo.
var forwardedConfig = app.Configuration.GetSection("ForwardedHeaders");
var knownProxies = forwardedConfig.GetSection("KnownProxies").Get<string[]>() ?? [];
var knownNetworks = forwardedConfig.GetSection("KnownNetworks").Get<string[]>() ?? [];
if (forwardedConfig.GetValue<bool>("TrustAllProxies"))
{
    forwardedHeadersOptions.KnownNetworks.Clear();
    forwardedHeadersOptions.KnownProxies.Clear();
    app.Logger.LogWarning("ForwardedHeaders:TrustAllProxies ligado: X-Forwarded-For é aceito de qualquer origem; a aplicação não pode ser alcançável sem passar pelo proxy.");
}
else
{
    foreach (var proxy in knownProxies)
        forwardedHeadersOptions.KnownProxies.Add(System.Net.IPAddress.Parse(proxy.Trim()));
    foreach (var network in knownNetworks)
    {
        var parts = network.Trim().Split('/');
        var prefix = System.Net.IPAddress.Parse(parts[0]);
        var fullLength = prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        forwardedHeadersOptions.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(
            prefix, parts.Length == 2 ? int.Parse(parts[1]) : fullLength));
    }
    if (!app.Environment.IsDevelopment() && knownProxies.Length == 0 && knownNetworks.Length == 0)
        app.Logger.LogWarning("Nenhum proxy confiável configurado (ForwardedHeaders:KnownProxies/KnownNetworks): atrás de um proxy, o IP do cliente e o HTTPS da borda não serão reconhecidos.");
}
app.UseForwardedHeaders(forwardedHeadersOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
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
        if (feature?.Error is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = 499;
            return;
        }

        var unavailable = feature?.Error is { } error && DatabaseFailure.IsTransient(error);
        // Modelo da rota, não o caminho real: a URL do webhook da Polp carrega o token secreto
        // (/api/polp/webhook/{token}) e não pode ir para o log. Sem rota resolvida, o segmento é mascarado.
        var route = (feature?.Endpoint as RouteEndpoint)?.RoutePattern.RawText
            ?? (context.Request.Path.StartsWithSegments("/api/polp/webhook") ? "/api/polp/webhook/***" : context.Request.Path.Value);
        if (feature?.Error is { } ex)
        {
            if (unavailable)
                logger.LogWarning("PostgreSQL indisponível em {Path}: {Failure}", route,
                    DatabaseConnectionDiagnostics.Describe(ex));
            else
                logger.LogError(ex, "Exceção não tratada em {Path}", route);
        }

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError;
        if (unavailable) context.Response.Headers["Retry-After"] = "5";
        await context.Response.WriteAsJsonAsync(new { message = unavailable
            ? "Banco de dados temporariamente indisponível. Tente novamente em instantes."
            : "Ocorreu um erro inesperado. Tente novamente." });
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
// Depois da autenticação: o limite das leituras de Investimentos é por usuário, então precisa do "sub".
app.UseRateLimiter();
app.MapControllers();

app.Run();
