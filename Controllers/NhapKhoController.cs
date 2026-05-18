using System.Security.Claims;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class NhapKhoController(INhapKhoService nhapKhoService) : Controller
{
    private const int DefaultPageSize = 10;
    private readonly INhapKhoService _nhapKhoService = nhapKhoService;

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

        if (!ModelState.IsValid)
        {
            model.ActiveTab = HasDetailErrors() ? "hang-hoa-nhap" : "thong-tin";
            return View("Index", await BuildPageModelForPostbackAsync(model, NhapKhoPopupMode.Create, HttpContext.RequestAborted));
        }

        var result = await _nhapKhoService.CreateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
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

        ValidateDetails(model);

        if (!ModelState.IsValid)
        {
            model.ActiveTab = HasDetailErrors() ? "hang-hoa-nhap" : "thong-tin";
            return View("Index", await BuildPageModelForPostbackAsync(model, NhapKhoPopupMode.Edit, HttpContext.RequestAborted));
        }

        var result = await _nhapKhoService.UpdateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể cập nhật phiếu nhập kho.");
            return View("Index", await BuildPageModelForPostbackAsync(model, NhapKhoPopupMode.Edit, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = NhapKhoPhieuStatus.Normalize(model.TrangThaiPhieu) == NhapKhoPhieuStatus.Imported
            ? "Đã nhập kho thành công."
            : "Cập nhật phiếu nhập kho thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page, model.Id));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(NhapKhoDeleteModel model)
    {
        var result = await _nhapKhoService.DeleteAsync(model.Id, HttpContext.RequestAborted);
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
