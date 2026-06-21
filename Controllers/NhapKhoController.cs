using System.Security.Claims;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class NhapKhoController(
    INhapKhoService nhapKhoService,
    INhapXuatImageService nhapXuatImageService,
    IWebHostEnvironment webHostEnvironment,
    ILogger<NhapKhoController> logger) : Controller
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
    private readonly INhapKhoService _nhapKhoService = nhapKhoService;
    private readonly INhapXuatImageService _nhapXuatImageService = nhapXuatImageService;
    private readonly IWebHostEnvironment _webHostEnvironment = webHostEnvironment;
    private readonly ILogger<NhapKhoController> _logger = logger;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] NhapKhoListQuery query)
    {
        var model = await BuildPageModelAsync(query, HttpContext.RequestAborted);
        if (query.EditId.HasValue && model.PopupMode == NhapKhoPopupMode.None)
        {
            TempData["StatusMessage"] = "Không tìm thấy phiếu nhập kho cần mở.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(query.Keyword, query.StatusFilter, model.CurrentPage));
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Export(int id)
    {
        var item = await _nhapKhoService.GetByIdAsync(id, HttpContext.RequestAborted);
        if (item is null)
        {
            TempData["StatusMessage"] = "KhÃ´ng tÃ¬m tháº¥y phiáº¿u nháº­p kho cáº§n xuáº¥t.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index));
        }

        if (NhapKhoPhieuStatus.Normalize(item.TrangThaiPhieu) != NhapKhoPhieuStatus.Imported)
        {
            TempData["StatusMessage"] = "Phiếu chưa nhập kho nên không thể in.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index));
        }

        var details = await _nhapKhoService.GetDetailsAsync(id, HttpContext.RequestAborted);
        return View(new NhapKhoExportViewModel
        {
            Header = item,
            Details = details
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] NhapKhoFormModel model)
    {
        model.NguoiNhapKho = GetCurrentAuditUser();
        model.TrangThaiPhieu = NhapKhoPhieuStatus.Draft;
        ValidateDetails(model);
        ValidateImages(model.NewImageFiles);

        if (!ModelState.IsValid)
        {
            model.ActiveTab = ResolveActiveTab();
            return View("Index", await BuildPageModelForPostbackAsync(model, NhapKhoPopupMode.Create, HttpContext.RequestAborted));
        }

        var uploadedImages = await SaveImagesAsync(model.NewImageFiles, HttpContext.RequestAborted);
        if (uploadedImages.ErrorMessage is not null)
        {
            DeleteLocalImagesIfOwned(uploadedImages.AbsolutePaths);
            ModelState.AddModelError("Form.NewImageFiles", uploadedImages.ErrorMessage);
            model.ActiveTab = "hinh-anh";
            return View("Index", await BuildPageModelForPostbackAsync(model, NhapKhoPopupMode.Create, HttpContext.RequestAborted));
        }

        model.UploadedImagePaths.AddRange(uploadedImages.RelativePaths);

        var result = await _nhapKhoService.CreateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            DeleteLocalImagesIfOwned(uploadedImages.AbsolutePaths);
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể tạo phiếu nhập kho.");
            model.ActiveTab = "hang-hoa-nhap";
            return View("Index", await BuildPageModelForPostbackAsync(model, NhapKhoPopupMode.Create, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Tạo phiếu nhập kho thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, 1, result.Id));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([Bind(Prefix = "Form")] NhapKhoFormModel model, string? command)
    {
        if (string.Equals(command, "import", StringComparison.OrdinalIgnoreCase))
        {
            model.TrangThaiPhieu = NhapKhoPhieuStatus.Imported;
        }

        var existingImages = model.Id.HasValue
            ? await _nhapXuatImageService.GetImagesAsync(model.Id.Value, NhapXuatImageLoaiPhieu.Nhap, HttpContext.RequestAborted)
            : [];
        if (model.ExistingImages.Count == 0)
        {
            model.ExistingImages = existingImages.ToList();
        }

        ValidateDetails(model);
        ValidateImages(model.NewImageFiles);

        if (!ModelState.IsValid)
        {
            model.ActiveTab = ResolveActiveTab();
            return View("Index", await BuildPageModelForPostbackAsync(model, NhapKhoPopupMode.Edit, HttpContext.RequestAborted));
        }

        var uploadedImages = await SaveImagesAsync(model.NewImageFiles, HttpContext.RequestAborted);
        if (uploadedImages.ErrorMessage is not null)
        {
            DeleteLocalImagesIfOwned(uploadedImages.AbsolutePaths);
            ModelState.AddModelError("Form.NewImageFiles", uploadedImages.ErrorMessage);
            model.ActiveTab = "hinh-anh";
            return View("Index", await BuildPageModelForPostbackAsync(model, NhapKhoPopupMode.Edit, HttpContext.RequestAborted));
        }

        model.UploadedImagePaths.AddRange(uploadedImages.RelativePaths);

        var result = await _nhapKhoService.UpdateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            DeleteLocalImagesIfOwned(uploadedImages.AbsolutePaths);
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể cập nhật phiếu nhập kho.");
            return View("Index", await BuildPageModelForPostbackAsync(model, NhapKhoPopupMode.Edit, HttpContext.RequestAborted));
        }

        DeleteLocalImagesIfOwned(existingImages
            .Where(image => model.RemovedImagePaths.Contains(image.ImagePath, StringComparer.OrdinalIgnoreCase))
            .Select(image => image.ImagePath));

        TempData["StatusMessage"] = NhapKhoPhieuStatus.Normalize(model.TrangThaiPhieu) == NhapKhoPhieuStatus.Imported
            ? "Đã nhập kho thành công."
            : "Cập nhật phiếu nhập kho thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page, model.Id));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateImages([Bind(Prefix = "Form")] NhapKhoFormModel model)
    {
        if (model.Id is null or <= 0)
        {
            TempData["StatusMessage"] = "Không xác định được phiếu nhập kho cần cập nhật hình ảnh.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
        }

        var existingImages = await _nhapXuatImageService.GetImagesAsync(model.Id.Value, NhapXuatImageLoaiPhieu.Nhap, HttpContext.RequestAborted);
        model.ExistingImages = existingImages.ToList();
        ValidateImages(model.NewImageFiles);

        if (!ModelState.IsValid)
        {
            model.ActiveTab = "hinh-anh";
            return View("Index", await BuildPageModelForPostbackAsync(model, NhapKhoPopupMode.Edit, HttpContext.RequestAborted));
        }

        var uploadedImages = await SaveImagesAsync(model.NewImageFiles, HttpContext.RequestAborted);
        if (uploadedImages.ErrorMessage is not null)
        {
            DeleteLocalImagesIfOwned(uploadedImages.AbsolutePaths);
            ModelState.AddModelError("Form.NewImageFiles", uploadedImages.ErrorMessage);
            model.ActiveTab = "hinh-anh";
            return View("Index", await BuildPageModelForPostbackAsync(model, NhapKhoPopupMode.Edit, HttpContext.RequestAborted));
        }

        var result = await _nhapKhoService.UpdateImagesAsync(
            model.Id.Value,
            model.RemovedImagePaths,
            uploadedImages.RelativePaths,
            HttpContext.RequestAborted);

        if (!result.Succeeded)
        {
            DeleteLocalImagesIfOwned(uploadedImages.AbsolutePaths);
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể cập nhật hình ảnh phiếu nhập kho.");
            model.ActiveTab = "hinh-anh";
            return View("Index", await BuildPageModelForPostbackAsync(model, NhapKhoPopupMode.Edit, HttpContext.RequestAborted));
        }

        DeleteLocalImagesIfOwned(existingImages
            .Where(image => model.RemovedImagePaths.Contains(image.ImagePath, StringComparer.OrdinalIgnoreCase))
            .Select(image => image.ImagePath));

        TempData["StatusMessage"] = "Cập nhật hình ảnh phiếu nhập kho thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page, model.Id));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadImage([FromForm] IFormFile? file, [FromForm] int? phieuId)
    {
        var validationError = ValidateImageFile(file);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            _logger.LogWarning("NhapKho image upload rejected. PhieuId={PhieuId}. Error={Error}", phieuId, validationError);
            return BadRequest(new { succeeded = false, message = validationError });
        }

        if (phieuId is > 0)
        {
            var existingPhieu = await _nhapKhoService.GetByIdAsync(phieuId.Value, HttpContext.RequestAborted);
            if (existingPhieu is null)
            {
                _logger.LogWarning("NhapKho image upload rejected because phieu was not found. PhieuId={PhieuId}", phieuId.Value);
                return NotFound(new { succeeded = false, message = "Không tìm thấy phiếu nhập kho cần lưu hình ảnh." });
            }
        }

        var uploadedImages = await SaveImagesAsync([file!], HttpContext.RequestAborted);
        if (uploadedImages.ErrorMessage is not null || uploadedImages.RelativePaths.Count == 0)
        {
            DeleteLocalImagesIfOwned(uploadedImages.AbsolutePaths);
            _logger.LogError("NhapKho image upload failed while saving file. PhieuId={PhieuId}. Error={Error}", phieuId, uploadedImages.ErrorMessage);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                succeeded = false,
                message = uploadedImages.ErrorMessage ?? "Không thể lưu ảnh chứng từ lên hệ thống."
            });
        }

        var imagePath = uploadedImages.RelativePaths[0];
        if (phieuId is > 0)
        {
            var result = await _nhapXuatImageService.AddImageAsync(
                phieuId.Value,
                NhapXuatImageLoaiPhieu.Nhap,
                imagePath,
                HttpContext.RequestAborted);

            if (!result.Succeeded || result.Image is null)
            {
                DeleteLocalImagesIfOwned(uploadedImages.AbsolutePaths);
                _logger.LogError("NhapKho image metadata save failed. PhieuId={PhieuId}. ImagePath={ImagePath}. Error={Error}", phieuId.Value, imagePath, result.ErrorMessage);
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    succeeded = false,
                    message = result.ErrorMessage ?? "Không thể lưu thông tin hình ảnh."
                });
            }

            _logger.LogInformation("NhapKho image uploaded. PhieuId={PhieuId}. ImagePath={ImagePath}", phieuId.Value, imagePath);
            return Json(new { succeeded = true, cached = false, image = result.Image });
        }

        _logger.LogInformation("NhapKho cached image uploaded before phieu creation. ImagePath={ImagePath}", imagePath);
        return Json(new
        {
            succeeded = true,
            cached = true,
            image = new NhapXuatImageItem
            {
                Id = 0,
                PhieuId = 0,
                LoaiPhieu = NhapXuatImageLoaiPhieu.Nhap,
                ImagePath = imagePath
            }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteImage([FromForm] string? imagePath, [FromForm] int? phieuId)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return BadRequest(new { succeeded = false, message = "Không xác định được hình ảnh cần xóa." });
        }

        if (phieuId is > 0)
        {
            var result = await _nhapXuatImageService.DeleteImageAsync(
                phieuId.Value,
                NhapXuatImageLoaiPhieu.Nhap,
                imagePath,
                HttpContext.RequestAborted);

            if (!result.Succeeded)
            {
                _logger.LogError("NhapKho image metadata delete failed. PhieuId={PhieuId}. ImagePath={ImagePath}. Error={Error}", phieuId.Value, imagePath, result.ErrorMessage);
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    succeeded = false,
                    message = result.ErrorMessage ?? "Không thể xóa hình ảnh."
                });
            }
        }

        DeleteLocalImageIfOwned(imagePath);
        _logger.LogInformation("NhapKho image deleted. PhieuId={PhieuId}. ImagePath={ImagePath}", phieuId, imagePath);
        return Json(new { succeeded = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(NhapKhoDeleteModel model)
    {
        var existingImages = await _nhapXuatImageService.GetImagesAsync(model.Id, NhapXuatImageLoaiPhieu.Nhap, HttpContext.RequestAborted);
        var result = await _nhapKhoService.DeleteAsync(model.Id, HttpContext.RequestAborted);
        if (result.Succeeded)
        {
            DeleteLocalImagesIfOwned(existingImages.Select(image => image.ImagePath));
        }

        TempData["StatusMessage"] = result.Succeeded
            ? "Đã xóa phiếu nhập kho."
            : result.ErrorMessage ?? "Không thể xóa phiếu nhập kho.";
        TempData["StatusType"] = result.Succeeded ? "success" : "error";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    private async Task<NhapKhoManagementViewModel> BuildPageModelAsync(NhapKhoListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _nhapKhoService.GetPagedAsync(
            query.Keyword,
            query.StatusFilter,
            query.Page,
            DefaultPageSize,
            cancellationToken);

        var lookup = await _nhapKhoService.GetLookupDataAsync(cancellationToken);
        var model = new NhapKhoManagementViewModel
        {
            Filter = new NhapKhoFilterState
            {
                Keyword = query.Keyword,
                StatusFilter = query.StatusFilter,
                Page = currentPage,
                PageSize = pageSize
            },
            Form = new NhapKhoFormModel
            {
                NgayNhapKho = DateTime.Today,
                NguoiNhapKho = GetCurrentAuditUser(),
                TrangThaiPhieu = NhapKhoPhieuStatus.Draft,
                Keyword = query.Keyword,
                StatusFilter = query.StatusFilter,
                Page = currentPage
            },
            Items = items,
            KhoOptions = lookup.KhoOptions,
            HangHoaOptions = lookup.HangHoaOptions,
            PhanLoaiHangHoaOptions = lookup.PhanLoaiHangHoaOptions,
            DonViTinhOptions = lookup.DonViTinhOptions,
            NhaCungCapOptions = lookup.NhaCungCapOptions,
            TotalCount = totalCount,
            TotalPages = totalPages,
            CurrentPage = currentPage,
            StatusMessage = TempData["StatusMessage"]?.ToString(),
            StatusType = TempData["StatusType"]?.ToString() ?? "info"
        };

        if (query.ShowCreatePopup)
        {
            model.PopupMode = NhapKhoPopupMode.Create;
            model.Form.MaPhieu = await _nhapKhoService.GenerateNextMaPhieuAsync(DateTime.Today, cancellationToken);
            return model;
        }

        if (!query.EditId.HasValue)
        {
            return model;
        }

        var item = await _nhapKhoService.GetByIdAsync(query.EditId.Value, cancellationToken);
        if (item is null)
        {
            return model;
        }

        var details = await _nhapKhoService.GetDetailsAsync(query.EditId.Value, cancellationToken);
        var images = await _nhapXuatImageService.GetImagesAsync(query.EditId.Value, NhapXuatImageLoaiPhieu.Nhap, cancellationToken);
        model.PopupMode = NhapKhoPopupMode.Edit;
        model.Form = new NhapKhoFormModel
        {
            Id = item.Id,
            MaPhieu = item.MaPhieu,
            NgayNhapKho = item.NgayNhapKho ?? DateTime.Today,
            NoiDungNhapKho = item.NoiDungNhapKho,
            NguoiNhapKho = item.NguoiNhapKho,
            KhoId = item.KhoId,
            NhaCungCapId = item.NhaCungCapId,
            TrangThaiPhieu = NhapKhoPhieuStatus.Normalize(item.TrangThaiPhieu),
            Details = details.ToList(),
            ExistingImages = images.ToList(),
            Keyword = query.Keyword,
            StatusFilter = query.StatusFilter,
            Page = currentPage
        };

        return model;
    }

    private async Task<NhapKhoManagementViewModel> BuildPageModelForPostbackAsync(NhapKhoFormModel form, NhapKhoPopupMode popupMode, CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _nhapKhoService.GetPagedAsync(
            form.Keyword,
            form.StatusFilter,
            form.Page,
            DefaultPageSize,
            cancellationToken);
        var lookup = await _nhapKhoService.GetLookupDataAsync(cancellationToken);

        form.Page = currentPage;
        form.NguoiNhapKho = string.IsNullOrWhiteSpace(form.NguoiNhapKho)
            ? GetCurrentAuditUser()
            : form.NguoiNhapKho;

        if (string.IsNullOrWhiteSpace(form.MaPhieu) && popupMode == NhapKhoPopupMode.Create)
        {
            form.MaPhieu = await _nhapKhoService.GenerateNextMaPhieuAsync(form.NgayNhapKho ?? DateTime.Today, cancellationToken);
        }

        return new NhapKhoManagementViewModel
        {
            Filter = new NhapKhoFilterState
            {
                Keyword = form.Keyword,
                StatusFilter = form.StatusFilter,
                Page = currentPage,
                PageSize = pageSize
            },
            Form = form,
            Items = items,
            KhoOptions = lookup.KhoOptions,
            HangHoaOptions = lookup.HangHoaOptions,
            PhanLoaiHangHoaOptions = lookup.PhanLoaiHangHoaOptions,
            DonViTinhOptions = lookup.DonViTinhOptions,
            NhaCungCapOptions = lookup.NhaCungCapOptions,
            PopupMode = popupMode,
            TotalCount = totalCount,
            TotalPages = totalPages,
            CurrentPage = currentPage,
            StatusMessage = TempData["StatusMessage"]?.ToString(),
            StatusType = TempData["StatusType"]?.ToString() ?? "info"
        };
    }

    private void ValidateDetails(NhapKhoFormModel model)
    {
        if (model.Details.Where(detail => detail.HangHoaId > 0).ToList().Count == 0)
        {
            ModelState.AddModelError("Form.Details", "Vui lòng thêm ít nhất một hàng hóa cần nhập.");
            return;
        }

        for (var index = 0; index < model.Details.Count; index++)
        {
            var detail = model.Details[index];
            var loaiHinhNhap = NhapKhoLoaiHinh.Normalize(detail.LoaiHinhNhap);
            if (detail.HangHoaId <= 0)
            {
                ModelState.AddModelError($"Form.Details[{index}].HangHoaId", "Vui lòng chọn hàng hóa nhập.");
            }

            if (detail.PhanLoaiHangHoaId is null or <= 0)
            {
                ModelState.AddModelError($"Form.Details[{index}].PhanLoaiHangHoaId", "Vui lòng chọn phân loại hàng hóa.");
            }

            if (detail.DonViTinhId is null or <= 0)
            {
                ModelState.AddModelError($"Form.Details[{index}].DonViTinhId", "Vui lòng chọn đơn vị tính.");
            }

            if (detail.SoLuongNhap <= 0)
            {
                ModelState.AddModelError($"Form.Details[{index}].SoLuongNhap", "Số lượng nhập phải lớn hơn 0.");
            }

            if (detail.SoLuongQuyDoi <= 0)
            {
                ModelState.AddModelError($"Form.Details[{index}].SoLuongQuyDoi", "So luong quy doi phai lon hon 0.");
            }

            if (detail.DonViNhapId is null or <= 0)
            {
                ModelState.AddModelError($"Form.Details[{index}].DonViNhapId", "Vui long chon don vi nhap.");
            }

            if (detail.DonGiaNhap < 0)
            {
                ModelState.AddModelError($"Form.Details[{index}].DonGiaNhap", "Don gia nhap khong hop le.");
            }

            if (detail.DonGiaBanLe < 0)
            {
                ModelState.AddModelError($"Form.Details[{index}].DonGiaBanLe", "Don gia ban le khong hop le.");
            }

            if (!string.IsNullOrWhiteSpace(detail.SoChungTu) && detail.SoChungTu.Length > 50)
            {
                ModelState.AddModelError($"Form.Details[{index}].SoChungTu", "So chung tu toi da 50 ky tu.");
            }

            if (string.IsNullOrWhiteSpace(loaiHinhNhap))
            {
                ModelState.AddModelError($"Form.Details[{index}].LoaiHinhNhap", "Vui lòng chọn loại hình nhập.");
            }
            else
            {
                detail.LoaiHinhNhap = loaiHinhNhap;
            }

            if (loaiHinhNhap == NhapKhoLoaiHinh.NhapTungVatTu && detail.SoLuongNhap % 1 != 0)
            {
                ModelState.AddModelError($"Form.Details[{index}].SoLuongNhap", "Nhập từng vật tư yêu cầu số lượng nhập là số nguyên.");
            }
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
            return "hang-hoa-nhap";
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

    private static string? ValidateImageFile(IFormFile? imageFile)
    {
        if (imageFile is null || imageFile.Length == 0)
        {
            return "Vui lòng chọn hình ảnh cần upload.";
        }

        var extension = Path.GetExtension(imageFile.FileName);
        if (!AllowedImageExtensions.Contains(extension))
        {
            return "Ảnh chứng từ chỉ hỗ trợ JPG, PNG hoặc WEBP.";
        }

        return imageFile.Length > MaxImageSizeInBytes
            ? "Mỗi ảnh chứng từ chỉ được tối đa 5MB."
            : null;
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save NhapKho image file. WebRootPath={WebRootPath}", _webHostEnvironment.WebRootPath);
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

    private object BuildRouteValues(string? keyword, string? statusFilter, int page, int? editId = null)
    {
        return new
        {
            keyword,
            statusFilter,
            page = Math.Max(page, 1),
            editId
        };
    }

    private string GetCurrentAuditUser()
    {
        return User.FindFirstValue("display_name")
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? User.Identity?.Name
            ?? "system";
    }
}
