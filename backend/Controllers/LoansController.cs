using System.Text.Json;
using ControlFinance.API.Data;
using ControlFinance.API.Services;
using ControlFinance.API.Services.Investments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers;

// Contratos são só leitura e nunca entram em totais: o valor liberado e as parcelas pagas já
// aparecem pelo extrato da conta. Login CNPJ recebe 404, como as outras rotas só de CPF.
[ApiController]
[Route("api/loans")]
[Authorize]
public class LoansController(AppDbContext db) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (!await InvestmentHolder.AllowsAsync(db, UserId, ct)) return NotFound();

        var integrations = await db.PolpIntegrations.AsNoTracking()
            .Where(p => p.UserId == UserId && p.Status == PolpSyncService.AuthorisedStatus)
            .Select(p => new { p.Id, p.InstitutionName, p.ConsentProductsJson })
            .ToListAsync(ct);
        var names = integrations.ToDictionary(p => p.Id, p => p.InstitutionName);

        var rows = await db.LoanContracts.AsNoTracking()
            .Where(l => l.UserId == UserId)
            .OrderBy(l => l.FirstObservedAt)
            .ToListAsync(ct);

        return Ok(new
        {
            authorizedConnections = integrations.Count(p => PolpLoanImportService.Authorizes(p.ConsentProductsJson)),
            connectionsWithoutAuthorization = integrations.Count(p => !PolpLoanImportService.Authorizes(p.ConsentProductsJson)),
            contracts = rows
                .Where(l => names.ContainsKey(l.IntegrationId))
                .Select(l => new
                {
                    id = l.Id,
                    institutionName = names[l.IntegrationId],
                    values = JsonSerializer.Deserialize<LoanContractValues>(l.ValuesJson),
                    providerUpdatedAt = l.ProviderUpdatedAt,
                    observedAt = l.LastObservedAt
                })
        });
    }
}
