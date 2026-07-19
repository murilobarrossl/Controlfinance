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

    [Required(ErrorMessage = "Número de celular é obrigatório.")]
    [RegularExpression(@"^\d{10,11}$", ErrorMessage = "Número de celular inválido. Informe apenas números com DDD (10 ou 11 dígitos).")]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// CPF (11 dígitos) ou CNPJ (14 dígitos), somente números.
    /// </summary>
    [Required(ErrorMessage = "CPF ou CNPJ é obrigatório.")]
    [RegularExpression(@"^\d{11}(\d{3})?$", ErrorMessage = "Documento inválido. Informe CPF (11 dígitos) ou CNPJ (14 dígitos).")]
    public string Document { get; set; } = string.Empty;

    [Required(ErrorMessage = "Senha é obrigatória.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).{8,}$",
        ErrorMessage = "A senha deve ter no mínimo 8 caracteres, incluindo letras e números.")]
    public string Password { get; set; } = string.Empty;
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

public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public UserInfoDto User { get; set; } = new();
}

public class UserInfoDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
}
