using ControlFinance.API.Data;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ControlFinance.API.Controllers;

[ApiController]
[Route("api/credit-cards")]
[Authorize]
public class CreditCardsController(AppDbContext db) : ApiControllerBase
{
    // Cartões reconhecidos do usuário: contas da Polp identificadas como cartão. Ver
    // CreditCardSummaryBuilder.BuildRecognizedListAsync.
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct) =>
        Ok(await CreditCardSummaryBuilder.BuildRecognizedListAsync(db, UserId, await UserTodayAsync(db, ct), ct));
}
