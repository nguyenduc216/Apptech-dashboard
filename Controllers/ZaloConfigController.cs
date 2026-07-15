using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/zalo/config")]
public sealed class ZaloConfigController(IZaloSettingsService settingsService) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status()
    {
        var options = settingsService.Current;
        var publicBaseUrl = options.PublicBaseUrl?.TrimEnd('/');
        var webhookUrl = !string.IsNullOrWhiteSpace(options.WebhookUrl)
            ? options.WebhookUrl
            : string.IsNullOrWhiteSpace(publicBaseUrl) ? null : $"{publicBaseUrl}/api/zalo/webhook";

        return Ok(new
        {
            appIdConfigured = IsConfigured(options.AppId),
            appSecretConfigured = IsConfigured(options.AppSecret),
            oaIdConfigured = IsConfigured(options.OaId),
            oaSecretKeyConfigured = IsConfigured(options.OaSecretKey),
            enableSignatureValidation = options.EnableSignatureValidation,
            webhookSignatureCanValidate = !options.EnableSignatureValidation || IsConfigured(options.OaSecretKey),
            source = settingsService.Source,
            oauthRedirectUri = options.OAuthRedirectUri,
            webhookUrl
        });
    }

    private static bool IsConfigured(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Trim().StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
}
