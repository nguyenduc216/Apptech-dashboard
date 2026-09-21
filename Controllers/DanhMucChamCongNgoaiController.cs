using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public sealed class DanhMucChamCongNgoaiController(IDanhMucChamCongNgoaiService service) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? keyword, bool? statusFilter, int? editId)
    {
        var model = new DanhMucChamCongNgoaiPageModel
        {
            Keyword = keyword,
            StatusFilter = statusFilter,
            Items = await service.SearchAsync(keyword, statusFilter, HttpContext.RequestAborted),
            StatusMessage = TempData["StatusMessage"]?.ToString(),
            StatusType = TempData["StatusType"]?.ToString() ?? "info"
        };
        if (editId is > 0 && await service.GetByIdAsync(editId.Value, HttpContext.RequestAborted) is { } item)
        {
            model.Form = new DanhMucChamCongNgoaiForm { Id = item.Id, TenNoiDung = item.TenNoiDung, ThuTuHienThi = item.ThuTuHienThi, IsActive = item.IsActive };
        }
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([Bind(Prefix = "Form")] DanhMucChamCongNgoaiForm form)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", new DanhMucChamCongNgoaiPageModel { Form = form, Items = await service.SearchAsync(null, null, HttpContext.RequestAborted) });
        }
        var result = await service.SaveAsync(form, HttpContext.RequestAborted);
        TempData["StatusMessage"] = result.Succeeded ? "Đã lưu nội dung chấm công ngoài." : result.ErrorMessage;
        TempData["StatusType"] = result.Succeeded ? "success" : "error";
        return RedirectToAction(nameof(Index), result.Succeeded ? null : new { editId = form.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool isActive)
    {
        var result = await service.SetActiveAsync(id, isActive, HttpContext.RequestAborted);
        TempData["StatusMessage"] = result.Succeeded ? (isActive ? "Đã bật sử dụng danh mục." : "Đã ngừng sử dụng danh mục.") : result.ErrorMessage;
        TempData["StatusType"] = result.Succeeded ? "success" : "error";
        return RedirectToAction(nameof(Index));
    }
}
