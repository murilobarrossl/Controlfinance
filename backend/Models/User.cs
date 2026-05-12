namespace ControlFinance.API.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Pode ser CPF (11 dígitos) ou CNPJ (14 dígitos), armazenado apenas números.
    /// </summary>
    public string Document { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
