namespace ControlFinance.API.Models;

/// <summary>
/// Registro de acesso a aplicação (Marco Civil, art. 5º, VIII: data e hora de uso a partir de um
/// IP). DESLIGADO por padrão (AccessLog:Mode = Off). A guarda por 6 meses (art. 15) é obrigatória
/// para provedor de aplicação "constituído na forma de pessoa jurídica" que atue "de forma
/// organizada, profissionalmente e com fins econômicos". Enquanto não existir a pessoa jurídica
/// (CNPJ ainda pendente) nem operação comercial, a condição não está demonstrada; quando houver,
/// a obrigação passa a se aplicar e este registro deve ser ligado (ver relatório, Parte H.8). O
/// mecanismo existe pronto pra isso, sem nova migration.
///
/// Sem FK pra Users de propósito: um registro de guarda obrigatória precisa sobreviver à exclusão
/// da conta (Marco Civil, art. 7º, X ressalva a guarda obrigatória). IP cifrado (Decreto
/// 8.771/2016, art. 13, IV). Apagado automaticamente ao fim de AccessLog:RetentionDays.
/// </summary>
public class AccessLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public Guid? UserId { get; set; }

    /// <summary>"login" ou "register".</summary>
    public string Event { get; set; } = string.Empty;

    public string? IpAddress { get; set; }
}
