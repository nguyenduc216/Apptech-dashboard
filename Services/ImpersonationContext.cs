using System.Security.Claims;

namespace ApptechDashboard.Services;

public static class ImpersonationContext
{
    public const string ActorAccountIdClaim = "impersonator_account_id";
    public const string ActorUserNameClaim = "impersonator_username";
    public const string ActorDisplayNameClaim = "impersonator_display_name";
    public const string StartedAtClaim = "impersonation_started_at";

    public static bool IsImpersonating(this ClaimsPrincipal user) =>
        !string.IsNullOrWhiteSpace(user.FindFirstValue(ActorAccountIdClaim));

    public static string GetEffectiveUserName(this ClaimsPrincipal user) =>
        (user.FindFirstValue(ClaimTypes.Name) ??
         user.Identity?.Name ??
         user.FindFirstValue("display_name") ??
         "system").Trim();

    public static string GetAuditUserName(this ClaimsPrincipal user)
    {
        var effectiveUser = user.GetEffectiveUserName();
        var actorUser = user.FindFirstValue(ActorUserNameClaim)?.Trim();
        return string.IsNullOrWhiteSpace(actorUser)
            ? effectiveUser
            : $"{actorUser} [đăng nhập hộ: {effectiveUser}]";
    }
}
