using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class SettingController(IAttendanceSettingsService attendanceSettingsService) : Controller
{
    public async Task<IActionResult> Index([FromQuery] string? attendanceSettingsSaved = null)
    {
        ViewData["Title"] = "Cài đặt";
        ViewData["Breadcrumb"] = "Trang chủ / Cài đặt";

        var savedStatus = NormalizeSavedStatus(attendanceSettingsSaved);
        var tempDataMessage = TempData["AttendanceSettingsMessage"] as string;
        var tempDataType = TempData["AttendanceSettingsType"] as string;

        return View(new AttendanceSettingsViewModel
        {
            Schedule = await attendanceSettingsService.GetScheduleAsync(HttpContext.RequestAborted),
            StatusMessage = ResolveStatusMessage(savedStatus, tempDataMessage),
            StatusType = savedStatus ?? tempDataType ?? "info"
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAttendanceSchedule(
        [Bind(Prefix = "Schedule")] AttendanceScheduleSettingsForm form)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "Cài đặt";
            ViewData["Breadcrumb"] = "Trang chủ / Cài đặt";
            return View("Index", new AttendanceSettingsViewModel
            {
                Schedule = form,
                StatusMessage = "Vui lòng kiểm tra lại 4 mốc giờ chấm công.",
                StatusType = "error"
            });
        }

        var result = await attendanceSettingsService.SaveScheduleAsync(form, HttpContext.RequestAborted);
        if (result.Succeeded)
        {
            return RedirectToAction(nameof(Index), new { attendanceSettingsSaved = "success" });
        }

        TempData["AttendanceSettingsMessage"] = result.ErrorMessage ?? "Không thể lưu cấu hình giờ chấm công.";
        TempData["AttendanceSettingsType"] = "error";
        return RedirectToAction(nameof(Index), new { attendanceSettingsSaved = "error" });
    }

    private static string? NormalizeSavedStatus(string? value)
    {
        return value is "success" or "error"
            ? value
            : null;
    }

    private static string? ResolveStatusMessage(string? savedStatus, string? tempDataMessage)
    {
        if (!string.IsNullOrWhiteSpace(tempDataMessage))
        {
            return tempDataMessage;
        }

        return savedStatus switch
        {
            "success" => "Đã lưu cấu hình giờ chấm công.",
            "error" => "Không thể lưu cấu hình giờ chấm công.",
            _ => null
        };
    }
}
