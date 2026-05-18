using System.Security.Claims;
using System.Text.Json;
using ApptechDashboard.Models;

namespace ApptechDashboard.Services;

public static class UserPermissionSession
{
    public const string SessionKey = "CurrentUserPermissions";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<IReadOnlyList<UserPermission>> GetOrLoadAsync(
        HttpContext httpContext,
        IUserPermissionService permissionService,
        CancellationToken cancellationToken = default)
    {
        var permissions = Get(httpContext);
        if (permissions.Count > 0)
        {
            return permissions;
        }

        if (!Guid.TryParse(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
        {
            return [];
        }

        permissions = await permissionService.GetPermissionsAsync(accountId, cancellationToken);
        Set(httpContext, permissions);
        return permissions;
    }

    public static IReadOnlyList<UserPermission> Get(HttpContext httpContext)
    {
        var rawValue = httpContext.Session.GetString(SessionKey);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<UserPermission>>(rawValue, JsonOptions) ?? [];
        }
        catch
        {
            httpContext.Session.Remove(SessionKey);
            return [];
        }
    }

    public static void Set(HttpContext httpContext, IReadOnlyList<UserPermission> permissions)
    {
        httpContext.Session.SetString(SessionKey, JsonSerializer.Serialize(permissions, JsonOptions));
    }

    public static void Clear(HttpContext httpContext)
    {
        httpContext.Session.Remove(SessionKey);
    }
}
