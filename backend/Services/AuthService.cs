using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public interface IAuthService
{
    Task<(bool Success, string Error, AuthResponseDto? Data)> RegisterAsync(RegisterRequestDto dto);
    Task<(bool Success, string Error, AuthResponseDto? Data)> LoginAsync(LoginRequestDto dto);
}

public class AuthService : IAuthService
{
    private readonly AppDbContext  _db;
    private readonly ITokenService _tokenService;

    public AuthService(AppDbContext db, ITokenService tokenService)
    {
        _db           = db;
        _tokenService = tokenService;
    }

    // ──────────────────────────────────────────
    //  REGISTER
    // ──────────────────────────────────────────

    public async Task<(bool Success, string Error, AuthResponseDto? Data)> RegisterAsync(RegisterRequestDto dto)
    {
        // Verifica e-mail duplicado
        var emailExists = await _db.Users.AnyAsync(u => u.Email == dto.Email.ToLower());
        if (emailExists)
            return (false, "E-mail já cadastrado.", null);

        // Verifica documento duplicado (apenas números)
        var cleanDocument = CleanDocument(dto.Document);
        var docExists = await _db.Users.AnyAsync(u => u.Document == cleanDocument);
        if (docExists)
            return (false, "CPF/CNPJ já cadastrado.", null);

        var user = new User
        {
            Name         = dto.Name.Trim(),
            Email        = dto.Email.ToLower().Trim(),
            PhoneNumber  = CleanPhone(dto.PhoneNumber),
            Document     = cleanDocument,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var token = _tokenService.GenerateToken(user);

        return (true, string.Empty, BuildResponse(user, token));
    }

    // ──────────────────────────────────────────
    //  LOGIN
    // ──────────────────────────────────────────

    public async Task<(bool Success, string Error, AuthResponseDto? Data)> LoginAsync(LoginRequestDto dto)
    {
        var identifier = dto.Identifier.Trim();

        // Tenta localizar por e-mail ou documento
        var user = await _db.Users
            .FirstOrDefaultAsync(u =>
                u.Email    == identifier.ToLower() ||
                u.Document == CleanDocument(identifier));

        if (user is null || !user.IsActive)
            return (false, "Credenciais inválidas.", null);

        var passwordValid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
        if (!passwordValid)
            return (false, "Credenciais inválidas.", null);

        var token = _tokenService.GenerateToken(user);

        return (true, string.Empty, BuildResponse(user, token));
    }

    // ──────────────────────────────────────────
    //  HELPERS
    // ──────────────────────────────────────────

    private static string CleanDocument(string doc) =>
        new string(doc.Where(char.IsDigit).ToArray());

    private static string CleanPhone(string phone) =>
        new string(phone.Where(char.IsDigit).ToArray());

    private static AuthResponseDto BuildResponse(User user, string token) => new()
    {
        Token = token,
        User  = new UserInfoDto
        {
            Id       = user.Id,
            Name     = user.Name,
            Email    = user.Email,
            Document = user.Document,
        }
    };
}
