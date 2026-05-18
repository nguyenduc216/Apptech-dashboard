using System.Security.Claims;
using System.Text;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class CongViecController(
    ICongViecService congViecService,
    ISimpleExcelService simpleExcelService) : Controller
{
    private const int DefaultPageSize = 10;

    private readonly ICongViecService _congViecService = congViecService;
    private readonly ISimpleExcelService _simpleExcelService = simpleExcelService;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] CongViecListQuery query)
    {
        var model = await BuildPageModelAsync(query, HttpContext.RequestAborted);

        if (query.EditId.HasValue && model.PopupMode == CongViecPopupMode.None)
        {
            TempData["StatusMessage"] = "Không tìm thấy công việc cần chỉnh sửa.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(query.Keyword, query.StatusFilter, model.CurrentPage));
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] CongViecFormModel model)
    {
        NormalizeFormState(model);

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageModelForPostbackAsync(model, CongViecPopupMode.Create, HttpContext.RequestAborted));
        }

        var result = await _congViecService.CreateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể thêm mới công việc.");
            return View("Index", await BuildPageModelForPostbackAsync(model, CongViecPopupMode.Create, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Lưu công việc thành công.";
        TempData["StatusType"] = "success";
        TempData["ClearChecklistDraftCache"] = "true";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, 1));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([Bind(Prefix = "Form")] CongViecFormModel model)
    {
        NormalizeFormState(model);

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageModelForPostbackAsync(model, CongViecPopupMode.Edit, HttpContext.RequestAborted));
        }

        var result = await _congViecService.UpdateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể cập nhật công việc.");
            return View("Index", await BuildPageModelForPostbackAsync(model, CongViecPopupMode.Edit, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Cập nhật công việc thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(CongViecDeleteModel model)
    {
        var result = await _congViecService.DeleteAsync(model.Id, HttpContext.RequestAborted);
        TempData["StatusMessage"] = result.Succeeded
            ? "Đã xóa công việc."
            : result.ErrorMessage ?? "Không thể xóa công việc.";
        TempData["StatusType"] = result.Succeeded ? "success" : "error";

        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    [HttpGet]
    public IActionResult ExportTemplate()
    {
        var bytes = _simpleExcelService.BuildCongViecTemplate();
        var fileName = $"cong-viec-template-{DateTime.Now:yyyyMMddHHmmss}.xlsx";

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
            var rows = _simpleExcelService.ReadCongViecTemplate(stream);
            if (rows.Count == 0)
            {
                TempData["StatusMessage"] = "File import không có dữ liệu hợp lệ. Hãy dùng đúng template công việc.";
                TempData["StatusType"] = "error";
                return RedirectToAction(nameof(Index), BuildRouteValues(keyword, statusFilter, page));
            }

            var result = await _congViecService.ImportAsync(rows, GetCurrentAuditUser(), HttpContext.RequestAborted);
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

    private async Task<CongViecManagementViewModel> BuildPageModelAsync(
        CongViecListQuery query,
        CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _congViecService.GetPagedAsync(
            query.Keyword,
            query.StatusFilter,
            query.Page,
            DefaultPageSize,
            cancellationToken);

        var model = new CongViecManagementViewModel
        {
            Filter = new CongViecFilterState
            {
                Keyword = query.Keyword,
                StatusFilter = query.StatusFilter,
                Page = currentPage,
                PageSize = pageSize
            },
            Form = new CongViecFormModel
            {
                TrangThaiSuDung = true,
                Keyword = query.Keyword,
                StatusFilter = query.StatusFilter,
                Page = currentPage,
                ActiveTab = "cong-viec"
            },
            Items = items,
            TotalCount = totalCount,
            TotalPages = totalPages,
            CurrentPage = currentPage,
            StatusMessage = TempData["StatusMessage"]?.ToString(),
            StatusType = TempData["StatusType"]?.ToString() ?? "info",
            ShouldClearChecklistDraftCache = string.Equals(TempData["ClearChecklistDraftCache"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase)
        };

        if (query.ShowCreatePopup)
        {
            model.PopupMode = CongViecPopupMode.Create;
            return model;
        }

        if (!query.EditId.HasValue)
        {
            return model;
        }

        var item = await _congViecService.GetByIdAsync(query.EditId.Value, cancellationToken);
        if (item is null)
        {
            return model;
        }

        model.PopupMode = CongViecPopupMode.Edit;
        model.Form = new CongViecFormModel
        {
            Id = item.Id,
            TenCongViec = item.TenCongViec,
            MieuTa = item.MieuTa,
            DonGia = item.DonGia,
            SoLuongAnhCheckIn = item.SoLuongAnhCheckIn,
            SoLuongAnhCheckOut = item.SoLuongAnhCheckOut,
            DanhSachChecklist = item.DanhSachChecklist.OrderBy(checklist => checklist.ViTri).ToList(),
            TrangThaiSuDung = item.TrangThaiSuDung,
            Keyword = query.Keyword,
            StatusFilter = query.StatusFilter,
            Page = currentPage,
            ActiveTab = "cong-viec"
        };

        return model;
    }

    private async Task<CongViecManagementViewModel> BuildPageModelForPostbackAsync(
        CongViecFormModel form,
        CongViecPopupMode popupMode,
        CancellationToken cancellationToken)
    {
        NormalizeFormState(form);

        var (items, totalCount, currentPage, totalPages, pageSize) = await _congViecService.GetPagedAsync(
            form.Keyword,
            form.StatusFilter,
            form.Page,
            DefaultPageSize,
            cancellationToken);

        form.Page = currentPage;

        return new CongViecManagementViewModel
        {
            Filter = new CongViecFilterState
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

    private static void NormalizeFormState(CongViecFormModel form)
    {
        form.DanhSachChecklist ??= [];
        form.ActiveTab = string.IsNullOrWhiteSpace(form.ActiveTab) ? "cong-viec" : form.ActiveTab.Trim();
    }

    private static string BuildImportStatusMessage(CongViecImportResult result)
    {
        if (result.ImportedCount <= 0)
        {
            return result.SkippedCount > 0
                ? $"Không import được dữ liệu nào. Có {result.SkippedCount} dòng bị bỏ qua do trống hoặc trùng tên công việc."
                : "Không import được dữ liệu nào từ file.";
        }

        var builder = new StringBuilder();
        builder.Append($"Đã import {result.ImportedCount} công việc.");

        if (result.SkippedCount > 0)
        {
            builder.Append($" Bỏ qua {result.SkippedCount} dòng do trống hoặc trùng tên công việc.");
        }

        return builder.ToString();
    }
}
