using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

public enum PayeePaymentLevel { Identified = 2 }

public sealed record PayeeMatch(Guid Id, PayeePaymentLevel Level);

// Um documento que identifica quem recebe: o CPF do próprio funcionário ou o titular de um método
// (chave Pix CPF/CNPJ ou titular declarado). CpfHashes: todas as chaves de lookup ativas. AccountHashes:
// conta do método TED a que o titular está ligado; nulo quando não há conta para contradizer (Pix ou
// o próprio funcionário).
public sealed record PayeeHolder(IReadOnlySet<string>? CpfHashes, string? Cnpj, IReadOnlySet<string>? AccountHashes = null);

// Conta para TED cadastrada; HolderType = tipo do titular declarado (nulo em método antigo).
public sealed record PayeeAccount(IReadOnlySet<string> AccountHashes, string? HolderType);

public sealed record EmployeeIdentity(Guid Id, IReadOnlyList<PayeeHolder> Holders, IReadOnlyList<PayeeAccount> Accounts);

// Dados do destinatário que vêm do banco (partie_*): CPF e conta completa só como HMAC por empresa.
// BankCnpj só quando o CNPJ veio do banco; CounterpartyCnpj pode ser do enriquecimento (nunca prova).
public sealed record PaymentEvidence(
    string? CounterpartyCnpj, string? CounterpartyCpfHash,
    string? CounterpartyAccountHash = null, string? CounterpartyPersonType = null, string? BankCnpj = null,
    Guid TransactionId = default, decimal Amount = 0m, DateTime Date = default,
    string? AccountName = null, string? AccountNumber = null);

// Liga uma saída a um funcionário cadastrado só por identificador exato vindo do banco; nome, descrição
// e enriquecimento nunca entram. A Polp v2 não informa a chave Pix usada, só o titular que recebeu:
// 1. Documento do titular informado pelo banco (CPF, ou CNPJ vindo do banco) igual ao CPF do
//    funcionário ou ao titular de um método dele. Se o banco também informa a conta e o método é uma
//    conta TED diferente, esse método não confirma. Documento presente decide sozinho: sem candidato,
//    nada (nunca cai para conta).
// 2. Sem documento do banco: CNPJ só do enriquecimento = nada.
// 3. Sem documento: conta completa (banco, agência, número e dígito) igual a uma conta TED cadastrada,
//    com tipo de pessoa compatível com o titular declarado.
// Mais de um funcionário candidato = nada: melhor sem identificação do que com a pessoa errada.
public static class SupplierPaymentMatcher
{
    public static PayeeMatch? MatchEmployee(PaymentEvidence tx, IReadOnlyList<EmployeeIdentity> employees)
    {
        var account = tx.CounterpartyAccountHash;
        if (tx.CounterpartyCpfHash is { } cpfHash)
            return Identified(employees.Where(e => e.Holders.Any(h => h.CpfHashes is not null && h.CpfHashes.Contains(cpfHash) && Fits(h, account))).Select(e => e.Id));
        if (tx.BankCnpj is { } cnpj)
            return Identified(employees.Where(e => e.Holders.Any(h => h.Cnpj == cnpj && Fits(h, account))).Select(e => e.Id));
        if (tx.CounterpartyCnpj is not null) return null;
        if (account is not null)
            return Identified(employees.Where(e => e.Accounts.Any(a => a.AccountHashes.Contains(account) && TypeFits(a.HolderType, tx.CounterpartyPersonType))).Select(e => e.Id));
        return null;
    }

    // Conta informada pelo banco que não é a do método TED = contradição.
    private static bool Fits(PayeeHolder holder, string? account) =>
        holder.AccountHashes is null || account is null || holder.AccountHashes.Contains(account);

    // Sem titular declarado (método antigo), a regra continua: destinatário pessoa jurídica nunca é o funcionário.
    private static bool TypeFits(string? holderType, string? personType) => holderType switch
    {
        DocumentTypes.Cnpj => personType != "PESSOA_NATURAL",
        _ => personType != "PESSOA_JURIDICA",
    };

    private static PayeeMatch? Identified(IEnumerable<Guid> ids) =>
        Single(ids) is { } id ? new PayeeMatch(id, PayeePaymentLevel.Identified) : null;

    private static Guid? Single(IEnumerable<Guid> ids)
    {
        Guid? found = null;
        foreach (var id in ids)
        {
            if (found is not null) return null;
            found = id;
        }
        return found;
    }
}
