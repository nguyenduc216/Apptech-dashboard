using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

public sealed class AccessController : Controller
{
    [AllowAnonymous, HttpGet("/khong-co-quyen")]
    [ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public IActionResult Denied()
    {
        Response.StatusCode=StatusCodes.Status403Forbidden;
        return View();
    }
}
