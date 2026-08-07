namespace ControlFinance.API.Services;

// Valida o dígito verificador de CPF/CNPJ (algoritmo público, mod 11). Não confirma que o
// documento está de fato registrado na Receita Federal (isso exigiria um serviço externo pago,
// tipo um KYC, fora do escopo daqui), só rejeita documentos estruturalmente inválidos:
// sequências repetidas ("11111111111") e dígitos verificadores que não batem com o algoritmo.
public static class DocumentValidator
{
    public static bool IsValid(string digits) => digits.Length switch
    {
        11 => IsValidCpf(digits),
        14 => IsValidCnpj(digits),
        _ => false,
    };

    public static bool IsValidCpf(string cpf)
    {
        if (cpf.Length != 11 || !cpf.All(char.IsDigit)) return false;
        if (AllSameDigit(cpf)) return false;

        var digits = cpf.Select(c => c - '0').ToArray();

        var v1 = CheckDigit(digits, count: 9, firstWeight: 10);
        if (digits[9] != v1) return false;

        var v2 = CheckDigit(digits, count: 10, firstWeight: 11);
        return digits[10] == v2;
    }

    public static bool IsValidCnpj(string cnpj)
    {
        if (cnpj.Length != 14 || !cnpj.All(char.IsDigit)) return false;
        if (AllSameDigit(cnpj)) return false;

        var digits = cnpj.Select(c => c - '0').ToArray();
        int[] weights1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] weights2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

        var v1 = CheckDigitWeighted(digits, count: 12, weights1);
        if (digits[12] != v1) return false;

        var v2 = CheckDigitWeighted(digits, count: 13, weights2);
        return digits[13] == v2;
    }

    private static bool AllSameDigit(string doc) => doc.Distinct().Count() == 1;

    // CPF: peso decrescente a partir de firstWeight (10 pro 1º dígito verificador, 11 pro 2º,
    // já incluindo o 1º dígito verificador como mais um termo da soma).
    private static int CheckDigit(int[] digits, int count, int firstWeight)
    {
        var sum = 0;
        for (var i = 0; i < count; i++)
            sum += digits[i] * (firstWeight - i);

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }

    private static int CheckDigitWeighted(int[] digits, int count, int[] weights)
    {
        var sum = 0;
        for (var i = 0; i < count; i++)
            sum += digits[i] * weights[i];

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
