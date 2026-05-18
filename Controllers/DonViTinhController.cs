using System.Security.Claims;
using System.Text;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class DonViTinhController(
    IDonViTinhService donViTinhService,
    ISimpleExcelService simpleExcelService) : Controller
{
    private const int DefaultPageSize = 10;
    private readonly IDonViTinhService _donViTinhService = donViTinhService;
    private readonly ISimpleExcelService _simpleExcelService = simpleExcelService;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] DonViTinhListQuery query)
    {
        var model = await BuildPageModelAsync(query, HttpContext.RequestAborted);

        if (query.EditId.HasValue && model.PopupMode == DonViTinhPopupMode.None)
        {
            TempData["StatusMessage"] = "Không tìm thấy đơn vị tính cần chỉnh sửa.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(query.Keyword, query.StatusFilter, model.CurrentPage));
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] DonViTinhFormModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageModelForPostbackAsync(model, DonViTinhPopupMode.Create, HttpContext.RequestAborted));
        }

        var result = await _donViTinhService.CreateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể thêm mới đơn vị tính.");
            return View("Index", await BuildPageModelForPostbackAsync(model, DonViTinhPopupMode.Create, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Lưu đơn vị tính thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, 1));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([Bind(Prefix = "Form")] DonViTinhFormModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageModelForPostbackAsync(model, DonViTinhPopupMode.Edit, HttpContext.RequestAborted));
        }

        var result = await _donViTinhService.UpdateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể cập nhật đơn vị tính.");
            return View("Index", await BuildPageModelForPostbackAsync(model, DonViTinhPopupMode.Edit, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Cập nhật đơn vị tính thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(DonViTinhDeleteModel model)
    {
        var result = await _donViTinhService.DeleteAsync(model.Id, HttpContext.RequestAborted);
        TempData["StatusMessage"] = result.Succeeded
            ? "Đã xóa đơn vị tính."
            : result.ErrorMessage ?? "Không thể xóa đơn vị tính.";
        TempData["StatusType"] = result.Succeeded ? "success" : "error";

        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    [HttpGet]
    public IActionResult ExportTemplate()
    {
        var bytes = _simpleExcelService.BuildDonViTinhTemplate();
        var fileName = $"don-vi-tinh-template-{DateTime.Now:yyyyMMddHHmmss}.xlsx";

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportData(IFormFile? file, string? keyword, bool? statusFilter, int page = 1)
    {
        if (file is null || file.Length == 0)
        {
            TempData["StatusMessage"] = "Vui lòng chọn file Excel để import.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(keyword, statusFilter, page));
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            TempData["StatusMessage"] = "Chỉ hỗ trợ import file Excel định dạng .xlsx.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(keyword, statusFilter, page));
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var rows = _simpleExcelService.ReadDonViTinhTemplate(stream);
            if (rows.Count == 0)
            {
                TempData["StatusMessage"] = "File import không có dữ liệu hợp lệ. Hãy dùng đúng template gồm cột Tên đơn vị và Mã đơn vị.";
                TempData["StatusType"] = "error";
                return RedirectToAction(nameof(Index), BuildRouteValues(keyword, statusFilter, page));
            }

            var result = await _donViTinhService.ImportAsync(rows, GetCurrentAuditUser(), HttpContext.RequestAborted);
            TempData["StatusMessage"] = BuildImportStatusMessage(result);
            TempData["StatusType"] = result.ImportedCount > 0 ? "success" : "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(keyword, statusFilter, page));
        }
        catch
        {
            TempData["StatusMessage"] = "Không thể đọc file Excel. Hãy kiểm tra lại định dạng file template.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(keyword, statusFilter, page));
        }
    }

    private async Task<DonViTinhManagementViewModel> BuildPageModelAsync(
        DonViTinhListQuery query,
        CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _donViTinhService.GetPagedAsync(
            query.Keyword,
            query.StatusFilter,
            query.Page,
            DefaultPageSize,
            cancellationToken);

        var model = new DonViTinhManagementViewModel
        {
            Filter = new DonViTinhFilterState
            {
                Keyword = query.Keyword,
                StatusFilter = query.StatusFilter,
                Page = currentPage,
                PageSize = pageSize
            },
            Form = new DonViTinhFormModel
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
            model.PopupMode = DonViTinhPopupMode.Create;
            return model;
        }

        if (!query.EditId.HasValue)
        {
            return model;
        }

        var item = await _donViTinhService.GetByIdAsync(query.EditId.Value, cancellationToken);
        if (item is null)
        {
            return model;
        }

        model.PopupMode = DonViTinhPopupMode.Edit;
        model.Form = new DonViTinhFormModel
        {
            Id = item.Id,
            TenDonVi = item.TenDonVi,
            TenVietTat = item.TenVietTat,
            TrangThaiSuDung = item.TrangThaiSuDung,
            Keyword = query.Keyword,
            StatusFilter = query.StatusFilter,
            Page = currentPage
        };

        return model;
    }

    private async Task<DonViTinhManagementViewModel> BuildPageModelForPostbackAsync(
        DonViTinhFormModel form,
        DonViTinhPopupMode popupMode,
        CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _donViTinhService.GetPagedAsync(
            form.Keyword,
            form.StatusFilter,
            form.Page,
            DefaultPageSize,
            cancellationToken);

        form.Page = currentPage;

        return new DonViTinhManagementViewModel
        {
            Filter = new DonViTinhFilterState
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

    private string GetCurrentAuditUser()
    {
        var username = User.Identity?.Name
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? User.FindFirstValue("display_name")
            ?? "system";

        return username.Trim();
    }

    private static string BuildImportStatusMessage(DonViTinhImportResult result)
    {
        if (result.ImportedCount <= 0)
        {
            return result.SkippedCount > 0
                ? $"Không import được dữ liệu nào. Có {result.SkippedCount} dòng bị bỏ qua do trống hoặc trùng tên đơn vị."
                : "Không import được dữ liệu nào từ file.";
        }

        var builder = new StringBuilder();
        builder.Append($"Đã import {result.ImportedCount} đơn vị tính.");

        if (result.SkippedCount > 0)
        {
            builder.Append($" Bỏ qua {result.SkippedCount} dòng do trống hoặc trùng tên đơn vị.");
        }

        return builder.ToString();
    }
}
