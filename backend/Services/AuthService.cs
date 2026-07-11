using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public interface IAuthService
{
    Task<(bool Success, string Error, AuthResponseDto? Data)> RegisterAsync(RegisterRequestDto dto, string ipAddress);
    Task<(bool Success, string Error, AuthResponseDto? Data)> LoginAsync(LoginRequestDto dto, string ipAddress);
}

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly EmailService _emailService;
    private readonly RateLimitService _rateLimit;
    private readonly IEncryptionService _encryption;
    private readonly ILogger<AuthService> _logger;

    public AuthService(AppDbContext db, ITokenService tokenService,
        EmailService emailService, RateLimitService rateLimit, IEncryptionService encryption,
        ILogger<AuthService> logger)
    {
        _db = db;
        _tokenService = tokenService;
        _emailService = emailService;
        _rateLimit = rateLimit;
        _encryption = encryption;
        _logger = logger;
    }

    public async Task<(bool Success, string Error, AuthResponseDto? Data)> RegisterAsync(RegisterRequestDto dto, string ipAddress)
    {
        // Mesmo limiter do login, com chaves próprias — evita criação em massa de contas
        // (ou abuso do envio de e-mail de boas-vindas) sem travar tentativas de login legítimas.
        var registerIdentifier = $"register:{dto.Email.ToLower()}";
        var registerIp = $"register:{ipAddress}";
        if (_rateLimit.IsBlocked(registerIdentifier, registerIp))
            return (false, "Muitas tentativas de cadastro. Tente novamente em 15 minutos.", null);

        var emailExists = await _db.Users.AnyAsync(u => u.Email == dto.Email.ToLower());
        if (emailExists)
        {
            _rateLimit.RegisterFailure(registerIdentifier, registerIp);
            return (false, "E-mail já cadastrado.", null);
        }

        var cleanDocument = CleanDocument(dto.Document);
        var documentHash = _encryption.ComputeLookupHash(cleanDocument);
        var docExists = await _db.Users.AnyAsync(u => u.DocumentHash == documentHash);
        if (docExists)
        {
            _rateLimit.RegisterFailure(registerIdentifier, registerIp);
            return (false, "CPF/CNPJ já cadastrado.", null);
        }

        var user = new User
        {
            Name = dto.Name.Trim(),
            Email = dto.Email.ToLower().Trim(),
            PhoneNumber = CleanPhone(dto.PhoneNumber),
            Document = cleanDocument,
            DocumentHash = documentHash,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        _rateLimit.RegisterSuccess(registerIdentifier, registerIp);
        var token = _tokenService.GenerateToken(user);

        // Envia email de boas-vindas (não bloqueia o cadastro se falhar)
        try { await _emailService.SendWelcomeEmailAsync(user.Email, user.Name); }
        catch (Exception ex) { _logger.LogWarning(ex, "Falha ao enviar e-mail de boas-vindas para {Email}", user.Email); }
        return (true, string.Empty, BuildResponse(user, token));
    }

    public async Task<(bool Success, string Error, AuthResponseDto? Data)> LoginAsync(LoginRequestDto dto, string ipAddress)
    {
        var identifier = dto.Identifier.Trim();

        // Rate limiting por identificador E por IP — só por identificador permitiria testar
        // senha em várias contas diferentes do mesmo IP sem nunca travar.
        if (_rateLimit.IsBlocked(identifier, ipAddress))
            return (false, "Muitas tentativas. Tente novamente em 15 minutos.", null);

        var identifierDocumentHash = _encryption.ComputeLookupHash(CleanDocument(identifier));
        var user = await _db.Users
            .FirstOrDefaultAsync(u =>
                u.Email == identifier.ToLower() ||
                u.DocumentHash == identifierDocumentHash);

        if (user is null || !user.IsActive)
        {
            _rateLimit.RegisterFailure(identifier, ipAddress);
            return (false, "Credenciais inválidas.", null);
        }

        var passwordValid = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
        if (!passwordValid)
        {
            _rateLimit.RegisterFailure(identifier, ipAddress);
            return (false, $"Credenciais inválidas. {_rateLimit.RemainingAttempts(identifier)} tentativas restantes.", null);
        }

        _rateLimit.RegisterSuccess(identifier, ipAddress);
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
