using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ControlFinance.API.DTOs;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ITokenRevocationService _tokenRevocation;

    public AuthController(IAuthService authService, ITokenRevocationService tokenRevocation)
    {
        _authService = authService;
        _tokenRevocation = tokenRevocation;
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
        var (success, error, data) = await _authService.RegisterAsync(dto);

        if (!success)
        {
            // 409 para duplicidade, 400 para outros erros de negócio
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
    /// Revoga o token atual — sem isso, "sair" só apagava o token no navegador e ele
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
