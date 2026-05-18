using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class VaiTroController(IVaiTroService vaiTroService) : Controller
{
    private const int DefaultPageSize = 10;
    private readonly IVaiTroService _vaiTroService = vaiTroService;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] VaiTroListQuery query)
    {
        var model = await BuildPageModelAsync(query, HttpContext.RequestAborted);

        if ((query.EditId.HasValue || query.PermissionRoleId.HasValue) && model.PopupMode == VaiTroPopupMode.None)
        {
            TempData["StatusMessage"] = "Không tìm thấy vai trò cần xử lý.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(query.Keyword, query.StatusFilter, model.CurrentPage));
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] VaiTroFormModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageModelForPostbackAsync(model, VaiTroPopupMode.Create, HttpContext.RequestAborted));
        }

        var result = await _vaiTroService.CreateAsync(model, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể thêm mới vai trò.");
            return View("Index", await BuildPageModelForPostbackAsync(model, VaiTroPopupMode.Create, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Lưu vai trò thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, 1));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([Bind(Prefix = "Form")] VaiTroFormModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageModelForPostbackAsync(model, VaiTroPopupMode.Edit, HttpContext.RequestAborted));
        }

        var result = await _vaiTroService.UpdateAsync(model, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể cập nhật vai trò.");
            return View("Index", await BuildPageModelForPostbackAsync(model, VaiTroPopupMode.Edit, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Cập nhật vai trò thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(VaiTroDeleteModel model)
    {
        var result = await _vaiTroService.DeleteAsync(model.Id, HttpContext.RequestAborted);
        TempData["StatusMessage"] = result.Succeeded
            ? "Đã xóa vai trò."
            : result.ErrorMessage ?? "Không thể xóa vai trò.";
        TempData["StatusType"] = result.Succeeded ? "success" : "error";

        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePermissions(VaiTroPermissionSaveModel model)
    {
        var result = await _vaiTroService.SavePermissionsAsync(
            model.RoleId,
            model.SelectedPermissionIds,
            HttpContext.RequestAborted);

        TempData["StatusMessage"] = result.Succeeded
            ? "Đã lưu phân quyền vai trò."
            : result.ErrorMessage ?? "Không thể lưu phân quyền vai trò.";
        TempData["StatusType"] = result.Succeeded ? "success" : "error";

        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    private async Task<VaiTroManagementViewModel> BuildPageModelAsync(
        VaiTroListQuery query,
        CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _vaiTroService.GetPagedAsync(
            query.Keyword,
            query.StatusFilter,
            query.Page,
            DefaultPageSize,
            cancellationToken);

        var model = new VaiTroManagementViewModel
        {
            Filter = new VaiTroFilterState
            {
                Keyword = query.Keyword,
                StatusFilter = query.StatusFilter,
                Page = currentPage,
                PageSize = pageSize
            },
            Form = new VaiTroFormModel
            {
                TrangThaiSuDung = true,
                Keyword = query.Keyword,
                StatusFilter = query.StatusFilter,
                Page = currentPage
            },
            Items = items,
            TotalCount = totalCount,
            TotalPages = totalPages,
            CurrentPage = currentPage,
            StatusMessage = TempData["StatusMessage"]?.ToString(),
            StatusType = TempData["StatusType"]?.ToString() ?? "info"
        };

        if (query.ShowCreatePopup)
        {
            model.PopupMode = VaiTroPopupMode.Create;
            return model;
        }

        if (query.EditId.HasValue)
        {
            var item = await _vaiTroService.GetByIdAsync(query.EditId.Value, cancellationToken);
            if (item is null)
            {
                return model;
            }

            model.PopupMode = VaiTroPopupMode.Edit;
            model.Form = new VaiTroFormModel
            {
                Id = item.Id,
                TenVaiTro = item.TenVaiTro,
                MieuTa = item.MieuTa,
                TrangThaiSuDung = item.TrangThaiSuDung,
                Keyword = query.Keyword,
                StatusFilter = query.StatusFilter,
                Page = currentPage
            };
            return model;
        }

        if (query.PermissionRoleId.HasValue)
        {
            var matrix = await _vaiTroService.GetPermissionMatrixAsync(query.PermissionRoleId.Value, cancellationToken);
            if (matrix is null)
            {
                return model;
            }

            model.PopupMode = VaiTroPopupMode.Permissions;
            model.PermissionMatrix = matrix;
        }

        return model;
    }

    private async Task<VaiTroManagementViewModel> BuildPageModelForPostbackAsync(
        VaiTroFormModel form,
        VaiTroPopupMode popupMode,
        CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _vaiTroService.GetPagedAsync(
            form.Keyword,
            form.StatusFilter,
            form.Page,
            DefaultPageSize,
            cancellationToken);

        form.Page = currentPage;
        return new VaiTroManagementViewModel
        {
            Filter = new VaiTroFilterState
            {
                Keyword = form.Keyword,
                StatusFilter = form.StatusFilter,
                Page = currentPage,
                PageSize = pageSize
            },
            Form = form,
            Items = items,
            PopupMode = popupMode,
            TotalCount = totalCount,
            TotalPages = totalPages,
            CurrentPage = currentPage,
            StatusMessage = TempData["StatusMessage"]?.ToString(),
            StatusType = TempData["StatusType"]?.ToString() ?? "info"
        };
    }

    private object BuildRouteValues(string? keyword, bool? statusFilter, int page)
    {
        return new
        {
            keyword,
            statusFilter,
            page = Math.Max(page, 1)
        };
    }
}
