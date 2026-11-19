using System.Net;
using System.Text.Json;
using ControlFinance.API.Models;
using ControlFinance.API.Services;
using ControlFinance.API.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Tests;

public class PolpLoanImportTests
{
    private sealed class LoansPolp(ScriptedPolp inner) : IPolpService
    {
        public List<PolpLoanDto> Loans { get; set; } = [];
        public HttpStatusCode? Failure { get; set; }
        public int Calls { get; private set; }

        public Task<List<PolpLoanDto>> GetLoansAsync(Guid consentId, CancellationToken ct = default)
        {
            Calls++;
            return Failure is { } status
                ? throw new HttpRequestException("Falha simulada.", null, status)
                : Task.FromResult(Loans.ToList());
        }

        // Consulta individual: contrato fora de Loans responde 404 (removido na Polp), a menos que esteja em
        // StillExisting (lista transitória incompleta) ou que SingleFailure simule uma falha do provedor.
        public List<PolpLoanDto> StillExisting { get; set; } = [];
        public HttpStatusCode? SingleFailure { get; set; }

        public Task<PolpLoanDto> GetLoanAsync(Guid loanId, CancellationToken ct = default)
        {
            if (SingleFailure is { } status) throw new HttpRequestException("Falha simulada.", null, status);
            var found = Loans.Concat(StillExisting).FirstOrDefault(l => l.Id == loanId);
            return found is not null
                ? Task.FromResult(found)
                : throw new HttpRequestException("Não encontrado.", null, HttpStatusCode.NotFound);
        }

        public Task<List<PolpInstitutionDto>> GetInstitutionsAsync(CancellationToken ct = default) => inner.GetInstitutionsAsync(ct);
        public Task<PolpConsentDto> CreateConsentAsync(CreatePolpConsentRequest request, CancellationToken ct = default) => inner.CreateConsentAsync(request, ct);
        public Task<PolpConsentDto> GetConsentAsync(Guid consentId, CancellationToken ct = default) => inner.GetConsentAsync(consentId, ct);
        public Task<List<PolpAccountDto>> GetAccountsAsync(Guid consentId, PolpQueryWindow? window = null, CancellationToken ct = default) => inner.GetAccountsAsync(consentId, window, ct);
        public Task<List<PolpCreditCardDto>> GetCreditCardsAsync(Guid consentId, PolpQueryWindow? window = null, CancellationToken ct = default) => inner.GetCreditCardsAsync(consentId, window, ct);
        public Task<List<PolpBillDto>> GetBillsAsync(Guid creditCardId, PolpQueryWindow? window = null, CancellationToken ct = default) => inner.GetBillsAsync(creditCardId, window, ct);
        public Task<List<PolpAccountTransactionDto>> GetAccountTransactionsAsync(Guid accountId, PolpQueryWindow? window = null, CancellationToken ct = default) => inner.GetAccountTransactionsAsync(accountId, window, ct);
        public Task<List<PolpCardTransactionDto>> GetCardTransactionsAsync(Guid creditCardId, PolpQueryWindow? window = null, CancellationToken ct = default) => inner.GetCardTransactionsAsync(creditCardId, window, ct);
        public Task<PolpAccountDto> SyncBalanceAsync(Guid accountId, CancellationToken ct = default) => inner.SyncBalanceAsync(accountId, ct);
        public Task DeleteConsentAsync(Guid consentId, CancellationToken ct = default) => inner.DeleteConsentAsync(consentId, ct);
    }

    private static PolpIntegration Integration(TestDb t, string documentType, string products)
    {
        t.User.DocumentType = documentType;
        var integration = SyncHarness.AddIntegration(t);
        integration.ConsentProductsJson = products;
        t.Db.SaveChanges();
        return integration;
    }

    private const string WithCredit = "[\"ACCOUNT\",\"CREDIT_CARD_ACCOUNT\",\"INVESTMENTS\",\"CREDIT_OPERATIONS\"]";

    private static PolpLoanDto Loan(Guid id, Guid consent, string? amount, string? outstanding = null) => new()
    {
        Id = id, ConsentId = consent, ProductSubType = "EMPRESTIMO_PESSOAL", ContractDate = "2026-01-10",
        ContractAmount = amount, Cet = "0.290000",
        ScheduledInstalments = new PolpLoanInstalmentsDto { Total = 12, Paid = 3 },
        Payments = outstanding is null ? null : new PolpLoanPaymentsDto { OutstandingBalance = outstanding }
    };

    private static PolpLoanImportService Service(TestDb t, IPolpService polp) => new(t.Db, polp, new IntegrationLock(), t.Clock);

