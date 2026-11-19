using ControlFinance.API.DTOs;
using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

public sealed record PayeeBankAccount(string BankCode, string Branch, string Account, PayeeAccountType AccountType);

public sealed record ParsedPaymentMethod(PayeePaymentKind Kind, PixKeyType PixType, string PixKey, PayeeBankAccount? Bank,
    string? HolderType = null, string? HolderDocument = null);

public static class PayeePayment
{
    public const int MaxMethodsPerPayee = 10;

    // Só validação e normalização. Duplicidade de chave Pix (por empresa) e de conta (por cadastro)
    // é checada por quem chama, porque cada cadastro tem a própria tabela e o próprio HMAC.
    public static string? ParseMethod(PayeePaymentMethodDto dto, out ParsedPaymentMethod? parsed)
    {
        parsed = null;
        if (!Enum.TryParse<PayeePaymentKind>(dto.Kind, true, out var kind) || !Enum.IsDefined(kind))
            return "Escolha Pix ou transferência.";
        if (kind == PayeePaymentKind.Pix)
        {
            var pixError = ParsePix(dto.Pix ?? new PayeePixDto(null, null), out var type, out var key);
            if (pixError is not null) return pixError;
            string? pixHolderType = null, pixHolder = null;
            if (!IsDocumentKey(type) && ParseHolder(dto.HolderDocument, out pixHolderType, out pixHolder) is { } pixHolderError)
                return pixHolderError;
            parsed = new ParsedPaymentMethod(kind, type, key, null, pixHolderType, pixHolder);
            return null;
        }
        var bankError = ParseBank(dto.Bank ?? new PayeeBankDto(null, null, null, null), out var bank);
        if (bankError is not null) return bankError;
        if (ParseHolder(dto.HolderDocument, out var holderType, out var holder) is { } holderError) return holderError;
        parsed = new ParsedPaymentMethod(kind, default, string.Empty, bank, holderType, holder);
        return null;
    }

    public static bool SameAccount(PayeePaymentMethod m, PayeeBankAccount bank) =>
        m.Kind == PayeePaymentKind.Bank && m.BankCode == bank.BankCode && m.Branch == bank.Branch && m.Account == bank.Account;

    public static void Assign(PayeePaymentMethod method, ParsedPaymentMethod parsed, string? pixKeyHash)
    {
        method.Kind = parsed.Kind;
        var isPix = parsed.Kind == PayeePaymentKind.Pix;
        method.PixKeyType = isPix ? parsed.PixType : null;
        method.PixKey = isPix ? parsed.PixKey : null;
        method.PixKeyHash = isPix ? pixKeyHash : null;
        method.BankCode = parsed.Bank?.BankCode;
        method.Branch = parsed.Bank?.Branch;
        method.Account = parsed.Bank?.Account;
        method.AccountType = parsed.Bank?.AccountType;
        method.HolderDocumentType = parsed.HolderType;
        method.HolderDocument = parsed.HolderDocument;
    }

    public static bool IsDocumentKey(PixKeyType type) => type is PixKeyType.Cpf or PixKeyType.Cnpj;

    // CPF ou CNPJ de quem recebe, validado pelo dígito; nunca presumido.
    private static string? ParseHolder(string? raw, out string? type, out string? digits)
    {
        type = null;
        digits = null;
        var value = PayeeKeys.Digits(raw);
        if (value.Length == 0) return "Informe o CPF ou CNPJ do titular que recebe.";
        if (value.Length == 11 && DocumentValidator.IsValidCpf(value)) type = DocumentTypes.Cpf;
        else if (value.Length == 14 && DocumentValidator.IsValidCnpj(value)) type = DocumentTypes.Cnpj;
        else return "CPF ou CNPJ do titular inválido.";
        digits = value;
        return null;
    }

    // Documento de quem recebe pelo método: a própria chave Pix CPF/CNPJ ou o titular declarado.
    // Nulo em método antigo sem titular.
    public static (string Type, string Digits)? Holder(PayeePaymentMethod m) =>
        m.Kind == PayeePaymentKind.Pix && m.PixKeyType is { } t && IsDocumentKey(t) && m.PixKey is { } key
            ? (t == PixKeyType.Cpf ? DocumentTypes.Cpf : DocumentTypes.Cnpj, key)
            : m.HolderDocumentType is { } type && m.HolderDocument is { } doc ? (type, doc) : null;

    private static bool NeedsHolder(PayeePaymentMethod m) =>
        m.Kind == PayeePaymentKind.Bank || (m.PixKeyType is { } t && !IsDocumentKey(t));

    public static List<PayeePaymentMethodView> Views(IEnumerable<PayeePaymentMethod> methods) =>
        methods.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .Select(m => new PayeePaymentMethodView(m.Id, m.Kind.ToString(),
                m.Kind == PayeePaymentKind.Pix ? PixView(m.PixKeyType, m.PixKey) : null,
                m.Kind == PayeePaymentKind.Bank ? BankView(m.BankCode, m.Branch, m.Account, m.AccountType) : null,
                NeedsHolder(m) && m.HolderDocument is { } doc ? PayeeKeys.MaskDocument(doc) : null,
                NeedsHolder(m) && m.HolderDocument is null))
            .ToList();

    public static string? ParsePix(PayeePixDto dto, out PixKeyType type, out string key)
    {
        key = string.Empty;
        if (!Enum.TryParse(dto.Type, true, out type) || !Enum.IsDefined(type)) return "Tipo de chave Pix inválido.";
        var normalized = PayeeKeys.NormalizePixKey(type, dto.Key);
        if (normalized is null) return PixKeyError(type);
        key = normalized;
        return null;
    }

    public static string? ParseBank(PayeeBankDto dto, out PayeeBankAccount? account)
    {
        account = null;
        var bankCode = dto.BankCode?.Trim();
        if (!PayeeKeys.IsBankCode(bankCode)) return "Informe o código do banco com 3 dígitos.";
        var branch = PayeeKeys.NormalizeBranch(dto.Branch);
        if (branch is null) return "Agência inválida.";
        var number = PayeeKeys.NormalizeAccount(dto.Account);
        if (number is null) return "Número da conta inválido.";
        if (!Enum.TryParse<PayeeAccountType>(dto.AccountType, true, out var accountType) || !Enum.IsDefined(accountType))
            return "Tipo de conta inválido.";
        account = new PayeeBankAccount(bankCode!, branch, number, accountType);
        return null;
    }

    public static PayeePixView? PixView(PixKeyType? type, string? key) =>
        type is { } t && key is { } k ? new PayeePixView(t.ToString(), PayeeKeys.MaskPixKey(t, k)) : null;

    public static PayeeBankView? BankView(string? bankCode, string? branch, string? account, PayeeAccountType? accountType) =>
        bankCode is { } code && branch is { } b && account is { } a && accountType is { } type
            ? new PayeeBankView(code, b, AccountNumberMask.Mask(a) ?? "••••", type.ToString())
            : null;

    private static string PixKeyError(PixKeyType type) => type switch
    {
        PixKeyType.Cpf => "Chave Pix: CPF inválido.",
        PixKeyType.Cnpj => "Chave Pix: CNPJ inválido.",
        PixKeyType.Email => "Chave Pix: e-mail inválido.",
        PixKeyType.Phone => "Chave Pix: informe o celular com DDD.",
        _ => "Chave Pix aleatória inválida.",
    };
}
