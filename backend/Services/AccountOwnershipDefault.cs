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
}
