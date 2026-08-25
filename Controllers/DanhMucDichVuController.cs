using System.Security.Claims;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class DanhMucDichVuController(IDanhMucDichVuService danhMucDichVuService) : Controller
{
    private const int DefaultPageSize = 10;
    private readonly IDanhMucDichVuService _danhMucDichVuService = danhMucDichVuService;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] DanhMucDichVuListQuery query)
    {
        var model = await BuildPageModelAsync(query, HttpContext.RequestAborted);
        if (query.EditId.HasValue && model.PopupMode == DanhMucDichVuPopupMode.None)
        {
            TempData["StatusMessage"] = "Không tìm thấy dịch vụ cần chỉnh sửa.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(query.Keyword, query.StatusFilter, model.CurrentPage));
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] DanhMucDichVuFormModel model)
    {
        NormalizeFormState(model);
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageModelForPostbackAsync(model, DanhMucDichVuPopupMode.Create, HttpContext.RequestAborted));
        }

        var result = await _danhMucDichVuService.CreateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể thêm dịch vụ.");
            return View("Index", await BuildPageModelForPostbackAsync(model, DanhMucDichVuPopupMode.Create, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Lưu dịch vụ thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, 1));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([Bind(Prefix = "Form")] DanhMucDichVuFormModel model)
    {
        NormalizeFormState(model);
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageModelForPostbackAsync(model, DanhMucDichVuPopupMode.Edit, HttpContext.RequestAborted));
        }

        var result = await _danhMucDichVuService.UpdateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể cập nhật dịch vụ.");
            return View("Index", await BuildPageModelForPostbackAsync(model, DanhMucDichVuPopupMode.Edit, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Cập nhật dịch vụ thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(DanhMucDichVuDeleteModel model)
    {
        var result = await _danhMucDichVuService.DeleteAsync(model.Id, GetCurrentAuditUser(), HttpContext.RequestAborted);
        TempData["StatusMessage"] = result.Succeeded
            ? result.ErrorMessage ?? "Đã xóa dịch vụ."
            : result.ErrorMessage ?? "Không thể xóa dịch vụ.";
        TempData["StatusType"] = result.Succeeded ? "success" : "error";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    [HttpGet]
    public async Task<IActionResult> GetWorks([FromQuery] int id, CancellationToken cancellationToken)
    {
        var (service, works) = await _danhMucDichVuService.GetWorksAsync(id, cancellationToken);
        if (service is null)
        {
            return NotFound(new { message = "Không tìm thấy dịch vụ." });
        }

        return Json(new
        {
            service = new
            {
                id = service.Id,
                name = service.TenDichVu,
                workCount = service.SoCongViec
            },
            works = works.Select(work => new
            {
                idCongViec = work.IDCongViec,
                id = work.IDCongViec,
                tenCongViec = work.TenCongViec,
                mieuTa = work.MieuTa,
                donGia = work.DonGia,
                thuTu = work.ThuTu,
                soLuongAnhCheckIn = work.SoLuongAnhCheckIn,
                soLuongAnhCheckOut = work.SoLuongAnhCheckOut,
                checklists = work.Checklists.Select(checklist => new
                {
                    checklistId = checklist.ChecklistId,
                    tenChecklist = checklist.TenChecklist,
                    viTri = checklist.ViTri
                })
            })
        });
    }

    private async Task<DanhMucDichVuManagementViewModel> BuildPageModelAsync(
        DanhMucDichVuListQuery query,
        CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _danhMucDichVuService.GetPagedAsync(
            query.Keyword,
            query.StatusFilter,
            query.Page,
            DefaultPageSize,
            cancellationToken);

        var model = new DanhMucDichVuManagementViewModel
        {
            Filter = new DanhMucDichVuFilterState
            {
                Keyword = query.Keyword,
                StatusFilter = query.StatusFilter,
                Page = currentPage,
                PageSize = pageSize
            },
            Form = new DanhMucDichVuFormModel
            {
                TrangThaiSuDung = true,
                Keyword = query.Keyword,
                StatusFilter = query.StatusFilter,
                Page = currentPage
            },
            WorkOptions = await _danhMucDichVuService.GetWorkOptionsAsync(cancellationToken: cancellationToken),
            Items = items,
            TotalCount = totalCount,
            TotalPages = totalPages,
            CurrentPage = currentPage,
            StatusMessage = TempData["StatusMessage"]?.ToString(),
            StatusType = TempData["StatusType"]?.ToString() ?? "info"
        };

        if (query.ShowCreatePopup)
        {
            model.PopupMode = DanhMucDichVuPopupMode.Create;
            return model;
        }

        if (!query.EditId.HasValue)
        {
            return model;
        }

        var item = await _danhMucDichVuService.GetByIdAsync(query.EditId.Value, cancellationToken);
        if (item is null)
        {
            return model;
        }

        model.PopupMode = DanhMucDichVuPopupMode.Edit;
        model.WorkOptions = MergeWorkOptions(model.WorkOptions, item.CongViecs);
        model.Form = new DanhMucDichVuFormModel
        {
            Id = item.Id,
            TenDichVu = item.TenDichVu,
            MieuTa = item.MieuTa,
            TrangThaiSuDung = item.TrangThaiSuDung,
            CongViecs = item.CongViecs
                .OrderBy(work => work.ThuTu <= 0 ? int.MaxValue : work.ThuTu)
                .Select((work, index) => new DanhMucDichVuFormWorkItem
                {
                    IDCongViec = work.IDCongViec,
                    ThuTu = work.ThuTu > 0 ? work.ThuTu : index + 1
                })
                .ToList(),
            Keyword = query.Keyword,
            StatusFilter = query.StatusFilter,
            Page = currentPage
        };

        return model;
    }

    private async Task<DanhMucDichVuManagementViewModel> BuildPageModelForPostbackAsync(
        DanhMucDichVuFormModel form,
        DanhMucDichVuPopupMode popupMode,
        CancellationToken cancellationToken)
    {
        NormalizeFormState(form);

        var (items, totalCount, currentPage, totalPages, pageSize) = await _danhMucDichVuService.GetPagedAsync(
            form.Keyword,
            form.StatusFilter,
            form.Page,
            DefaultPageSize,
            cancellationToken);

        form.Page = currentPage;
        return new DanhMucDichVuManagementViewModel
        {
            Filter = new DanhMucDichVuFilterState
            {
                Keyword = form.Keyword,
                StatusFilter = form.StatusFilter,
                Page = currentPage,
                PageSize = pageSize
            },
            Form = form,
            PopupMode = popupMode,
            WorkOptions = await _danhMucDichVuService.GetWorkOptionsAsync(cancellationToken: cancellationToken),
            Items = items,
            TotalCount = totalCount,
            TotalPages = totalPages,
            CurrentPage = currentPage,
            StatusMessage = TempData["StatusMessage"]?.ToString(),
            StatusType = TempData["StatusType"]?.ToString() ?? "info"
        };
    }

    private static object BuildRouteValues(string? keyword, bool? statusFilter, int page)
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

    private static IReadOnlyList<DanhMucDichVuWorkItem> MergeWorkOptions(
        IReadOnlyList<DanhMucDichVuWorkItem> activeOptions,
        IReadOnlyList<DanhMucDichVuWorkItem> selectedOptions)
    {
        var merged = activeOptions
            .Concat(selectedOptions)
            .GroupBy(work => work.IDCongViec)
            .Select(group => group.First())
            .OrderBy(work => work.TenCongViec)
            .ToList();

        return merged;
    }

    private static void NormalizeFormState(DanhMucDichVuFormModel form)
    {
        form.Page = Math.Max(form.Page, 1);
        form.TenDichVu = form.TenDichVu?.Trim() ?? string.Empty;
        form.MieuTa = string.IsNullOrWhiteSpace(form.MieuTa) ? null : form.MieuTa.Trim();
        form.CongViecs = form.CongViecs?
            .Where(work => work.IDCongViec > 0)
            .GroupBy(work => work.IDCongViec)
            .Select((group, index) => new DanhMucDichVuFormWorkItem
            {
                IDCongViec = group.Key,
                ThuTu = group.First().ThuTu > 0 ? group.First().ThuTu : index + 1
            })
            .OrderBy(work => work.ThuTu)
            .ToList() ?? [];
    }
}
