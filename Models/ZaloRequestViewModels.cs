namespace ApptechDashboard.Models;

public sealed record ZaloRequestLinkResult(
    string Token,
    string QrUrl,
    string QrImageBase64,
    string Status,
    DateTime ExpiresAtUtc,
    bool ZaloConnected,
    string? ZaloDisplayName,
    string? ZaloPhoneNumber,
    bool Rated,
    int? RatingScore,
    DateTime? RatingSubmittedAtUtc,
    string CustomerName,
    string? PhoneNumber);

public sealed record ZaloRequestLinkStatus(
    string Status,
    int OpenCount,
    bool ZaloConnected,
    bool Rated,
    DateTime? LastOpenedAtUtc,
    DateTime? ExpiresAtUtc,
    string? ZaloDisplayName,
    string? ZaloPhoneNumber,
    int? RatingScore,
    DateTime? RatingSubmittedAtUtc);

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
    public bool ZaloConnected { get; set; }
    public string? ZaloDisplayName { get; set; }
    public string? ZaloPhoneNumber { get; set; }
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

public sealed class CustomerZaloProfileInfo
{
    public bool Connected { get; set; }
    public int? CustomerId { get; set; }
    public string? ZaloUserId { get; set; }
    public string? ZaloDisplayName { get; set; }
    public string? ZaloAvatarUrl { get; set; }
    public string? ZaloPhoneNumber { get; set; }
    public bool? IsFollowingOa { get; set; }
    public DateTime? ConnectedAtUtc { get; set; }
    public DateTime? LastInteractionAtUtc { get; set; }
    public string? Source { get; set; }
}

public sealed class RequestRatingInfo
{
    public bool HasRating { get; set; }
    public Guid? RatingId { get; set; }
    public int? RequestId { get; set; }
    public int? CustomerId { get; set; }
    public int? RatingScore { get; set; }
    public string? Note { get; set; }
    public string? CustomerComment { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public string? Source { get; set; }
    public IReadOnlyList<RequestRatingItemInfo> Items { get; set; } = [];
}

public sealed class RequestRatingItemInfo
{
    public int? RequestWorkItemId { get; set; }
    public string WorkName { get; set; } = string.Empty;
    public int RatingScore { get; set; }
    public string? Note { get; set; }
}
