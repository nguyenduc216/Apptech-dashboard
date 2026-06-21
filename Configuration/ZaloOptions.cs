namespace ApptechDashboard.Configuration;

public sealed class ZaloOptions
{
    public const string SectionName = "Zalo";

    public string? AppId { get; set; }
    public string? AppSecret { get; set; }
    public string? OaId { get; set; }
    public string? OaSecretKey { get; set; }
    public string? OAuthRedirectUri { get; set; }
    public string ApiBaseUrl { get; set; } = "https://openapi.zalo.me";
    public string OAuthBaseUrl { get; set; } = "https://oauth.zaloapp.com";
    public string? PublicBaseUrl { get; set; }
    public int RefreshBeforeExpiryMinutes { get; set; } = 120;
    public int AccessTokenLifetimeHours { get; set; } = 25;
    public string TextMessageEndpoint { get; set; } = "/v3.0/oa/message/cs";
    public string TokenEndpoint { get; set; } = "/v4/oa/access_token";
    public string OAuthAuthorizePath { get; set; } = "/v4/oa/permission";
    public bool EnableSignatureValidation { get; set; } = true;
}
