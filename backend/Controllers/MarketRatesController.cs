using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/market")]
[Authorize]
public class MarketRatesController(ReferenceRatesService rates) : ApiControllerBase
{
    [HttpGet("reference-rates")]
    public async Task<IActionResult> GetReferenceRates(CancellationToken ct) => Ok(await rates.GetAsync(ct));
}
