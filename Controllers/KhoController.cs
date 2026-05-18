using System.Security.Claims;
using System.Text;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class KhoController(
    IKhoService khoService,
    ISimpleExcelService simpleExcelService) : Controller
{
    private const int DefaultPageSize = 10;
    private readonly IKhoService _khoService = khoService;
    private readonly ISimpleExcelService _simpleExcelService = simpleExcelService;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] KhoListQuery query)
    {
        var model = await BuildPageModelAsync(query, HttpContext.RequestAborted);

        if (query.EditId.HasValue && model.PopupMode == KhoPopupMode.None)
        {
            TempData["StatusMessage"] = "Không tìm thấy kho cần chỉnh sửa.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(query.Keyword, query.StatusFilter, model.CurrentPage));
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] KhoFormModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageModelForPostbackAsync(model, KhoPopupMode.Create, HttpContext.RequestAborted));
        }

        var result = await _khoService.CreateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể thêm mới kho.");
            return View("Index", await BuildPageModelForPostbackAsync(model, KhoPopupMode.Create, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Lưu kho thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, 1));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([Bind(Prefix = "Form")] KhoFormModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageModelForPostbackAsync(model, KhoPopupMode.Edit, HttpContext.RequestAborted));
        }

        var result = await _khoService.UpdateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể cập nhật kho.");
            return View("Index", await BuildPageModelForPostbackAsync(model, KhoPopupMode.Edit, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Cập nhật kho thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(KhoDeleteModel model)
    {
        var result = await _khoService.DeleteAsync(model.Id, HttpContext.RequestAborted);
        TempData["StatusMessage"] = result.Succeeded
            ? "Đã xóa kho."
            : result.ErrorMessage ?? "Không thể xóa kho.";
        TempData["StatusType"] = result.Succeeded ? "success" : "error";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    [HttpGet]
    public IActionResult ExportTemplate()
    {
        var bytes = _simpleExcelService.BuildKhoTemplate();
        var fileName = $"kho-template-{DateTime.Now:yyyyMMddHHmmss}.xlsx";

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
            var rows = _simpleExcelService.ReadKhoTemplate(stream);
            if (rows.Count == 0)
            {
                TempData["StatusMessage"] = "File import không có dữ liệu hợp lệ. Hãy dùng đúng template gồm cột Tên kho và Mã kho.";
                TempData["StatusType"] = "error";
                return RedirectToAction(nameof(Index), BuildRouteValues(keyword, statusFilter, page));
            }

            var result = await _khoService.ImportAsync(rows, GetCurrentAuditUser(), HttpContext.RequestAborted);
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

    private async Task<KhoManagementViewModel> BuildPageModelAsync(KhoListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _khoService.GetPagedAsync(
            query.Keyword,
            query.StatusFilter,
            query.Page,
            DefaultPageSize,
            cancellationToken);

        var model = new KhoManagementViewModel
        {
            Filter = new KhoFilterState
            {
                Keyword = query.Keyword,
                StatusFilter = query.StatusFilter,
                Page = currentPage,
                PageSize = pageSize
            },
            Form = new KhoFormModel
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
            model.PopupMode = KhoPopupMode.Create;
            return model;
        }

        if (!query.EditId.HasValue)
        {
            return model;
        }

        var item = await _khoService.GetByIdAsync(query.EditId.Value, cancellationToken);
        if (item is null)
        {
            return model;
        }

        model.PopupMode = KhoPopupMode.Edit;
        model.Form = new KhoFormModel
        {
            Id = item.Id,
            TenKho = item.TenKho,
            MaKho = item.MaKho,
            TrangThaiSuDung = item.TrangThaiSuDung,
            Keyword = query.Keyword,
            StatusFilter = query.StatusFilter,
            Page = currentPage
        };

        return model;
    }

    private async Task<KhoManagementViewModel> BuildPageModelForPostbackAsync(
        KhoFormModel form,
        KhoPopupMode popupMode,
        CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _khoService.GetPagedAsync(
            form.Keyword,
            form.StatusFilter,
            form.Page,
            DefaultPageSize,
            cancellationToken);

        form.Page = currentPage;

        return new KhoManagementViewModel
        {
            Filter = new KhoFilterState
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

    private static string BuildImportStatusMessage(KhoImportResult result)
    {
        if (result.ImportedCount <= 0)
        {
            return result.SkippedCount > 0
                ? $"Không import được dữ liệu nào. Có {result.SkippedCount} dòng bị bỏ qua do trống hoặc trùng tên kho."
                : "Không import được dữ liệu nào từ file.";
        }

        var builder = new StringBuilder();
        builder.Append($"Đã import {result.ImportedCount} kho.");

        if (result.SkippedCount > 0)
        {
            builder.Append($" Bỏ qua {result.SkippedCount} dòng do trống hoặc trùng tên kho.");
        }

        return builder.ToString();
    }
}
