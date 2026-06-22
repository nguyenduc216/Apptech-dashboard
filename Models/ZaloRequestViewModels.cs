namespace ApptechDashboard.Models;

public sealed record ZaloRequestLinkResult(
    string Token,
    string QrUrl,
    string QrImageBase64,
    string Status,
    DateTime ExpiresAtUtc);

public sealed record ZaloRequestLinkStatus(
    string Status,
    int OpenCount,
    bool ZaloConnected,
    bool Rated,
    DateTime? LastOpenedAtUtc);

public sealed class ZaloRequestLandingView
{
    public string Token { get; set; } = string.Empty;
    public string UserExternalId { get; set; } = string.Empty;
    public string RequestCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public DateTime? ExecutionDate { get; set; }
    public string? OaId { get; set; }
    public bool IsRated { get; set; }
    public IReadOnlyList<ZaloRequestWorkItem> Works { get; set; } = [];
}

public sealed class ZaloRequestWorkItem
{
    public int? RequestWorkItemId { get; set; }
    public string WorkName { get; set; } = string.Empty;
    public string? Status { get; set; }
}

public sealed class ZaloRequestRatingSubmit
{
    public string? Token { get; set; }
    public int RatingScore { get; set; }
    public string? Note { get; set; }
    public string? CustomerComment { get; set; }
    public List<ZaloRequestRatingItemSubmit> Items { get; set; } = [];
}

public sealed class ZaloRequestRatingItemSubmit
{
    public int? RequestWorkItemId { get; set; }
    public string? WorkName { get; set; }
    public int RatingScore { get; set; }
    public string? Note { get; set; }
}

public sealed record ZaloRequestRatingResult(
    Guid RatingId,
    int RequestId,
    int? CustomerId,
    int RatingScore);
