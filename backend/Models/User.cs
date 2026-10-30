namespace ControlFinance.API.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// LEGADO — CPF/CNPJ em texto recuperável (cifrado com AES-GCM). Não é mais gravado: o único
    /// uso real do documento em claro era mandar pra Polp ao conectar um banco, e isso agora usa
    /// o documento digitado pelo usuário naquele momento (só em memória, conferido contra
    /// DocumentHash). PrivacyBackfill zera esse campo nas linhas antigas; a coluna sai numa
    /// migration seguinte, depois que o backfill tiver rodado em produção.
    /// </summary>
    public string? Document { get; set; }

    /// <summary>
    /// Hash determinístico do documento (HMAC-SHA256 com chave do servidor), usado só para
    /// login por CPF/CNPJ, unicidade e pra conferir o documento digitado ao conectar um banco.
    /// É dado pseudonimizado (não anônimo): quem tiver a chave consegue testar documentos.
    /// </summary>
    public string DocumentHash { get; set; } = string.Empty;

    /// <summary>"CPF" ou "CNPJ". Substitui o documento inteiro no único uso que precisava dele
    /// guardado: o chute inicial de conta pessoal/empresa (AccountOwnershipDefault).</summary>
    public string? DocumentType { get; set; }

    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Incrementado em "sair de todos os dispositivos": todo JWT carrega a versão em que foi
    /// emitido, e a validação recusa tokens de versão anterior. Sem isso só dava pra revogar o
    /// token da sessão atual (por JTI), as outras sessões continuavam válidas até expirar.
    /// </summary>
    public int TokenVersion { get; set; }

    /// <summary>Lembrete por e-mail de contas pendentes perto do vencimento (padrão: ligado,
    /// é a funcionalidade anunciada; o usuário pode desligar em Privacidade e dados).</summary>
    public bool DueRemindersEnabled { get; set; } = true;

    /// <summary>Aviso mensal por e-mail (padrão: desligado, só com opt-in explícito).</summary>
    public bool MonthlySummaryEnabled { get; set; }

    /// <summary>Quando o e-mail de resumo mensal foi enviado pela última vez (evita reenvio no mesmo mês).</summary>
    public DateTime? LastMonthlySummarySentAt { get; set; }
}
