using System.Globalization;
using System.Security.Claims;
using System.Text;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
public class ReportController(
    IChamCongReportService chamCongReportService,
    ICongViecReportService congViecReportService,
    IUserAccountService userAccountService,
    IUserPermissionService userPermissionService) : Controller
{
    private readonly IChamCongReportService _chamCongReportService = chamCongReportService;
    private readonly ICongViecReportService _congViecReportService = congViecReportService;
    private readonly IUserAccountService _userAccountService = userAccountService;
    private readonly IUserPermissionService _userPermissionService = userPermissionService;

    [HttpGet]
    public async Task<IActionResult> ChamCong([FromQuery] ChamCongReportQuery query)
    {
        if (!await CanViewChamCongReportAsync(HttpContext.RequestAborted))
        {
            return Forbid();
        }

        var today = DateTime.Today;
        var month = query.Month.GetValueOrDefault(today.Month);
        var year = query.Year.GetValueOrDefault(today.Year);
        var model = await _chamCongReportService.GetMonthlyReportAsync(month, year, query.Tab, query.EmployeeIds, HttpContext.RequestAborted);

        ViewData["Title"] = "Báo cáo chấm công";
        ViewData["Breadcrumb"] = "Trang chủ / Báo cáo / Chấm công";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> CongViec([FromQuery] CongViecReportQuery query)
    {
        if (!await CanViewCongViecReportAsync(HttpContext.RequestAborted))
        {
            return Forbid();
        }

        var model = await _congViecReportService.GetReportAsync(query.DateFrom, query.DateTo, query.EmployeeIds, HttpContext.RequestAborted);

        ViewData["Title"] = "Báo cáo công việc";
        ViewData["Breadcrumb"] = "Trang chủ / Báo cáo / Công việc";
        return View(model);
    }

    private async Task<bool> CanViewChamCongReportAsync(CancellationToken cancellationToken)
    {
        var account = await GetCurrentAccountAsync(cancellationToken);
        if (IsAdminAccount(account))
        {
            return true;
        }

        if (account is null)
        {
            return false;
        }

        var permissions = await UserPermissionSession.GetOrLoadAsync(HttpContext, _userPermissionService, cancellationToken);
        return permissions.Any(permission =>
            string.Equals(permission.PermissionCode, "3068_View", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizePermissionCode(permission.PermissionCode), "3068view", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> CanViewCongViecReportAsync(CancellationToken cancellationToken)
    {
        var account = await GetCurrentAccountAsync(cancellationToken);
        if (IsAdminAccount(account))
        {
            return true;
        }

        if (account is null)
        {
            return false;
        }

        var permissions = await UserPermissionSession.GetOrLoadAsync(HttpContext, _userPermissionService, cancellationToken);
        var expectedCode = NormalizePermissionCode(PermissionCatalogService.WorkReportViewPermissionCode);
        return permissions.Any(permission =>
            string.Equals(permission.PermissionCode, PermissionCatalogService.WorkReportViewPermissionCode, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizePermissionCode(permission.PermissionCode), expectedCode, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<UserAccount?> GetCurrentAccountAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userId, out var parsedUserId)
            ? await _userAccountService.GetAccountByIdAsync(parsedUserId, cancellationToken)
            : null;
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

    private static bool IsAdminText(string? value)
    {
        var normalized = NormalizePermissionCode(value);
        return normalized.Contains("admin", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("quantri", StringComparison.OrdinalIgnoreCase);
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
}
