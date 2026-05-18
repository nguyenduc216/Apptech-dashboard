using System.Security.Claims;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class XuatKhoController(IXuatKhoService xuatKhoService) : Controller
{
    private const int DefaultPageSize = 10;
    private readonly IXuatKhoService _xuatKhoService = xuatKhoService;

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

        if (!ModelState.IsValid)
        {
            model.ActiveTab = HasDetailErrors() ? "vat-tu-xuat" : "thong-tin";
            return View("Index", await BuildPageModelForPostbackAsync(model, XuatKhoPopupMode.Create, HttpContext.RequestAborted));
        }

        var result = await _xuatKhoService.CreateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
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

        ValidateNgayXuatKho(model);
        ValidateDetails(model);

        if (!ModelState.IsValid)
        {
            model.ActiveTab = HasDetailErrors() ? "vat-tu-xuat" : "thong-tin";
            return View("Index", await BuildPageModelForPostbackAsync(model, XuatKhoPopupMode.Edit, HttpContext.RequestAborted));
        }

        var result = await _xuatKhoService.UpdateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể cập nhật phiếu xuất kho.");
            return View("Index", await BuildPageModelForPostbackAsync(model, XuatKhoPopupMode.Edit, HttpContext.RequestAborted));
        }

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
        var result = await _xuatKhoService.DeleteAsync(model.Id, HttpContext.RequestAborted);
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
