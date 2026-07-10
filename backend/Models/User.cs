namespace ControlFinance.API.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Pode ser CPF (11 dígitos) ou CNPJ (14 dígitos). Guardado criptografado (AES-GCM) —
    /// o valor em memória (fora do banco) sempre é o texto puro, a conversão é transparente.
    /// </summary>
    public string Document { get; set; } = string.Empty;

    /// <summary>
    /// Hash determinístico do documento (HMAC-SHA256), usado só para busca/unicidade,
    /// já que o valor criptografado muda a cada gravação e não permite comparação direta.
    /// </summary>
    public string DocumentHash { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    /// <summary>Quando o e-mail de resumo mensal foi enviado pela última vez (evita reenvio no mesmo mês).</summary>
    public DateTime? LastMonthlySummarySentAt { get; set; }
}
