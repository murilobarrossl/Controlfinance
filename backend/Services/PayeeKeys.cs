using System.Text.RegularExpressions;
using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

public static partial class PayeeKeys
{
    // O id da empresa entra no texto do HMAC: o mesmo documento em duas empresas gera hashes diferentes
    // e nunca coincide com o DocumentHash de um login.
    public static string LookupInput(string scope, Guid companyId, string value) => $"{scope}:{companyId:N}:{value}";

    public static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    // Conta completa do destinatário (banco + agência sem dígito + número com dígito verificador), na
    // mesma forma dos dois lados: cadastro ("45678-9") e banco (partie_number + partie_check_digit).
    // Zeros à esquerda não diferenciam contas. Qualquer parte ausente ou inválida = sem identidade.
    public static string? AccountLookupInput(Guid companyId, string? bankCode, string? branch, string? accountWithDigit)
    {
        var bank = Digits(bankCode).TrimStart('0');
        var agency = Digits((branch ?? string.Empty).Split('-')[0]).TrimStart('0');
        var account = new string((accountWithDigit ?? string.Empty).ToUpperInvariant()
            .Where(c => char.IsAsciiDigit(c) || c == 'X').ToArray()).TrimStart('0');
        if (bank.Length is 0 or > 3 || agency.Length is 0 or > 5 || account.Length < 2 || account.Length > 21) return null;
        if (account[..^1].Contains('X')) return null;
        return LookupInput("payee-account", companyId, $"{bank}|{agency}|{account}");
    }

    /// <summary>Chave no formato canônico do Pix, ou nulo se não for uma chave válida desse tipo.
    /// Formato válido não prova que a chave existe nem de quem ela é.</summary>
    public static string? NormalizePixKey(PixKeyType type, string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        switch (type)
        {
            case PixKeyType.Cpf:
                var cpf = Digits(value);
                return DocumentValidator.IsValidCpf(cpf) ? cpf : null;
            case PixKeyType.Cnpj:
                var cnpj = Digits(value);
                return DocumentValidator.IsValidCnpj(cnpj) ? cnpj : null;
            case PixKeyType.Email:
                var email = value.ToLowerInvariant();
                return email.Length <= 77 && EmailPattern().IsMatch(email) ? email : null;
            case PixKeyType.Phone:
                var phone = Digits(value);
                if (phone.Length is 10 or 11) phone = "55" + phone;
                // +55, DDD de 11 a 99 e 8 ou 9 dígitos de número.
                return phone.Length is 12 or 13 && phone.StartsWith("55", StringComparison.Ordinal) && phone[2] != '0'
                    ? "+" + phone
                    : null;
            case PixKeyType.Random:
                return Guid.TryParseExact(value, "D", out var evp) ? evp.ToString("D") : null;
            default:
                return null;
        }
    }

    public static string MaskCnpj(string cnpj) =>
        cnpj.Length == 14 ? $"••.•••.•••/{cnpj[8..12]}-{cnpj[12..]}" : "••.•••.•••/••••-••";

    public static string MaskDocument(string digits) => digits.Length == 14 ? MaskCnpj(digits) : MaskCpf(digits);

    // Documento do fornecedor na tela: CNPJ é cadastro público de empresa e aparece inteiro; CPF de
    // fornecedor pessoa física continua mascarado. O valor guardado segue cifrado.
    public static string SupplierDocumentView(string digits) =>
        digits.Length == 14 ? $"{digits[..2]}.{digits[2..5]}.{digits[5..8]}/{digits[8..12]}-{digits[12..]}" : MaskCpf(digits);

    public static string MaskCpf(string cpf) =>
        cpf.Length == 11 ? $"•••.•••.{cpf[6..9]}-{cpf[9..]}" : "•••.•••.•••-••";

    public static string MaskPixKey(PixKeyType type, string key) => type switch
    {
        PixKeyType.Cpf => MaskCpf(key),
        PixKeyType.Cnpj when key.Length == 14 => MaskCnpj(key),
        PixKeyType.Email when key.IndexOf('@') is > 0 and var at => $"{key[0]}•••{key[at..]}",
        PixKeyType.Phone when key.Length >= 4 => $"+55 •• •••••-{key[^4..]}",
        PixKeyType.Random when key.Length >= 8 => $"{key[..4]}…{key[^4..]}",
        _ => "••••",
    };

    // Agência: até 4 dígitos, dígito verificador opcional. Conta: até 20 dígitos com dígito
    // verificador opcional (alguns bancos usam "X").
    public static string? NormalizeBranch(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        return BranchPattern().IsMatch(value) ? value : null;
    }

    public static string? NormalizeAccount(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().ToUpperInvariant();
        return AccountPattern().IsMatch(value) ? value : null;
    }

    public static bool IsBankCode(string? code) => code is { Length: 3 } && code.All(char.IsAsciiDigit);

    [GeneratedRegex(@"^[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,}$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^\d{1,4}(-[\dX])?$")]
    private static partial Regex BranchPattern();

    [GeneratedRegex(@"^\d{1,20}(-[\dX])?$")]
    private static partial Regex AccountPattern();
}