    [Fact]
    public async Task Resync_updates_in_place_without_duplicates_and_drops_contracts_gone_from_polp()
    {
        using var t = new TestDb();
        var integration = Integration(t, DocumentTypes.Cpf, WithCredit);
        var polp = new LoansPolp(new ScriptedPolp());
        var kept = Guid.NewGuid();
        var gone = Guid.NewGuid();
        polp.Loans = [Loan(kept, integration.PolpIntegrationId, "10000.00"), Loan(gone, integration.PolpIntegrationId, "500.00"), Loan(kept, integration.PolpIntegrationId, "10000.00")];

        await Service(t, polp).ImportAsync(integration, default);
        Assert.Equal(2, await t.Db.LoanContracts.CountAsync());

        polp.Loans = [Loan(kept, integration.PolpIntegrationId, "10000.00", "7500.50")];
        await Service(t, polp).ImportAsync(integration, default);

        var row = Assert.Single(await t.Db.LoanContracts.AsNoTracking().ToListAsync());
        Assert.Equal(kept, row.PolpLoanId);
        var values = JsonSerializer.Deserialize<LoanContractValues>(row.ValuesJson)!;
        Assert.Equal(10000.00m, values.ContractAmount);
        Assert.Equal(7500.50m, values.OutstandingBalance);
        Assert.Equal(0.29m, values.Cet);
        Assert.Null(values.NextInstalmentAmount);
        Assert.Null(values.PastDueInstalments);
    }

    [Fact]
    public async Task Empty_or_incomplete_list_keeps_contracts_until_polp_confirms_removal()
    {
        using var t = new TestDb();
        var integration = Integration(t, DocumentTypes.Cpf, WithCredit);
        var contract = Loan(Guid.NewGuid(), integration.PolpIntegrationId, "10000.00");
        var polp = new LoansPolp(new ScriptedPolp()) { Loans = [contract] };
        await Service(t, polp).ImportAsync(integration, default);

        polp.Loans = [];
        polp.StillExisting = [contract];
        await Service(t, polp).ImportAsync(integration, default);
        Assert.Single(await t.Db.LoanContracts.ToListAsync());

        polp.StillExisting = [];
        polp.SingleFailure = HttpStatusCode.ServiceUnavailable;
        await Service(t, polp).ImportAsync(integration, default);
        Assert.Single(await t.Db.LoanContracts.ToListAsync());

        polp.SingleFailure = null;
        await Service(t, polp).ImportAsync(integration, default);
        Assert.Empty(await t.Db.LoanContracts.ToListAsync());
    }

    [Fact]
    public async Task Missing_or_invalid_values_stay_null()
    {
        using var t = new TestDb();
        var integration = Integration(t, DocumentTypes.Cpf, WithCredit);
        var polp = new LoansPolp(new ScriptedPolp()) { Loans = [Loan(Guid.NewGuid(), integration.PolpIntegrationId, "abc")] };

        await Service(t, polp).ImportAsync(integration, default);

        var values = JsonSerializer.Deserialize<LoanContractValues>((await t.Db.LoanContracts.SingleAsync()).ValuesJson)!;
        Assert.Null(values.ContractAmount);
        Assert.Null(values.OutstandingBalance);
    }

    [Fact]
    public async Task Contract_of_another_consent_is_ignored()
    {
        using var t = new TestDb();
        var integration = Integration(t, DocumentTypes.Cpf, WithCredit);
        var polp = new LoansPolp(new ScriptedPolp()) { Loans = [Loan(Guid.NewGuid(), Guid.NewGuid(), "100.00")] };

        await Service(t, polp).ImportAsync(integration, default);

        Assert.Empty(await t.Db.LoanContracts.ToListAsync());
    }

    [Theory]
    [InlineData(DocumentTypes.Cnpj, WithCredit)]
    [InlineData(DocumentTypes.Cpf, "[\"ACCOUNT\",\"CREDIT_CARD_ACCOUNT\",\"INVESTMENTS\"]")]
    public async Task Cnpj_or_consent_without_credit_operations_never_calls_polp(string documentType, string products)
    {
        using var t = new TestDb();
        var integration = Integration(t, documentType, products);
        var polp = new LoansPolp(new ScriptedPolp()) { Loans = [Loan(Guid.NewGuid(), integration.PolpIntegrationId, "100.00")] };

        await Service(t, polp).ImportAsync(integration, default);

        Assert.Equal(0, polp.Calls);
        Assert.Empty(await t.Db.LoanContracts.ToListAsync());
    }

    [Fact]
    public async Task Forbidden_keeps_existing_contracts_and_server_error_propagates_for_retry()
    {
        using var t = new TestDb();
        var integration = Integration(t, DocumentTypes.Cpf, WithCredit);
        var polp = new LoansPolp(new ScriptedPolp()) { Loans = [Loan(Guid.NewGuid(), integration.PolpIntegrationId, "100.00")] };
        await Service(t, polp).ImportAsync(integration, default);

        polp.Failure = HttpStatusCode.Forbidden;
        await Service(t, polp).ImportAsync(integration, default);
        Assert.Single(await t.Db.LoanContracts.ToListAsync());

        polp.Failure = HttpStatusCode.InternalServerError;
        await Assert.ThrowsAsync<HttpRequestException>(() => Service(t, polp).ImportAsync(integration, default));
        Assert.Single(await t.Db.LoanContracts.ToListAsync());
    }

    [Fact]
    public void Personal_consents_request_credit_operations_and_business_ones_do_not()
    {
        Assert.Contains(PolpService.LoanContractsProduct, PolpService.ProductsFor(business: false));
        Assert.DoesNotContain(PolpService.LoanContractsProduct, PolpService.ProductsFor(business: true));
        Assert.True(PolpService.HasAllRequestedProducts(["ACCOUNT", "CREDIT_CARD_ACCOUNT", "INVESTMENTS"]));
    }
}
