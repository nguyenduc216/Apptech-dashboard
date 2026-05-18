using System.Globalization;
using System.Security.Claims;
using System.Text;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace ApptechDashboard.TagHelpers;

[HtmlTargetElement(Attributes = PermissionActionAttributeName)]
[HtmlTargetElement(Attributes = PermissionCodeAttributeName)]
public sealed class PermissionActionTagHelper(IUserPermissionService userPermissionService) : TagHelper
{
    private const string PermissionActionAttributeName = "asp-permission-action";
    private const string PermissionCodeAttributeName = "asp-permission-code";
    private const string PermissionFunctionAttributeName = "asp-permission-function";

    private static readonly IReadOnlyDictionary<string, string[]> PermissionAliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["insert"] = ["insert", "create", "add", "them", "themmoi"],
        ["update"] = ["update", "edit", "save", "sua", "capnhat", "luu"],
        ["delete"] = ["delete", "remove", "xoa"]
    };

    private readonly IUserPermissionService _userPermissionService = userPermissionService;

    [HtmlAttributeName(PermissionActionAttributeName)]
    public string? PermissionAction { get; set; }

    [HtmlAttributeName(PermissionCodeAttributeName)]
    public string? PermissionCode { get; set; }

    [HtmlAttributeName(PermissionFunctionAttributeName)]
    public string? PermissionFunction { get; set; }

    [ViewContextAttribute]
    public ViewContext ViewContext { get; set; } = default!;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.Attributes.RemoveAll(PermissionActionAttributeName);
        output.Attributes.RemoveAll(PermissionCodeAttributeName);
        output.Attributes.RemoveAll(PermissionFunctionAttributeName);

        if (await IsAllowedAsync())
        {
            return;
        }

        output.SuppressOutput();
    }

    private async Task<bool> IsAllowedAsync()
    {
        var httpContext = ViewContext.HttpContext;
        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (IsAdministrator(httpContext.User))
        {
            return true;
        }

        var permissions = await UserPermissionSession.GetOrLoadAsync(
            httpContext,
            _userPermissionService,
            httpContext.RequestAborted);

        if (permissions.Count == 0)
        {
            return false;
        }

        var permissionCode = NormalizeKey(PermissionCode);
        if (!string.IsNullOrWhiteSpace(permissionCode))
        {
            return permissions.Any(permission =>
                NormalizeKey(permission.PermissionCode).Equals(permissionCode, StringComparison.OrdinalIgnoreCase));
        }

        var actionAliases = GetActionAliases(PermissionAction);
        if (actionAliases.Count == 0)
        {
            return false;
        }

        var functionKeys = GetCurrentFunctionKeys();
        return permissions.Any(permission =>
            MatchesFunction(permission, functionKeys) &&
            MatchesAction(permission, actionAliases));
    }

    private IReadOnlyCollection<string> GetActionAliases(string? permissionAction)
    {
        var normalized = NormalizeKey(permissionAction);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        return PermissionAliases.TryGetValue(normalized, out var aliases)
            ? aliases
            : [normalized];
    }

    private IReadOnlyCollection<string> GetCurrentFunctionKeys()
    {
        var keys = new List<string>();

        AddKey(keys, PermissionFunction);
        AddKey(keys, ViewContext.RouteData.Values["controller"]?.ToString());

        var path = ViewContext.HttpContext.Request.Path.Value;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var firstSegment = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            AddKey(keys, firstSegment);
        }

        return keys;
    }

    private static bool MatchesFunction(UserPermission permission, IReadOnlyCollection<string> functionKeys)
    {
        if (functionKeys.Count == 0)
        {
            return false;
        }

        var permissionKeys = new[]
        {
            NormalizeKey(permission.FunctionCode),
            NormalizeKey(permission.FunctionName),
            NormalizeKey(permission.FunctionUrl)
        }.Where(static value => !string.IsNullOrWhiteSpace(value));

        return permissionKeys.Any(permissionKey =>
            functionKeys.Any(functionKey =>
                permissionKey.Equals(functionKey, StringComparison.OrdinalIgnoreCase) ||
                permissionKey.Contains(functionKey, StringComparison.OrdinalIgnoreCase) ||
                functionKey.Contains(permissionKey, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool MatchesAction(UserPermission permission, IReadOnlyCollection<string> actionAliases)
    {
        var permissionText = $"{NormalizeKey(permission.PermissionCode)} {NormalizeKey(permission.PermissionName)}";
        return actionAliases.Any(alias => permissionText.Contains(alias, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAdministrator(ClaimsPrincipal user)
    {
        return user.IsInRole("Administrator") ||
            IsAdminText(user.Identity?.Name) ||
            IsAdminText(user.FindFirstValue(ClaimTypes.Name)) ||
            IsAdminText(user.FindFirstValue("role_label")) ||
            IsAdminText(user.FindFirstValue("group_name"));
    }

    private static bool IsAdminText(string? value)
    {
        var normalized = NormalizeKey(value);
        return normalized.Contains("admin", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("quantri", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddKey(List<string> keys, string? value)
    {
        var normalized = NormalizeKey(value);
        if (!string.IsNullOrWhiteSpace(normalized) &&
            !keys.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            keys.Add(normalized);
        }
    }

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
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
