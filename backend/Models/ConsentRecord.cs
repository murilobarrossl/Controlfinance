namespace ControlFinance.API.Models;

/// <summary>
/// Registro append-only de aceites: Termos de Uso, Política de Privacidade e cada autorização de
/// conexão bancária. Existe porque o ônus de provar o aceite é do controlador (LGPD art. 8º,
/// § 2º) e o checkbox do cadastro antes nem chegava ao servidor.
///
/// Guarda só o necessário pra prova: o quê, qual versão do texto e quando. IP e user-agent
/// ficaram de fora de propósito (minimização, LGPD art. 6º, III): data e versão já identificam o
/// que foi aceito e quando. Os registros são apagados junto com a conta, porque o art. 16 da LGPD
/// não lista "prova de aceite" entre as hipóteses de conservação após o término do tratamento
/// (ver relatório, Parte H, risco jurídico em aberto).
/// </summary>
public class ConsentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Ver ConsentKinds.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Versão do texto aceito (ver LegalDocuments).</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Para conexão bancária: id da instituição na Polp. Nulo nos demais tipos.</summary>
    public string? Reference { get; set; }

    public DateTime AcceptedAt { get; set; } = DateTime.UtcNow;
}

public static class ConsentKinds
{
    public const string Terms = "terms";
    public const string Privacy = "privacy";
    public const string BankConnection = "bank_connection";
    public const string AdultDeclaration = "adult_declaration";
}
