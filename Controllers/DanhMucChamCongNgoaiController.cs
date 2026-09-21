using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public sealed class DanhMucChamCongNgoaiController(
    IDanhMucChamCongNgoaiService service,
    IUserPermissionService userPermissionService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? keyword, bool? statusFilter, int? editId)
    {
        if (!await HasPermissionAsync(PermissionCatalogService.DanhMucChamCongNgoaiViewPermissionCode))
        {
            return Forbid();
        }

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
        var permissionCode = form.Id is > 0
            ? PermissionCatalogService.DanhMucChamCongNgoaiUpdatePermissionCode
            : PermissionCatalogService.DanhMucChamCongNgoaiCreatePermissionCode;
        if (!await HasPermissionAsync(permissionCode))
        {
            return Forbid();
        }

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
        if (!await HasPermissionAsync(PermissionCatalogService.DanhMucChamCongNgoaiDeletePermissionCode))
        {
            return Forbid();
        }

        var result = await service.SetActiveAsync(id, isActive, HttpContext.RequestAborted);
        TempData["StatusMessage"] = result.Succeeded ? (isActive ? "Đã bật sử dụng danh mục." : "Đã ngừng sử dụng danh mục.") : result.ErrorMessage;
        TempData["StatusType"] = result.Succeeded ? "success" : "error";
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> HasPermissionAsync(string permissionCode)
    {
        if (User.IsInRole("Administrator"))
        {
            return true;
        }

        var permissions = await UserPermissionSession.GetOrLoadAsync(
            HttpContext,
            userPermissionService,
            HttpContext.RequestAborted);
        return permissions.Any(permission =>
            string.Equals(permission.PermissionCode, permissionCode, StringComparison.OrdinalIgnoreCase));
    }
}
