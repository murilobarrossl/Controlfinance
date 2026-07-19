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
    private readonly AppDbContext _db;

    public AuthController(IAuthService authService, ITokenRevocationService tokenRevocation, AppDbContext db)
    {
        _authService = authService;
        _tokenRevocation = tokenRevocation;
        _db = db;
    }

    /// <summary>
    /// Cadastra um novo usuário e retorna o JWT.
    /// </summary>
    /// POST /api/auth/register
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto dto)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var (success, error, data) = await _authService.RegisterAsync(dto, ipAddress);

        if (!success)
        {
            var statusCode = error.Contains("já cadastrado")
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;

            return StatusCode(statusCode, new { message = error });
        }

        return CreatedAtAction(nameof(Register), data);
    }

    /// <summary>
    /// Autentica o usuário com e-mail ou CPF/CNPJ + senha e retorna o JWT.
    /// </summary>
    /// POST /api/auth/login
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var (success, error, data) = await _authService.LoginAsync(dto, ipAddress);

        if (!success)
            return Unauthorized(new { message = error });

        return Ok(data);
    }

    /// <summary>
    /// Dados do usuário autenticado, usado pra exibir nome/e-mail em telas que não
    /// vieram de um login/cadastro recente (ex.: sessão já aberta antes, refresh de página).
    /// </summary>
    /// GET /api/auth/me
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserInfoDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var user = await _db.Users
            .Where(u => u.Id == UserId)
            .Select(u => new UserInfoDto { Id = u.Id, Name = u.Name, Email = u.Email, Document = u.Document })
            .FirstOrDefaultAsync(ct);

        if (user is null) return NotFound();

        return Ok(user);
    }

    /// <summary>
    /// Revoga o token atual: sem isso, "sair" só apagava o token no navegador e ele
    /// continuava válido (JWT é stateless) até expirar sozinho.
    /// </summary>
    /// POST /api/auth/logout
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var jti = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
        var expClaim = User.FindFirstValue(JwtRegisteredClaimNames.Exp);

        if (string.IsNullOrEmpty(jti) || !long.TryParse(expClaim, out var expUnix))
            return BadRequest(new { message = "Token inválido." });

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;
        await _tokenRevocation.RevokeAsync(jti, expiresAt);

        return NoContent();
    }
}
