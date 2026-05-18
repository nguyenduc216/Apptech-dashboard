using System.Security.Claims;
using System.Globalization;
using System.Text;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class HomeController(
    IUserAccountService userAccountService,
    IUserPermissionService userPermissionService,
    IChamCongService chamCongService,
    INhanVienService nhanVienService,
    IWebHostEnvironment webHostEnvironment) : Controller
{
    private static readonly HashSet<string> AllowedAvatarExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp"
    };

    private const long MaxAvatarSizeInBytes = 2 * 1024 * 1024;
    private const long MaxCheckinImageSizeInBytes = 5 * 1024 * 1024;

    private readonly IUserAccountService _userAccountService = userAccountService;
    private readonly IUserPermissionService _userPermissionService = userPermissionService;
    private readonly IChamCongService _chamCongService = chamCongService;
    private readonly INhanVienService _nhanVienService = nhanVienService;
    private readonly IWebHostEnvironment _webHostEnvironment = webHostEnvironment;

    public async Task<IActionResult> Index(DateTime? chamCongDate = null, [FromQuery] int[] employeeIds = null!)
    {
        var model = DashboardViewModel.BuildSample();
        model.ChamCong = await BuildChamCongDashboardModelAsync(chamCongDate ?? DateTime.Today, employeeIds, HttpContext.RequestAborted);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> ChamCongHistory(DateTime? date, [FromQuery] int[] employeeIds = null!)
    {
        var employeeId = await GetCurrentEmployeeIdAsync(HttpContext.RequestAborted);
        var canSelectEmployees = await CanSelectChamCongEmployeesAsync(HttpContext.RequestAborted);
        var canAdminManageAttendance = await CanAdminManageChamCongAsync(HttpContext.RequestAborted);
        var selectableEmployeeOptions = canSelectEmployees
            ? await _nhanVienService.GetChamCongEmployeeOptionsAsync(HttpContext.RequestAborted)
            : [];
        if (!employeeId.HasValue && !canSelectEmployees)
        {
            return Json(new
            {
                succeeded = false,
                message = "Tài khoản chưa liên kết nhân viên nên không thể xem lịch sử chấm công."
            });
        }

        var selectedDate = date?.Date ?? DateTime.Today;
        var selectedEmployeeIds = NormalizeSelectedEmployeeIds(
            employeeIds,
            employeeId,
            canSelectEmployees,
            canAdminManageAttendance,
            selectableEmployeeOptions.Select(employee => employee.Id));
        var history = await _chamCongService.GetHistoryAsync(selectedEmployeeIds, selectedDate, HttpContext.RequestAborted);
        var actionEmployeeId = selectedEmployeeIds.FirstOrDefault(employeeId ?? 0);
        var openCheckin = actionEmployeeId > 0
            ? await _chamCongService.GetOpenCheckinAsync(actionEmployeeId, selectedDate, HttpContext.RequestAborted)
            : null;
        return Json(new
        {
            succeeded = true,
            selectedDate = selectedDate.ToString("yyyy-MM-dd"),
            openCheckinId = openCheckin?.Id,
            actionEmployeeId,
            canAdminManageAttendance,
            selectedEmployeeIds,
            history = history
                .OrderBy(item => item.ThoiDiem ?? DateTime.MaxValue)
                .ThenBy(item => item.Id)
                .Select(BuildChamCongHistoryJson)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChamCongCheckin([FromForm] ChamCongCheckinRequest model, IFormFile? imageFile)
    {
        model.GhiChuNhanVien = string.IsNullOrWhiteSpace(model.GhiChuNhanVien) ? null : model.GhiChuNhanVien.Trim();
        model.LongAddress ??= ParseInvariantDecimal(Request.Form["LongAddress"].FirstOrDefault());
        model.LatAddress ??= ParseInvariantDecimal(Request.Form["LatAddress"].FirstOrDefault());

        var currentEmployeeId = await GetCurrentEmployeeIdAsync(HttpContext.RequestAborted);
        var canSelectEmployees = await CanSelectChamCongEmployeesAsync(HttpContext.RequestAborted);
        var canAdminManageAttendance = await CanAdminManageChamCongAsync(HttpContext.RequestAborted);
        if (!canSelectEmployees)
        {
            model.ThoiDiem = null;
        }

        var targetEmployeeId = await ResolveAttendanceEmployeeIdAsync(model.IDNhanVien, currentEmployeeId, HttpContext.RequestAborted);
        if (targetEmployeeId <= 0)
        {
            return BadRequest(new { message = "Tài khoản chưa liên kết nhân viên nên không thể chấm công." });
        }

        var checkinDate = (model.ThoiDiem ?? DateTime.Now).Date;
        var dateError = ValidateAttendanceActionDate(checkinDate, targetEmployeeId, currentEmployeeId, canAdminManageAttendance);
        if (dateError is not null)
        {
            return BadRequest(new { message = dateError });
        }

        var imageError = ValidateCheckinImage(imageFile);
        if (imageError is not null)
        {
            return BadRequest(new { message = imageError });
        }

        var uploadResult = await SaveCheckinImageAsync(imageFile!, HttpContext.RequestAborted);
        if (!uploadResult.Succeeded)
        {
            return BadRequest(new { message = uploadResult.ErrorMessage ?? "Không thể lưu ảnh checkin." });
        }

        model.ImgPath = uploadResult.RelativeUrl;
        var result = await _chamCongService.CheckinAsync(targetEmployeeId, model, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            DeleteLocalCheckinImageIfOwned(uploadResult.AbsolutePath);
            return BadRequest(new { message = result.ErrorMessage ?? "Không thể lưu thông tin checkin." });
        }

        return Json(new { succeeded = true, id = result.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChamCongCheckout([FromForm] ChamCongCheckoutRequest model, IFormFile? imageFile)
    {
        model.GhiChuCheckOut = string.IsNullOrWhiteSpace(model.GhiChuCheckOut) ? null : model.GhiChuCheckOut.Trim();
        model.LongAddressCheckOut ??= ParseInvariantDecimal(Request.Form["LongAddressCheckOut"].FirstOrDefault());
        model.LatAddressCheckOut ??= ParseInvariantDecimal(Request.Form["LatAddressCheckOut"].FirstOrDefault());

        var currentEmployeeId = await GetCurrentEmployeeIdAsync(HttpContext.RequestAborted);
        var canSelectEmployees = await CanSelectChamCongEmployeesAsync(HttpContext.RequestAborted);
        var canAdminManageAttendance = await CanAdminManageChamCongAsync(HttpContext.RequestAborted);
        if (!canSelectEmployees)
        {
            model.ThoiDiemCheckOut = null;
        }

        var targetEmployeeId = await ResolveAttendanceEmployeeIdAsync(model.IDNhanVien, currentEmployeeId, HttpContext.RequestAborted);
        if (targetEmployeeId <= 0)
        {
            return BadRequest(new { message = "Tài khoản chưa liên kết nhân viên nên không thể checkout." });
        }

        var checkoutDate = (model.ThoiDiemCheckOut ?? DateTime.Now).Date;
        var dateError = ValidateAttendanceActionDate(checkoutDate, targetEmployeeId, currentEmployeeId, canAdminManageAttendance);
        if (dateError is not null)
        {
            return BadRequest(new { message = dateError });
        }

        var imageError = ValidateCheckinImage(imageFile);
        if (imageError is not null)
        {
            return BadRequest(new { message = imageError });
        }

        var uploadResult = await SaveCheckinImageAsync(imageFile!, HttpContext.RequestAborted);
        if (!uploadResult.Succeeded)
        {
            return BadRequest(new { message = uploadResult.ErrorMessage ?? "Không thể lưu ảnh checkout." });
        }

        model.ImgPathCheckOut = uploadResult.RelativeUrl;
        var result = await _chamCongService.CheckoutAsync(targetEmployeeId, model, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            DeleteLocalCheckinImageIfOwned(uploadResult.AbsolutePath);
            return BadRequest(new { message = result.ErrorMessage ?? "Không thể lưu thông tin checkout." });
        }

        return Json(new { succeeded = true });
    }

    [HttpGet]
    public async Task<IActionResult> UpdateAccount()
    {
        var account = await GetCurrentAccountAsync();
        if (account is null)
        {
            return await RedirectToLoginAfterSignOutAsync();
        }

        SetUpdateAccountPageData();
        return View(BuildUpdateAccountViewModel(account));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAccount([Bind(Prefix = "Profile")] UpdateProfileInputModel model)
    {
        var account = await GetCurrentAccountAsync();
        if (account is null)
        {
            return await RedirectToLoginAfterSignOutAsync();
        }

        ValidateAvatar(model.AvatarFile);

        if (!ModelState.IsValid)
        {
            SetUpdateAccountPageData("profile");
            return View(BuildUpdateAccountViewModel(account, profileOverride: model));
        }

        string? uploadedAvatarUrl = null;
        string? uploadedAvatarPath = null;

        if (model.AvatarFile is { Length: > 0 })
        {
            var avatarUpload = await SaveAvatarAsync(model.AvatarFile, account.Id, HttpContext.RequestAborted);
            if (!avatarUpload.Succeeded)
            {
                ModelState.AddModelError("Profile.AvatarFile", avatarUpload.ErrorMessage ?? "Không thể tải avatar lên lúc này.");
                SetUpdateAccountPageData("profile");
                return View(BuildUpdateAccountViewModel(account, profileOverride: model));
            }

            uploadedAvatarUrl = avatarUpload.RelativeUrl;
            uploadedAvatarPath = avatarUpload.AbsolutePath;
        }

        var updateResult = await _userAccountService.UpdateProfileAsync(
            new UserProfileUpdateRequest(
                account.Id,
                model.FullName,
                model.Email,
                model.DateOfBirth,
                model.Address,
                model.PhoneNumber,
                model.Gender,
                model.GroupName,
                model.ZaloId,
                uploadedAvatarUrl ?? account.AvatarUrl,
                account.Username),
            HttpContext.RequestAborted);

        if (!updateResult.Succeeded || updateResult.Account is null)
        {
            DeleteLocalAvatarIfOwned(uploadedAvatarPath);
            ModelState.AddModelError(string.Empty, updateResult.ErrorMessage ?? "Không thể cập nhật thông tin người dùng.");
            SetUpdateAccountPageData("profile");
            return View(BuildUpdateAccountViewModel(account, profileOverride: model));
        }

        if (!string.IsNullOrWhiteSpace(uploadedAvatarPath) &&
            !string.Equals(NormalizeAvatarUrl(account.AvatarUrl), NormalizeAvatarUrl(uploadedAvatarUrl), StringComparison.OrdinalIgnoreCase))
        {
            DeleteLocalAvatarIfOwned(account.AvatarUrl);
        }

        await RefreshAuthenticatedUserAsync(updateResult.Account);

        TempData["StatusMessage"] = "Đã cập nhật thông tin người dùng.";
        TempData["StatusType"] = "success";
        return RedirectToAction(nameof(UpdateAccount));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword([Bind(Prefix = "Password")] ChangePasswordInputModel model)
    {
        var account = await GetCurrentAccountAsync();
        if (account is null)
        {
            return await RedirectToLoginAfterSignOutAsync();
        }

        if (!ModelState.IsValid)
        {
            SetUpdateAccountPageData("password");
            return View("UpdateAccount", BuildUpdateAccountViewModel(account, passwordOverride: model));
        }

        var changeResult = await _userAccountService.ChangePasswordAsync(
            account.Id,
            model.CurrentPassword,
            model.NewPassword,
            account.Username,
            HttpContext.RequestAborted);

        if (!changeResult.Succeeded)
        {
            ModelState.AddModelError(string.Empty, changeResult.ErrorMessage ?? "Không thể đổi mật khẩu lúc này.");
            SetUpdateAccountPageData("password");
            return View("UpdateAccount", BuildUpdateAccountViewModel(account, passwordOverride: model));
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["LoginMessage"] = "Đổi mật khẩu thành công. Vui lòng đăng nhập lại bằng mật khẩu mới.";
        TempData["LoginMessageType"] = "success";
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToLocal(returnUrl);
        }

        ViewData["Title"] = "Đăng nhập";
        ViewData["LoginMessage"] = TempData["LoginMessage"];
        ViewData["LoginMessageType"] = TempData["LoginMessageType"];

        return View(new LoginViewModel
        {
            ReturnUrl = returnUrl
        });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "Đăng nhập";
            return View(model);
        }

        var authenticationResult = await _userAccountService.AuthenticateAsync(
            model.Username,
            model.Password,
            HttpContext.RequestAborted);

        if (!authenticationResult.Succeeded || authenticationResult.Account is null)
        {
            ModelState.AddModelError(
                string.Empty,
                authenticationResult.ErrorMessage ?? "Tên đăng nhập hoặc mật khẩu không đúng.");
            ViewData["Title"] = "Đăng nhập";
            return View(model);
        }

        await SignInAccountAsync(
            authenticationResult.Account,
            new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                AllowRefresh = true,
                ExpiresUtc = model.RememberMe
                    ? DateTimeOffset.UtcNow.AddDays(14)
                    : DateTimeOffset.UtcNow.AddHours(12)
            });
        await LoadPermissionsToSessionAsync(authenticationResult.Account);

        return RedirectToLocal(model.ReturnUrl);
    }

    [HttpGet]
    public async Task<IActionResult> Logout()
    {
        UserPermissionSession.Clear(HttpContext);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult Error()
    {
        return View();
    }

    private async Task<UserAccount?> GetCurrentAccountAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userId, out var parsedUserId)
            ? await _userAccountService.GetAccountByIdAsync(parsedUserId, HttpContext.RequestAborted)
            : null;
    }

    private async Task<ChamCongDashboardModel> BuildChamCongDashboardModelAsync(
        DateTime selectedDate,
        IReadOnlyCollection<int>? selectedEmployeeIds,
        CancellationToken cancellationToken)
    {
        var employeeId = await GetCurrentEmployeeIdAsync(cancellationToken);
        var locations = await _chamCongService.GetApptechLocationsAsync(cancellationToken);
        var canSelectEmployees = await CanSelectChamCongEmployeesAsync(cancellationToken);
        var canAdminManageAttendance = await CanAdminManageChamCongAsync(cancellationToken);
        var employeeOptions = canSelectEmployees
            ? await _nhanVienService.GetChamCongEmployeeOptionsAsync(cancellationToken)
            : [];
        var normalizedSelectedEmployeeIds = NormalizeSelectedEmployeeIds(
            selectedEmployeeIds,
            employeeId,
            canSelectEmployees,
            canAdminManageAttendance,
            employeeOptions.Select(employee => employee.Id));
        var history = normalizedSelectedEmployeeIds.Count > 0
            ? await _chamCongService.GetHistoryAsync(normalizedSelectedEmployeeIds, selectedDate.Date, cancellationToken)
            : [];
        var actionEmployeeId = normalizedSelectedEmployeeIds.FirstOrDefault(employeeId ?? 0);
        var openCheckin = actionEmployeeId > 0
            ? await _chamCongService.GetOpenCheckinAsync(actionEmployeeId, selectedDate.Date, cancellationToken)
            : null;
        var distanceLimit = await _chamCongService.GetCheckinDistanceLimitMetersAsync(cancellationToken);

        return new ChamCongDashboardModel
        {
            SelectedDate = selectedDate.Date,
            CurrentEmployeeId = employeeId,
            LocationOptions = locations,
            EmployeeOptions = employeeOptions,
            SelectedEmployeeIds = normalizedSelectedEmployeeIds,
            History = history
                .OrderBy(item => item.ThoiDiem ?? DateTime.MaxValue)
                .ThenBy(item => item.Id)
                .ToList(),
            OpenCheckin = openCheckin,
            CheckinDistanceLimitMeters = distanceLimit,
            CanSelectEmployees = canSelectEmployees,
            CanAdminManageAttendance = canAdminManageAttendance
        };
    }

    private async Task<int?> GetCurrentEmployeeIdAsync(CancellationToken cancellationToken)
    {
        var claimValue = User.FindFirstValue("employee_id");
        if (int.TryParse(claimValue, out var claimEmployeeId) && claimEmployeeId > 0)
        {
            return claimEmployeeId;
        }

        var account = await GetCurrentAccountAsync();
        return account?.EmployeeId is > 0 ? account.EmployeeId : null;
    }

    private static object BuildChamCongHistoryJson(ChamCongHistoryItem item)
    {
        return new
        {
            id = item.Id,
            idDiaDiem = item.IDDiaDiem,
            idNhanVien = item.IDNhanVien,
            hoTenNhanVien = item.HoTenNhanVien,
            tenKhachHang = item.TenKhachHang,
            diaChi = item.DiaChi,
            thoiDiem = item.ThoiDiem?.ToString("dd/MM/yyyy HH:mm"),
            thoiDiemCheckOut = item.ThoiDiemCheckOut?.ToString("dd/MM/yyyy HH:mm"),
            checkinSortKey = item.ThoiDiem?.ToString("yyyyMMddHHmmss") ?? "99999999999999",
            checkinTime = item.ThoiDiem?.ToString("HH:mm"),
            checkoutTime = item.ThoiDiemCheckOut?.ToString("HH:mm"),
            isOpen = item.IsOpen,
            latAddress = item.LatAddress,
            longAddress = item.LongAddress,
            latAddressCheckOut = item.LatAddressCheckOut,
            longAddressCheckOut = item.LongAddressCheckOut,
            imgPath = item.ImgPath,
            imgPathCheckOut = item.ImgPathCheckOut,
            ghiChuNhanVien = item.GhiChuNhanVien,
            ghiChuCheckOut = item.GhiChuCheckOut,
            isCheckinViolation = item.IsCheckinViolation,
            isCheckoutViolation = item.IsCheckoutViolation
        };
    }

    private IReadOnlyList<int> NormalizeSelectedEmployeeIds(
        IReadOnlyCollection<int>? selectedEmployeeIds,
        int? currentEmployeeId,
        bool canSelectEmployees,
        bool isAdminAccount,
        IEnumerable<int>? selectableEmployeeIds = null)
    {
        if (!canSelectEmployees)
        {
            return currentEmployeeId.HasValue && currentEmployeeId.Value > 0
                ? [currentEmployeeId.Value]
                : [];
        }

        var selectableIds = (selectableEmployeeIds ?? [])
            .Where(id => id > 0)
            .Distinct()
            .ToHashSet();
        var normalized = (selectedEmployeeIds ?? [])
            .Where(id => id > 0)
            .Where(id => selectableIds.Count == 0 || selectableIds.Contains(id))
            .Distinct()
            .ToList();

        if (isAdminAccount && normalized.Count == 0 && selectableIds.Count > 0)
        {
            return selectableIds.OrderBy(id => id).ToList();
        }

        if (normalized.Count == 0 && currentEmployeeId.HasValue && currentEmployeeId.Value > 0)
        {
            if (selectableIds.Count == 0 || selectableIds.Contains(currentEmployeeId.Value))
            {
                normalized.Add(currentEmployeeId.Value);
            }
        }

        return normalized;
    }

    private async Task<int> ResolveAttendanceEmployeeIdAsync(int? requestedEmployeeId, int? currentEmployeeId, CancellationToken cancellationToken)
    {
        if (requestedEmployeeId.HasValue &&
            requestedEmployeeId.Value > 0 &&
            await CanSelectChamCongEmployeesAsync(cancellationToken))
        {
            return requestedEmployeeId.Value;
        }

        return currentEmployeeId.GetValueOrDefault();
    }

    private static string? ValidateAttendanceActionDate(
        DateTime actionDate,
        int targetEmployeeId,
        int? currentEmployeeId,
        bool canAdminManageAttendance)
    {
        var today = DateTime.Today;
        if (actionDate.Date > today)
        {
            return "Không được checkin/checkout vào ngày tương lai.";
        }

        if (actionDate.Date < today &&
            targetEmployeeId != currentEmployeeId.GetValueOrDefault() &&
            !canAdminManageAttendance)
        {
            return "Chỉ quản trị hệ thống mới được checkin/checkout lùi ngày cho nhân viên khác.";
        }

        return null;
    }

    private async Task<bool> CanAdminManageChamCongAsync(CancellationToken cancellationToken)
    {
        var account = await GetCurrentAccountAsync();
        return IsAdminAccount(account);
    }

    private async Task<bool> CanSelectChamCongEmployeesAsync(CancellationToken cancellationToken)
    {
        var account = await GetCurrentAccountAsync();
        if (IsAdminAccount(account))
        {
            return true;
        }

        var permissions = await UserPermissionSession.GetOrLoadAsync(HttpContext, _userPermissionService, cancellationToken);
        return permissions.Any(permission =>
            string.Equals(permission.PermissionCode, "Dashboard_View_CheckIn", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(permission.PermissionCode, "Dasboard_View_CheckIn", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizePermissionCode(permission.PermissionCode), "dasboardviewcheckin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizePermissionCode(permission.PermissionCode), "dashboardviewcheckin", StringComparison.OrdinalIgnoreCase));
    }

    private bool IsAdminAccount(UserAccount? account)
    {
        return account?.IsAdministrator == true ||
            User.IsInRole("Administrator") ||
            IsAdminText(User.Identity?.Name) ||
            IsAdminText(User.FindFirstValue(ClaimTypes.Name)) ||
            IsAdminText(User.FindFirstValue("role_label")) ||
            IsAdminText(User.FindFirstValue("group_name")) ||
            IsAdminText(account?.Username) ||
            IsAdminText(account?.GroupName);
    }

    private static string NormalizePermissionCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (character is 'đ' or 'Đ')
            {
                builder.Append('d');
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static bool IsAdminText(string? value)
    {
        var normalized = NormalizePermissionCode(value);
        return normalized.Contains("admin", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("quantri", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal? ParseInvariantDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Replace(',', '.');
        return decimal.TryParse(
            normalized,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;
    }

    private UpdateAccountViewModel BuildUpdateAccountViewModel(
        UserAccount account,
        UpdateProfileInputModel? profileOverride = null,
        ChangePasswordInputModel? passwordOverride = null)
    {
        var profileModel = profileOverride ?? new UpdateProfileInputModel();

        profileModel.Username = string.IsNullOrWhiteSpace(profileModel.Username) ? account.Username : profileModel.Username;
        profileModel.GroupName = string.IsNullOrWhiteSpace(profileModel.GroupName)
            ? (!string.IsNullOrWhiteSpace(account.GroupName) ? account.GroupName : (account.IsAdministrator ? "Quản trị viên" : ""))
            : profileModel.GroupName;
        profileModel.FullName = string.IsNullOrWhiteSpace(profileModel.FullName) ? account.FullName : profileModel.FullName;
        profileModel.Email = string.IsNullOrWhiteSpace(profileModel.Email) ? account.Email : profileModel.Email;
        profileModel.DateOfBirth ??= account.DateOfBirth;
        profileModel.Address = string.IsNullOrWhiteSpace(profileModel.Address) ? account.Address : profileModel.Address;
        profileModel.PhoneNumber = string.IsNullOrWhiteSpace(profileModel.PhoneNumber) ? account.PhoneNumber : profileModel.PhoneNumber;
        profileModel.Gender = string.IsNullOrWhiteSpace(profileModel.Gender) ? account.Gender : profileModel.Gender;
        profileModel.ZaloId = string.IsNullOrWhiteSpace(profileModel.ZaloId) ? account.ZaloId : profileModel.ZaloId;
        profileModel.AvatarUrl = string.IsNullOrWhiteSpace(profileModel.AvatarUrl)
            ? NormalizeAvatarUrl(account.AvatarUrl)
            : NormalizeAvatarUrl(profileModel.AvatarUrl);

        return new UpdateAccountViewModel
        {
            Profile = profileModel,
            Password = passwordOverride ?? new ChangePasswordInputModel()
        };
    }

    private void SetUpdateAccountPageData(string activeForm = "profile")
    {
        ViewData["Title"] = "Thông tin cá nhân";
        ViewData["ActiveForm"] = activeForm;
        ViewData["StatusMessage"] = TempData["StatusMessage"];
        ViewData["StatusType"] = TempData["StatusType"];
    }

    private async Task SignInAccountAsync(UserAccount account, AuthenticationProperties? properties = null)
    {
        var identity = new ClaimsIdentity(BuildClaims(account), CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            properties ?? new AuthenticationProperties
            {
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
            });
    }

    private async Task RefreshAuthenticatedUserAsync(UserAccount account)
    {
        var authenticateResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        await SignInAccountAsync(
            account,
            authenticateResult.Properties ?? new AuthenticationProperties
            {
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
            });
        await LoadPermissionsToSessionAsync(account);
    }

    private async Task LoadPermissionsToSessionAsync(UserAccount account)
    {
        if (account.IsAdministrator)
        {
            UserPermissionSession.Clear(HttpContext);
            return;
        }

        var permissions = await _userPermissionService.GetPermissionsAsync(account.Id, HttpContext.RequestAborted);
        UserPermissionSession.Set(HttpContext, permissions);
    }

    private IEnumerable<Claim> BuildClaims(UserAccount account)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Name, account.Username),
            new("display_name", account.FullName),
            new("initials", account.Initials),
            new("role_label", account.RoleDisplay)
        };

        if (!string.IsNullOrWhiteSpace(account.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, account.Email));
        }

        if (account.IsAdministrator)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
        }

        if (account.EmployeeId.HasValue && account.EmployeeId.Value > 0)
        {
            claims.Add(new Claim("employee_id", account.EmployeeId.Value.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(account.GroupName))
        {
            claims.Add(new Claim("group_name", account.GroupName));
        }

        var avatarUrl = NormalizeAvatarUrl(account.AvatarUrl);
        if (!string.IsNullOrWhiteSpace(avatarUrl))
        {
            claims.Add(new Claim("avatar_url", avatarUrl));
        }

        return claims;
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
            ModelState.AddModelError("Profile.AvatarFile", "Avatar chỉ hỗ trợ định dạng JPG, PNG hoặc WEBP.");
        }

        if (avatarFile.Length > MaxAvatarSizeInBytes)
        {
            ModelState.AddModelError("Profile.AvatarFile", "Dung lượng avatar tối đa là 2MB.");
        }
    }

    private static string? ValidateCheckinImage(IFormFile? imageFile)
    {
        if (imageFile is null || imageFile.Length == 0)
        {
            return "Vui lòng chụp ảnh chấm công.";
        }

        var extension = Path.GetExtension(imageFile.FileName);
        if (!AllowedAvatarExtensions.Contains(extension))
        {
            return "Ảnh chấm công chỉ hỗ trợ định dạng JPG, PNG hoặc WEBP.";
        }

        return imageFile.Length > MaxCheckinImageSizeInBytes
            ? "Dung lượng ảnh chấm công tối đa là 5MB."
            : null;
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

            var fileName = $"chamcong-{Guid.NewGuid():N}-{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
            var absolutePath = Path.Combine(uploadsFolder, fileName);

            await using var stream = System.IO.File.Create(absolutePath);
            await imageFile.CopyToAsync(stream, cancellationToken);

            return (true, $"/uploads/checkin/{fileName}", absolutePath, null);
        }
        catch
        {
            return (false, null, null, "Không thể lưu ảnh chấm công lên hệ thống.");
        }
    }

    private async Task<(bool Succeeded, string? RelativeUrl, string? AbsolutePath, string? ErrorMessage)> SaveAvatarAsync(
        IFormFile avatarFile,
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var extension = Path.GetExtension(avatarFile.FileName).ToLowerInvariant();
            var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "avatars");
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{userId:N}-{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
            var absolutePath = Path.Combine(uploadsFolder, fileName);

            await using var stream = System.IO.File.Create(absolutePath);
            await avatarFile.CopyToAsync(stream, cancellationToken);

            return (true, $"/uploads/avatars/{fileName}", absolutePath, null);
        }
        catch
        {
            return (false, null, null, "Không thể lưu avatar lên hệ thống.");
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

    private static string NormalizeAvatarUrl(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl))
        {
            return "";
        }

        return avatarUrl.Trim().Replace("\\", "/").TrimStart('/') switch
        {
            "" => "",
            var normalized => $"/{normalized}"
        };
    }

    private async Task<IActionResult> RedirectToLoginAfterSignOutAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return Redirect("/trang-chu");
    }
}
