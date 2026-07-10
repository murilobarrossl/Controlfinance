using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ControlFinance.API.Data;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Segredos reais ficam em appsettings.Local.json (gitignorado) ou em variáveis de
// ambiente — appsettings.json versionado no git só tem placeholders.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// ──────────────────────────────────────────
//  BANCO DE DADOS
// ──────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ──────────────────────────────────────────
//  SERVIÇOS DA APLICAÇÃO
// ──────────────────────────────────────────
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenRevocationService, TokenRevocationService>();
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

        // Rejeita tokens revogados (logout) mesmo que ainda não tenham expirado —
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
// Origens liberadas vêm de config (Cors:AllowedOrigins) — em produção, adicionar o domínio
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
            .AllowAnyMethod();
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

// Confia nos headers X-Forwarded-* do proxy (Cloudflare/DigitalOcean) na frente da aplicação —
// sem isso, UseHttpsRedirection/UseHsts não enxergam que a conexão já chegou em HTTPS na borda
// e podem causar loop de redirecionamento.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseHttpsRedirection();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Headers de segurança básicos — ASP.NET Core não adiciona nada disso por padrão.
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

app.UseCors("FrontendPolicy");

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
