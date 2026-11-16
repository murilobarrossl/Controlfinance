using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

// Chute inicial de pessoal/empresa pra conta nova: CNPJ = Business, CPF = Personal. Usa só o
// tipo do documento (User.DocumentType), não o documento em si, que não é mais guardado.
public static class AccountOwnershipDefault
{
    public static AccountOwnership FromDocumentType(string? documentType) =>
        documentType == DocumentTypes.Cnpj ? AccountOwnership.Business : AccountOwnership.Personal;
}

public static class DocumentTypes
{
    public const string Cpf = "CPF";
    public const string Cnpj = "CNPJ";

    public static string FromDigits(string digits) => digits.Length == 14 ? Cnpj : Cpf;

    // Regra do POST /consents da v2: instituição BUSINESS exige CNPJ e PERSONAL proíbe CNPJ. BOTH,
    // tipo desconhecido ou lista indisponível passam: quem decide nesses casos é a Polp.
    public static bool InstitutionAccepts(string? institutionType, string documentType) => institutionType switch
    {
        "BUSINESS" => documentType == Cnpj,
        "PERSONAL" => documentType == Cpf,
        _ => true,
    };
}
