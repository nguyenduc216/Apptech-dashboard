using System.Security.Claims;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class NhanVienController(
    INhanVienService nhanVienService,
    IUserAccountService userAccountService,
    IWebHostEnvironment webHostEnvironment) : Controller
{
    private static readonly HashSet<string> AllowedAvatarExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    private const int DefaultPageSize = 10;
    private const long MaxAvatarSizeInBytes = 2 * 1024 * 1024;
    private const string DefaultAvatarUrl = "/images/default-avatar.svg";

    private readonly INhanVienService _nhanVienService = nhanVienService;
    private readonly IUserAccountService _userAccountService = userAccountService;
    private readonly IWebHostEnvironment _webHostEnvironment = webHostEnvironment;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] NhanVienListQuery query)
    {
        var model = await BuildListModelAsync(query, HttpContext.RequestAborted);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create(string? keyword, bool? statusFilter, int page = 1)
    {
        var form = new NhanVienFormModel
        {
            Keyword = keyword,
            StatusFilter = statusFilter,
            Page = Math.Max(page, 1),
            TrangThaiSuDung = true,
            Avatar = DefaultAvatarUrl
        };

        return View("Detail", await BuildDetailModelAsync(form, HttpContext.RequestAborted));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, string? keyword, bool? statusFilter, int page = 1)
    {
        var item = await _nhanVienService.GetByIdAsync(id, HttpContext.RequestAborted);
        if (item is null)
        {
            TempData["StatusMessage"] = "Không tìm thấy nhân viên cần chỉnh sửa.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(keyword, statusFilter, page));
        }

        var form = new NhanVienFormModel
        {
            Id = item.Id,
            Ho = item.Ho,
            Ten = item.Ten,
            GioiTinh = item.GioiTinh ?? "Nam",
            NgaySinh = item.NgaySinh,
            IDPhongBan = item.IDPhongBan,
            TrangThaiSuDung = item.TrangThaiSuDung,
            ChucVu = item.ChucVu,
            Email = item.Email,
            Avatar = NormalizeAvatarUrl(item.Avatar),
            TaoTaiKhoan = item.IDTaiKhoan.HasValue,
            IDTaiKhoan = item.IDTaiKhoan,
            IsAdministrator = item.IsAdministrator,
            TenDangNhap = item.TenDangNhap,
            Keyword = keyword,
            StatusFilter = statusFilter,
            Page = Math.Max(page, 1)
        };

        return View("Detail", await BuildDetailModelAsync(form, HttpContext.RequestAborted));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Form")] NhanVienFormModel model)
    {
        NormalizeFormState(model);
        ValidateAvatar(model.AvatarFile);
        ValidateAccountFields(model);

        if (!ModelState.IsValid)
        {
            return View("Detail", await BuildDetailModelAsync(model, HttpContext.RequestAborted));
        }

        var avatarUpload = await SaveAvatarIfNeededAsync(model, HttpContext.RequestAborted);
        if (!avatarUpload.Succeeded)
        {
            ModelState.AddModelError("Form.AvatarFile", avatarUpload.ErrorMessage ?? "Không thể tải avatar lên lúc này.");
            return View("Detail", await BuildDetailModelAsync(model, HttpContext.RequestAborted));
        }

        var result = await _nhanVienService.CreateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            DeleteLocalAvatarIfOwned(avatarUpload.AbsolutePath);
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể thêm mới nhân viên.");
            return View("Detail", await BuildDetailModelAsync(model, HttpContext.RequestAborted));
        }

        TempData["StatusMessage"] = "Lưu nhân viên thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Edit), new
        {
            id = result.Id,
            keyword = model.Keyword,
            statusFilter = model.StatusFilter,
            page = Math.Max(model.Page, 1)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([Bind(Prefix = "Form")] NhanVienFormModel model)
    {
        NormalizeFormState(model);
        ValidateAvatar(model.AvatarFile);
        ValidateAccountFields(model);

        if (!ModelState.IsValid)
        {
            return View("Detail", await BuildDetailModelAsync(model, HttpContext.RequestAborted));
        }

        var oldAvatar = model.Avatar;
        var avatarUpload = await SaveAvatarIfNeededAsync(model, HttpContext.RequestAborted);
        if (!avatarUpload.Succeeded)
        {
            ModelState.AddModelError("Form.AvatarFile", avatarUpload.ErrorMessage ?? "Không thể tải avatar lên lúc này.");
            return View("Detail", await BuildDetailModelAsync(model, HttpContext.RequestAborted));
        }

        var result = await _nhanVienService.UpdateAsync(model, GetCurrentAuditUser(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            DeleteLocalAvatarIfOwned(avatarUpload.AbsolutePath);
            model.Avatar = oldAvatar;
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể cập nhật nhân viên.");
            return View("Detail", await BuildDetailModelAsync(model, HttpContext.RequestAborted));
        }

        if (!string.IsNullOrWhiteSpace(avatarUpload.AbsolutePath))
        {
            DeleteLocalAvatarIfOwned(oldAvatar);
        }

        if (IsCurrentAccount(model.IDTaiKhoan))
        {
            if (!string.IsNullOrWhiteSpace(model.MatKhau) && !model.IsAdministrator)
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                TempData["LoginMessage"] = "Mật khẩu đã được cập nhật. Vui lòng đăng nhập lại.";
                TempData["LoginMessageType"] = "success";
                return RedirectToAction("Login", "Home");
            }

            await RefreshCurrentAccountClaimsAsync(model, HttpContext.RequestAborted);
        }

        TempData["StatusMessage"] = "Cập nhật nhân viên thành công.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(Edit), new
        {
            id = model.Id,
            keyword = model.Keyword,
            statusFilter = model.StatusFilter,
            page = Math.Max(model.Page, 1)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRoles(NhanVienRoleAssignmentModel model)
    {
        var employee = await _nhanVienService.GetByIdAsync(model.EmployeeId, HttpContext.RequestAborted);
        if (employee is null || employee.IDTaiKhoan != model.AccountId)
        {
            TempData["StatusMessage"] = "Không tìm thấy tài khoản nhân viên để phân vai trò.";
            TempData["StatusType"] = "error";
            return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
        }

        if (employee.IsAdministrator)
        {
            TempData["StatusMessage"] = "Tài khoản quản trị đã có toàn quyền hệ thống.";
            TempData["StatusType"] = "info";
            return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
        }

        var result = await _nhanVienService.SaveRoleAssignmentsAsync(model.AccountId, model.SelectedRoleIds, HttpContext.RequestAborted);
        if (result.Succeeded && IsCurrentAccount(model.AccountId))
        {
            UserPermissionSession.Clear(HttpContext);
        }

        TempData["StatusMessage"] = result.Succeeded
            ? "Đã cập nhật vai trò nhân viên."
            : result.ErrorMessage ?? "Không thể cập nhật vai trò nhân viên.";
        TempData["StatusType"] = result.Succeeded ? "success" : "error";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(NhanVienDeleteModel model)
    {
        var current = await _nhanVienService.GetByIdAsync(model.Id, HttpContext.RequestAborted);
        var result = await _nhanVienService.DeleteAsync(model.Id, HttpContext.RequestAborted);
        if (result.Succeeded)
        {
            DeleteLocalAvatarIfOwned(current?.Avatar);
        }

        TempData["StatusMessage"] = result.Succeeded
            ? "Đã xóa nhân viên."
            : result.ErrorMessage ?? "Không thể xóa nhân viên.";
        TempData["StatusType"] = result.Succeeded ? "success" : "error";
        return RedirectToAction(nameof(Index), BuildRouteValues(model.Keyword, model.StatusFilter, model.Page));
    }

    private async Task<NhanVienManagementViewModel> BuildListModelAsync(
        NhanVienListQuery query,
        CancellationToken cancellationToken)
    {
        var (items, totalCount, currentPage, totalPages, pageSize) = await _nhanVienService.GetPagedAsync(
            query.Keyword,
            query.StatusFilter,
            query.Page,
            DefaultPageSize,
            cancellationToken);

        return new NhanVienManagementViewModel
        {
            Filter = new NhanVienFilterState
            {
                Keyword = query.Keyword,
                StatusFilter = query.StatusFilter,
                Page = currentPage,
                PageSize = pageSize
            },
            Items = items,
            TotalCount = totalCount,
            TotalPages = totalPages,
            CurrentPage = currentPage,
            RoleOptions = await _nhanVienService.GetRoleOptionsAsync(cancellationToken),
            RoleAssignment = await BuildRoleAssignmentModelAsync(items, query.RoleEmployeeId, cancellationToken),
            StatusMessage = TempData["StatusMessage"]?.ToString(),
            StatusType = TempData["StatusType"]?.ToString() ?? "info"
        };
    }

    private async Task<NhanVienRoleAssignmentViewModel?> BuildRoleAssignmentModelAsync(
        IReadOnlyList<NhanVienListItem> employees,
        int? employeeId,
        CancellationToken cancellationToken)
    {
        if (!employeeId.HasValue || employeeId.Value <= 0)
        {
            return null;
        }

        var employee = employees.FirstOrDefault(item => item.Id == employeeId.Value)
            ?? await _nhanVienService.GetByIdAsync(employeeId.Value, cancellationToken);
        if (employee?.IDTaiKhoan is not { } accountId || accountId == Guid.Empty)
        {
            TempData["StatusMessage"] = "Nhân viên chưa có tài khoản để phân vai trò.";
            TempData["StatusType"] = "error";
            return null;
        }

        if (employee.IsAdministrator)
        {
            TempData["StatusMessage"] = "Tài khoản quản trị đã có toàn quyền hệ thống.";
            TempData["StatusType"] = "info";
            return null;
        }

        return new NhanVienRoleAssignmentViewModel
        {
            EmployeeId = employee.Id,
            AccountId = accountId,
            EmployeeName = employee.HoTen,
            Username = employee.TenDangNhap,
            SelectedRoleIds = await _nhanVienService.GetAssignedRoleIdsAsync(accountId, cancellationToken)
        };
    }

    private async Task<NhanVienDetailViewModel> BuildDetailModelAsync(
        NhanVienFormModel form,
        CancellationToken cancellationToken)
    {
        NormalizeFormState(form);

        return new NhanVienDetailViewModel
        {
            Filter = new NhanVienFilterState
            {
                Keyword = form.Keyword,
                StatusFilter = form.StatusFilter,
                Page = form.Page,
                PageSize = DefaultPageSize
            },
            Form = form,
            PhongBanOptions = await _nhanVienService.GetPhongBanOptionsAsync(cancellationToken),
            StatusMessage = TempData["StatusMessage"]?.ToString(),
            StatusType = TempData["StatusType"]?.ToString() ?? "info"
        };
    }

    private async Task<(bool Succeeded, string? AbsolutePath, string? ErrorMessage)> SaveAvatarIfNeededAsync(
        NhanVienFormModel model,
        CancellationToken cancellationToken)
    {
        if (model.AvatarFile is not { Length: > 0 })
        {
            model.Avatar = NormalizeAvatarUrl(model.Avatar);
            return (true, null, null);
        }

        try
        {
            var extension = Path.GetExtension(model.AvatarFile.FileName).ToLowerInvariant();
            var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "avatars");
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"nhan-vien-{Guid.NewGuid():N}-{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
            var absolutePath = Path.Combine(uploadsFolder, fileName);

            await using var stream = System.IO.File.Create(absolutePath);
            await model.AvatarFile.CopyToAsync(stream, cancellationToken);
            model.Avatar = $"/uploads/avatars/{fileName}";
            return (true, absolutePath, null);
        }
        catch
        {
            return (false, null, "Không thể lưu avatar lên hệ thống.");
        }
    }

    private void ValidateAvatar(IFormFile? avatarFile)
    {
        if (avatarFile is null || avatarFile.Length == 0)
        {
            return;
        }

        var extension = Path.GetExtension(avatarFile.FileName);
        if (!AllowedAvatarExtensions.Contains(extension))
        {
            ModelState.AddModelError("Form.AvatarFile", "Avatar chỉ hỗ trợ định dạng JPG, PNG hoặc WEBP.");
        }

        if (avatarFile.Length > MaxAvatarSizeInBytes)
        {
            ModelState.AddModelError("Form.AvatarFile", "Dung lượng avatar tối đa là 2MB.");
        }
    }

    private void ValidateAccountFields(NhanVienFormModel model)
    {
        if (!model.TaoTaiKhoan)
        {
            return;
        }

        if (model.IsAdministrator)
        {
            model.TaoTaiKhoan = true;
            model.TenDangNhap = null;
            model.MatKhau = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(model.TenDangNhap))
        {
            ModelState.AddModelError("Form.TenDangNhap", "Vui lòng nhập tên đăng nhập.");
        }
        else if (model.TenDangNhap.Any(char.IsWhiteSpace))
        {
            ModelState.AddModelError("Form.TenDangNhap", "Tên đăng nhập không được chứa khoảng trắng.");
        }

        if (!model.IDTaiKhoan.HasValue && string.IsNullOrWhiteSpace(model.MatKhau))
        {
            ModelState.AddModelError("Form.MatKhau", "Vui lòng nhập mật khẩu khi tạo tài khoản.");
        }

        if (!string.IsNullOrEmpty(model.MatKhau) && model.MatKhau.Any(char.IsWhiteSpace))
        {
            ModelState.AddModelError("Form.MatKhau", "Mật khẩu không được chứa khoảng trắng.");
        }
    }

    private void DeleteLocalAvatarIfOwned(string? avatarUrlOrPath)
    {
        if (string.IsNullOrWhiteSpace(avatarUrlOrPath))
        {
            return;
        }

        string absolutePath;
        if (Path.IsPathRooted(avatarUrlOrPath))
        {
            absolutePath = avatarUrlOrPath;
        }
        else
        {
            var relativePath = avatarUrlOrPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            absolutePath = Path.Combine(_webHostEnvironment.WebRootPath, relativePath);
        }

        var uploadsRoot = Path.GetFullPath(Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "avatars"));
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

    private static void NormalizeFormState(NhanVienFormModel form)
    {
        form.Page = Math.Max(form.Page, 1);
        form.Ho = form.Ho?.Trim() ?? "";
        form.Ten = form.Ten?.Trim() ?? "";
        form.GioiTinh = string.IsNullOrWhiteSpace(form.GioiTinh) ? "Nam" : form.GioiTinh.Trim();
        form.Avatar = NormalizeAvatarUrl(form.Avatar);
        form.TenDangNhap = form.TenDangNhap?.Trim();
        if (!form.TaoTaiKhoan)
        {
            form.MatKhau = null;
        }
    }

    private static string NormalizeAvatarUrl(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
        {
            return DefaultAvatarUrl;
        }

        return avatarUrl.Trim().Replace("\\", "/").TrimStart('/') switch
        {
            "" => "",
            var normalized => $"/{normalized}"
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

    private bool IsCurrentAccount(Guid? accountId)
    {
        return accountId.HasValue &&
            Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentAccountId) &&
            currentAccountId == accountId.Value;
    }

    private async Task RefreshCurrentAccountClaimsAsync(NhanVienFormModel model, CancellationToken cancellationToken)
    {
        if (!model.IDTaiKhoan.HasValue)
        {
            return;
        }

        var existingAccount = await _userAccountService.GetAccountByIdAsync(model.IDTaiKhoan.Value, cancellationToken);
        var authenticateResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var existingClaims = User.Claims.ToList();
        var displayName = string.Join(" ", new[] { model.Ho, model.Ten }.Where(static part => !string.IsNullOrWhiteSpace(part)));
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = existingAccount?.FullName ?? User.FindFirstValue("display_name") ?? User.Identity?.Name ?? "Tài khoản";
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, model.IDTaiKhoan.Value.ToString()),
            new(ClaimTypes.Name, existingAccount?.Username ?? User.Identity?.Name ?? ""),
            new("display_name", displayName),
            new("initials", BuildInitials(displayName)),
            new("role_label", User.FindFirstValue("role_label") ?? existingAccount?.RoleDisplay ?? "Người dùng hệ thống")
        };

        var email = model.Email ?? existingAccount?.Email;
        if (!string.IsNullOrWhiteSpace(email))
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        foreach (var role in existingClaims.Where(static claim => claim.Type == ClaimTypes.Role))
        {
            claims.Add(role);
        }

        var groupName = User.FindFirstValue("group_name") ?? existingAccount?.GroupName;
        if (!string.IsNullOrWhiteSpace(groupName))
        {
            claims.Add(new Claim("group_name", groupName));
        }

        var avatarUrl = NormalizeAvatarUrl(model.Avatar);
        if (!string.IsNullOrWhiteSpace(avatarUrl))
        {
            claims.Add(new Claim("avatar_url", avatarUrl));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            authenticateResult.Properties ?? new AuthenticationProperties
            {
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
            });
    }

    private static string BuildInitials(string displayName)
    {
        var tokens = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return "U";
        }

        return tokens.Length == 1
            ? tokens[0][0].ToString().ToUpperInvariant()
            : string.Concat(tokens[0][0], tokens[^1][0]).ToUpperInvariant();
    }
}
