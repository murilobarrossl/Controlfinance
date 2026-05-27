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
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly EmailService _emailService;
    private readonly RateLimitService _rateLimit;

    public AuthService(AppDbContext db, ITokenService tokenService,
        EmailService emailService, RateLimitService rateLimit)
    {
        _db = db;
        _tokenService = tokenService;
        _emailService = emailService;
        _rateLimit = rateLimit;
    }

    public async Task<(bool Success, string Error, AuthResponseDto? Data)> RegisterAsync(RegisterRequestDto dto)
    {
        var emailExists = await _db.Users.AnyAsync(u => u.Email == dto.Email.ToLower());
        if (emailExists)
            return (false, "E-mail já cadastrado.", null);

        var cleanDocument = CleanDocument(dto.Document);
        var docExists = await _db.Users.AnyAsync(u => u.Document == cleanDocument);
        if (docExists)
            return (false, "CPF/CNPJ já cadastrado.", null);

        var user = new User
        {
            Name = dto.Name.Trim(),
            Email = dto.Email.ToLower().Trim(),
            PhoneNumber = CleanPhone(dto.PhoneNumber),
            Document = cleanDocument,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var token = _tokenService.GenerateToken(user);

        // Envia email de boas-vindas (não bloqueia o cadastro se falhar)
        try { await _emailService.SendWelcomeEmailAsync(user.Email, user.Name); }
        catch (Exception ex) { Console.WriteLine($"EMAIL ERROR: {ex.Message}"); }
        return (true, string.Empty, BuildResponse(user, token));
    }

    public async Task<(bool Success, string Error, AuthResponseDto? Data)> LoginAsync(LoginRequestDto dto)
    {
        var identifier = dto.Identifier.Trim();

        // Rate limiting por identificador
        if (_rateLimit.IsBlocked(identifier))
            return (false, "Muitas tentativas. Tente novamente em 15 minutos.", null);

        var user = await _db.Users
            .FirstOrDefaultAsync(u =>
                u.Email == identifier.ToLower() ||
                u.Document == CleanDocument(identifier));

        if (user is null || !user.IsActive)
        {
            _rateLimit.RegisterFailure(identifier);
            return (false, "Credenciais inválidas.", null);
        }

        var passwordValid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
        if (!passwordValid)
        {
            _rateLimit.RegisterFailure(identifier);
            return (false, $"Credenciais inválidas. {_rateLimit.RemainingAttempts(identifier)} tentativas restantes.", null);
        }

        _rateLimit.RegisterSuccess(identifier);
        var token = _tokenService.GenerateToken(user);
        return (true, string.Empty, BuildResponse(user, token));
    }

    private static string CleanDocument(string doc) =>
        new string(doc.Where(char.IsDigit).ToArray());

    private static string CleanPhone(string phone) =>
        new string(phone.Where(char.IsDigit).ToArray());

    private static AuthResponseDto BuildResponse(User user, string token) => new()
    {
        Token = token,
        User = new UserInfoDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Document = user.Document,
        }
    };
}
