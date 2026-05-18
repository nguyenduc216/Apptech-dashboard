using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class NhaCungCapController(INhaCungCapService nhaCungCapService) : Controller
{
    private const int DefaultPageSize = 10;
    private readonly INhaCungCapService _nhaCungCapService = nhaCungCapService;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] NhaCungCapListQuery query)
    {
        var model = await BuildPageModelAsync(query, HttpContext.RequestAborted);

        if (query.EditId.HasValue && model.PopupMode == NhaCungCapPopupMode.None)
        {
            TempData["StatusMessage"] = "Không tìm thấy nhà cung cấp cần chỉnh sửa.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(query.Keyword, query.StatusFilter, model.CurrentPage));
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] NhaCungCapFormModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageModelForPostbackAsync(model, NhaCungCapPopupMode.Create, HttpContext.RequestAborted));
        }

        var result = await _nhaCungCapService.CreateAsync(model, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể thêm mới nhà cung cấp.");
            return View("Index", await BuildPageModelForPostbackAsync(model, NhaCungCapPopupMode.Create, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Lưu nhà cung cấp thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, 1));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([Bind(Prefix = "Form")] NhaCungCapFormModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageModelForPostbackAsync(model, NhaCungCapPopupMode.Edit, HttpContext.RequestAborted));
        }

        var result = await _nhaCungCapService.UpdateAsync(model, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể cập nhật nhà cung cấp.");
            return View("Index", await BuildPageModelForPostbackAsync(model, NhaCungCapPopupMode.Edit, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Cập nhật nhà cung cấp thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(NhaCungCapDeleteModel model)
    {
        var result = await _nhaCungCapService.DeleteAsync(model.Id, HttpContext.RequestAborted);
        TempData["StatusMessage"] = result.Succeeded
            ? "Đã xóa nhà cung cấp."
            : result.ErrorMessage ?? "Không thể xóa nhà cung cấp.";
        TempData["StatusType"] = result.Succeeded ? "success" : "error";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    private async Task<NhaCungCapManagementViewModel> BuildPageModelAsync(NhaCungCapListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _nhaCungCapService.GetPagedAsync(
            query.Keyword,
            query.StatusFilter,
            query.Page,
            DefaultPageSize,
            cancellationToken);

        var model = new NhaCungCapManagementViewModel
        {
            Filter = new NhaCungCapFilterState
            {
                Keyword = query.Keyword,
                StatusFilter = query.StatusFilter,
                Page = currentPage,
                PageSize = pageSize
            },
            Form = new NhaCungCapFormModel
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
            model.PopupMode = NhaCungCapPopupMode.Create;
            return model;
        }

        if (!query.EditId.HasValue)
        {
            return model;
        }

        var item = await _nhaCungCapService.GetByIdAsync(query.EditId.Value, cancellationToken);
        if (item is null)
        {
            return model;
        }

        model.PopupMode = NhaCungCapPopupMode.Edit;
        model.Form = new NhaCungCapFormModel
        {
            Id = item.Id,
            TenNhaCungCap = item.TenNhaCungCap,
            SoDienThoai = item.SoDienThoai,
            Email = item.Email,
            DiaChi = item.DiaChi,
            TrangThaiSuDung = item.TrangThaiSuDung,
            Keyword = query.Keyword,
            StatusFilter = query.StatusFilter,
            Page = currentPage
        };

        return model;
    }

    private async Task<NhaCungCapManagementViewModel> BuildPageModelForPostbackAsync(
        NhaCungCapFormModel form,
        NhaCungCapPopupMode popupMode,
        CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _nhaCungCapService.GetPagedAsync(
            form.Keyword,
            form.StatusFilter,
            form.Page,
            DefaultPageSize,
            cancellationToken);

        form.Page = currentPage;

        return new NhaCungCapManagementViewModel
        {
            Filter = new NhaCungCapFilterState
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
