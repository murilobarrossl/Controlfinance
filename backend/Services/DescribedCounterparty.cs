using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ControlFinance.API.Services;

// Nome da outra parte lido do transaction_name do banco, só quando o texto segue um padrão
// reconhecível. Na dúvida devolve null: a tela mostra "não informado" em vez de um palpite.
public static class DescribedCounterparty
{
    private static readonly Regex Transfer = new(
        @"^(?:PIX|TED|DOC|TRANSF(?:ERENCIA)?(?:\s+PIX)?)\b[\s\-:]*" +
        @"(?<dir>ENVIAD[OA]|RECEBID[OA]|EMITID[OA]|ENVIO|RECEBIMENTO|ENV|REC)?\b[\s\-:]*" +
        @"(?<prep>PARA|DE|P/)?\b[\s\-:]*(?<name>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TrailingNoise = new(
        @"(\s+|^)(\d{2}/\d{2}(/\d{2,4})?|\d{2}:\d{2}(:\d{2})?|[\d.*\-/]{4,}|PARC(ELA)?\.?\s*\d+\s*/\s*\d+|\d{1,2}\s*/\s*\d{1,2})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "PIX", "QR", "CODE", "QRCODE", "CHAVE", "PAGAMENTO", "PAGTO", "PGTO", "TRANSFERENCIA", "TRANSF", "ENVIADO", "ENVIADA",
        "RECEBIDO", "RECEBIDA", "CONTA", "CORRENTE", "SALDO", "AGENDADO", "AGENDAMENTO", "MESMA", "TITULARIDADE",
        "DEVOLUCAO", "ESTORNO", "TARIFA", "ESTATICO", "DINAMICO", "AUTOMATICO", "INTERNA", "ENTRE", "CONTAS",
        "DE", "DA", "DO", "PARA", "EM", "E", "TED", "DOC", "REC", "ENV", "BANCO", "LIMITE", "CREDITO", "DEBITO",
    };

    public static string? FromAccount(string? bankDescription, bool isCredit)
    {
        var text = Normalize(bankDescription);
        if (text is null) return null;

        var pipe = text.Split('|');
        if (pipe.Length == 2)
        {
            var kind = Fold(pipe[0]);
            if (!Regex.IsMatch(kind, @"\b(PIX|TED|DOC|TRANSF)", RegexOptions.IgnoreCase)) return null;
            if (Conflicts(kind, isCredit)) return null;
            return CleanName(pipe[1]);
        }
        if (pipe.Length > 2) return null;

        var match = Transfer.Match(Fold(text));
        if (!match.Success) return null;
        var direction = $"{match.Groups["dir"].Value} {match.Groups["prep"].Value}";
        if (Conflicts(direction, isCredit)) return null;

        // O grupo foi casado no texto sem acento; o nome é recortado do original na mesma posição.
        var name = text[match.Groups["name"].Index..];
        return CleanName(name);
    }

    public static string? FromCard(string? bankDescription)
    {
        var text = Normalize(bankDescription);
        if (text is null) return null;
        text = TrailingNoise.Replace(text, "").Trim();
        return text.Count(char.IsLetter) >= 2 ? text : null;
    }

    private static bool Conflicts(string direction, bool isCredit)
    {
        var d = direction.ToUpperInvariant();
        var sent = Regex.IsMatch(d, @"\b(ENVIAD[OA]|EMITID[OA]|ENVIO|ENV|PARA|P/)\b");
        var received = Regex.IsMatch(d, @"\b(RECEBID[OA]|RECEBIMENTO|REC|DE)\b");
        return (sent && isCredit) || (received && !isCredit);
    }

    private static string? CleanName(string raw)
    {
        var name = raw.Trim(' ', '-', ':', '*');
        for (var i = 0; i < 3; i++) name = TrailingNoise.Replace(name, "").Trim(' ', '-', ':', '*');

        if (name.Length is < 2 or > 60) return null;
        if (!name.All(c => char.IsLetter(c) || c is ' ' or '.' or '&' or '\'' or '-')) return null;

        var words = Fold(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.All(GenericWords.Contains)) return null;
        return name.Count(char.IsLetter) >= 2 ? name : null;
    }

    private static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return Regex.Replace(text.Trim(), @"\s+", " ");
    }

    // Mesmo comprimento do original (só troca letras acentuadas), para os índices do regex valerem nos dois.
    private static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsSurrogate(c))
            {
                sb.Append(c);
                continue;
            }
            var decomposed = c.ToString().Normalize(NormalizationForm.FormD);
            sb.Append(decomposed.Length > 0 && CharUnicodeInfo.GetUnicodeCategory(decomposed[0]) != UnicodeCategory.NonSpacingMark
                ? decomposed[0]
                : c);
        }
        return sb.ToString();
    }
}
