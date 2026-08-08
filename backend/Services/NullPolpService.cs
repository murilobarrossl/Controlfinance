namespace ControlFinance.API.Services;

// Usado no lugar de PolpService quando Polp:ClientId/Polp:ClientSecret não estão configurados
// (ex.: plano com a Polp encerrado/não renovado) — ver registro condicional em Program.cs. Sem
// isso, PolpService.ctor lança exceção assim que o ASP.NET tenta construir PolpController (mesmo
// pra ações que só leem do banco, como listar as contas já sincronizadas no seletor), derrubando
// a aplicação inteira com 500 em vez de só desativar a parte que precisa falar com a Polp de
// verdade. Contas/cartões/transações já sincronizados continuam funcionando normalmente — essa
// classe só entra em jogo pra ações que exigiriam uma chamada de rede nova.
public class NullPolpService : IPolpService
{
    private const string DisabledMessage =
        "A integração com bancos está temporariamente indisponível. Tente novamente mais tarde.";

    public Task<List<PolpInstitutionDto>> GetInstitutionsAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<PolpInstitutionDto>());

    public Task<PolpIntegrationDto> CreateIntegrationAsync(int institutionId, string document, CancellationToken ct = default) =>
        throw new HttpRequestException(DisabledMessage);

    public Task<PolpIntegrationDto> GetIntegrationAsync(int integrationId, CancellationToken ct = default) =>
        throw new HttpRequestException(DisabledMessage);

    public Task<List<PolpAccountDto>> GetAccountsAsync(int integrationId, CancellationToken ct = default) =>
        Task.FromResult(new List<PolpAccountDto>());

    public Task<List<PolpTransactionDto>> GetTransactionsAsync(int accountId, CancellationToken ct = default) =>
        Task.FromResult(new List<PolpTransactionDto>());

    public Task<List<PolpCreditCardDto>> GetCreditCardsAsync(int integrationId, CancellationToken ct = default) =>
        Task.FromResult(new List<PolpCreditCardDto>());

    public Task<List<PolpBillDto>> GetBillsAsync(int creditCardId, CancellationToken ct = default) =>
        Task.FromResult(new List<PolpBillDto>());
}
