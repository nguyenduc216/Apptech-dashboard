using ApptechDashboard.Configuration;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Controllers;

[Authorize]
[Route("api/zalo/oauth")]
public sealed class ZaloOAuthController(
    IZaloAuthService zaloAuthService,
    IOptions<ZaloOptions> options) : Controller
{
    private readonly ZaloOptions _options = options.Value;

    [HttpGet("start")]
    public IActionResult Start()
    {
        if (string.IsNullOrWhiteSpace(_options.AppId) || string.IsNullOrWhiteSpace(_options.OAuthRedirectUri))
        {
            return BadRequest(new { message = "Missing Zalo AppId or OAuthRedirectUri." });
        }

        var authorizeBase = string.IsNullOrWhiteSpace(_options.OAuthBaseUrl)
            ? "https://oauth.zaloapp.com"
            : _options.OAuthBaseUrl.TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(_options.OAuthAuthorizePath)
            ? "/v4/oa/permission"
            : _options.OAuthAuthorizePath.StartsWith('/') ? _options.OAuthAuthorizePath : "/" + _options.OAuthAuthorizePath;
        var state = Guid.NewGuid().ToString("N");
        HttpContext.Session.SetString("zalo_oauth_state", state);

        var url = $"{authorizeBase}{path}?app_id={Uri.EscapeDataString(_options.AppId)}&redirect_uri={Uri.EscapeDataString(_options.OAuthRedirectUri)}&state={Uri.EscapeDataString(state)}";
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
        return result.Succeeded
            ? Ok(new { message = result.Message })
            : BadRequest(new { message = result.Message });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        await zaloAuthService.ForceRefreshTokenAsync(cancellationToken);
        return Ok(new { message = "Zalo token refreshed." });
    }
}
