namespace ApptechDashboard.Models;

public sealed class ZaloManagementViewModel
{
    public ZaloSettingsFormModel Settings { get; set; } = new();
    public string? AppId { get; set; }
    public string? OaId { get; set; }
    public string? OAuthRedirectUri { get; set; }
    public string? PublicBaseUrl { get; set; }
    public string? ApiBaseUrl { get; set; }
    public string? WebhookUrl { get; set; }
    public bool HasAppSecret { get; set; }
    public bool HasOaSecretKey { get; set; }
    public DateTime? AccessTokenExpiresAtUtc { get; set; }
    public int? MinutesUntilExpiry { get; set; }
    public DateTime? LastRefreshSuccessAtUtc { get; set; }
    public string? LastError { get; set; }
    public IReadOnlyList<ZaloMessageLogItem> MessageLogs { get; set; } = [];
    public IReadOnlyList<ZaloWebhookEventItem> WebhookEvents { get; set; } = [];
}

public sealed class ZaloSettingsFormModel
{
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

public sealed class ZaloMessageLogItem
{
    public Guid Id { get; set; }
    public int? CustomerId { get; set; }
    public int? BookingId { get; set; }
    public string? ZaloUserId { get; set; }
    public string? PhoneNumber { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class ZaloWebhookEventItem
{
    public Guid Id { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string? OaId { get; set; }
    public bool IsSignatureValid { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
