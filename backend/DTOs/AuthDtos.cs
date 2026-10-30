using System.ComponentModel.DataAnnotations;

namespace ControlFinance.API.DTOs;

public class RegisterRequestDto
{
    [Required(ErrorMessage = "Nome é obrigatório.")]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// CPF (11 dígitos) ou CNPJ (14 dígitos), somente números. Só o hash (HMAC) e o tipo são
    /// guardados; o número em si é descartado depois do cadastro.
    /// </summary>
    [Required(ErrorMessage = "CPF ou CNPJ é obrigatório.")]
    [RegularExpression(@"^\d{11}(\d{3})?$", ErrorMessage = "Documento inválido. Informe CPF (11 dígitos) ou CNPJ (14 dígitos).")]
    public string Document { get; set; } = string.Empty;

    [Required(ErrorMessage = "Senha é obrigatória.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).{8,}$",
        ErrorMessage = "A senha deve ter no mínimo 8 caracteres, incluindo letras e números.")]
    public string Password { get; set; } = string.Empty;

    /// <summary>Versão dos Termos de Uso exibida e aceita (ver LegalDocuments).</summary>
    [Required(ErrorMessage = "É preciso aceitar os Termos de Uso.")]
    public string AcceptedTermsVersion { get; set; } = string.Empty;

    /// <summary>Autodeclaração de maioridade. O serviço é para maiores de 18 anos: contrato exige
    /// agente capaz (Código Civil, arts. 3º, 4º, I, e 104, I) e dado de criança/adolescente tem
    /// regime próprio (LGPD, art. 14) que o produto não implementa. Só o "sim" é guardado (como
    /// registro de aceite); não pedimos data de nascimento.</summary>
    public bool ConfirmsAdult { get; set; }

    /// <summary>Versão da Política de Privacidade exibida e lida (ver LegalDocuments).</summary>
    [Required(ErrorMessage = "É preciso ler a Política de Privacidade.")]
    public string AcceptedPrivacyVersion { get; set; } = string.Empty;
}

public class LoginRequestDto
{
    /// <summary>
    /// E-mail ou CPF/CNPJ (somente números).
    /// </summary>
    [Required(ErrorMessage = "E-mail ou documento é obrigatório.")]
    public string Identifier { get; set; } = string.Empty;

    [Required(ErrorMessage = "Senha é obrigatória.")]
    public string Password { get; set; } = string.Empty;
}

// O JWT não vai mais no corpo: fica só no cookie httpOnly (SessionCookie). O corpo leva o
// segredo anti-CSRF da sessão, que o frontend guarda em memória (ver CsrfProtection).
public class AuthResponseDto
{
    public string CsrfToken { get; set; } = string.Empty;
    public UserInfoDto User { get; set; } = new();
}

// CPF/CNPJ não vem mais aqui (antes ia completo pro navegador em todo /auth/me sem nenhuma tela
// precisar dele): só o tipo, que basta pra montar a máscara do campo na conexão bancária.
public class UserInfoDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DocumentType { get; set; }
    public bool DueRemindersEnabled { get; set; }
    public bool MonthlySummaryEnabled { get; set; }
}

public class NotificationPreferencesDto
{
    public bool DueRemindersEnabled { get; set; }
    public bool MonthlySummaryEnabled { get; set; }
}

public class ConfirmPasswordDto
{
    [Required(ErrorMessage = "Informe sua senha para confirmar.")]
    public string Password { get; set; } = string.Empty;
}
