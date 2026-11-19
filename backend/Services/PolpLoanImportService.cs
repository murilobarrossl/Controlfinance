using System.Globalization;
using System.Text.Json;
using ControlFinance.API.Data;
using ControlFinance.API.Models;
using ControlFinance.API.Services.Investments;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Services;

public sealed record LoanContractValues(
    string? ProductType, string? ProductSubType, string? ProductName, string? ContractDate,
    decimal? ContractAmount, string? Currency, decimal? Cet, string? InstalmentPeriodicity,
    string? AmortizationScheduled, string? FirstInstalmentDueDate, string? DueDate, string? SettlementDate,
    decimal? NextInstalmentAmount, int? TotalInstalments, int? RemainingInstalments, int? PaidInstalments,
    int? DueInstalments, int? PastDueInstalments, decimal? OutstandingBalance, decimal? TotalRemainingAmount);

// Espelha a lista de contratos do consentimento: a lista inteira vem da Polp e substitui a local
// (contrato que sumiu da Polp sai daqui). Campo ausente ou inválido fica nulo, nunca estimado.
public class PolpLoanImportService(AppDbContext db, IPolpService polp, IIntegrationLock integrationLock, TimeProvider time)
{
    public static bool Authorizes(string? productsJson)
    {
        if (productsJson is null) return false;
        try { return JsonSerializer.Deserialize<string[]>(productsJson)?.Contains(PolpService.LoanContractsProduct) == true; }
        catch (JsonException) { return false; }
    }

    public async Task ImportAsync(PolpIntegration local, CancellationToken ct)
    {
        if (local.Status != PolpSyncService.AuthorisedStatus || !Authorizes(local.ConsentProductsJson)) return;
        if (!await InvestmentHolder.AllowsAsync(db, local.UserId, ct)) return;

        List<PolpLoanDto> remote;
        try
        {
            remote = await polp.GetLoansAsync(local.PolpIntegrationId, ct);
        }
        // 403/404: consentimento inativo ou sem acesso a empréstimos. O status do consentimento é
        // atualizado pelo fluxo de metadados; repetir a leitura não mudaria a resposta.
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
        {
            return;
        }
        var now = time.GetUtcNow().UtcDateTime;

        await integrationLock.RunAsync(db, local.Id, async () =>
        {
            var active = await db.PolpIntegrations.AnyAsync(p => p.Id == local.Id && p.UserId == local.UserId
                && p.Status == PolpSyncService.AuthorisedStatus, ct);
            if (!active) return false;

            var existing = await db.LoanContracts
                .Where(x => x.UserId == local.UserId && x.IntegrationId == local.Id)
                .ToDictionaryAsync(x => x.PolpLoanId, ct);
            var seen = new HashSet<Guid>();
            foreach (var loan in remote)
            {
                if (loan.Id == Guid.Empty || (loan.ConsentId != Guid.Empty && loan.ConsentId != local.PolpIntegrationId)) continue;
                if (!seen.Add(loan.Id)) continue;
                var values = JsonSerializer.Serialize(ToValues(loan));
                if (existing.TryGetValue(loan.Id, out var row))
                {
                    row.ValuesJson = values;
                    row.ProviderUpdatedAt = loan.UpdatedAt?.ToUniversalTime();
                    row.LastObservedAt = now;
                }
                else
                {
                    db.LoanContracts.Add(new LoanContract
                    {
                        UserId = local.UserId, IntegrationId = local.Id, PolpLoanId = loan.Id, ValuesJson = values,
                        ProviderUpdatedAt = loan.UpdatedAt?.ToUniversalTime(), FirstObservedAt = now, LastObservedAt = now
                    });
                }
            }
            db.LoanContracts.RemoveRange(existing.Where(e => !seen.Contains(e.Key)).Select(e => e.Value));
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    private static LoanContractValues ToValues(PolpLoanDto l) => new(
        l.ProductType, l.ProductSubType, l.ProductName, Date(l.ContractDate), Money(l.ContractAmount), l.Currency,
        Money(l.Cet), l.InstalmentPeriodicity, l.AmortizationScheduled, Date(l.FirstInstalmentDueDate), Date(l.DueDate),
        Date(l.SettlementDate), Money(l.NextInstalmentAmount), l.ScheduledInstalments?.Total, l.ScheduledInstalments?.Remaining,
        l.ScheduledInstalments?.Paid, l.ScheduledInstalments?.Due, l.ScheduledInstalments?.PastDue,
        Money(l.Payments?.OutstandingBalance), Money(l.Payments?.TotalRemainingAmount));

    private static decimal? Money(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string? Date(string? value) =>
        DateOnly.TryParseExact(value?.Length >= 10 ? value[..10] : value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
}
