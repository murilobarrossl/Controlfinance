namespace ControlFinance.API.Services;

// Número de conta/cartão reduzido ao mínimo útil pra o usuário reconhecer o produto na tela.
// Guardar o número inteiro não habilitava nenhuma funcionalidade e aumentava o estrago de um
// vazamento do banco.
public static class AccountNumberMask
{
    private const string Prefix = "•••• ";

    public static string? Mask(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (raw.StartsWith(Prefix, StringComparison.Ordinal) && raw.Length <= Prefix.Length + 4) return raw;

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? Prefix + digits[^4..] : null;
    }
}
