using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Controllers;

[Authorize]
public sealed class ZaloController(
    IZaloAuthService zaloAuthService,
    IZaloSettingsService zaloSettingsService,
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<ZaloController> logger) : Controller
{
    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = await BuildModelAsync(cancellationToken);
        model.WebhookUrl = BuildUrl("/api/zalo/webhook");
        ViewData["Title"] = "Quan ly Zalo OA";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings(
        [Bind(Prefix = "Settings")] ZaloSettingsFormModel form,
        CancellationToken cancellationToken)
    {
        ValidateSettings(form);
        if (!ModelState.IsValid)
        {
            var model = await BuildModelAsync(cancellationToken);
            model.Settings = form;
            model.WebhookUrl = BuildUrl("/api/zalo/webhook");
            ViewData["Title"] = "Quan ly Zalo OA";
            return View("Index", model);
        }

        try
        {
            await zaloSettingsService.SaveAsync(
                new ZaloOptions
                {
                    AppId = form.AppId,
                    AppSecret = form.AppSecret,
                    OaId = form.OaId,
                    OaSecretKey = form.OaSecretKey,
                    OAuthRedirectUri = form.OAuthRedirectUri,
                    ApiBaseUrl = form.ApiBaseUrl,
                    OAuthBaseUrl = form.OAuthBaseUrl,
                    PublicBaseUrl = form.PublicBaseUrl,
                    RefreshBeforeExpiryMinutes = form.RefreshBeforeExpiryMinutes,
                    AccessTokenLifetimeHours = form.AccessTokenLifetimeHours,
                    TextMessageEndpoint = form.TextMessageEndpoint,
                    TokenEndpoint = form.TokenEndpoint,
                    OAuthAuthorizePath = form.OAuthAuthorizePath,
                    EnableSignatureValidation = form.EnableSignatureValidation
                },
                !string.IsNullOrWhiteSpace(form.AppSecret),
                !string.IsNullOrWhiteSpace(form.OaSecretKey),
                cancellationToken);
            TempData["StatusMessage"] = "Da luu cau hinh Zalo OA. Cau hinh moi duoc ap dung ngay.";
            TempData["StatusType"] = "success";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save Zalo settings.");
            TempData["StatusMessage"] = $"Khong the luu cau hinh Zalo: {ex.Message}";
            TempData["StatusType"] = "error";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshToken(CancellationToken cancellationToken)
    {
        try
        {
            await zaloAuthService.ForceRefreshTokenAsync(cancellationToken);
            TempData["StatusMessage"] = "Da refresh Zalo access token.";
            TempData["StatusType"] = "success";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh Zalo token from management page.");
            TempData["StatusMessage"] = ex.Message;
            TempData["StatusType"] = "error";
        }

        return Redirect("/admin/zalo-settings");
    }

    private async Task<ZaloManagementViewModel> BuildModelAsync(CancellationToken cancellationToken)
    {
        var options = zaloSettingsService.Current;
        var status = await zaloAuthService.GetStatusAsync(cancellationToken);
        return new ZaloManagementViewModel
        {
            Settings = new ZaloSettingsFormModel
            {
                AppId = options.AppId,
                OaId = options.OaId,
                OAuthRedirectUri = options.OAuthRedirectUri,
                ApiBaseUrl = options.ApiBaseUrl,
                OAuthBaseUrl = options.OAuthBaseUrl,
                PublicBaseUrl = options.PublicBaseUrl,
                RefreshBeforeExpiryMinutes = options.RefreshBeforeExpiryMinutes,
                AccessTokenLifetimeHours = options.AccessTokenLifetimeHours,
                TextMessageEndpoint = options.TextMessageEndpoint,
                TokenEndpoint = options.TokenEndpoint,
                OAuthAuthorizePath = options.OAuthAuthorizePath,
                EnableSignatureValidation = options.EnableSignatureValidation
            },
            AppId = options.AppId,
            OaId = options.OaId,
            OAuthRedirectUri = options.OAuthRedirectUri,
            PublicBaseUrl = options.PublicBaseUrl ?? configuration["APP_PUBLIC_BASE_URL"],
            ApiBaseUrl = options.ApiBaseUrl,
            HasAppSecret = !string.IsNullOrWhiteSpace(options.AppSecret),
            HasOaSecretKey = !string.IsNullOrWhiteSpace(options.OaSecretKey),
            AccessTokenExpiresAtUtc = status.AccessTokenExpiresAtUtc,
            MinutesUntilExpiry = status.MinutesUntilExpiry,
            LastRefreshSuccessAtUtc = status.LastRefreshSuccessAtUtc,
            LastError = status.LastError,
            MessageLogs = await LoadMessageLogsAsync(cancellationToken),
            WebhookEvents = await LoadWebhookEventsAsync(cancellationToken)
        };
    }

    private string BuildUrl(string path)
    {
        var baseUrl = zaloSettingsService.Current.PublicBaseUrl ?? configuration["APP_PUBLIC_BASE_URL"];
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            return baseUrl.TrimEnd('/') + path;
        }

        return $"{Request.Scheme}://{Request.Host}{path}";
    }

    private void ValidateSettings(ZaloSettingsFormModel form)
    {
        ValidateAbsoluteUrl(form.ApiBaseUrl, nameof(form.ApiBaseUrl), true);
        ValidateAbsoluteUrl(form.OAuthBaseUrl, nameof(form.OAuthBaseUrl), true);
        ValidateAbsoluteUrl(form.OAuthRedirectUri, nameof(form.OAuthRedirectUri), false);
        ValidateAbsoluteUrl(form.PublicBaseUrl, nameof(form.PublicBaseUrl), false);

        if (form.RefreshBeforeExpiryMinutes is < 1 or > 1440)
        {
            ModelState.AddModelError("Settings.RefreshBeforeExpiryMinutes", "Thoi gian refresh phai tu 1 den 1440 phut.");
        }

        if (form.AccessTokenLifetimeHours is < 1 or > 168)
        {
            ModelState.AddModelError("Settings.AccessTokenLifetimeHours", "Thoi han token phai tu 1 den 168 gio.");
        }
    }

    private void ValidateAbsoluteUrl(string? value, string propertyName, bool required)
    {
        var key = $"Settings.{propertyName}";
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                ModelState.AddModelError(key, "Thong tin nay la bat buoc.");
            }

            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ModelState.AddModelError(key, "Dia chi URL khong hop le.");
        }
    }

    private async Task<IReadOnlyList<ZaloMessageLogItem>> LoadMessageLogsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            if (!await TableExistsAsync(connection, "TblZaloMessageLogs", cancellationToken))
            {
                return [];
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TOP (20)
                    Id, CustomerId, BookingId, ZaloUserId, PhoneNumber, MessageType,
                    IsSuccess, ErrorMessage, CreatedAtUtc
                FROM [TblZaloMessageLogs]
                ORDER BY CreatedAtUtc DESC
                """;
            var items = new List<ZaloMessageLogItem>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new ZaloMessageLogItem
                {
                    Id = reader.GetGuid(reader.GetOrdinal("Id")),
                    CustomerId = reader["CustomerId"] == DBNull.Value ? null : Convert.ToInt32(reader["CustomerId"]),
                    BookingId = reader["BookingId"] == DBNull.Value ? null : Convert.ToInt32(reader["BookingId"]),
                    ZaloUserId = reader["ZaloUserId"]?.ToString(),
                    PhoneNumber = reader["PhoneNumber"]?.ToString(),
                    MessageType = reader["MessageType"]?.ToString() ?? string.Empty,
                    IsSuccess = Convert.ToBoolean(reader["IsSuccess"]),
                    ErrorMessage = reader["ErrorMessage"]?.ToString(),
                    CreatedAtUtc = Convert.ToDateTime(reader["CreatedAtUtc"])
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load Zalo message logs.");
            return [];
        }
    }

    private async Task<IReadOnlyList<ZaloWebhookEventItem>> LoadWebhookEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            if (!await TableExistsAsync(connection, "TblZaloWebhookEvents", cancellationToken))
            {
                return [];
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TOP (20)
                    Id, EventName, OaId, IsSignatureValid, ProcessedAtUtc, CreatedAtUtc
                FROM [TblZaloWebhookEvents]
                ORDER BY CreatedAtUtc DESC
                """;
            var items = new List<ZaloWebhookEventItem>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new ZaloWebhookEventItem
                {
                    Id = reader.GetGuid(reader.GetOrdinal("Id")),
                    EventName = reader["EventName"]?.ToString() ?? string.Empty,
                    OaId = reader["OaId"]?.ToString(),
                    IsSignatureValid = Convert.ToBoolean(reader["IsSignatureValid"]),
                    ProcessedAtUtc = reader["ProcessedAtUtc"] == DBNull.Value ? null : Convert.ToDateTime(reader["ProcessedAtUtc"]),
                    CreatedAtUtc = Convert.ToDateTime(reader["CreatedAtUtc"])
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load Zalo webhook events.");
            return [];
        }
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = !string.IsNullOrWhiteSpace(_connectionString)
            ? _connectionString
            : _sqlOptions.BuildConnectionString();

        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<bool> TableExistsAsync(SqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT OBJECT_ID(@TableName, 'U')";
        command.Parameters.Add(new SqlParameter("@TableName", $"dbo.{tableName}"));
        return await command.ExecuteScalarAsync(cancellationToken) is not null and not DBNull;
    }
}
