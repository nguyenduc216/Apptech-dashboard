using System.Security.Claims;
using System.Text.RegularExpressions;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class VatTuController(
    IVatTuService vatTuService,
    IWebHostEnvironment webHostEnvironment,
    IQrCodeBatchService qrCodeBatchService) : Controller
{
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    private const int DefaultPageSize = 1000;
    private const long MaxImageSizeInBytes = 5 * 1024 * 1024;
    private const string InvalidQrCodeMessage = "Mã QR không hợp lệ. Chỉ chấp nhận mã QR do hệ thống sinh theo dạng appTech-XXXXXXXXX.";
    private const string DuplicateQrCodeMessage = "Mã QR đang được sử dụng trên hệ thống. Vui lòng dùng mã khác.";
    private static readonly Regex AppTechQrCodePattern = new(
        "^appTech-[A-Za-z0-9]{9}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IVatTuService _vatTuService = vatTuService;
    private readonly IWebHostEnvironment _webHostEnvironment = webHostEnvironment;
    private readonly IQrCodeBatchService _qrCodeBatchService = qrCodeBatchService;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] VatTuListQuery query)
    {
        var model = await BuildPageModelAsync(query, HttpContext.RequestAborted);

        if (query.EditId.HasValue && model.PopupMode == VatTuPopupMode.None)
        {
            TempData["StatusMessage"] = "Không tìm thấy vật tư cần chỉnh sửa.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(query.Keyword, model.CurrentPage));
        }

        return View(model);
    }

    [HttpGet]
    public IActionResult QrPreview([FromQuery] string? value)
    {
        var qrValue = value?.Trim();
        if (string.IsNullOrWhiteSpace(qrValue))
        {
            return BadRequest();
        }

        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        return Content(_qrCodeBatchService.GenerateSvgMarkup(qrValue), "image/svg+xml; charset=utf-8");
    }

    [HttpGet]
    public async Task<IActionResult> ValidateQrCodeUsage([FromQuery] string? value, [FromQuery] int? excludingId)
    {
        var qrValue = NormalizeQrCode(value);
        if (string.IsNullOrWhiteSpace(qrValue))
        {
            return Json(new { isInUse = false });
        }

        if (!AppTechQrCodePattern.IsMatch(qrValue))
        {
            return Json(new { isInUse = false });
        }

        var isInUse = await _vatTuService.IsQrCodeInUseAsync(qrValue, excludingId, HttpContext.RequestAborted);
        return Json(new { isInUse });
    }

    [HttpGet]
    public async Task<IActionResult> FindByQrCode([FromQuery] string? value)
    {
        var qrValue = NormalizeQrCode(value);
        if (string.IsNullOrWhiteSpace(qrValue) || !AppTechQrCodePattern.IsMatch(qrValue))
        {
            return Json(new { found = false });
        }

        var itemId = await _vatTuService.FindIdByQrCodeAsync(qrValue, HttpContext.RequestAborted);
        if (!itemId.HasValue || itemId.Value <= 0)
        {
            return Json(new { found = false });
        }

        var redirectUrl = Url.Action(nameof(Index), "VatTu", new
        {
            editId = itemId.Value,
            page = 1
        });

        return Json(new
        {
            found = true,
            id = itemId.Value,
            redirectUrl
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] VatTuFormModel model)
    {
        model.QRCode = NormalizeQrCode(model.QRCode);
        ValidateQrCode(model.QRCode);
        await ValidateQrCodeUniquenessAsync(model.QRCode, excludingId: null, HttpContext.RequestAborted);
        ValidateImages(model.NewImageFiles);

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageModelForPostbackAsync(model, VatTuPopupMode.Create, HttpContext.RequestAborted));
        }

        var uploadedImages = await SaveImagesAsync(model.NewImageFiles, HttpContext.RequestAborted);
        if (uploadedImages.ErrorMessage is not null)
        {
            DeleteLocalImagesIfOwned(uploadedImages.AbsolutePaths);
            ModelState.AddModelError("Form.NewImageFiles", uploadedImages.ErrorMessage);
            model.ActiveTab = "hinh-anh";
            return View("Index", await BuildPageModelForPostbackAsync(model, VatTuPopupMode.Create, HttpContext.RequestAborted));
        }

        model.UploadedImageUrls = uploadedImages.RelativeUrls;
        model.ImageUrl = ResolvePrimaryImageUrl(model.PrimaryImageSelection, uploadedImages.RelativeUrls);

        var result = await _vatTuService.CreateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            DeleteLocalImagesIfOwned(uploadedImages.AbsolutePaths);
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể thêm mới vật tư.");
            return View("Index", await BuildPageModelForPostbackAsync(model, VatTuPopupMode.Create, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Lưu vật tư thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, 1));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([Bind(Prefix = "Form")] VatTuFormModel model)
    {
        model.QRCode = NormalizeQrCode(model.QRCode);
        var existingItem = model.Id.HasValue
            ? await _vatTuService.GetByIdAsync(model.Id.Value, HttpContext.RequestAborted)
            : null;

        if (existingItem is not null && model.ExistingImages.Count == 0)
        {
            model.ExistingImages = existingItem.Images.ToList();
        }

        ValidateQrCode(model.QRCode);
        await ValidateQrCodeUniquenessAsync(model.QRCode, model.Id, HttpContext.RequestAborted);
        ValidateImages(model.NewImageFiles);

        if (!ModelState.IsValid)
        {
            model.QRCode ??= existingItem?.QRCode;
            return View("Index", await BuildPageModelForPostbackAsync(model, VatTuPopupMode.Edit, HttpContext.RequestAborted));
        }

        var uploadedImages = await SaveImagesAsync(model.NewImageFiles, HttpContext.RequestAborted);
        if (uploadedImages.ErrorMessage is not null)
        {
            DeleteLocalImagesIfOwned(uploadedImages.AbsolutePaths);
            ModelState.AddModelError("Form.NewImageFiles", uploadedImages.ErrorMessage);
            model.ActiveTab = "hinh-anh";
            model.QRCode ??= existingItem?.QRCode;
            return View("Index", await BuildPageModelForPostbackAsync(model, VatTuPopupMode.Edit, HttpContext.RequestAborted));
        }

        model.UploadedImageUrls = uploadedImages.RelativeUrls;
        model.ImageUrl = ResolvePrimaryImageUrl(model.PrimaryImageSelection, uploadedImages.RelativeUrls);

        var result = await _vatTuService.UpdateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            DeleteLocalImagesIfOwned(uploadedImages.AbsolutePaths);
            model.QRCode ??= existingItem?.QRCode;
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể cập nhật vật tư.");
            return View("Index", await BuildPageModelForPostbackAsync(model, VatTuPopupMode.Edit, HttpContext.RequestAborted));
        }

        var removedImageUrls = existingItem?.Images
            .Where(image => model.RemovedImageUrls.Contains(image.ImageUrl, StringComparer.OrdinalIgnoreCase))
            .Select(image => image.ImageUrl)
            .ToArray() ?? [];
        DeleteLocalImagesIfOwned(removedImageUrls);

        TempData["StatusMessage"] = "Cập nhật vật tư thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.Page));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(VatTuDeleteModel model)
    {
        var existingItem = await _vatTuService.GetByIdAsync(model.Id, HttpContext.RequestAborted);
        var result = await _vatTuService.DeleteAsync(model.Id, HttpContext.RequestAborted);
        if (result.Succeeded)
        {
            DeleteLocalImagesIfOwned(existingItem?.Images.Select(image => image.ImageUrl));
        }

        TempData["StatusMessage"] = result.Succeeded
            ? "Đã xóa vật tư."
            : result.ErrorMessage ?? "Không thể xóa vật tư.";
        TempData["StatusType"] = result.Succeeded ? "success" : "error";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.Page));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Copy(VatTuCopyModel model)
    {
        var selectedIds = ParseSelectedIds(model.SelectedIds);
        if (selectedIds.Count == 0)
        {
            TempData["StatusMessage"] = "Vui lòng chọn ít nhất một vật tư để copy.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.Page));
        }

        if (model.CopyQuantity <= 0)
        {
            TempData["StatusMessage"] = "Số lượng copy phải lớn hơn 0.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.Page));
        }

        var result = await _vatTuService.CopyAsync(
            selectedIds,
            model.CopyQuantity,
            GetCurrentAuditUser(),
            HttpContext.RequestAborted);

        TempData["StatusMessage"] = result.Succeeded
            ? BuildCopyStatusMessage(result.SourceCount, result.CreatedCount)
            : result.ErrorMessage ?? "Không thể copy vật tư.";
        TempData["StatusType"] = result.Succeeded ? "success" : "error";

        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.Page));
    }

    private async Task<VatTuManagementViewModel> BuildPageModelAsync(VatTuListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _vatTuService.GetPagedAsync(
            query.Keyword,
            query.Page,
            DefaultPageSize,
            cancellationToken);

        var model = new VatTuManagementViewModel
        {
            Filter = new VatTuFilterState
            {
                Keyword = query.Keyword,
                Page = currentPage,
                PageSize = pageSize
            },
            Form = new VatTuFormModel
            {
                TrangThaiSuDung = true,
                Keyword = query.Keyword,
                Page = currentPage
            },
            Items = items,
            TotalCount = totalCount,
            TotalPages = totalPages,
            CurrentPage = currentPage,
            StatusMessage = TempData["StatusMessage"]?.ToString(),
            StatusType = TempData["StatusType"]?.ToString() ?? "info"
        };

        await PopulateLookupsAsync(model, cancellationToken);

        if (query.ShowCreatePopup)
        {
            model.PopupMode = VatTuPopupMode.Create;
            return model;
        }

        if (!query.EditId.HasValue)
        {
            return model;
        }

        var item = await _vatTuService.GetByIdAsync(query.EditId.Value, cancellationToken);
        if (item is null)
        {
            return model;
        }

        model.PopupMode = VatTuPopupMode.Edit;
        model.Form = new VatTuFormModel
        {
            Id = item.Id,
            TrangThaiSuDung = item.TrangThaiSuDung,
            KhoId = item.KhoId,
            HangHoaId = item.HangHoaId,
            DonViTinhId = item.DonViTinhId,
            DonViNhapId = item.DonViNhapId,
            TenDonViNhap = item.TenDonViNhap,
            TenVietTatDonViNhap = item.TenVietTatDonViNhap,
            TenChiTiet = item.TenChiTiet,
            SoLuongTon = item.SoLuongTon,
            DonGiaBanLe = item.DonGiaBanLe,
            MaSoLo = item.MaSoLo,
            ViTriLuuKho = item.ViTriLuuKho,
            GhiChu = item.GhiChu,
            QRCode = item.QRCode,
            ImageUrl = item.ImageUrl,
            PhieuNhapChiTietId = item.PhieuNhapChiTietId,
            PhieuNhapId = item.PhieuNhapId,
            MaPhieuNhap = item.MaPhieuNhap,
            PhieuXuatId = item.PhieuXuatId,
            MaPhieuXuat = item.MaPhieuXuat,
            ExistingImages = item.Images.ToList(),
            PrimaryImageSelection = !string.IsNullOrWhiteSpace(item.ImageUrl) ? $"existing:{item.ImageUrl}" : null,
            Keyword = query.Keyword,
            Page = currentPage
        };

        return model;
    }

    private async Task<VatTuManagementViewModel> BuildPageModelForPostbackAsync(
        VatTuFormModel form,
        VatTuPopupMode popupMode,
        CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _vatTuService.GetPagedAsync(
            form.Keyword,
            form.Page,
            DefaultPageSize,
            cancellationToken);

        form.Page = currentPage;
        if (string.IsNullOrWhiteSpace(form.ActiveTab))
        {
            form.ActiveTab = "thong-tin";
        }

        var model = new VatTuManagementViewModel
        {
            Filter = new VatTuFilterState
            {
                Keyword = form.Keyword,
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

        await PopulateLookupsAsync(model, cancellationToken);
        return model;
    }

    private async Task PopulateLookupsAsync(VatTuManagementViewModel model, CancellationToken cancellationToken)
    {
        var (khoOptions, hangHoaOptions, donViTinhOptions) = await _vatTuService.GetLookupDataAsync(cancellationToken);
        model.KhoOptions = khoOptions;
        model.HangHoaOptions = hangHoaOptions;
        model.DonViTinhOptions = donViTinhOptions;
    }

    private object BuildRouteValues(string? keyword, int page)
    {
        return new
        {
            keyword,
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

    private static IReadOnlyList<int> ParseSelectedIds(string? selectedIds)
    {
        if (string.IsNullOrWhiteSpace(selectedIds))
        {
            return [];
        }

        return selectedIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var parsedId) ? parsedId : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
    }

    private static string BuildCopyStatusMessage(int sourceCount, int createdCount)
    {
        return $"Đã copy {sourceCount} vật tư, tạo {createdCount} vật tư mới. Bản sao không bao gồm mã QR và hình ảnh.";
    }

    private void ValidateImages(IEnumerable<IFormFile>? imageFiles)
    {
        var hasError = false;
        foreach (var imageFile in imageFiles ?? [])
        {
            if (imageFile is null || imageFile.Length == 0)
            {
                continue;
            }

            var extension = Path.GetExtension(imageFile.FileName);
            if (!AllowedImageExtensions.Contains(extension))
            {
                ModelState.AddModelError("Form.NewImageFiles", "Ảnh vật tư chỉ hỗ trợ JPG, PNG hoặc WEBP.");
                hasError = true;
            }

            if (imageFile.Length > MaxImageSizeInBytes)
            {
                ModelState.AddModelError("Form.NewImageFiles", "Mỗi ảnh vật tư chỉ được tối đa 5MB.");
                hasError = true;
            }
        }

        if (hasError)
        {
            ModelState.AddModelError("Form.ActiveTab", "hinh-anh");
        }
    }

    private void ValidateQrCode(string? qrCode)
    {
        if (string.IsNullOrWhiteSpace(qrCode))
        {
            return;
        }

        if (!AppTechQrCodePattern.IsMatch(qrCode))
        {
            ModelState.AddModelError("Form.QRCode", InvalidQrCodeMessage);
        }
    }

    private async Task ValidateQrCodeUniquenessAsync(
        string? qrCode,
        int? excludingId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(qrCode))
        {
            return;
        }

        if (ViewData.ModelState.TryGetValue("Form.QRCode", out var qrState) && qrState.Errors.Count > 0)
        {
            return;
        }

        if (await _vatTuService.IsQrCodeInUseAsync(qrCode, excludingId, cancellationToken))
        {
            ModelState.AddModelError("Form.QRCode", DuplicateQrCodeMessage);
        }
    }

    private static string? NormalizeQrCode(string? qrCode)
    {
        var normalizedValue = qrCode?.Trim();
        return string.IsNullOrWhiteSpace(normalizedValue) ? null : normalizedValue;
    }

    private async Task<(List<string> RelativeUrls, List<string> AbsolutePaths, string? ErrorMessage)> SaveImagesAsync(
        IEnumerable<IFormFile>? imageFiles,
        CancellationToken cancellationToken)
    {
        var relativeUrls = new List<string>();
        var absolutePaths = new List<string>();

        foreach (var imageFile in imageFiles ?? [])
        {
            if (imageFile is null || imageFile.Length == 0)
            {
                continue;
            }

            var uploadResult = await SaveImageAsync(imageFile, cancellationToken);
            if (!uploadResult.Succeeded)
            {
                return (relativeUrls, absolutePaths, uploadResult.ErrorMessage ?? "Không thể lưu ảnh vật tư lên hệ thống.");
            }

            if (!string.IsNullOrWhiteSpace(uploadResult.RelativeUrl))
            {
                relativeUrls.Add(uploadResult.RelativeUrl);
            }

            if (!string.IsNullOrWhiteSpace(uploadResult.AbsolutePath))
            {
                absolutePaths.Add(uploadResult.AbsolutePath);
            }
        }

        return (relativeUrls, absolutePaths, null);
    }

    private async Task<(bool Succeeded, string? RelativeUrl, string? AbsolutePath, string? ErrorMessage)> SaveImageAsync(
        IFormFile imageFile,
        CancellationToken cancellationToken)
    {
        try
        {
            var extension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
            var uploadsRoot = Path.Combine(_webHostEnvironment.WebRootPath, "uploads");
            var uploadsFolder = Path.Combine(uploadsRoot, "vat-tu");
            Directory.CreateDirectory(uploadsRoot);
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"vat-tu-{Guid.NewGuid():N}-{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
            var absolutePath = Path.Combine(uploadsFolder, fileName);

            await using var stream = System.IO.File.Create(absolutePath);
            await imageFile.CopyToAsync(stream, cancellationToken);

            return (true, $"/uploads/vat-tu/{fileName}", absolutePath, null);
        }
        catch
        {
            return (false, null, null, "Không thể lưu ảnh vật tư lên hệ thống.");
        }
    }

    private void DeleteLocalImagesIfOwned(IEnumerable<string?>? imageUrlsOrPaths)
    {
        foreach (var imageUrlOrPath in imageUrlsOrPaths ?? [])
        {
            DeleteLocalImageIfOwned(imageUrlOrPath);
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

        var uploadsRoot = Path.GetFullPath(Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "vat-tu"));
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

    private static string? ResolvePrimaryImageUrl(string? primaryImageSelection, IReadOnlyList<string> uploadedImageUrls)
    {
        if (string.IsNullOrWhiteSpace(primaryImageSelection))
        {
            return null;
        }

        var selection = primaryImageSelection.Trim();
        if (selection.StartsWith("existing:", StringComparison.OrdinalIgnoreCase))
        {
            return selection["existing:".Length..].Trim();
        }

        if (selection.StartsWith("new:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(selection["new:".Length..], out var uploadedIndex) &&
            uploadedIndex >= 0 &&
            uploadedIndex < uploadedImageUrls.Count)
        {
            return uploadedImageUrls[uploadedIndex];
        }

        return null;
    }
}
