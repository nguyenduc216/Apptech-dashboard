using ApptechDashboard.Configuration;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
[Route("api/zalo/oauth")]
public sealed class ZaloOAuthController(
    IZaloAuthService zaloAuthService,
    IZaloSettingsService settingsService) : Controller
{
    [HttpGet("start")]
    public IActionResult Start()
    {
        var options = settingsService.Current;
        if (!IsConfigured(options.AppId) ||
            !IsConfigured(options.AppSecret) ||
            string.IsNullOrWhiteSpace(options.OAuthRedirectUri))
        {
            TempData["StatusMessage"] = "Thieu Zalo App ID, App Secret hoac OAuth Callback URL. Vui long bo sung cau hinh.";
            TempData["StatusType"] = "error";
            return Redirect("/admin/zalo-settings");
        }

        var authorizeBase = string.IsNullOrWhiteSpace(options.OAuthBaseUrl)
            ? "https://oauth.zaloapp.com"
            : options.OAuthBaseUrl.TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(options.OAuthAuthorizePath)
            ? "/v4/oa/permission"
            : options.OAuthAuthorizePath.StartsWith('/') ? options.OAuthAuthorizePath : "/" + options.OAuthAuthorizePath;
        var state = Guid.NewGuid().ToString("N");
        HttpContext.Session.SetString("zalo_oauth_state", state);

        var url = $"{authorizeBase}{path}?app_id={Uri.EscapeDataString(options.AppId!)}&redirect_uri={Uri.EscapeDataString(options.OAuthRedirectUri!)}&state={Uri.EscapeDataString(state)}";
        return Redirect(url);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, CancellationToken cancellationToken)
    {
        var expectedState = HttpContext.Session.GetString("zalo_oauth_state");
        if (!string.IsNullOrWhiteSpace(expectedState) &&
            !string.Equals(expectedState, state, StringComparison.Ordinal))
        {
            return BadRequest(new { message = "Invalid OAuth state." });
        }

        var result = await zaloAuthService.ExchangeAuthorizationCodeAsync(code ?? string.Empty, cancellationToken);
        if (!result.Succeeded &&
            result.Message.Contains("Missing Zalo AppId/AppSecret", StringComparison.OrdinalIgnoreCase))
        {
            TempData["StatusMessage"] = "Thieu Zalo App ID hoac App Secret. Vui long bo sung cau hinh.";
            TempData["StatusType"] = "error";
            return Redirect("/admin/zalo-settings");
        }

        return result.Succeeded
            ? Redirect("/admin/zalo-settings")
            : BadRequest(new { message = result.Message });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        try
        {
            await zaloAuthService.ForceRefreshTokenAsync(cancellationToken);
            return Ok(new { message = "Zalo token refreshed." });
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("Missing Zalo AppId/AppSecret", StringComparison.OrdinalIgnoreCase))
        {
            TempData["StatusMessage"] = "Thieu Zalo App ID hoac App Secret. Vui long bo sung cau hinh.";
            TempData["StatusType"] = "error";
            return Redirect("/admin/zalo-settings");
        }
    }

    private static bool IsConfigured(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Trim().StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
}
