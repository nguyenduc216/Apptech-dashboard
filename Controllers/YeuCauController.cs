using System.Security.Claims;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class YeuCauController(
    IYeuCauService yeuCauService,
    IUserAccountService userAccountService,
    IWebHostEnvironment webHostEnvironment) : Controller
{
    private static readonly HashSet<string> AllowedCheckinImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    private const long MaxCheckinImageSizeInBytes = 6 * 1024 * 1024;
    private const int DefaultPageSize = 10;
    private static readonly Regex WorkEmployeeIdKeyPattern = new(
        @"^Form\.CongViecs\[(\d+)\]\.NhanViens\[(\d+)\]\.NhanVienId$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IYeuCauService _yeuCauService = yeuCauService;
    private readonly IUserAccountService _userAccountService = userAccountService;
    private readonly IWebHostEnvironment _webHostEnvironment = webHostEnvironment;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] YeuCauListQuery query)
    {
        var model = await BuildListModelAsync(query, HttpContext.RequestAborted);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create(string? keyword, string? statusFilter, string? workStatusFilter, int page = 1)
    {
        var requestDate = DateTime.Today;
        var checkinDistanceLimitMeters = await _yeuCauService.GetCheckinDistanceLimitMetersAsync(HttpContext.RequestAborted);
        var form = new YeuCauFormModel
        {
            CheckinTheoKhoangCach = HasPositiveDistanceLimit(checkinDistanceLimitMeters),
            Keyword = keyword,
            StatusFilter = statusFilter,
            WorkStatusFilter = workStatusFilter,
            Page = Math.Max(page, 1),
            NgayYeuCau = requestDate,
            TrangThaiYeuCau = YeuCauTrangThaiCatalog.TaoMoi,
            ActiveTab = "thong-tin"
        };

        return View("Detail", await BuildDetailModelAsync(form, null, HttpContext.RequestAborted));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, string? keyword, string? statusFilter, string? workStatusFilter, int page = 1)
    {
        var item = await _yeuCauService.GetByIdAsync(id, HttpContext.RequestAborted);
        if (item is null)
        {
            TempData["StatusMessage"] = "Không tìm thấy yêu cầu cần chỉnh sửa.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(keyword, statusFilter, page, workStatusFilter: workStatusFilter));
        }

        var works = await _yeuCauService.GetAssignedWorksAsync(id, HttpContext.RequestAborted);

        var form = new YeuCauFormModel
        {
            Id = item.Id,
            MaYeuCau = item.MaYeuCau,
            IDKhachHang = item.IDKhachHang,
            NgayYeuCau = item.NgayYeuCau?.Date,
            IDDiaDiem = item.IDDiaDiem,
            GhiChu = item.GhiChu,
            NhanVienThucHienText = item.NhanVienThucHien,
            CheckinTheoKhoangCach = item.CheckinTheoKhoangCach,
            TrangThaiYeuCau = YeuCauTrangThaiCatalog.Normalize(item.TrangThaiYeuCau),
            NgayThucHien = item.NgayThucHien?.Date,
            NgayHetHan = item.NgayHetHan?.Date,
            NgayHoanThanh = item.NgayHoanThanh?.Date,
            NgayHenTiepTheo = item.NgayHenTiepTheo?.Date,
            CongViecs = works.ToList(),
            Keyword = keyword,
            StatusFilter = statusFilter,
            WorkStatusFilter = workStatusFilter,
            Page = Math.Max(page, 1),
            ActiveTab = "thong-tin"
        };

        return View("Detail", await BuildDetailModelAsync(form, item.IDDiaDiem, HttpContext.RequestAborted));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] YeuCauFormModel model)
    {
        NormalizeFormState(model);
        await NormalizeDistanceConstraintAsync(model, HttpContext.RequestAborted);
        NormalizePostedWorkEmployees(model, Request.Form);

        if (!ModelState.IsValid)
        {
            return View("Detail", await BuildDetailModelAsync(model, model.IDDiaDiem, HttpContext.RequestAborted));
        }

        var result = await _yeuCauService.CreateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể thêm mới yêu cầu.");
            return View("Detail", await BuildDetailModelAsync(model, model.IDDiaDiem, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Lưu yêu cầu thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Edit), new
        {
            id = result.Id,
            keyword = model.Keyword,
            statusFilter = model.StatusFilter,
            workStatusFilter = model.WorkStatusFilter,
            page = Math.Max(model.Page, 1)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([Bind(Prefix = "Form")] YeuCauFormModel model)
    {
        NormalizeFormState(model);
        await NormalizeDistanceConstraintAsync(model, HttpContext.RequestAborted);
        NormalizePostedWorkEmployees(model, Request.Form);

        if (!ModelState.IsValid)
        {
            return View("Detail", await BuildDetailModelAsync(model, model.IDDiaDiem, HttpContext.RequestAborted));
        }

        var result = await _yeuCauService.UpdateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể cập nhật yêu cầu.");
            return View("Detail", await BuildDetailModelAsync(model, model.IDDiaDiem, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Cập nhật yêu cầu thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Edit), new
        {
            id = model.Id,
            keyword = model.Keyword,
            statusFilter = model.StatusFilter,
            workStatusFilter = model.WorkStatusFilter,
            page = Math.Max(model.Page, 1)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(YeuCauDeleteModel model)
    {
        var result = await _yeuCauService.DeleteAsync(model.Id, HttpContext.RequestAborted);
        TempData["StatusMessage"] = result.Succeeded
            ? "Đã xóa yêu cầu."
            : result.ErrorMessage ?? "Không thể xóa yêu cầu.";
        TempData["StatusType"] = result.Succeeded ? "success" : "error";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page, model.RequestDateFrom, model.RequestDateTo, model.ExecutionDateFrom, model.ExecutionDateTo, model.AssigneeKeyword, model.WorkStatusFilter));
    }

    [HttpGet]
    public async Task<IActionResult> SearchLocations([FromQuery] string? keyword, CancellationToken cancellationToken)
    {
        var items = await _yeuCauService.SearchLocationsAsync(keyword, 12, cancellationToken);
        return Json(items.Select(item => new
        {
            idDiaDiem = item.IDDiaDiem,
            idKhachHang = item.IDKhachHang,
            tenKhachHang = item.TenKhachHangDisplay,
            diaChi = item.DiaChi,
            nguoiLienHe = item.NguoiLienHe,
            dienThoai = item.DienThoai,
            longAddress = item.LongAddress,
            latAddress = item.LatAddress,
            displayLabel = item.DisplayLabel,
            trangThaiSuDung = item.TrangThaiSuDung
        }));
    }

    [HttpGet]
    public async Task<IActionResult> PreviewCode([FromQuery] DateTime? ngayYeuCau, CancellationToken cancellationToken)
    {
        var code = await _yeuCauService.GenerateNextCodeAsync(ngayYeuCau, cancellationToken);
        return Json(new { code });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateLocationCoordinates([FromForm] YeuCauLocationCoordinateUpdateModel model)
    {
        model.LongAddress ??= ParseInvariantDecimal(Request.Form["LongAddress"].FirstOrDefault());
        model.LatAddress ??= ParseInvariantDecimal(Request.Form["LatAddress"].FirstOrDefault());

        if (model.IDYeuCau <= 0 || model.IDDiaDiem <= 0)
        {
            return BadRequest(new { message = "Khong xac dinh duoc yeu cau hoac dia diem." });
        }

        if (!model.LongAddress.HasValue || !model.LatAddress.HasValue)
        {
            return BadRequest(new { message = "Vui long cap quyen GPS de lay toa do hien tai." });
        }

        var request = await _yeuCauService.GetByIdAsync(model.IDYeuCau, HttpContext.RequestAborted);
        if (request is null || request.IDDiaDiem != model.IDDiaDiem)
        {
            return BadRequest(new { message = "Dia diem khong thuoc yeu cau dang chon." });
        }

        var location = await _yeuCauService.GetLocationByIdAsync(model.IDDiaDiem, HttpContext.RequestAborted);
        if (location is null)
        {
            return BadRequest(new { message = "Khong tim thay dia diem can cap nhat toa do." });
        }

        if (location.LatAddress.HasValue || location.LongAddress.HasValue)
        {
            return BadRequest(new { message = "Dia diem nay da co toa do." });
        }

        var isAdmin = await IsCurrentUserAdminAsync(HttpContext.RequestAborted);
        var currentEmployeeId = await GetCurrentEmployeeIdAsync(HttpContext.RequestAborted);
        if (!isAdmin &&
            (!currentEmployeeId.HasValue ||
             !await _yeuCauService.IsEmployeeAssignedToRequestAsync(model.IDYeuCau, currentEmployeeId.Value, HttpContext.RequestAborted)))
        {
            return ForbidCheckin("Ban khong co quyen cap nhat toa do dia diem nay.");
        }

        var result = await _yeuCauService.UpdateLocationCoordinatesAsync(
            model.IDDiaDiem,
            model.LongAddress.Value,
            model.LatAddress.Value,
            GetCurrentAuditUser(),
            HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            return BadRequest(new { message = result.ErrorMessage ?? "Khong the cap nhat toa do dia diem." });
        }

        return Json(new
        {
            succeeded = true,
            longAddress = model.LongAddress.Value,
            latAddress = model.LatAddress.Value,
            coordinateDisplay = $"{model.LatAddress.Value:0.00000}, {model.LongAddress.Value:0.00000}"
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCheckin([FromForm] YeuCauCheckinCreateModel model, IFormFile? imageFile)
    {
        model.GhiChuNhanVien = string.IsNullOrWhiteSpace(model.GhiChuNhanVien) ? null : model.GhiChuNhanVien.Trim();
        model.LongAddress ??= ParseInvariantDecimal(Request.Form["LongAddress"].FirstOrDefault());
        model.LatAddress ??= ParseInvariantDecimal(Request.Form["LatAddress"].FirstOrDefault());

        var request = await _yeuCauService.GetByIdAsync(model.IDYeuCau, HttpContext.RequestAborted);
        if (request is null)
        {
            return BadRequest(new { message = "Không tìm thấy yêu cầu cần checkin." });
        }

        model.IDKhachHang = request.IDKhachHang;
        model.IDDiaDiem = request.IDDiaDiem;

        var isAdmin = await IsCurrentUserAdminAsync(HttpContext.RequestAborted);
        var currentEmployeeId = await GetCurrentEmployeeIdAsync(HttpContext.RequestAborted);
        if (!isAdmin)
        {
            if (!currentEmployeeId.HasValue ||
                !await _yeuCauService.IsEmployeeAssignedToRequestAsync(model.IDYeuCau, currentEmployeeId.Value, HttpContext.RequestAborted))
            {
                return ForbidCheckin("Bạn không có quyền tham gia công việc này");
            }

            model.IDNhanVien = currentEmployeeId.Value;
        }
        else if (!model.IDNhanVien.HasValue ||
            !await _yeuCauService.IsEmployeeAssignedToRequestAsync(model.IDYeuCau, model.IDNhanVien.Value, HttpContext.RequestAborted))
        {
            return BadRequest(new { message = "Vui lòng chọn nhân viên trong danh sách nhân viên thực hiện yêu cầu." });
        }

        if (!model.IDNhanVien.HasValue || model.IDNhanVien.Value <= 0)
        {
            return BadRequest(new { message = "Vui lòng chọn nhân viên checkin." });
        }

        var now = DateTime.Now;
        var thoiDiem = model.ThoiDiem ?? now;
        if (thoiDiem < now.AddMinutes(-15) || thoiDiem > now.AddMinutes(1))
        {
            return BadRequest(new { message = "Thời điểm checkin chỉ được nằm trong 15 phút trước thời điểm hiện tại." });
        }
        model.ThoiDiem = thoiDiem;

        if (!model.LatAddress.HasValue || !model.LongAddress.HasValue)
        {
            return BadRequest(new { message = "Vui lòng cấp quyền GPS để lấy vị trí checkin." });
        }

        var checkinDistanceLimitMeters = await _yeuCauService.GetCheckinDistanceLimitMetersAsync(HttpContext.RequestAborted);
        if (request.CheckinTheoKhoangCach &&
            checkinDistanceLimitMeters.HasValue &&
            checkinDistanceLimitMeters.Value > 0 &&
            request.LatAddress.HasValue &&
            request.LongAddress.HasValue)
        {
            var distanceMeters = CalculateDistanceMeters(
                Convert.ToDouble(request.LatAddress.Value),
                Convert.ToDouble(request.LongAddress.Value),
                Convert.ToDouble(model.LatAddress.Value),
                Convert.ToDouble(model.LongAddress.Value));
            if (distanceMeters > Convert.ToDouble(checkinDistanceLimitMeters.Value))
            {
                return BadRequest(new { message = $"Khoảng cách checkin không được quá {checkinDistanceLimitMeters.Value:0.##} mét" });
            }
        }

        if (imageFile is null || imageFile.Length == 0)
        {
            return BadRequest(new { message = "Vui lòng chụp ảnh checkin." });
        }

        var imageValidationError = ValidateCheckinImage(imageFile);
        if (imageValidationError is not null)
        {
            return BadRequest(new { message = imageValidationError });
        }

        var uploadResult = await SaveCheckinImageAsync(imageFile, HttpContext.RequestAborted);
        if (!uploadResult.Succeeded)
        {
            return BadRequest(new { message = uploadResult.ErrorMessage ?? "Không thể lưu ảnh checkin." });
        }

        model.ImgPath = uploadResult.RelativeUrl;
        var result = await _yeuCauService.CreateCheckinAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            DeleteLocalCheckinImageIfOwned(uploadResult.AbsolutePath);
            return BadRequest(new { message = result.ErrorMessage ?? "Không thể lưu thông tin checkin." });
        }

        TempData["StatusMessage"] = "Đã lưu checkin.";
        TempData["StatusType"] = "success";
        return Json(new { succeeded = true, id = result.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCheckout([FromForm] YeuCauCheckoutCreateModel model, IFormFile? imageFile)
    {
        model.GhiChuCheckOut = string.IsNullOrWhiteSpace(model.GhiChuCheckOut) ? null : model.GhiChuCheckOut.Trim();
        model.LongAddressCheckOut ??= ParseInvariantDecimal(Request.Form["LongAddressCheckOut"].FirstOrDefault());
        model.LatAddressCheckOut ??= ParseInvariantDecimal(Request.Form["LatAddressCheckOut"].FirstOrDefault());

        var request = await _yeuCauService.GetByIdAsync(model.IDYeuCau, HttpContext.RequestAborted);
        if (request is null)
        {
            return BadRequest(new { message = "Khong tim thay yeu cau can checkout." });
        }

        var isAdmin = await IsCurrentUserAdminAsync(HttpContext.RequestAborted);
        var currentEmployeeId = await GetCurrentEmployeeIdAsync(HttpContext.RequestAborted);
        if (!isAdmin)
        {
            if (!currentEmployeeId.HasValue ||
                !await _yeuCauService.IsEmployeeAssignedToRequestAsync(model.IDYeuCau, currentEmployeeId.Value, HttpContext.RequestAborted))
            {
                return ForbidCheckin("Ban khong co quyen checkout cong viec nay.");
            }

            model.IDNhanVien = currentEmployeeId.Value;
        }
        else if (!model.IDNhanVien.HasValue ||
            !await _yeuCauService.IsEmployeeAssignedToRequestAsync(model.IDYeuCau, model.IDNhanVien.Value, HttpContext.RequestAborted))
        {
            return BadRequest(new { message = "Vui long chon nhan vien trong danh sach nhan vien thuc hien yeu cau." });
        }

        var now = DateTime.Now;
        var thoiDiem = model.ThoiDiemCheckOut ?? now;
        if (thoiDiem < now.AddMinutes(-15) || thoiDiem > now.AddMinutes(1))
        {
            return BadRequest(new { message = "Thoi diem checkout chi duoc nam trong 15 phut truoc thoi diem hien tai." });
        }
        model.ThoiDiemCheckOut = thoiDiem;

        if (!model.LatAddressCheckOut.HasValue || !model.LongAddressCheckOut.HasValue)
        {
            return BadRequest(new { message = "Vui long cap quyen GPS de lay vi tri checkout." });
        }

        if (imageFile is null || imageFile.Length == 0)
        {
            return BadRequest(new { message = "Vui long chup anh checkout." });
        }

        var imageValidationError = ValidateCheckinImage(imageFile);
        if (imageValidationError is not null)
        {
            return BadRequest(new { message = imageValidationError });
        }

        var uploadResult = await SaveCheckinImageAsync(imageFile, HttpContext.RequestAborted);
        if (!uploadResult.Succeeded)
        {
            return BadRequest(new { message = uploadResult.ErrorMessage ?? "Khong the luu anh checkout." });
        }

        model.ImgPathCheckOut = uploadResult.RelativeUrl;
        var result = await _yeuCauService.CreateCheckoutAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            DeleteLocalCheckinImageIfOwned(uploadResult.AbsolutePath);
            return BadRequest(new { message = result.ErrorMessage ?? "Khong the luu thong tin checkout." });
        }

        TempData["StatusMessage"] = "Da luu checkout.";
        TempData["StatusType"] = "success";
        return Json(new { succeeded = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadWorkImage([FromForm] YeuCauCongViecImageUploadModel model, IFormFile? imageFile)
    {
        var imageType = YeuCauCongViecImageTypes.Normalize(model.ImageType);
        if (model.IDCongViec <= 0)
        {
            return BadRequest(new { message = "Khong xac dinh duoc cong viec can luu anh." });
        }

        if (imageFile is null || imageFile.Length == 0)
        {
            return BadRequest(new { message = "Vui long chon anh cong viec." });
        }

        var imageValidationError = ValidateCheckinImage(imageFile);
        if (imageValidationError is not null)
        {
            return BadRequest(new { message = imageValidationError });
        }

        var uploadResult = await SaveWorkImageAsync(imageFile, imageType, HttpContext.RequestAborted);
        if (!uploadResult.Succeeded)
        {
            return BadRequest(new { message = uploadResult.ErrorMessage ?? "Khong the luu anh cong viec." });
        }

        var result = await _yeuCauService.CreateWorkImageAsync(
            model.IDCongViec,
            uploadResult.RelativeUrl!,
            imageType,
            HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            DeleteLocalWorkImageIfOwned(uploadResult.AbsolutePath);
            return BadRequest(new { message = result.ErrorMessage ?? "Khong the luu anh cong viec." });
        }

        return Json(new
        {
            succeeded = true,
            id = result.Id,
            imagePath = uploadResult.RelativeUrl,
            imageType
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCheckin(int id, int yeuCauId)
    {
        if (!await IsCurrentUserAdminAsync(HttpContext.RequestAborted))
        {
            return ForbidCheckin("Bạn không có quyền xóa thông tin checkin.");
        }

        var result = await _yeuCauService.DeleteCheckinAsync(id, yeuCauId, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            return BadRequest(new { message = result.ErrorMessage ?? "Không thể xóa thông tin checkin." });
        }

        DeleteLocalCheckinImageIfOwned(result.ImgPath);
        TempData["StatusMessage"] = "Đã xóa checkin.";
        TempData["StatusType"] = "success";
        return Json(new { succeeded = true });
    }

    private async Task<YeuCauManagementViewModel> BuildListModelAsync(
        YeuCauListQuery query,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = string.IsNullOrWhiteSpace(query.StatusFilter)
            ? null
            : YeuCauTrangThaiCatalog.Normalize(query.StatusFilter);
        var isAdmin = await IsCurrentUserAdminAsync(cancellationToken);
        var currentEmployeeId = await GetCurrentEmployeeIdAsync(cancellationToken);
        var assignedEmployeeId = isAdmin ? null : currentEmployeeId;
        var employeeOptions = await _yeuCauService.GetNhanVienOptionsAsync(cancellationToken);
        var currentEmployeeOption = currentEmployeeId.HasValue
            ? employeeOptions.FirstOrDefault(employee => employee.Id == currentEmployeeId.Value)
            : null;
        var effectiveAssigneeKeyword = isAdmin ? query.AssigneeKeyword : null;
        var normalizedWorkStatus = YeuCauCongViecTrangThaiFilter.Normalize(
            query.WorkStatusFilter,
            isAdmin ? YeuCauCongViecTrangThaiFilter.TatCa : YeuCauCongViecTrangThaiFilter.ChuaHoanThanh);

        var (items, totalCount, currentPage, totalPages, pageSize) = await _yeuCauService.GetPagedAsync(
            query.Keyword,
            normalizedStatus,
            query.RequestDateFrom,
            query.RequestDateTo,
            query.ExecutionDateFrom,
            query.ExecutionDateTo,
            effectiveAssigneeKeyword,
            normalizedWorkStatus,
            assignedEmployeeId,
            query.Page,
            DefaultPageSize,
            cancellationToken);

        return new YeuCauManagementViewModel
        {
            Filter = new YeuCauFilterState
            {
                Keyword = query.Keyword,
                StatusFilter = normalizedStatus,
                RequestDateFrom = query.RequestDateFrom,
                RequestDateTo = query.RequestDateTo,
                ExecutionDateFrom = query.ExecutionDateFrom,
                ExecutionDateTo = query.ExecutionDateTo,
                AssigneeKeyword = isAdmin ? query.AssigneeKeyword : currentEmployeeOption?.DisplayText,
                WorkStatusFilter = normalizedWorkStatus,
                Page = currentPage,
                PageSize = pageSize
            },
            Items = items,
            NhanVienOptions = isAdmin
                ? employeeOptions
                : employeeOptions.Where(employee => currentEmployeeId.HasValue && employee.Id == currentEmployeeId.Value).ToList(),
            TotalCount = totalCount,
            TotalPages = totalPages,
            CurrentPage = currentPage,
            StatusMessage = TempData["StatusMessage"]?.ToString(),
            StatusType = TempData["StatusType"]?.ToString() ?? "info",
            CurrentUserIsAdmin = isAdmin,
            CurrentEmployeeId = currentEmployeeId
        };
    }

    private async Task<YeuCauDetailViewModel> BuildDetailModelAsync(
        YeuCauFormModel form,
        int? selectedLocationId,
        CancellationToken cancellationToken)
    {
        NormalizeFormState(form);

        var selectedLocation = selectedLocationId.HasValue && selectedLocationId.Value > 0
            ? await _yeuCauService.GetLocationByIdAsync(selectedLocationId.Value, cancellationToken)
            : null;

        if (selectedLocation is not null && (!form.IDKhachHang.HasValue || form.IDKhachHang.Value <= 0))
        {
            form.IDKhachHang = selectedLocation.IDKhachHang;
        }

        var generatedCode = await _yeuCauService.GenerateNextCodeAsync(form.NgayYeuCau, cancellationToken);
        var checkinDistanceLimitMeters = await _yeuCauService.GetCheckinDistanceLimitMetersAsync(cancellationToken);
        if (!HasPositiveDistanceLimit(checkinDistanceLimitMeters))
        {
            form.CheckinTheoKhoangCach = false;
        }

        return new YeuCauDetailViewModel
        {
            Filter = new YeuCauFilterState
            {
                Keyword = form.Keyword,
                StatusFilter = form.StatusFilter,
                WorkStatusFilter = form.WorkStatusFilter,
                Page = form.Page,
                PageSize = DefaultPageSize
            },
            Form = form,
            NhanVienOptions = await _yeuCauService.GetNhanVienOptionsAsync(cancellationToken),
            WorkOptions = await _yeuCauService.GetWorkOptionsAsync(cancellationToken),
            Checkins = form.Id.HasValue && form.Id.Value > 0
                ? await _yeuCauService.GetCheckinsAsync(form.Id.Value, cancellationToken)
                : [],
            SelectedLocation = selectedLocation,
            GeneratedCode = generatedCode,
            CurrentEmployeeId = await GetCurrentEmployeeIdAsync(cancellationToken),
            CurrentUserIsAdmin = await IsCurrentUserAdminAsync(cancellationToken),
            CheckinDistanceLimitMeters = checkinDistanceLimitMeters,
            StatusMessage = TempData["StatusMessage"]?.ToString(),
            StatusType = TempData["StatusType"]?.ToString() ?? "info"
        };
    }

    private object BuildRouteValues(
        string? keyword,
        string? statusFilter,
        int page,
        DateTime? requestDateFrom = null,
        DateTime? requestDateTo = null,
        DateTime? executionDateFrom = null,
        DateTime? executionDateTo = null,
        string? assigneeKeyword = null,
        string? workStatusFilter = null)
    {
        return new
        {
            keyword,
            statusFilter,
            requestDateFrom = requestDateFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            requestDateTo = requestDateTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            executionDateFrom = executionDateFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            executionDateTo = executionDateTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            assigneeKeyword,
            workStatusFilter,
            page = Math.Max(page, 1)
        };
    }

    private string GetCurrentAuditUser()
    {
        var username = User.FindFirstValue("display_name")
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? User.Identity?.Name
            ?? "system";

        return username.Trim();
    }

    private int? GetCurrentEmployeeId()
    {
        var rawValue = User.FindFirstValue("employee_id");
        return int.TryParse(rawValue, out var employeeId) && employeeId > 0 ? employeeId : null;
    }

    private async Task<int?> GetCurrentEmployeeIdAsync(CancellationToken cancellationToken)
    {
        var employeeId = GetCurrentEmployeeId();
        if (employeeId.HasValue)
        {
            return employeeId;
        }

        var rawAccountId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawAccountId, out var accountId))
        {
            return null;
        }

        var account = await _userAccountService.GetAccountByIdAsync(accountId, cancellationToken);
        return account?.EmployeeId is > 0 ? account.EmployeeId : null;
    }

    private async Task<bool> IsCurrentUserAdminAsync(CancellationToken cancellationToken)
    {
        if (User.IsInRole("Administrator") ||
            IsAdminUsername(User.Identity?.Name) ||
            IsAdminUsername(User.FindFirstValue(ClaimTypes.Name)) ||
            IsAdminText(User.FindFirstValue("role_label")) ||
            IsAdminText(User.FindFirstValue("group_name")))
        {
            return true;
        }

        var rawAccountId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawAccountId, out var accountId))
        {
            return false;
        }

        var account = await _userAccountService.GetAccountByIdAsync(accountId, cancellationToken);
        return account?.IsAdministrator == true ||
            IsAdminUsername(account?.Username) ||
            IsAdminText(account?.GroupName);
    }

    private static bool IsAdminUsername(string? value)
    {
        return NormalizeAdminText(value) == "admin";
    }

    private static bool IsAdminText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return IsAdminTextNormalized(value); /*

        var normalized = NormalizeAdminText(value);
        return normalized.Contains("admin", StringComparison.Ordinal) ||
            normalized.Contains("quantri", StringComparison.Ordinal) ||
            normalized.Contains("quản trị");
    }

    */
    }
    private static bool IsAdminTextNormalized(string value)
    {
        var normalized = NormalizeAdminText(value);
        return normalized.Contains("admin", StringComparison.Ordinal) ||
            normalized.Contains("quantri", StringComparison.Ordinal);
    }

    private static string NormalizeAdminText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark ||
                char.IsWhiteSpace(character) ||
                character is '-' or '_')
            {
                continue;
            }

            builder.Append(character is '\u0111' ? 'd' : character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private IActionResult ForbidCheckin(string message)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Json(new { message });
    }

    private static string? ValidateCheckinImage(IFormFile imageFile)
    {
        var extension = Path.GetExtension(imageFile.FileName);
        if (!AllowedCheckinImageExtensions.Contains(extension))
        {
            return "Ảnh checkin chỉ hỗ trợ JPG, PNG hoặc WEBP.";
        }

        if (imageFile.Length > MaxCheckinImageSizeInBytes)
        {
            return "Dung lượng ảnh checkin tối đa là 6MB.";
        }

        return null;
    }

    private static decimal? ParseInvariantDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double CalculateDistanceMeters(double startLat, double startLng, double endLat, double endLng)
    {
        const double earthRadiusMeters = 6371000;
        static double ToRadians(double value) => value * Math.PI / 180;

        var deltaLat = ToRadians(endLat - startLat);
        var deltaLng = ToRadians(endLng - startLng);
        var startLatRad = ToRadians(startLat);
        var endLatRad = ToRadians(endLat);
        var haversine =
            Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
            Math.Cos(startLatRad) * Math.Cos(endLatRad) * Math.Sin(deltaLng / 2) * Math.Sin(deltaLng / 2);
        return 2 * earthRadiusMeters * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(1 - haversine));
    }

    private async Task<(bool Succeeded, string? RelativeUrl, string? AbsolutePath, string? ErrorMessage)> SaveCheckinImageAsync(
        IFormFile imageFile,
        CancellationToken cancellationToken)
    {
        try
        {
            var extension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
            var uploadsRoot = Path.Combine(_webHostEnvironment.WebRootPath, "uploads");
            var uploadsFolder = Path.Combine(uploadsRoot, "checkin");
            Directory.CreateDirectory(uploadsRoot);
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"checkin-{Guid.NewGuid():N}-{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
            var absolutePath = Path.Combine(uploadsFolder, fileName);

            await using var stream = System.IO.File.Create(absolutePath);
            await imageFile.CopyToAsync(stream, cancellationToken);

            return (true, $"/uploads/checkin/{fileName}", absolutePath, null);
        }
        catch
        {
            return (false, null, null, "Không thể lưu ảnh checkin lên hệ thống.");
        }
    }

    private async Task<(bool Succeeded, string? RelativeUrl, string? AbsolutePath, string? ErrorMessage)> SaveWorkImageAsync(
        IFormFile imageFile,
        string imageType,
        CancellationToken cancellationToken)
    {
        try
        {
            var normalizedType = YeuCauCongViecImageTypes.Normalize(imageType);
            var folderName = normalizedType == YeuCauCongViecImageTypes.CheckOut ? "CheckOut" : "CheckIn";
            var extension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
            var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "Uploads", "Images", "CongViec", folderName);
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{folderName.ToLowerInvariant()}-{Guid.NewGuid():N}-{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
            var absolutePath = Path.Combine(uploadsFolder, fileName);

            await using var stream = System.IO.File.Create(absolutePath);
            await imageFile.CopyToAsync(stream, cancellationToken);

            return (true, $"/Uploads/Images/CongViec/{folderName}/{fileName}", absolutePath, null);
        }
        catch
        {
            return (false, null, null, "Khong the luu anh cong viec len he thong.");
        }
    }

    private void DeleteLocalCheckinImageIfOwned(string? imageUrlOrPath)
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

        var uploadsRoot = Path.GetFullPath(Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "checkin"));
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

    private void DeleteLocalWorkImageIfOwned(string? imageUrlOrPath)
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

        var uploadsRoot = Path.GetFullPath(Path.Combine(_webHostEnvironment.WebRootPath, "Uploads", "Images", "CongViec"));
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

    private static void NormalizeFormState(YeuCauFormModel form)
    {
        form.Page = Math.Max(form.Page, 1);
        form.NgayYeuCau ??= DateTime.Today;
        form.TrangThaiYeuCau = YeuCauTrangThaiCatalog.Normalize(form.TrangThaiYeuCau);
        form.GhiChu = string.IsNullOrWhiteSpace(form.GhiChu) ? null : form.GhiChu.Trim();
        form.MaYeuCau = string.IsNullOrWhiteSpace(form.MaYeuCau) ? null : form.MaYeuCau.Trim();
        form.NhanVienThucHienText = string.IsNullOrWhiteSpace(form.NhanVienThucHienText) ? null : form.NhanVienThucHienText.Trim();
        form.NhanVienLienKet ??= [];
        form.CongViecs ??= [];
        form.StatusFilter = string.IsNullOrWhiteSpace(form.StatusFilter) ? null : YeuCauTrangThaiCatalog.Normalize(form.StatusFilter);
        form.WorkStatusFilter = string.IsNullOrWhiteSpace(form.WorkStatusFilter) ? null : YeuCauCongViecTrangThaiFilter.Normalize(form.WorkStatusFilter);
        form.ActiveTab = string.IsNullOrWhiteSpace(form.ActiveTab) ? "thong-tin" : form.ActiveTab.Trim();
        if (string.Equals(form.ActiveTab, "khach-hang", StringComparison.OrdinalIgnoreCase))
        {
            form.ActiveTab = "thong-tin";
        }
    }

    private async Task NormalizeDistanceConstraintAsync(YeuCauFormModel form, CancellationToken cancellationToken)
    {
        var checkinDistanceLimitMeters = await _yeuCauService.GetCheckinDistanceLimitMetersAsync(cancellationToken);
        if (!HasPositiveDistanceLimit(checkinDistanceLimitMeters))
        {
            form.CheckinTheoKhoangCach = false;
        }
    }

    private static bool HasPositiveDistanceLimit(decimal? value) => value.HasValue && value.Value > 0;

    private static void NormalizePostedWorkEmployees(YeuCauFormModel form, IFormCollection postedForm)
    {
        form.CongViecs ??= [];
        MergePostedWorkEmployeesJson(form, postedForm);

        foreach (var key in postedForm.Keys)
        {
            var match = WorkEmployeeIdKeyPattern.Match(key);
            if (!match.Success ||
                !int.TryParse(match.Groups[1].Value, out var workIndex) ||
                !int.TryParse(match.Groups[2].Value, out var employeeIndex) ||
                workIndex < 0 ||
                employeeIndex < 0)
            {
                continue;
            }

            while (form.CongViecs.Count <= workIndex)
            {
                form.CongViecs.Add(new YeuCauCongViecFormItem());
            }

            var work = form.CongViecs[workIndex];
            work.NhanViens ??= [];

            foreach (var rawEmployeeId in postedForm[key])
            {
                if (!int.TryParse(rawEmployeeId, out var employeeId) || employeeId <= 0)
                {
                    continue;
                }

                if (work.NhanViens.Any(item => item.NhanVienId == employeeId))
                {
                    continue;
                }

                var prefix = $"Form.CongViecs[{workIndex}].NhanViens[{employeeIndex}]";
                work.NhanViens.Add(new YeuCauCongViecNhanVienFormItem
                {
                    NhanVienId = employeeId,
                    HoTen = postedForm[$"{prefix}.HoTen"].FirstOrDefault() ?? string.Empty,
                    ChucVu = postedForm[$"{prefix}.ChucVu"].FirstOrDefault(),
                    Avatar = postedForm[$"{prefix}.Avatar"].FirstOrDefault()
                });
            }
        }
    }

    private static void MergePostedWorkEmployeesJson(YeuCauFormModel form, IFormCollection postedForm)
    {
        var rawJson = postedForm["Form.WorkEmployeesJson"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                var work = ResolvePostedWork(form, item);
                if (work is null)
                {
                    continue;
                }

                if (!item.TryGetProperty("employeeIds", out var employeeIdsElement) ||
                    employeeIdsElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                work.NhanViens ??= [];
                foreach (var employeeIdElement in employeeIdsElement.EnumerateArray())
                {
                    if (!employeeIdElement.TryGetInt32(out var employeeId) || employeeId <= 0)
                    {
                        continue;
                    }

                    if (work.NhanViens.Any(employee => employee.NhanVienId == employeeId))
                    {
                        continue;
                    }

                    work.NhanViens.Add(new YeuCauCongViecNhanVienFormItem
                    {
                        NhanVienId = employeeId
                    });
                }
            }
        }
        catch (JsonException)
        {
            return;
        }
    }

    private static YeuCauCongViecFormItem? ResolvePostedWork(YeuCauFormModel form, JsonElement item)
    {
        if (item.TryGetProperty("yeuCauCongViecId", out var workIdElement) &&
            workIdElement.ValueKind == JsonValueKind.Number &&
            workIdElement.TryGetInt32(out var workId) &&
            workId > 0)
        {
            var existingWork = form.CongViecs.FirstOrDefault(work => work.YeuCauCongViecId == workId);
            if (existingWork is not null)
            {
                return existingWork;
            }
        }

        if (item.TryGetProperty("workIndex", out var workIndexElement) &&
            workIndexElement.ValueKind == JsonValueKind.Number &&
            workIndexElement.TryGetInt32(out var workIndex) &&
            workIndex >= 0)
        {
            while (form.CongViecs.Count <= workIndex)
            {
                form.CongViecs.Add(new YeuCauCongViecFormItem());
            }

            return form.CongViecs[workIndex];
        }

        return null;
    }
}
