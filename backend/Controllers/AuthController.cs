using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ApiControllerBase
{
    private readonly IAuthService _authService;
    private readonly ITokenRevocationService _tokenRevocation;
    private readonly SessionValidator _sessionValidator;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly AccessLogService _accessLog;

    public AuthController(IAuthService authService, ITokenRevocationService tokenRevocation,
        SessionValidator sessionValidator, AppDbContext db, IConfiguration config, AccessLogService accessLog)
    {
        _accessLog = accessLog;
        _authService = authService;
        _tokenRevocation = tokenRevocation;
        _sessionValidator = sessionValidator;
        _db = db;
        _config = config;
    }

    /// <summary>
    /// Cadastra um novo usuário e abre a sessão (cookie httpOnly).
    /// </summary>
    /// POST /api/auth/register
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto dto)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var (success, error, code, data) = await _authService.RegisterAsync(dto, ipAddress);

        if (!success)
        {
            var statusCode = code == RegisterError.AlreadyRegistered
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;

            return StatusCode(statusCode, new { message = error });
        }

        await _accessLog.RecordAsync(data!.User.Id, "register", ipAddress);
        return StatusCode(StatusCodes.Status201Created, await StartSessionAsync(data!));
    }

    /// <summary>
    /// Autentica o usuário com e-mail ou CPF/CNPJ + senha e abre a sessão (cookie httpOnly).
    /// </summary>
    /// POST /api/auth/login
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var (success, error, data, lockedFor) = await _authService.LoginAsync(dto, ipAddress);

        if (lockedFor is { } remaining)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
            Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = error, retryAfterSeconds = seconds });
        }

        if (!success)
            return Unauthorized(new { message = error });

        await _accessLog.RecordAsync(data!.User.Id, "login", ipAddress);
        return Ok(await StartSessionAsync(data!));
    }

    /// <summary>
    /// Dados do usuário da sessão atual + o segredo anti-CSRF dela. É como o frontend descobre,
    /// ao abrir/recarregar a página, se já existe uma sessão válida (o cookie não é legível por JS).
    /// </summary>
    /// GET /api/auth/me
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == UserId, ct);
        if (user is null) return NotFound();

        var info = AuthService.ToUserInfo(user);
        info.PendingPrivacyVersion = await PendingPrivacyVersionAsync(user.Id);
        return Ok(new AuthResponseDto
        {
            User = info,
            CsrfToken = User.FindFirstValue(SessionClaims.Csrf) ?? string.Empty,
        });
    }

    /// <summary>
    /// Encerra a sessão atual: revoga o token (por JTI) e apaga o cookie. Revogar é o que torna o
    /// logout real, já que o JWT continuaria válido até expirar.
    /// </summary>
    /// POST /api/auth/logout
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var jti = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
        var expClaim = User.FindFirstValue(JwtRegisteredClaimNames.Exp);

        if (!string.IsNullOrEmpty(jti) && long.TryParse(expClaim, out var expUnix))
        {
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;
            await _tokenRevocation.RevokeAsync(jti, expiresAt);
        }

        SessionCookie.Delete(HttpContext, _config);
        return NoContent();
    }

    /// <summary>
    /// Encerra todas as sessões do usuário, em qualquer aparelho: incrementa a versão de sessão,
    /// e todo token emitido antes passa a ser recusado (ver SessionValidator).
    /// </summary>
    /// POST /api/auth/logout-all
    [Authorize]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        await _db.Users
            .Where(u => u.Id == UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1), ct);

        _sessionValidator.Invalidate(UserId);
        SessionCookie.Delete(HttpContext, _config);
        return NoContent();
    }

    private async Task<AuthResponseDto> StartSessionAsync(AuthResult result)
    {
        SessionCookie.Append(HttpContext, _config, result.Token.Jwt, result.Token.ExpiresAt);
        var info = AuthService.ToUserInfo(result.User);
        info.PendingPrivacyVersion = await PendingPrivacyVersionAsync(result.User.Id);
        return new AuthResponseDto
        {
            User = info,
            CsrfToken = result.Token.CsrfToken,
        };
    }

    // Aviso pendente até existir ciência (privacy_notice) ou o registro do cadastro da versão vigente.
    private async Task<string?> PendingPrivacyVersionAsync(Guid userId) =>
        await LegalDocuments.HasCurrentPrivacyRecordAsync(_db, userId, LegalDocuments.PrivacyVersion, HttpContext.RequestAborted)
            ? null
            : LegalDocuments.PrivacyVersion;
}
