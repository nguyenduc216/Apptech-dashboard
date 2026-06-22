using System.Globalization;
using System.Security.Claims;
using System.Text;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class KhachHangController(
    IKhachHangService khachHangService,
    ICustomerLinkService customerLinkService,
    IUserAccountService userAccountService) : Controller
{
    private const int DefaultPageSize = 10;
    private readonly IKhachHangService _khachHangService = khachHangService;
    private readonly ICustomerLinkService _customerLinkService = customerLinkService;
    private readonly IUserAccountService _userAccountService = userAccountService;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] KhachHangListQuery query)
    {
        var model = await BuildListModelAsync(query, HttpContext.RequestAborted);
        return View(model);
    }

    [HttpGet]
    public IActionResult Create(string? keyword, int page = 1)
    {
        var form = new KhachHangFormModel
        {
            Keyword = keyword,
            Page = Math.Max(page, 1),
            ActiveTab = "thong-tin"
        };

        return View("Detail", BuildDetailModel(form));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, string? keyword, int page = 1)
    {
        var model = await BuildDetailModelAsync(id, keyword, page, HttpContext.RequestAborted);
        if (model is not null)
        {
            return View("Detail", model);
        }

        TempData["StatusMessage"] = "Không tìm thấy khách hàng cần chỉnh sửa.";
        TempData["StatusType"] = "error";
        return RedirectToAction(nameof(Index), BuildRouteValues(keyword, page));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] KhachHangFormModel model)
    {
        NormalizeFormState(model);

        if (!ModelState.IsValid)
        {
            return View("Detail", await BuildDetailModelForPostbackAsync(model, HttpContext.RequestAborted));
        }

        var result = await _khachHangService.CreateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể thêm mới khách hàng.");
            return View("Detail", BuildDetailModel(model));
        }

        TempData["StatusMessage"] = "Lưu khách hàng thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Edit), new
        {
            id = result.Id,
            keyword = model.Keyword,
            page = Math.Max(model.Page, 1)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([Bind(Prefix = "Form")] KhachHangFormModel model)
    {
        NormalizeFormState(model);

        if (model.Id.HasValue &&
            !await CanCurrentUserModifyCustomerAsync(model.Id.Value, HttpContext.RequestAborted))
        {
            ModelState.AddModelError(string.Empty, "Khách hàng có mã bắt đầu bằng Apptech chỉ tài khoản admin mới được sửa.");
            return View("Detail", await BuildDetailModelForPostbackAsync(model, HttpContext.RequestAborted));
        }

        if (!ModelState.IsValid)
        {
            return View("Detail", await BuildDetailModelForPostbackAsync(model, HttpContext.RequestAborted));
        }

        var result = await _khachHangService.UpdateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể cập nhật khách hàng.");
            return View("Detail", BuildDetailModel(model));
        }

        TempData["StatusMessage"] = "Cập nhật khách hàng thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Edit), new
        {
            id = model.Id,
            keyword = model.Keyword,
            page = Math.Max(model.Page, 1)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateZaloLink(
        int id,
        string? keyword,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var customer = await _khachHangService.GetByIdAsync(id, cancellationToken);
        if (customer is null)
        {
            TempData["StatusMessage"] = "Không tìm thấy khách hàng để tạo link Zalo.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(keyword, page));
        }

        try
        {
            var result = await _customerLinkService.CreateLinkAsync(
                id,
                requestId: null,
                purpose: "ConnectZalo",
                expiresInDays: 30,
                cancellationToken);

            TempData["ZaloCustomerLink"] = result.Link;
            TempData["ZaloCustomerLinkToken"] = result.Token;
            TempData["StatusMessage"] = "Đã tạo link Zalo riêng cho khách hàng. Link có hiệu lực trong 30 ngày.";
            TempData["StatusType"] = "success";
        }
        catch (Exception ex)
        {
            TempData["StatusMessage"] = $"Không thể tạo link Zalo: {ex.Message}";
            TempData["StatusType"] = "error";
        }

        return RedirectToAction(nameof(Edit), new
        {
            id,
            keyword,
            page = Math.Max(page, 1)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLocation([FromBody] KhachHangDiaDiemSaveModel model)
    {
        if (model.IDKhachHang.HasValue &&
            !await CanCurrentUserModifyCustomerAsync(model.IDKhachHang.Value, HttpContext.RequestAborted))
        {
            return Json(new
            {
                succeeded = false,
                errorMessage = "Khách hàng có mã bắt đầu bằng Apptech chỉ tài khoản admin mới được sửa."
            });
        }

        var result = await _khachHangService.SaveLocationAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded || result.Item is null)
        {
            return Json(new
            {
                succeeded = false,
                errorMessage = result.ErrorMessage ?? "Không thể lưu địa điểm làm việc."
            });
        }

        return Json(new
        {
            succeeded = true,
            item = new
            {
                id = result.Item.Id,
                diaChi = result.Item.DiaChi,
                nguoiLienHe = result.Item.NguoiLienHe,
                dienThoai = result.Item.DienThoai,
                longAddress = result.Item.LongAddress,
                latAddress = result.Item.LatAddress,
                trangThaiSuDung = result.Item.TrangThaiSuDung
            }
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteLocation([FromBody] KhachHangDiaDiemSaveModel model)
    {
        if (model.IDKhachHang.HasValue &&
            !await CanCurrentUserModifyCustomerAsync(model.IDKhachHang.Value, HttpContext.RequestAborted))
        {
            return Json(new
            {
                succeeded = false,
                errorMessage = "Khách hàng có mã bắt đầu bằng Apptech chỉ tài khoản admin mới được xóa."
            });
        }

        var result = await _khachHangService.DeleteLocationAsync(model.IDKhachHang ?? 0, model.Id ?? 0, HttpContext.RequestAborted);
        return Json(new
        {
            succeeded = result.Succeeded,
            errorMessage = result.Succeeded ? null : result.ErrorMessage ?? "Không thể xóa địa điểm làm việc."
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(KhachHangDeleteModel model)
    {
        if (!await CanCurrentUserModifyCustomerAsync(model.Id, HttpContext.RequestAborted))
        {
            TempData["StatusMessage"] = "Khách hàng có mã bắt đầu bằng Apptech chỉ tài khoản admin mới được xóa.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.Page));
        }

        var result = await _khachHangService.DeleteAsync(model.Id, HttpContext.RequestAborted);
        TempData["StatusMessage"] = result.Succeeded
            ? "Đã xóa khách hàng."
            : result.ErrorMessage ?? "Không thể xóa khách hàng.";
        TempData["StatusType"] = result.Succeeded ? "success" : "error";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.Page));
    }

    private async Task<KhachHangManagementViewModel> BuildListModelAsync(
        KhachHangListQuery query,
        CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _khachHangService.GetPagedAsync(
            query.Keyword,
            query.Page,
            DefaultPageSize,
            cancellationToken);

        return new KhachHangManagementViewModel
        {
            Filter = new KhachHangFilterState
            {
                Keyword = query.Keyword,
                Page = currentPage,
                PageSize = pageSize
            },
            Items = items,
            TotalCount = totalCount,
            TotalPages = totalPages,
            CurrentPage = currentPage,
            StatusMessage = TempData["StatusMessage"]?.ToString(),
            StatusType = TempData["StatusType"]?.ToString() ?? "info",
            CurrentUserIsAdmin = await IsCurrentUserAdminAsync(cancellationToken)
        };
    }

    private async Task<KhachHangDetailViewModel?> BuildDetailModelAsync(
        int id,
        string? keyword,
        int page,
        CancellationToken cancellationToken)
    {
        var item = await _khachHangService.GetByIdAsync(id, cancellationToken);
        if (item is null)
        {
            return null;
        }

        return BuildDetailModel(new KhachHangFormModel
        {
            Id = item.Id,
            TenKhachHang = item.TenKhachHang,
            MaKhachHang = item.MaKhachHang,
            DiaChi = item.DiaChi,
            SoDienThoai = item.SoDienThoai,
            NguoiDaiDien = item.NguoiDaiDien,
            NganhNghe = item.NganhNghe,
            ZaloID = item.ZaloID,
            GhiChu = item.GhiChu,
            DiaDiemLamViec = item.DiaDiemLamViec.ToList(),
            Keyword = keyword,
            Page = Math.Max(page, 1),
            ActiveTab = "thong-tin"
        }, await IsCurrentUserAdminAsync(cancellationToken));
    }

    private async Task<KhachHangDetailViewModel> BuildDetailModelForPostbackAsync(KhachHangFormModel form, CancellationToken cancellationToken)
    {
        return BuildDetailModel(form, await IsCurrentUserAdminAsync(cancellationToken));
    }

    private KhachHangDetailViewModel BuildDetailModel(KhachHangFormModel form, bool currentUserIsAdmin = false)
    {
        NormalizeFormState(form);

        return new KhachHangDetailViewModel
        {
            Filter = new KhachHangFilterState
            {
                Keyword = form.Keyword,
                Page = form.Page,
                PageSize = DefaultPageSize
            },
            Form = form,
            StatusMessage = TempData["StatusMessage"]?.ToString(),
            StatusType = TempData["StatusType"]?.ToString() ?? "info",
            ZaloCustomerLink = TempData["ZaloCustomerLink"]?.ToString(),
            ZaloCustomerLinkToken = TempData["ZaloCustomerLinkToken"]?.ToString(),
            CurrentUserIsAdmin = currentUserIsAdmin
        };
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

    private async Task<bool> CanCurrentUserModifyCustomerAsync(int customerId, CancellationToken cancellationToken)
    {
        var customer = await _khachHangService.GetByIdAsync(customerId, cancellationToken);
        if (customer is null)
        {
            return true;
        }

        return !customer.IsApptechProtected || await IsCurrentUserAdminAsync(cancellationToken);
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

    private static void NormalizeFormState(KhachHangFormModel form)
    {
        form.DiaDiemLamViec ??= [];
        form.Page = Math.Max(form.Page, 1);
        form.ActiveTab = string.IsNullOrWhiteSpace(form.ActiveTab) ? "thong-tin" : form.ActiveTab.Trim();
    }
}
