using System.Security.Claims;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class XuatKhoController(
    IXuatKhoService xuatKhoService,
    INhapXuatImageService nhapXuatImageService,
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
    private readonly IXuatKhoService _xuatKhoService = xuatKhoService;
    private readonly INhapXuatImageService _nhapXuatImageService = nhapXuatImageService;
    private readonly IWebHostEnvironment _webHostEnvironment = webHostEnvironment;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] XuatKhoListQuery query)
    {
        var model = await BuildPageModelAsync(query, HttpContext.RequestAborted);

        if (query.EditId.HasValue && model.PopupMode == XuatKhoPopupMode.None)
        {
            TempData["StatusMessage"] = "Không tìm thấy phiếu xuất kho cần mở.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(query.Keyword, query.StatusFilter, query.FromDate, query.ToDate, model.CurrentPage));
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Export(int id)
    {
        var item = await _xuatKhoService.GetByIdAsync(id, HttpContext.RequestAborted);
        if (item is null)
        {
            TempData["StatusMessage"] = "Không tìm thấy phiếu xuất kho cần in.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index));
        }

        if (XuatKhoPhieuStatus.Normalize(item.TrangThaiPhieu) != XuatKhoPhieuStatus.Exported)
        {
            TempData["StatusMessage"] = "Phiếu chưa xuất kho nên không thể in.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index));
        }

        var details = await _xuatKhoService.GetDetailsAsync(id, HttpContext.RequestAborted);
        return View(new XuatKhoExportViewModel
        {
            Header = item,
            Details = details
        });
    }

    [HttpGet]
    public async Task<IActionResult> FindVatTuByQrCode([FromQuery] string? value)
    {
        var qrValue = value?.Trim();
        if (string.IsNullOrWhiteSpace(qrValue))
        {
            return Json(new { found = false, errorMessage = "Vui lòng cung cấp mã QR vật tư." });
        }

        var item = await _xuatKhoService.FindVatTuByQrCodeAsync(qrValue, HttpContext.RequestAborted);
        if (item is null)
        {
            return Json(new { found = false, errorMessage = "Không tìm thấy vật tư còn tồn kho cho mã QR này." });
        }

        return Json(new
        {
            found = true,
            item = new
            {
                vatTuId = item.VatTuId,
                hangHoaId = item.HangHoaId,
                tenChiTiet = item.TenChiTiet,
                tenHangHoa = item.TenHangHoa,
                maHangHoa = item.MaHangHoa,
                tenKho = item.TenKho,
                maKho = item.MaKho,
                donViTinh = item.DonViTinh,
                qrCode = item.QRCode,
                soLuongNhap = item.SoLuongNhap,
                ngayNhapKho = item.NgayNhapKho?.ToString("yyyy-MM-dd"),
                soLuongTon = item.SoLuongTon,
                soLuongXuat = item.SoLuongXuat,
                donGiaNhap = item.DonGiaNhap,
                donGiaBanLe = item.DonGiaBanLe,
                donGiaXuat = item.DonGiaXuat,
                tongTienXuat = item.TongTienXuat,
                maSoLo = item.MaSoLo,
                viTriLuuKho = item.ViTriLuuKho,
                imageUrl = item.ImageUrl
            }
        });
    }

    [HttpGet]
    public async Task<IActionResult> SearchVatTu([FromQuery] string? keyword)
    {
        var normalizedKeyword = keyword?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            return Json(new { succeeded = true, totalCount = 0, items = Array.Empty<object>() });
        }

        var items = await _xuatKhoService.SearchVatTuForExportAsync(normalizedKeyword, HttpContext.RequestAborted);
        return Json(new
        {
            succeeded = true,
            totalCount = items.Count,
            items = items.Select(item => new
            {
                vatTuId = item.VatTuId,
                hangHoaId = item.HangHoaId,
                tenChiTiet = item.TenChiTiet,
                tenHangHoa = item.TenHangHoa,
                maHangHoa = item.MaHangHoa,
                tenKho = item.TenKho,
                maKho = item.MaKho,
                donViTinh = item.DonViTinh,
                qrCode = item.QRCode,
                soLuongNhap = item.SoLuongNhap,
                ngayNhapKho = item.NgayNhapKho?.ToString("yyyy-MM-dd"),
                soLuongTon = item.SoLuongTon,
                soLuongXuat = item.SoLuongXuat,
                donGiaNhap = item.DonGiaNhap,
                donGiaBanLe = item.DonGiaBanLe,
                donGiaXuat = item.DonGiaXuat,
                tongTienXuat = item.TongTienXuat,
                maSoLo = item.MaSoLo,
                viTriLuuKho = item.ViTriLuuKho,
                imageUrl = item.ImageUrl
            })
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] XuatKhoFormModel model)
    {
        model.NguoiXuatKho = GetCurrentAuditUser();
        model.TrangThaiPhieu = XuatKhoPhieuStatus.Draft;
        ValidateNgayXuatKho(model);
        ValidateDetails(model);
        ValidateImages(model.NewImageFiles);

        if (!ModelState.IsValid)
        {
            model.ActiveTab = ResolveActiveTab();
            return View("Index", await BuildPageModelForPostbackAsync(model, XuatKhoPopupMode.Create, HttpContext.RequestAborted));
        }

        var uploadedImages = await SaveImagesAsync(model.NewImageFiles, HttpContext.RequestAborted);
        if (uploadedImages.ErrorMessage is not null)
        {
            DeleteLocalImagesIfOwned(uploadedImages.AbsolutePaths);
            ModelState.AddModelError("Form.NewImageFiles", uploadedImages.ErrorMessage);
            model.ActiveTab = "hinh-anh";
            return View("Index", await BuildPageModelForPostbackAsync(model, XuatKhoPopupMode.Create, HttpContext.RequestAborted));
        }

        model.UploadedImagePaths.AddRange(uploadedImages.RelativePaths);

        var result = await _xuatKhoService.CreateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            DeleteLocalImagesIfOwned(uploadedImages.AbsolutePaths);
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể tạo phiếu xuất kho.");
            model.ActiveTab = "vat-tu-xuat";
            return View("Index", await BuildPageModelForPostbackAsync(model, XuatKhoPopupMode.Create, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Tạo phiếu xuất kho thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.FromDate, model.ToDate, 1, result.Id));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([Bind(Prefix = "Form")] XuatKhoFormModel model, string? command)
    {
        if (string.Equals(command, "export", StringComparison.OrdinalIgnoreCase))
        {
            model.TrangThaiPhieu = XuatKhoPhieuStatus.Exported;
        }

        var existingImages = model.Id.HasValue
            ? await _nhapXuatImageService.GetImagesAsync(model.Id.Value, NhapXuatImageLoaiPhieu.Xuat, HttpContext.RequestAborted)
            : [];
        if (model.ExistingImages.Count == 0)
        {
            model.ExistingImages = existingImages.ToList();
        }

        ValidateNgayXuatKho(model);
        ValidateDetails(model);
        ValidateImages(model.NewImageFiles);

        if (!ModelState.IsValid)
        {
            model.ActiveTab = ResolveActiveTab();
            return View("Index", await BuildPageModelForPostbackAsync(model, XuatKhoPopupMode.Edit, HttpContext.RequestAborted));
        }

        var uploadedImages = await SaveImagesAsync(model.NewImageFiles, HttpContext.RequestAborted);
        if (uploadedImages.ErrorMessage is not null)
        {
            DeleteLocalImagesIfOwned(uploadedImages.AbsolutePaths);
            ModelState.AddModelError("Form.NewImageFiles", uploadedImages.ErrorMessage);
            model.ActiveTab = "hinh-anh";
            return View("Index", await BuildPageModelForPostbackAsync(model, XuatKhoPopupMode.Edit, HttpContext.RequestAborted));
        }

        model.UploadedImagePaths.AddRange(uploadedImages.RelativePaths);

        var result = await _xuatKhoService.UpdateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            DeleteLocalImagesIfOwned(uploadedImages.AbsolutePaths);
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể cập nhật phiếu xuất kho.");
            return View("Index", await BuildPageModelForPostbackAsync(model, XuatKhoPopupMode.Edit, HttpContext.RequestAborted));
        }

        DeleteLocalImagesIfOwned(existingImages
            .Where(image => model.RemovedImagePaths.Contains(image.ImagePath, StringComparer.OrdinalIgnoreCase))
            .Select(image => image.ImagePath));

        TempData["StatusMessage"] = XuatKhoPhieuStatus.Normalize(model.TrangThaiPhieu) == XuatKhoPhieuStatus.Exported
            ? "Đã xuất kho và cập nhật tồn kho thành công."
            : "Cập nhật phiếu xuất kho thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.FromDate, model.ToDate, model.Page, model.Id));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(XuatKhoDeleteModel model)
    {
        var existingImages = await _nhapXuatImageService.GetImagesAsync(model.Id, NhapXuatImageLoaiPhieu.Xuat, HttpContext.RequestAborted);
        var result = await _xuatKhoService.DeleteAsync(model.Id, HttpContext.RequestAborted);
        if (result.Succeeded)
        {
            DeleteLocalImagesIfOwned(existingImages.Select(image => image.ImagePath));
        }

        TempData["StatusMessage"] = result.Succeeded
            ? "Đã xóa phiếu xuất kho."
            : result.ErrorMessage ?? "Không thể xóa phiếu xuất kho.";
        TempData["StatusType"] = result.Succeeded ? "success" : "error";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.FromDate, model.ToDate, model.Page));
    }

    private async Task<XuatKhoManagementViewModel> BuildPageModelAsync(XuatKhoListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _xuatKhoService.GetPagedAsync(
            query.Keyword,
            query.StatusFilter,
            query.FromDate,
            query.ToDate,
            query.Page,
            DefaultPageSize,
            cancellationToken);

        var currentUser = GetCurrentAuditUser();
        var model = new XuatKhoManagementViewModel
        {
            Filter = new XuatKhoFilterState
            {
                Keyword = query.Keyword,
                StatusFilter = query.StatusFilter,
                FromDate = query.FromDate,
                ToDate = query.ToDate,
                Page = currentPage,
                PageSize = pageSize
            },
            Form = new XuatKhoFormModel
            {
                NgayXuatKho = DateTime.Today,
                MucDichXuat = XuatKhoMucDich.XuatBanHang,
                NguoiXuatKho = currentUser,
                TrangThaiPhieu = XuatKhoPhieuStatus.Draft,
                Keyword = query.Keyword,
                StatusFilter = query.StatusFilter,
                FromDate = query.FromDate,
                ToDate = query.ToDate,
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
            model.PopupMode = XuatKhoPopupMode.Create;
            model.Form.MaPhieu = await _xuatKhoService.GenerateNextMaPhieuAsync(DateTime.Today, cancellationToken);
            return model;
        }

        if (!query.EditId.HasValue)
        {
            return model;
        }

        var item = await _xuatKhoService.GetByIdAsync(query.EditId.Value, cancellationToken);
        if (item is null)
        {
            return model;
        }

        var details = await _xuatKhoService.GetDetailsAsync(query.EditId.Value, cancellationToken);
        var images = await _nhapXuatImageService.GetImagesAsync(query.EditId.Value, NhapXuatImageLoaiPhieu.Xuat, cancellationToken);
        model.PopupMode = XuatKhoPopupMode.Edit;
        model.Form = new XuatKhoFormModel
        {
            Id = item.Id,
            MaPhieu = item.MaPhieu,
            NgayXuatKho = item.NgayXuatKho ?? DateTime.Today,
            NoiDungXuatKho = item.NoiDungXuatKho,
            MucDichXuat = XuatKhoMucDich.Normalize(item.MucDichXuat),
            NguoiXuatKho = item.NguoiXuatKho,
            NguoiNhanHang = item.NguoiNhanHang,
            DiaChiNguoiNhanHang = item.DiaChiNguoiNhanHang,
            TrangThaiPhieu = XuatKhoPhieuStatus.Normalize(item.TrangThaiPhieu),
            Details = details.ToList(),
            ExistingImages = images.ToList(),
            Keyword = query.Keyword,
            StatusFilter = query.StatusFilter,
            FromDate = query.FromDate,
            ToDate = query.ToDate,
            Page = currentPage
        };

        return model;
    }

    private async Task<XuatKhoManagementViewModel> BuildPageModelForPostbackAsync(
        XuatKhoFormModel form,
        XuatKhoPopupMode popupMode,
        CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _xuatKhoService.GetPagedAsync(
            form.Keyword,
            form.StatusFilter,
            form.FromDate,
            form.ToDate,
            form.Page,
            DefaultPageSize,
            cancellationToken);

        form.Page = currentPage;
        form.NguoiXuatKho = string.IsNullOrWhiteSpace(form.NguoiXuatKho)
            ? GetCurrentAuditUser()
            : form.NguoiXuatKho;

        if (string.IsNullOrWhiteSpace(form.MaPhieu) && popupMode == XuatKhoPopupMode.Create)
        {
            form.MaPhieu = await _xuatKhoService.GenerateNextMaPhieuAsync(form.NgayXuatKho ?? DateTime.Today, cancellationToken);
        }

        return new XuatKhoManagementViewModel
        {
            Filter = new XuatKhoFilterState
            {
                Keyword = form.Keyword,
                StatusFilter = form.StatusFilter,
                FromDate = form.FromDate,
                ToDate = form.ToDate,
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

    private void ValidateDetails(XuatKhoFormModel model)
    {
        var validDetails = model.Details
            .Where(detail => detail.VatTuId > 0)
            .ToList();

        if (validDetails.Count == 0)
        {
            ModelState.AddModelError("Form.Details", "Vui lòng scan hoặc thêm ít nhất một vật tư cần xuất.");
            return;
        }

        var duplicatedVatTuId = validDetails
            .GroupBy(detail => detail.VatTuId)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicatedVatTuId.HasValue)
        {
            ModelState.AddModelError("Form.Details", "Danh sách vật tư xuất không được trùng vật tư.");
        }

        for (var index = 0; index < validDetails.Count; index++)
        {
            var detail = validDetails[index];
            if (detail.SoLuongXuat < 1)
            {
                ModelState.AddModelError($"Form.Details[{index}].SoLuongXuat", "Số lượng xuất không được nhỏ hơn 1.");
            }

            if (detail.SoLuongTon > 0 && detail.SoLuongXuat > detail.SoLuongTon)
            {
                ModelState.AddModelError($"Form.Details[{index}].SoLuongXuat", "Số lượng xuất không được vượt quá tồn kho.");
            }
        }
    }

    private void ValidateNgayXuatKho(XuatKhoFormModel model)
    {
        model.NgayXuatKho ??= DateTime.Today;
        if (model.NgayXuatKho.Value.Date > DateTime.Today)
        {
            ModelState.AddModelError("Form.NgayXuatKho", "Ngày xuất kho không được vượt quá ngày hiện tại.");
        }
    }

    private bool HasDetailErrors()
    {
        return ModelState.Keys.Any(key => key.StartsWith("Form.Details", StringComparison.OrdinalIgnoreCase));
    }

    private bool HasImageErrors()
    {
        return ModelState.Keys.Any(key => key.StartsWith("Form.NewImageFiles", StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveActiveTab()
    {
        if (HasDetailErrors())
        {
            return "vat-tu-xuat";
        }

        return HasImageErrors() ? "hinh-anh" : "thong-tin";
    }

    private void ValidateImages(IEnumerable<IFormFile>? imageFiles)
    {
        foreach (var imageFile in imageFiles ?? [])
        {
            if (imageFile is null || imageFile.Length == 0)
            {
                continue;
            }

            var extension = Path.GetExtension(imageFile.FileName);
            if (!AllowedImageExtensions.Contains(extension))
            {
                ModelState.AddModelError("Form.NewImageFiles", "Ảnh chứng từ chỉ hỗ trợ JPG, PNG hoặc WEBP.");
            }

            if (imageFile.Length > MaxImageSizeInBytes)
            {
                ModelState.AddModelError("Form.NewImageFiles", "Mỗi ảnh chứng từ chỉ được tối đa 5MB.");
            }
        }
    }

    private async Task<(List<string> RelativePaths, List<string> AbsolutePaths, string? ErrorMessage)> SaveImagesAsync(
        IEnumerable<IFormFile>? imageFiles,
        CancellationToken cancellationToken)
    {
        var relativePaths = new List<string>();
        var absolutePaths = new List<string>();

        foreach (var imageFile in imageFiles ?? [])
        {
            if (imageFile is null || imageFile.Length == 0)
            {
                continue;
            }

            try
            {
                var extension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
                var uploadsRoot = Path.Combine(_webHostEnvironment.WebRootPath, "uploads");
                var uploadsFolder = Path.Combine(uploadsRoot, "nhap-xuat");
                Directory.CreateDirectory(uploadsRoot);
                Directory.CreateDirectory(uploadsFolder);

                var fileName = $"nhap-xuat-{Guid.NewGuid():N}-{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
                var absolutePath = Path.Combine(uploadsFolder, fileName);
                await using var stream = System.IO.File.Create(absolutePath);
                await imageFile.CopyToAsync(stream, cancellationToken);

                relativePaths.Add($"/uploads/nhap-xuat/{fileName}");
                absolutePaths.Add(absolutePath);
            }
            catch
            {
                return (relativePaths, absolutePaths, "Không thể lưu ảnh chứng từ lên hệ thống.");
            }
        }

        return (relativePaths, absolutePaths, null);
    }

    private void DeleteLocalImagesIfOwned(IEnumerable<string?>? imagePaths)
    {
        foreach (var imagePath in imagePaths ?? [])
        {
            DeleteLocalImageIfOwned(imagePath);
        }
    }

    private void DeleteLocalImageIfOwned(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return;
        }

        string absolutePath;
        if (Path.IsPathRooted(imagePath))
        {
            absolutePath = imagePath;
        }
        else
        {
            var relativePath = imagePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            absolutePath = Path.Combine(_webHostEnvironment.WebRootPath, relativePath);
        }

        var uploadsRoot = Path.GetFullPath(Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "nhap-xuat"));
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

    private object BuildRouteValues(string? keyword, string? statusFilter, DateTime? fromDate, DateTime? toDate, int page, int? editId = null)
    {
        return new
        {
            keyword,
            statusFilter,
            fromDate = fromDate?.ToString("yyyy-MM-dd"),
            toDate = toDate?.ToString("yyyy-MM-dd"),
            page = Math.Max(page, 1),
            editId
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
}
