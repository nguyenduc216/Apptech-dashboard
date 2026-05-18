using System.Security.Claims;
using System.Text;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class HangHoaController(
    IHangHoaService hangHoaService,
    ISimpleExcelService simpleExcelService,
    IWebHostEnvironment webHostEnvironment) : Controller
{
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    private const int DefaultPageSize = 10;
    private const long MaxImageSizeInBytes = 5 * 1024 * 1024;

    private readonly IHangHoaService _hangHoaService = hangHoaService;
    private readonly ISimpleExcelService _simpleExcelService = simpleExcelService;
    private readonly IWebHostEnvironment _webHostEnvironment = webHostEnvironment;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] HangHoaListQuery query)
    {
        var model = await BuildPageModelAsync(query, HttpContext.RequestAborted);

        if (query.EditId.HasValue && model.PopupMode == HangHoaPopupMode.None)
        {
            TempData["StatusMessage"] = "Không tìm thấy hàng hóa cần chỉnh sửa.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(query.Keyword, query.StatusFilter, model.CurrentPage));
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] HangHoaFormModel model)
    {
        ValidateImage(model.ImageFile);

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageModelForPostbackAsync(model, HangHoaPopupMode.Create, HttpContext.RequestAborted));
        }

        string? uploadedImageUrl = null;
        string? uploadedImagePath = null;

        if (model.ImageFile is { Length: > 0 })
        {
            var uploadResult = await SaveImageAsync(model.ImageFile, HttpContext.RequestAborted);
            if (!uploadResult.Succeeded)
            {
                ModelState.AddModelError("Form.ImageFile", uploadResult.ErrorMessage ?? "Không thể tải ảnh hàng hóa lên lúc này.");
                return View("Index", await BuildPageModelForPostbackAsync(model, HangHoaPopupMode.Create, HttpContext.RequestAborted));
            }

            uploadedImageUrl = uploadResult.RelativeUrl;
            uploadedImagePath = uploadResult.AbsolutePath;
            model.ImageUrl = uploadedImageUrl;
        }

        var result = await _hangHoaService.CreateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            DeleteLocalImageIfOwned(uploadedImagePath);
            model.ImageUrl = null;
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể thêm mới hàng hóa.");
            return View("Index", await BuildPageModelForPostbackAsync(model, HangHoaPopupMode.Create, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Lưu hàng hóa thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, 1));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([Bind(Prefix = "Form")] HangHoaFormModel model)
    {
        var existingItem = model.Id.HasValue
            ? await _hangHoaService.GetByIdAsync(model.Id.Value, HttpContext.RequestAborted)
            : null;

        if (existingItem is not null && string.IsNullOrWhiteSpace(model.ImageUrl))
        {
            model.ImageUrl = existingItem.ImageUrl;
        }

        ValidateImage(model.ImageFile);

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageModelForPostbackAsync(model, HangHoaPopupMode.Edit, HttpContext.RequestAborted));
        }

        string? uploadedImageUrl = null;
        string? uploadedImagePath = null;
        if (model.ImageFile is { Length: > 0 })
        {
            var uploadResult = await SaveImageAsync(model.ImageFile, HttpContext.RequestAborted);
            if (!uploadResult.Succeeded)
            {
                ModelState.AddModelError("Form.ImageFile", uploadResult.ErrorMessage ?? "Không thể tải ảnh hàng hóa lên lúc này.");
                return View("Index", await BuildPageModelForPostbackAsync(model, HangHoaPopupMode.Edit, HttpContext.RequestAborted));
            }

            uploadedImageUrl = uploadResult.RelativeUrl;
            uploadedImagePath = uploadResult.AbsolutePath;
            model.ImageUrl = uploadedImageUrl;
        }

        var result = await _hangHoaService.UpdateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            DeleteLocalImageIfOwned(uploadedImagePath);
            model.ImageUrl = existingItem?.ImageUrl;
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể cập nhật hàng hóa.");
            return View("Index", await BuildPageModelForPostbackAsync(model, HangHoaPopupMode.Edit, HttpContext.RequestAborted));
        }

        if (!string.IsNullOrWhiteSpace(uploadedImagePath) &&
            !string.Equals(existingItem?.ImageUrl, uploadedImageUrl, StringComparison.OrdinalIgnoreCase))
        {
            DeleteLocalImageIfOwned(existingItem?.ImageUrl);
        }

        TempData["StatusMessage"] = "Cập nhật hàng hóa thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(HangHoaDeleteModel model)
    {
        var existingItem = await _hangHoaService.GetByIdAsync(model.Id, HttpContext.RequestAborted);
        var result = await _hangHoaService.DeleteAsync(model.Id, HttpContext.RequestAborted);
        if (result.Succeeded)
        {
            DeleteLocalImageIfOwned(existingItem?.ImageUrl);
        }

        TempData["StatusMessage"] = result.Succeeded
            ? "Đã xóa hàng hóa."
            : result.ErrorMessage ?? "Không thể xóa hàng hóa.";
        TempData["StatusType"] = result.Succeeded ? "success" : "error";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    [HttpGet]
    public IActionResult ExportTemplate()
    {
        var bytes = _simpleExcelService.BuildHangHoaTemplate();
        var fileName = $"hang-hoa-template-{DateTime.Now:yyyyMMddHHmmss}.xlsx";

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
            var rows = _simpleExcelService.ReadHangHoaTemplate(stream);
            if (rows.Count == 0)
            {
                TempData["StatusMessage"] = "File import không có dữ liệu hợp lệ. Hãy dùng đúng template gồm cột Tên hàng hóa, Mã hàng hóa và Đơn vị tính.";
                TempData["StatusType"] = "error";
                return RedirectToAction(nameof(Index), BuildRouteValues(keyword, statusFilter, page));
            }

            var result = await _hangHoaService.ImportAsync(rows, GetCurrentAuditUser(), HttpContext.RequestAborted);
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

    private async Task<HangHoaManagementViewModel> BuildPageModelAsync(HangHoaListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _hangHoaService.GetPagedAsync(
            query.Keyword,
            query.StatusFilter,
            query.Page,
            DefaultPageSize,
            cancellationToken);
        var donViTinhOptions = await _hangHoaService.GetDonViTinhOptionsAsync(cancellationToken);

        var model = new HangHoaManagementViewModel
        {
            Filter = new HangHoaFilterState
            {
                Keyword = query.Keyword,
                StatusFilter = query.StatusFilter,
                Page = currentPage,
                PageSize = pageSize
            },
            Form = new HangHoaFormModel
            {
                TrangThaiSuDung = true,
                Keyword = query.Keyword,
                StatusFilter = query.StatusFilter,
                Page = currentPage
            },
            Items = items,
            DonViTinhOptions = donViTinhOptions,
            TotalCount = totalCount,
            TotalPages = totalPages,
            CurrentPage = currentPage,
            StatusMessage = TempData["StatusMessage"]?.ToString(),
            StatusType = TempData["StatusType"]?.ToString() ?? "info"
        };

        if (query.ShowCreatePopup)
        {
            model.PopupMode = HangHoaPopupMode.Create;
            return model;
        }

        if (!query.EditId.HasValue)
        {
            return model;
        }

        var item = await _hangHoaService.GetByIdAsync(query.EditId.Value, cancellationToken);
        if (item is null)
        {
            return model;
        }

        model.PopupMode = HangHoaPopupMode.Edit;
        model.Form = new HangHoaFormModel
        {
            Id = item.Id,
            TenHangHoa = item.TenHangHoa,
            MaHangHoa = item.MaHangHoa,
            DonViTinhId = item.DonViTinhId,
            ImageUrl = item.ImageUrl,
            TrangThaiSuDung = item.TrangThaiSuDung,
            Keyword = query.Keyword,
            StatusFilter = query.StatusFilter,
            Page = currentPage
        };

        return model;
    }

    private async Task<HangHoaManagementViewModel> BuildPageModelForPostbackAsync(
        HangHoaFormModel form,
        HangHoaPopupMode popupMode,
        CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _hangHoaService.GetPagedAsync(
            form.Keyword,
            form.StatusFilter,
            form.Page,
            DefaultPageSize,
            cancellationToken);
        var donViTinhOptions = await _hangHoaService.GetDonViTinhOptionsAsync(cancellationToken);

        form.Page = currentPage;

        return new HangHoaManagementViewModel
        {
            Filter = new HangHoaFilterState
            {
                Keyword = form.Keyword,
                StatusFilter = form.StatusFilter,
                Page = currentPage,
                PageSize = pageSize
            },
            Form = form,
            Items = items,
            DonViTinhOptions = donViTinhOptions,
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

    private void ValidateImage(IFormFile? imageFile)
    {
        if (imageFile is null || imageFile.Length == 0)
        {
            return;
        }

        var extension = Path.GetExtension(imageFile.FileName);
        if (!AllowedImageExtensions.Contains(extension))
        {
            ModelState.AddModelError("Form.ImageFile", "Ảnh minh họa chỉ hỗ trợ JPG, PNG hoặc WEBP.");
        }

        if (imageFile.Length > MaxImageSizeInBytes)
        {
            ModelState.AddModelError("Form.ImageFile", "Dung lượng ảnh minh họa tối đa là 5MB.");
        }
    }

    private async Task<(bool Succeeded, string? RelativeUrl, string? AbsolutePath, string? ErrorMessage)> SaveImageAsync(
        IFormFile imageFile,
        CancellationToken cancellationToken)
    {
        try
        {
            var extension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
            var uploadsRoot = Path.Combine(_webHostEnvironment.WebRootPath, "uploads");
            var uploadsFolder = Path.Combine(uploadsRoot, "hang-hoa");
            Directory.CreateDirectory(uploadsRoot);
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"hang-hoa-{Guid.NewGuid():N}-{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
            var absolutePath = Path.Combine(uploadsFolder, fileName);

            await using var stream = System.IO.File.Create(absolutePath);
            await imageFile.CopyToAsync(stream, cancellationToken);

            return (true, $"/uploads/hang-hoa/{fileName}", absolutePath, null);
        }
        catch
        {
            return (false, null, null, "Không thể lưu ảnh minh họa lên hệ thống.");
        }
    }

    private void DeleteLocalImageIfOwned(string? imageUrlOrPath)
    {
        if (string.IsNullOrWhiteSpace(imageUrlOrPath))
        {
            return;
        }

        string absolutePath;
        if (Path.IsPathRooted(imageUrlOrPath))
        {
            absolutePath = imageUrlOrPath;
        }
        else
        {
            var relativePath = imageUrlOrPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            absolutePath = Path.Combine(_webHostEnvironment.WebRootPath, relativePath);
        }

        var uploadsRoot = Path.GetFullPath(Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "hang-hoa"));
        var normalizedAbsolutePath = Path.GetFullPath(absolutePath);

        if (!normalizedAbsolutePath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (System.IO.File.Exists(normalizedAbsolutePath))
        {
            System.IO.File.Delete(normalizedAbsolutePath);
        }
    }

    private static string BuildImportStatusMessage(HangHoaImportResult result)
    {
        if (result.ImportedCount <= 0)
        {
            var failureMessage = result.SkippedCount > 0
                ? $"Không import được dữ liệu nào. Có {result.SkippedCount} dòng bị bỏ qua do trống hoặc xung đột dữ liệu."
                : "Không import được dữ liệu nào từ file.";

            if (result.FailedCodes.Count > 0)
            {
                failureMessage += $" Mã hàng hóa lỗi: {string.Join(", ", result.FailedCodes)}.";
            }

            return failureMessage;
        }

        var builder = new StringBuilder();
        builder.Append($"Đã import {result.ImportedCount} hàng hóa.");

        if (result.SkippedCount > 0)
        {
            builder.Append($" Bỏ qua {result.SkippedCount} dòng do trống hoặc xung đột dữ liệu.");
        }

        if (result.FailedCodes.Count > 0)
        {
            builder.Append($" Mã hàng hóa lỗi: {string.Join(", ", result.FailedCodes)}.");
        }

        return builder.ToString();
    }
}
