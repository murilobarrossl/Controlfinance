using ControlFinance.API.Models;

namespace ControlFinance.API.Services;

// Chute inicial pra BankAccount.Ownership quando uma conta é criada: usuário cadastrado com CNPJ
// começa como Business, com CPF começa como Personal. Economiza o clique de quem só tem um tipo
// de conta. É só um ponto de partida: o usuário pode corrigir a qualquer momento, inclusive pra
// Mixed, e essa função nunca é chamada de novo depois da criação.
public static class AccountOwnershipDefault
{
    public static AccountOwnership FromDocument(string document) =>
        document.Length == 14 ? AccountOwnership.Business : AccountOwnership.Personal;
}
