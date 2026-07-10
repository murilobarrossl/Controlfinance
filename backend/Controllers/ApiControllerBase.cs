using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace ControlFinance.API.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
