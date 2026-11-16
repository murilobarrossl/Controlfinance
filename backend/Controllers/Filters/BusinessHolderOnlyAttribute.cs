using ControlFinance.API.Data;
using ControlFinance.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace ControlFinance.API.Controllers.Filters;

// Rotas do ambiente empresarial. O frontend esconde as telas, mas a regra precisa valer na API:
// o tipo vem do cadastro no banco (User.DocumentType), não do token nem de nada enviado pelo
// cliente. Login sem tipo conhecido é tratado como pessoa física, igual ao frontend.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class BusinessHolderOnlyAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetRequiredService<AppDbContext>();
        var userId = ApiControllerBase.ReadUserId(http.User);
        var documentType = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.DocumentType)
            .FirstOrDefaultAsync(http.RequestAborted);

        if (documentType != DocumentTypes.Cnpj)
        {
            context.Result = new ObjectResult(new { message = "Disponível apenas para contas de empresa (CNPJ)." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
