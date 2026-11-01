using Microsoft.AspNetCore.Mvc;

namespace ControlFinance.API.Tests.Support;

public static class Results
{
    public static T OkValue<T>(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsAssignableFrom<T>(ok.Value);
    }
}
