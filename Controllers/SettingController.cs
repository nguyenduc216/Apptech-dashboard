using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class SettingController : Controller
{
    public IActionResult Index()
    {
        ViewData["Title"] = "Cài đặt";
        ViewData["Breadcrumb"] = "Trang chủ / Cài đặt";
        return View();
    }
}
