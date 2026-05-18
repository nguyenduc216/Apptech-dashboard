using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Claims;
using System.Text;

namespace ApptechDashboard.Services;

public interface ISidebarMenuService
{
    Task<IReadOnlyList<NavigationMenuItem>> GetMenuAsync(CancellationToken cancellationToken = default);
}

public sealed class SidebarMenuService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor,
    IUserPermissionService userPermissionService,
    ILogger<SidebarMenuService> logger) : ISidebarMenuService
{
    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly IUserPermissionService _userPermissionService = userPermissionService;
    private readonly ILogger<SidebarMenuService> _logger = logger;

    public async Task<IReadOnlyList<NavigationMenuItem>> GetMenuAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString) && !_sqlOptions.IsConfigured)
        {
            return [];
        }

        try
        {
            var items = await LoadItemsAsync(cancellationToken);
            var tree = BuildTree(items);
            return await FilterByPermissionAsync(tree, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load sidebar menu from TblChucNang.");
            return [];
        }
    }

    private async Task<List<NavigationMenuItem>> LoadItemsAsync(CancellationToken cancellationToken)
    {
        var items = new List<NavigationMenuItem>();
        var connectionString = !string.IsNullOrWhiteSpace(_connectionString)
            ? _connectionString
            : _sqlOptions.BuildConnectionString();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                ID,
                MaChucNang,
                MaChucNangCha,
                TenChucNang,
                MieuTa,
                ThuTuHienThi,
                URL,
                CssClass
            FROM TblChucNang
            WHERE TrangThaiSuDung = 1
            ORDER BY
                CASE WHEN MaChucNangCha IS NULL OR LTRIM(RTRIM(MaChucNangCha)) = '' THEN 0 ELSE 1 END,
                TRY_CONVERT(decimal(10,2), ThuTuHienThi),
                ThuTuHienThi,
                ID
            """;

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new NavigationMenuItem
            {
                Id = reader.GetInt32(reader.GetOrdinal("ID")),
                Code = GetNullableString(reader, "MaChucNang") ?? "",
                ParentCode = GetNullableString(reader, "MaChucNangCha"),
                Title = GetNullableString(reader, "TenChucNang") ?? "Chức năng",
                Description = GetNullableString(reader, "MieuTa"),
                SortOrder = GetNullableString(reader, "ThuTuHienThi"),
                Url = NormalizeUrl(GetNullableString(reader, "URL")),
                CssClass = GetNullableString(reader, "CssClass")
            });
        }

        await PersistMissingIconsAsync(connection, items, cancellationToken);

        return items;
    }

    private async Task PersistMissingIconsAsync(
        SqlConnection connection,
        IEnumerable<NavigationMenuItem> items,
        CancellationToken cancellationToken)
    {
        var pendingUpdates = items
            .Select(item => new
            {
                Item = item,
                item.Id,
                NormalizedIconClass = MenuIconMap.NormalizeCssClass(item.CssClass),
                ForcedIconClass = MenuIconMap.ResolveForced(item.Code, item.Title, item.Url)
            })
            .Select(item => new
            {
                item.Item,
                item.Id,
                IconClass = !string.IsNullOrWhiteSpace(item.ForcedIconClass)
                    ? item.ForcedIconClass
                    : string.IsNullOrWhiteSpace(item.NormalizedIconClass)
                        ? item.Item.IconClass
                        : null,
                item.NormalizedIconClass
            })
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.IconClass) &&
                !string.Equals(item.NormalizedIconClass, item.IconClass, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pendingUpdates.Count == 0)
        {
            return;
        }

        foreach (var update in pendingUpdates)
        {
            update.Item.CssClass = update.IconClass;
        }

        const string updateSql = """
            UPDATE TblChucNang
            SET CssClass = @CssClass
            WHERE ID = @Id
            """;

        try
        {
            foreach (var update in pendingUpdates)
            {
                await using var command = new SqlCommand(updateSql, connection);
                command.Parameters.AddWithValue("@Id", update.Id);
                command.Parameters.AddWithValue("@CssClass", update.IconClass);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to persist fallback icons into TblChucNang.CssClass. Menu will continue with runtime icon mapping.");
        }
    }

    private static IReadOnlyList<NavigationMenuItem> BuildTree(IEnumerable<NavigationMenuItem> items)
    {
        var orderedItems = items
            .OrderBy(item => GetSortValue(item.SortOrder))
            .ThenBy(item => item.Id)
            .ToList();

        var byCode = orderedItems
            .Where(item => !string.IsNullOrWhiteSpace(item.Code))
            .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var roots = new List<NavigationMenuItem>();

        foreach (var item in orderedItems)
        {
            if (!string.IsNullOrWhiteSpace(item.ParentCode) &&
                byCode.TryGetValue(item.ParentCode, out var parent))
            {
                parent.Children.Add(item);
            }
            else
            {
                roots.Add(item);
            }
        }

        foreach (var root in roots)
        {
            root.Children = root.Children
                .OrderBy(child => GetSortValue(child.SortOrder))
                .ThenBy(child => child.Id)
                .ToList();
        }

        return roots;
    }

    private async Task<IReadOnlyList<NavigationMenuItem>> FilterByPermissionAsync(
        IReadOnlyList<NavigationMenuItem> roots,
        CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated != true)
        {
            return [];
        }

        if (IsAdministrator(httpContext.User))
        {
            return roots;
        }

        var permissions = await UserPermissionSession.GetOrLoadAsync(httpContext, _userPermissionService, cancellationToken);
        if (permissions.Count == 0)
        {
            return [];
        }

        var allowedFunctionIds = permissions
            .Where(permission => permission.FunctionId > 0)
            .Select(permission => permission.FunctionId)
            .ToHashSet();
        var allowedFunctionCodes = permissions
            .Select(permission => NormalizeKey(permission.FunctionCode))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowedFunctionNames = permissions
            .Select(permission => NormalizeKey(permission.FunctionName))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return roots
            .Select(item => FilterNode(item, allowedFunctionIds, allowedFunctionCodes, allowedFunctionNames))
            .Where(static item => item is not null)
            .Cast<NavigationMenuItem>()
            .ToList();
    }

    private static NavigationMenuItem? FilterNode(
        NavigationMenuItem item,
        HashSet<int> allowedFunctionIds,
        HashSet<string> allowedFunctionCodes,
        HashSet<string> allowedFunctionNames)
    {
        var children = item.Children
            .Select(child => FilterNode(child, allowedFunctionIds, allowedFunctionCodes, allowedFunctionNames))
            .Where(static child => child is not null)
            .Cast<NavigationMenuItem>()
            .ToList();

        if (!IsAllowed(item, allowedFunctionIds, allowedFunctionCodes, allowedFunctionNames) && children.Count == 0)
        {
            return null;
        }

        return new NavigationMenuItem
        {
            Id = item.Id,
            Code = item.Code,
            ParentCode = item.ParentCode,
            Title = item.Title,
            Description = item.Description,
            Url = item.Url,
            CssClass = item.CssClass,
            SortOrder = item.SortOrder,
            Children = children
        };
    }

    private static bool IsAllowed(
        NavigationMenuItem item,
        HashSet<int> allowedFunctionIds,
        HashSet<string> allowedFunctionCodes,
        HashSet<string> allowedFunctionNames)
    {
        return allowedFunctionIds.Contains(item.Id) ||
            allowedFunctionCodes.Contains(NormalizeKey(item.Code)) ||
            allowedFunctionNames.Contains(NormalizeKey(item.Title));
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
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = NormalizeKey(value);
        return normalized.Contains("admin", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("quantri", StringComparison.OrdinalIgnoreCase);
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

            if (character == 'đ' || character == 'Đ')
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

    private static string? GetNullableString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal).Trim();
    }

    private static decimal GetSortValue(string? sortOrder)
    {
        return decimal.TryParse(sortOrder, out var value) ? value : decimal.MaxValue;
    }

    private static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return url.StartsWith('/') ? url : $"/{url}";
    }
}
