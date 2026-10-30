namespace ControlFinance.API.Services;

// Chave das decisões de recorrência (RecurrenceDecision.NormalizedName). Antes era o nome
// normalizado da transação em texto puro — justamente o dado que passou a ser cifrado em
// Transaction.Name. Agora é um HMAC (chave de busca, com rótulo próprio) do nome normalizado:
// continua servindo pra igualdade/unicidade, sem guardar o nome.
//
// Candidatos incluem todas as versões da chave de busca E o texto puro legado: linhas antigas
// continuam funcionando sem conversão; a conversão em lote (PrivacyBackfill) é opcional por ser
// irreversível (Privacy:HashLegacyRecurrenceNames).
public static class RecurrenceKey
{
    public const string Prefix = "rh:";

    public static string For(IEncryptionService encryption, string normalizedName) =>
        Prefix + encryption.ComputeLookupHash("recurrence|" + normalizedName);

    public static IReadOnlyList<string> Candidates(IEncryptionService encryption, string normalizedName)
    {
        var candidates = encryption.ComputeLookupHashCandidates("recurrence|" + normalizedName)
            .Select(h => Prefix + h)
            .ToList();
        candidates.Add(normalizedName); // legado em texto puro
        return candidates;
    }

    public static bool IsCurrent(IEncryptionService encryption, string storedKey) =>
        storedKey.StartsWith(Prefix, StringComparison.Ordinal)
        && encryption.IsActiveLookupHash(storedKey[Prefix.Length..]);
}
