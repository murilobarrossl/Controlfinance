using ControlFinance.API.DTOs;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

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
            var statusCode = error.Contains("já cadastrado")
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;

            return StatusCode(statusCode, new { message = error });
        }

        return CreatedAtAction(nameof(Register), data);
    }

    /// POST /api/auth/login
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
    {
        // Pega o IP real do cliente (funciona atrás de proxy/Cloudflare)
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var (success, error, data) = await _authService.LoginAsync(dto, clientIp);

        if (!success)
        {
            if (error.Contains("Muitas tentativas"))
                return StatusCode(StatusCodes.Status429TooManyRequests, new { message = error });

            return Unauthorized(new { message = error });
        }

        return Ok(data);
    }
}
