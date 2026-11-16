using ControlFinance.API.DTOs;
using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

public sealed record PayeeBankAccount(string BankCode, string Branch, string Account, PayeeAccountType AccountType);

public static class PayeePayment
{
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
