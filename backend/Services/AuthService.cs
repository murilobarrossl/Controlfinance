using ControlFinance.API.Data;
using ControlFinance.API.DTOs;
using ControlFinance.API.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public record AuthResult(User User, IssuedToken Token);

public interface IAuthService
{
    Task<(bool Success, string Error, AuthResult? Data)> RegisterAsync(RegisterRequestDto dto, string ipAddress);
    Task<(bool Success, string Error, AuthResult? Data)> LoginAsync(LoginRequestDto dto, string ipAddress);
}

public class AuthService : IAuthService
{
    // Usado para rodar o BCrypt mesmo quando o usuário não existe: sem isso, a resposta
    // volta mais rápido para identificadores inexistentes (não roda o Verify), o que vaza
    // pelo tempo de resposta se aquele e-mail/CPF tem conta ou não.
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString());

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

    public async Task<(bool Success, string Error, AuthResult? Data)> RegisterAsync(RegisterRequestDto dto, string ipAddress)
    {
        // O cadastro só vale contra o texto vigente: assim o registro de aceite aponta pra versão
        // que a pessoa realmente viu na tela.
        if (dto.AcceptedTermsVersion != LegalDocuments.TermsVersion || dto.AcceptedPrivacyVersion != LegalDocuments.PrivacyVersion)
            return (false, "Os Termos de Uso ou a Política de Privacidade foram atualizados. Recarregue a página e confirme novamente.", null);

        if (!dto.ConfirmsAdult)
            return (false, "O Control Finance é destinado a maiores de 18 anos.", null);

        var normalizedEmail = dto.Email.Trim().ToLower();

        // Mesmo limiter do login, com chaves próprias: evita criação em massa de contas
        // (ou abuso do envio de e-mail de boas-vindas) sem travar tentativas de login legítimas.
        // Mensagem genérica de propósito, igual ao login: não avisa que o limite foi batido.
        var registerIdentifier = $"register:{normalizedEmail}";
        var registerIp = $"register:{ipAddress}";
        if (_rateLimit.IsBlocked(registerIdentifier, registerIp))
            return (false, "Não foi possível concluir o cadastro. Tente novamente mais tarde.", null);

        var emailExists = await _db.Users.AnyAsync(u => u.Email == normalizedEmail);
        if (emailExists)
        {
            _rateLimit.RegisterFailure(registerIdentifier, registerIp);
            return (false, "E-mail já cadastrado.", null);
        }

        var cleanDocument = CleanDocument(dto.Document);

        // Dígito verificador (mod 11): rejeita documento estruturalmente inválido (sequência
        // repetida, dígito verificador que não bate) antes de gastar uma consulta ao banco.
        // Não confirma que o CPF/CNPJ existe de fato na Receita Federal: isso exigiria um
        // serviço externo pago, fora do escopo aqui.
        if (!DocumentValidator.IsValid(cleanDocument))
        {
            _rateLimit.RegisterFailure(registerIdentifier, registerIp);
            return (false, "CPF/CNPJ inválido.", null);
        }

        // Unicidade conferida contra todas as versões da chave de busca: um CPF cadastrado com a
        // chave antiga também precisa bloquear um novo cadastro do mesmo CPF.
        var documentHash = _encryption.ComputeLookupHash(cleanDocument);
        var documentHashCandidates = _encryption.ComputeLookupHashCandidates(cleanDocument);
        var docExists = await _db.Users.AnyAsync(u => documentHashCandidates.Contains(u.DocumentHash));
        if (docExists)
        {
            _rateLimit.RegisterFailure(registerIdentifier, registerIp);
            return (false, "CPF/CNPJ já cadastrado.", null);
        }

        // Do documento, só ficam o hash (login por CPF/CNPJ, unicidade, conferência na conexão
        // bancária) e o tipo. O número em si não é gravado em lugar nenhum.
        var user = new User
        {
            Name = dto.Name.Trim(),
            Email = normalizedEmail,
            DocumentHash = documentHash,
            DocumentType = DocumentTypes.FromDigits(cleanDocument),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
        };

        _db.Users.Add(user);
        _db.ConsentRecords.AddRange(
            new ConsentRecord { UserId = user.Id, Kind = ConsentKinds.Terms, Version = LegalDocuments.TermsVersion },
            new ConsentRecord { UserId = user.Id, Kind = ConsentKinds.Privacy, Version = LegalDocuments.PrivacyVersion },
            new ConsentRecord { UserId = user.Id, Kind = ConsentKinds.AdultDeclaration, Version = LegalDocuments.TermsVersion });
        await _db.SaveChangesAsync();

        _rateLimit.RegisterSuccess(registerIdentifier, registerIp);
        var token = _tokenService.GenerateToken(user);

        // Envia email de boas-vindas (não bloqueia o cadastro se falhar). Só o id vai pro log,
        // nunca o e-mail, e a exceção não é anexada: a mensagem dela pode trazer a resposta do
        // provedor de e-mail, que repete o destinatário.
        try { await _emailService.SendWelcomeEmailAsync(user.Email); }
        catch (Exception ex) { _logger.LogWarning("Falha ao enviar e-mail de boas-vindas para o usuário {UserId} ({ErrorType})", user.Id, ex.GetType().Name); }
        return (true, string.Empty, new AuthResult(user, token));
    }

    public async Task<(bool Success, string Error, AuthResult? Data)> LoginAsync(LoginRequestDto dto, string ipAddress)
    {
        var identifier = dto.Identifier.Trim();
        const string genericError = "Credenciais inválidas.";

        // Rate limiting por identificador E por IP: só por identificador permitiria testar
        // senha em várias contas diferentes do mesmo IP sem nunca travar. Só marca a flag aqui
        // (sem retornar ainda): quando bloqueado, a resposta continua idêntica à de credenciais
        // erradas: nem o texto nem o tempo de resposta (pula o BCrypt, que já falharia mesmo)
        // deixam um atacante distinguir "bloqueado" de "senha errada".
        var blocked = _rateLimit.IsBlocked(identifier, ipAddress);

        var cleanIdentifierDocument = CleanDocument(identifier);
        var identifierHashCandidates = _encryption.ComputeLookupHashCandidates(cleanIdentifierDocument);
        var user = await _db.Users
            .FirstOrDefaultAsync(u =>
                u.Email == identifier.ToLower() ||
                identifierHashCandidates.Contains(u.DocumentHash));

        // Roda o BCrypt mesmo quando o usuário não existe (contra o hash dummy), e usa a
        // mesma mensagem de erro em todos os casos: senão dá pra descobrir se um e-mail/CPF
        // tem conta, ou se o rate limit foi batido, só pelo texto da resposta.
        var passwordValid = !blocked && BCrypt.Net.BCrypt.Verify(dto.Password, user?.PasswordHash ?? DummyPasswordHash);

        if (blocked || user is null || !user.IsActive || !passwordValid)
        {
            _rateLimit.RegisterFailure(identifier, ipAddress);
            return (false, genericError, null);
        }

        _rateLimit.RegisterSuccess(identifier, ipAddress);

        // Login por CPF/CNPJ é um dos dois momentos em que o documento passa pela aplicação:
        // aproveita pra regravar o hash com a chave de busca ativa (rotação gradual, já que o
        // documento não é guardado e não dá pra recalcular em lote).
        if (identifierHashCandidates.Contains(user.DocumentHash) && !_encryption.IsActiveLookupHash(user.DocumentHash))
        {
            user.DocumentHash = _encryption.ComputeLookupHash(cleanIdentifierDocument);
            await _db.SaveChangesAsync();
        }

        var token = _tokenService.GenerateToken(user);
        return (true, string.Empty, new AuthResult(user, token));
    }

    private static string CleanDocument(string doc) =>
        new string(doc.Where(char.IsDigit).ToArray());

    public static UserInfoDto ToUserInfo(User user) => new()
    {
        Id = user.Id,
        Name = user.Name,
        Email = user.Email,
        DocumentType = user.DocumentType,
        DueRemindersEnabled = user.DueRemindersEnabled,
        MonthlySummaryEnabled = user.MonthlySummaryEnabled,
    };
}
