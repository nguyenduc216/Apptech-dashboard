using System.Data;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApptechDashboard.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IZaloAuthService
{
    Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default);
    Task RefreshTokenIfNeededAsync(CancellationToken cancellationToken = default);
    Task ForceRefreshTokenAsync(CancellationToken cancellationToken = default);
    Task<ZaloSendResult> ExchangeAuthorizationCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<ZaloTokenStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

public interface IZaloMessageService
{
    Task<ZaloSendResult> SendBookingConfirmationAsync(int yeuCauId, CancellationToken cancellationToken = default);
    Task<ZaloSendResult> SendBookingReminderAsync(int yeuCauId, CancellationToken cancellationToken = default);
    Task<ZaloSendResult> SendRatingRequestAsync(int yeuCauId, CancellationToken cancellationToken = default);
    Task<ZaloSendResult> SendRatingResultMessageAsync(
        int yeuCauId,
        Guid ratingId,
        int ratingScore,
        CancellationToken cancellationToken = default);
}

public interface ICustomerLinkService
{
    Task<ZaloCustomerLinkResult> CreateLinkAsync(
        int customerId,
        int? requestId,
        string purpose,
        int expiresInDays,
        CancellationToken cancellationToken = default);

    Task<ZaloCustomerLinkView?> RegisterClickAsync(string token, CancellationToken cancellationToken = default);
}

public interface IZaloWebhookService
{
    Task<ZaloWebhookProcessResult> ProcessAsync(
        string rawJson,
        string? signature,
        CancellationToken cancellationToken = default);
}

public sealed class ZaloTokenRefreshWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ZaloTokenRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var authService = scope.ServiceProvider.GetRequiredService<IZaloAuthService>();
                await authService.RefreshTokenIfNeededAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning("Zalo token refresh skipped: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Zalo token refresh worker failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }
}

public sealed class ZaloIntegrationService(
    IOptions<SqlServerOptions> sqlOptions,
    IZaloSettingsService zaloSettings,
    IZaloRequestService zaloRequestService,
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<ZaloIntegrationService> logger)
    : IZaloAuthService, IZaloMessageService, ICustomerLinkService, IZaloWebhookService
{
    private const string TokenTableName = "TblZaloTokens";
    private const string LinkTableName = "TblCustomerInteractionLinks";
    private const string MappingTableName = "TblZaloUserMappings";
    private const string WebhookTableName = "TblZaloWebhookEvents";
    private const string MessageLogTableName = "TblZaloMessageLogs";
    private const string RatingTableName = "TblZaloRatings";
    private const string CustomerTableName = "TblKhachHang";
    private const string RequestTableName = "TblYeuCau";
    private const string LocationTableName = "TblKhachHangDiaDiem";
    private const string ConnectAcknowledgementMessage = "C\u1EA3m \u01A1n anh/ch\u1ECB \u0111\u00E3 k\u1EBFt n\u1ED1i t\u1EDBi zalo OA. Ch\u00FAng t\u00F4i \u0111\u00E3 ghi nh\u1EADn th\u00F4ng tin v\u00E0o h\u1EC7 th\u1ED1ng AppTech \u0111\u1EC3 h\u1ED7 tr\u1EE3 anh/ch\u1ECB t\u1ED1t h\u01A1n trong th\u1EDDi gian t\u1EDBi";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private ZaloOptions _zaloOptions => zaloSettings.Current;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<ZaloIntegrationService> _logger = logger;

    public async Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await RefreshTokenIfNeededAsync(cancellationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1) AccessToken
            FROM [{TokenTableName}]
            WHERE (@OaId IS NULL OR OaId = @OaId)
            ORDER BY CreatedAtUtc DESC
            """;
        command.Parameters.Add(new SqlParameter("@OaId", SqlDbType.NVarChar, 100) { Value = ToDbValue(_zaloOptions.OaId) });

        var token = (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Zalo token not found. Please connect OA first.");
        }

        return token;
    }

    public async Task RefreshTokenIfNeededAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        var token = await LoadLatestTokenAsync(connection, cancellationToken);
        if (token is null)
        {
            return;
        }

        var refreshBefore = TimeSpan.FromMinutes(Math.Max(1, _zaloOptions.RefreshBeforeExpiryMinutes));
        if (token.AccessTokenExpiresAtUtc > DateTime.UtcNow.Add(refreshBefore))
        {
            return;
        }

        if (IsInvalidRefreshTokenError(token.LastError))
        {
            _logger.LogWarning("Zalo token refresh is paused because the stored refresh token is invalid. Reconnect the Zalo OA to get a new token.");
            return;
        }

        await ForceRefreshTokenAsync(cancellationToken);
    }

    public async Task ForceRefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        var token = await LoadLatestTokenAsync(connection, cancellationToken)
            ?? throw new InvalidOperationException("Zalo token not found. Please connect OA first.");

        if (string.IsNullOrWhiteSpace(_zaloOptions.AppId) || string.IsNullOrWhiteSpace(_zaloOptions.AppSecret))
        {
            await SaveTokenErrorAsync(connection, token.Id, "Missing Zalo AppId/AppSecret.", cancellationToken);
            throw new InvalidOperationException("Missing Zalo AppId/AppSecret.");
        }

        Exception? lastException = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("ZaloOA");
                using var request = new HttpRequestMessage(HttpMethod.Post, BuildOAuthUri(_zaloOptions.TokenEndpoint));
                request.Headers.Add("secret_key", _zaloOptions.AppSecret);
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["refresh_token"] = token.RefreshToken,
                    ["app_id"] = _zaloOptions.AppId,
                    ["grant_type"] = "refresh_token"
                });

                using var response = await client.SendAsync(request, cancellationToken);
                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"Zalo refresh failed: {(int)response.StatusCode} {responseJson}");
                }

                using var document = JsonDocument.Parse(responseJson);
                var root = document.RootElement;
                var accessToken = GetJsonString(root, "access_token");
                var refreshToken = GetJsonString(root, "refresh_token") ?? token.RefreshToken;
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    var errorMessage = BuildZaloTokenErrorMessage(root, responseJson);
                    throw new InvalidOperationException(errorMessage);
                }

                var expiresAt = DateTime.UtcNow.AddHours(Math.Max(1, _zaloOptions.AccessTokenLifetimeHours));
                await UpdateTokenAsync(connection, token.Id, accessToken, refreshToken, expiresAt, responseJson, cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < 3)
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        await SaveTokenErrorAsync(connection, token.Id, lastException?.Message ?? "Unknown refresh error.", cancellationToken);
        throw lastException ?? new InvalidOperationException("Zalo refresh failed.");
    }

    public async Task<ZaloSendResult> ExchangeAuthorizationCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ZaloSendResult.Fail("Missing authorization code.");
        }

        if (string.IsNullOrWhiteSpace(_zaloOptions.AppId) || string.IsNullOrWhiteSpace(_zaloOptions.AppSecret))
        {
            return ZaloSendResult.Fail("Missing Zalo AppId/AppSecret.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient("ZaloOA");
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildOAuthUri(_zaloOptions.TokenEndpoint));
            request.Headers.Add("secret_key", _zaloOptions.AppSecret);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code.Trim(),
                ["app_id"] = _zaloOptions.AppId,
                ["grant_type"] = "authorization_code"
            });

            using var response = await client.SendAsync(request, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ZaloSendResult.Fail($"Zalo OAuth failed: {(int)response.StatusCode}");
            }

            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;
            var accessToken = GetJsonString(root, "access_token");
            var refreshToken = GetJsonString(root, "refresh_token");
            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken))
            {
                return ZaloSendResult.Fail("Zalo OAuth response missing token.");
            }

            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO [{TokenTableName}] (
                    Id, OaId, AccessToken, RefreshToken, AccessTokenExpiresAtUtc,
                    RefreshTokenUpdatedAtUtc, LastRefreshSuccessAtUtc, CreatedAtUtc, UpdatedAtUtc
                )
                VALUES (
                    NEWID(), @OaId, @AccessToken, @RefreshToken, @AccessTokenExpiresAtUtc,
                    SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME()
                );
                """;
            command.Parameters.Add(new SqlParameter("@OaId", SqlDbType.NVarChar, 100) { Value = ToDbValue(_zaloOptions.OaId) });
            command.Parameters.Add(new SqlParameter("@AccessToken", SqlDbType.NVarChar, -1) { Value = accessToken });
            command.Parameters.Add(new SqlParameter("@RefreshToken", SqlDbType.NVarChar, -1) { Value = refreshToken });
            command.Parameters.Add(new SqlParameter("@AccessTokenExpiresAtUtc", SqlDbType.DateTime2) { Value = DateTime.UtcNow.AddHours(Math.Max(1, _zaloOptions.AccessTokenLifetimeHours)) });
            await command.ExecuteNonQueryAsync(cancellationToken);
            return ZaloSendResult.Ok("Connected Zalo OA successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to exchange Zalo OAuth authorization code.");
            return ZaloSendResult.Fail(ex.Message);
        }
    }

    public async Task<ZaloTokenStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        var token = await LoadLatestTokenAsync(connection, cancellationToken);
        if (token is null)
        {
            return new ZaloTokenStatus(_zaloOptions.OaId, null, null, null, "Not connected");
        }

        var minutes = (int)Math.Round((token.AccessTokenExpiresAtUtc - DateTime.UtcNow).TotalMinutes);
        return new ZaloTokenStatus(token.OaId, token.AccessTokenExpiresAtUtc, minutes, token.LastRefreshSuccessAtUtc, token.LastError);
    }

    public async Task<ZaloSendResult> SendBookingConfirmationAsync(int yeuCauId, CancellationToken cancellationToken = default)
    {
        var booking = await LoadBookingAsync(yeuCauId, cancellationToken);
        if (booking is null)
        {
            return ZaloSendResult.Fail("Khong tim thay phieu yeu cau.");
        }

        var link = await CreateLinkAsync(booking.CustomerId, yeuCauId, "BookingReminder", 30, cancellationToken);
        var message = $"Xin chao {booking.CustomerName}, lich lam viec cua anh/chi voi cong ty duoc hen vao {booking.WorkTimeText}. Dia diem: {booking.Address}. Xem/ket noi Zalo: {link.Link}";
        return await SendMessageAsync(booking, message, "BookingReminder", cancellationToken);
    }

    public async Task<ZaloSendResult> SendBookingReminderAsync(int yeuCauId, CancellationToken cancellationToken = default)
    {
        var booking = await LoadBookingAsync(yeuCauId, cancellationToken);
        if (booking is null)
        {
            return ZaloSendResult.Fail("Khong tim thay phieu yeu cau.");
        }

        var message = $"Cong ty xin nhac anh/chi ve lich lam viec vao {booking.WorkTimeText}. Neu can thay doi lich, vui long phan hoi tin nhan nay.";
        return await SendMessageAsync(booking, message, "BookingReminder", cancellationToken);
    }

    public async Task<ZaloSendResult> SendRatingRequestAsync(int yeuCauId, CancellationToken cancellationToken = default)
    {
        var booking = await LoadBookingAsync(yeuCauId, cancellationToken);
        if (booking is null)
        {
            return ZaloSendResult.Fail("Khong tim thay phieu yeu cau.");
        }

        var link = await CreateLinkAsync(booking.CustomerId, yeuCauId, "Rating", 30, cancellationToken);
        var message = $"Cam on anh/chi da lam viec voi cong ty. Vui long danh gia trai nghiem tai day: {BuildPublicUrl($"/rating/{link.Token}")}";
        return await SendMessageAsync(booking, message, "RatingRequest", cancellationToken);
    }

    public async Task<ZaloSendResult> SendRatingResultMessageAsync(
        int yeuCauId,
        Guid ratingId,
        int ratingScore,
        CancellationToken cancellationToken = default)
    {
        var booking = await LoadBookingAsync(yeuCauId, cancellationToken);
        if (booking is null)
        {
            return ZaloSendResult.Fail("Không tìm thấy phiếu yêu cầu.");
        }

        var message = $"Cảm ơn anh/chị đã đánh giá chất lượng công việc. Phiếu yêu cầu: {booking.RequestId}. Kết quả đánh giá: {ratingScore}/5 sao. Công ty đã ghi nhận phản hồi của anh/chị.";
        return await SendMessageAsync(
            booking,
            message,
            "RatingResult",
            cancellationToken,
            requireZaloUserId: true);
    }

    public async Task<ZaloCustomerLinkResult> CreateLinkAsync(
        int customerId,
        int? requestId,
        string purpose,
        int expiresInDays,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        var token = SecureToken();
        var externalId = $"kh-{customerId}-yc-{requestId?.ToString() ?? "0"}-{SecureToken(12)}";
        var link = BuildPublicUrl($"/zalo/connect/{token}");
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO [{LinkTableName}] (
                CustomerId, BookingId, Token, UserExternalId, Purpose, TargetUrl,
                ClickCount, ExpiresAtUtc, IsUsed, CreatedAtUtc
            )
            VALUES (
                @CustomerId, @BookingId, @Token, @UserExternalId, @Purpose, @TargetUrl,
                0, @ExpiresAtUtc, 0, SYSUTCDATETIME()
            );
            """;
        command.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.Int) { Value = customerId });
        command.Parameters.Add(new SqlParameter("@BookingId", SqlDbType.Int) { Value = requestId.HasValue ? requestId.Value : DBNull.Value });
        command.Parameters.Add(new SqlParameter("@Token", SqlDbType.NVarChar, 200) { Value = token });
        command.Parameters.Add(new SqlParameter("@UserExternalId", SqlDbType.NVarChar, 200) { Value = externalId });
        command.Parameters.Add(new SqlParameter("@Purpose", SqlDbType.NVarChar, 50) { Value = NormalizePurpose(purpose) });
        command.Parameters.Add(new SqlParameter("@TargetUrl", SqlDbType.NVarChar, 1000) { Value = link });
        command.Parameters.Add(new SqlParameter("@ExpiresAtUtc", SqlDbType.DateTime2) { Value = DateTime.UtcNow.AddDays(Math.Clamp(expiresInDays, 1, 365)) });
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new ZaloCustomerLinkResult(link, token, externalId);
    }

    public async Task<ZaloCustomerLinkView?> RegisterClickAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var link = await LoadLinkAsync(connection, transaction, token.Trim(), cancellationToken);
        if (link is null || (link.ExpiresAtUtc.HasValue && link.ExpiresAtUtc.Value < DateTime.UtcNow))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE [{LinkTableName}]
            SET ClickCount = ClickCount + 1,
                FirstClickedAtUtc = ISNULL(FirstClickedAtUtc, SYSUTCDATETIME()),
                LastClickedAtUtc = SYSUTCDATETIME()
            WHERE Token = @Token
            """;
        command.Parameters.Add(new SqlParameter("@Token", SqlDbType.NVarChar, 200) { Value = token.Trim() });
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return link;
    }

    public async Task<ZaloWebhookProcessResult> ProcessAsync(
        string rawJson,
        string? signature,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);

        var validSignature = IsWebhookSignatureValid(rawJson, signature);
        var eventName = "unknown";
        string? userExternalId = null;
        string? zaloUserId = null;
        string? messageText = null;
        string? displayName = null;
        string? avatarUrl = null;
        string? phoneNumber = null;
        string? oaId = _zaloOptions.OaId;
        string? appId = _zaloOptions.AppId;

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;
            eventName = GetJsonString(root, "event_name") ?? GetJsonString(root, "event") ?? eventName;
            oaId = GetJsonString(root, "oa_id") ?? GetJsonString(root, "oaId") ?? oaId;
            appId = GetJsonString(root, "app_id") ?? GetJsonString(root, "appId") ?? appId;
            userExternalId = FirstNotEmpty(FindJsonString(root, "user_external_id"), FindJsonString(root, "userExternalId"));
            zaloUserId = FindZaloUserIdFromWebhook(root);
            messageText = FindIncomingMessageText(root);
            displayName = FirstNotEmpty(
                FindNestedString(root, "sender", "display_name", "name", "user_name"),
                FindJsonString(root, "display_name"),
                FindJsonString(root, "user_name"));
            avatarUrl = FirstNotEmpty(
                FindNestedString(root, "sender", "avatar", "avatar_url"),
                FindJsonString(root, "avatar_url"),
                FindJsonString(root, "avatar"));
            phoneNumber = FirstNotEmpty(FindJsonString(root, "phone"), FindJsonString(root, "phone_number"));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid Zalo webhook json.");
        }

        var webhookId = await SaveWebhookEventAsync(connection, eventName, oaId, appId, rawJson, signature, validSignature, cancellationToken);
        if (!validSignature && _zaloOptions.EnableSignatureValidation)
        {
            return new ZaloWebhookProcessResult(false, false, "Invalid signature");
        }

        if (string.IsNullOrWhiteSpace(userExternalId) && !string.IsNullOrWhiteSpace(messageText))
        {
            userExternalId = await ResolveExternalIdFromMessageAsync(connection, messageText, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(userExternalId) && !string.IsNullOrWhiteSpace(zaloUserId))
        {
            userExternalId = await ResolveRecentExternalIdAsync(connection, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(userExternalId) && !string.IsNullOrWhiteSpace(zaloUserId))
        {
            var source = eventName.Contains("widget", StringComparison.OrdinalIgnoreCase)
                ? "WidgetInteractionAccepted"
                : eventName.Contains("follow", StringComparison.OrdinalIgnoreCase)
                    ? "FollowOA"
                    : "Message";
            var profile = await TryLoadZaloUserProfileAsync(zaloUserId, cancellationToken);
            displayName = FirstNotEmpty(profile.DisplayName, displayName);
            avatarUrl = FirstNotEmpty(profile.AvatarUrl, avatarUrl);
            phoneNumber = FirstNotEmpty(profile.PhoneNumber, phoneNumber);

            await UpsertMappingFromExternalIdAsync(connection, userExternalId, zaloUserId, oaId, source, cancellationToken);
            await zaloRequestService.MapWebhookUserAsync(
                userExternalId,
                zaloUserId,
                oaId,
                source,
                displayName,
                avatarUrl,
                phoneNumber,
                cancellationToken);

            var context = await ResolveCustomerContextByExternalIdAsync(connection, userExternalId, cancellationToken);
            if (context is not null)
            {
                await SendDirectZaloMessageAsync(
                    connection,
                    context.CustomerId,
                    context.BookingId,
                    zaloUserId,
                    ConnectAcknowledgementMessage,
                    "ZaloConnectAcknowledgement",
                    cancellationToken);
            }
        }
        else if (!string.IsNullOrWhiteSpace(zaloUserId))
        {
            _logger.LogInformation(
                "Zalo webhook {EventName} from {ZaloUserId} had no customer link context, so no customer was mapped.",
                eventName,
                zaloUserId);
        }

        await MarkWebhookProcessedAsync(connection, webhookId, cancellationToken);
        return new ZaloWebhookProcessResult(true, true, eventName);
    }

    private async Task<string?> ResolveExternalIdFromMessageAsync(SqlConnection connection, string messageText, CancellationToken cancellationToken)
    {
        foreach (var candidate in ExtractMessageCandidates(messageText))
        {
            var externalId = await FindExternalIdByCandidateAsync(connection, candidate, cancellationToken);
            if (!string.IsNullOrWhiteSpace(externalId))
            {
                return externalId;
            }
        }

        return null;
    }

    private async Task<string?> FindExternalIdByCandidateAsync(SqlConnection connection, string candidate, CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT TOP (1) UserExternalId
                FROM [{LinkTableName}]
                WHERE UserExternalId = @Candidate OR Token = @Candidate
                ORDER BY CreatedAtUtc DESC
                """;
            command.Parameters.Add(new SqlParameter("@Candidate", SqlDbType.NVarChar, 200) { Value = candidate });
            var externalId = (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
            if (!string.IsNullOrWhiteSpace(externalId))
            {
                return externalId;
            }
        }

        if (!await TableExistsAsync(connection, "TblZaloRequestLinks", cancellationToken))
        {
            return null;
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT TOP (1) UserExternalId
                FROM [TblZaloRequestLinks]
                WHERE UserExternalId = @Candidate OR Token = @Candidate
                ORDER BY CreatedAtUtc DESC
                """;
            command.Parameters.Add(new SqlParameter("@Candidate", SqlDbType.NVarChar, 200) { Value = candidate });
            return (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
        }
    }

    private async Task<string?> ResolveRecentExternalIdAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var candidates = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT TOP (2) UserExternalId
                FROM [{LinkTableName}]
                WHERE LastClickedAtUtc >= DATEADD(MINUTE, -20, SYSUTCDATETIME())
                  AND IsUsed = 0
                  AND NULLIF(LTRIM(RTRIM(UserExternalId)), N'') IS NOT NULL
                ORDER BY LastClickedAtUtc DESC
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(reader.GetString(0));
            }
        }

        if (await TableExistsAsync(connection, "TblZaloRequestLinks", cancellationToken))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT TOP (2) UserExternalId
                FROM [TblZaloRequestLinks]
                WHERE LastOpenedAtUtc >= DATEADD(MINUTE, -20, SYSUTCDATETIME())
                  AND Status IN (N'Created', N'Opened')
                  AND NULLIF(LTRIM(RTRIM(UserExternalId)), N'') IS NOT NULL
                ORDER BY LastOpenedAtUtc DESC
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(reader.GetString(0));
            }
        }

        var uniqueCandidates = candidates.Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        if (uniqueCandidates.Length == 1)
        {
            return uniqueCandidates[0];
        }

        if (uniqueCandidates.Length > 1)
        {
            _logger.LogWarning("Zalo webhook without user_external_id matched multiple recently opened links; skipping automatic customer mapping.");
        }

        return null;
    }

    private async Task<ZaloWebhookProfile> TryLoadZaloUserProfileAsync(string zaloUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(zaloUserId))
        {
            return ZaloWebhookProfile.Empty;
        }

        try
        {
            var accessToken = await GetValidAccessTokenAsync(cancellationToken);
            var data = Uri.EscapeDataString(JsonSerializer.Serialize(new { user_id = zaloUserId.Trim() }, JsonOptions));
            var client = _httpClientFactory.CreateClient("ZaloOA");
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildApiUri($"/v3.0/oa/user/detail?data={data}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Zalo user profile lookup failed for {ZaloUserId}: {StatusCode} {Response}", zaloUserId, (int)response.StatusCode, responseJson);
                return ZaloWebhookProfile.Empty;
            }

            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;
            return new ZaloWebhookProfile(
                FirstNotEmpty(FindJsonString(root, "display_name"), FindJsonString(root, "name"), FindJsonString(root, "user_name")),
                FirstNotEmpty(FindJsonString(root, "avatar"), FindJsonString(root, "avatar_url")),
                FirstNotEmpty(FindJsonString(root, "phone"), FindJsonString(root, "phone_number")));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load Zalo user profile for {ZaloUserId}.", zaloUserId);
            return ZaloWebhookProfile.Empty;
        }
    }

    private async Task<ZaloCustomerContext?> ResolveCustomerContextByExternalIdAsync(SqlConnection connection, string userExternalId, CancellationToken cancellationToken)
    {
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT TOP (1) CustomerId, BookingId
                FROM [{LinkTableName}]
                WHERE UserExternalId = @UserExternalId
                ORDER BY CreatedAtUtc DESC
                """;
            command.Parameters.Add(new SqlParameter("@UserExternalId", SqlDbType.NVarChar, 200) { Value = userExternalId.Trim() });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new ZaloCustomerContext(
                    Convert.ToInt32(reader["CustomerId"]),
                    reader["BookingId"] == DBNull.Value ? null : Convert.ToInt32(reader["BookingId"]));
            }
        }

        if (!await TableExistsAsync(connection, "TblZaloRequestLinks", cancellationToken))
        {
            return null;
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT TOP (1) CustomerId, RequestId
                FROM [TblZaloRequestLinks]
                WHERE UserExternalId = @UserExternalId
                ORDER BY CreatedAtUtc DESC
                """;
            command.Parameters.Add(new SqlParameter("@UserExternalId", SqlDbType.NVarChar, 200) { Value = userExternalId.Trim() });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken) && reader["CustomerId"] != DBNull.Value)
            {
                return new ZaloCustomerContext(
                    Convert.ToInt32(reader["CustomerId"]),
                    reader["RequestId"] == DBNull.Value ? null : Convert.ToInt32(reader["RequestId"]));
            }
        }

        return null;
    }

    private async Task SendDirectZaloMessageAsync(
        SqlConnection connection,
        int customerId,
        int? bookingId,
        string zaloUserId,
        string message,
        string messageType,
        CancellationToken cancellationToken)
    {
        if (await HasSuccessfulMessageLogAsync(connection, customerId, zaloUserId, messageType, cancellationToken))
        {
            return;
        }

        var requestObject = new
        {
            recipient = new Dictionary<string, string?> { ["user_id"] = zaloUserId },
            message = new { text = message }
        };
        var requestJson = JsonSerializer.Serialize(requestObject, JsonOptions);

        try
        {
            var accessToken = await GetValidAccessTokenAsync(cancellationToken);
            var client = _httpClientFactory.CreateClient("ZaloOA");
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildApiUri(_zaloOptions.TextMessageEndpoint));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(requestJson, Encoding.UTF8);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await client.SendAsync(request, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            await SaveMessageLogAsync(connection, customerId, bookingId, zaloUserId, null, messageType, requestJson, responseJson, response.IsSuccessStatusCode, response.IsSuccessStatusCode ? null : responseJson, cancellationToken);
        }
        catch (Exception ex)
        {
            await SaveMessageLogAsync(connection, customerId, bookingId, zaloUserId, null, messageType, requestJson, null, false, ex.Message, cancellationToken);
            _logger.LogError(ex, "Failed to send Zalo direct message {MessageType} for customer {CustomerId}.", messageType, customerId);
        }
    }

    private static async Task<bool> HasSuccessfulMessageLogAsync(
        SqlConnection connection,
        int customerId,
        string zaloUserId,
        string messageType,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(1)
            FROM [{MessageLogTableName}]
            WHERE CustomerId = @CustomerId
              AND ZaloUserId = @ZaloUserId
              AND MessageType = @MessageType
              AND IsSuccess = 1
            """;
        command.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.Int) { Value = customerId });
        command.Parameters.Add(new SqlParameter("@ZaloUserId", SqlDbType.NVarChar, 200) { Value = zaloUserId.Trim() });
        command.Parameters.Add(new SqlParameter("@MessageType", SqlDbType.NVarChar, 50) { Value = messageType });
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0) > 0;
    }
    private async Task<ZaloSendResult> SendMessageAsync(
        ZaloBookingInfo booking,
        string message,
        string messageType,
        CancellationToken cancellationToken,
        bool requireZaloUserId = false)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        var zaloUserId = await FindZaloUserIdAsync(connection, booking.CustomerId, cancellationToken)
            ?? booking.CustomerZaloId;
        var phone = booking.PhoneNumber;

        if (requireZaloUserId && string.IsNullOrWhiteSpace(zaloUserId))
        {
            await SaveMessageLogAsync(
                connection,
                booking.CustomerId,
                booking.RequestId,
                null,
                phone,
                "PendingSendZaloMessage",
                "{}",
                null,
                false,
                $"Pending {messageType}: customer has no Zalo user id.",
                cancellationToken);
            return ZaloSendResult.Fail("Khách hàng chưa kết nối Zalo. Tin nhắn đã được lưu ở trạng thái chờ.");
        }

        if (string.IsNullOrWhiteSpace(zaloUserId) && string.IsNullOrWhiteSpace(phone))
        {
            await SaveMessageLogAsync(connection, booking.CustomerId, booking.RequestId, null, null, messageType, "{}", null, false, "Customer has no Zalo user id or phone.", cancellationToken);
            return ZaloSendResult.Fail("Khach hang chua co Zalo user id hoac so dien thoai.");
        }

        var recipient = string.IsNullOrWhiteSpace(zaloUserId)
            ? new Dictionary<string, string?> { ["phone"] = phone }
            : new Dictionary<string, string?> { ["user_id"] = zaloUserId };
        var requestObject = new
        {
            recipient,
            message = new { text = message }
        };
        var requestJson = JsonSerializer.Serialize(requestObject, JsonOptions);

        try
        {
            var accessToken = await GetValidAccessTokenAsync(cancellationToken);
            var client = _httpClientFactory.CreateClient("ZaloOA");
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildApiUri(_zaloOptions.TextMessageEndpoint));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(requestJson, Encoding.UTF8);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await client.SendAsync(request, cancellationToken);
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            await SaveMessageLogAsync(connection, booking.CustomerId, booking.RequestId, zaloUserId, phone, messageType, requestJson, responseJson, response.IsSuccessStatusCode, response.IsSuccessStatusCode ? null : responseJson, cancellationToken);

            return response.IsSuccessStatusCode
                ? ZaloSendResult.Ok("Da gui tin nhan Zalo OA.")
                : ZaloSendResult.Fail($"Zalo API loi: {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            await SaveMessageLogAsync(connection, booking.CustomerId, booking.RequestId, zaloUserId, phone, messageType, requestJson, null, false, ex.Message, cancellationToken);
            _logger.LogError(ex, "Failed to send Zalo message for request {RequestId}.", booking.RequestId);
            return ZaloSendResult.Fail(ex.Message);
        }
    }

    private async Task<ZaloBookingInfo?> LoadBookingAsync(int yeuCauId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1)
                yc.ID,
                yc.MaYeuCau,
                yc.IDKhachHang,
                yc.NgayThucHien,
                yc.NgayYeuCau,
                yc.NgayHenTiepTheo,
                kh.TenKhachHang,
                kh.SoDienThoai,
                kh.ZaloID,
                dd.DiaChi,
                dd.NguoiLienHe,
                dd.DienThoai
            FROM [{RequestTableName}] AS yc
            LEFT JOIN [{CustomerTableName}] AS kh ON kh.ID = yc.IDKhachHang
            LEFT JOIN [{LocationTableName}] AS dd ON dd.ID = yc.IDDiaDiem
            WHERE yc.ID = @Id
            """;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = yeuCauId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var workTime = GetNullableDateTime(reader, "NgayThucHien")
            ?? GetNullableDateTime(reader, "NgayHenTiepTheo")
            ?? GetNullableDateTime(reader, "NgayYeuCau");
        var workText = workTime.HasValue ? workTime.Value.ToString("dd/MM/yyyy HH:mm") : "chua xac dinh";

        return new ZaloBookingInfo(
            reader.GetInt32(reader.GetOrdinal("ID")),
            GetNullableInt32(reader, "IDKhachHang") ?? 0,
            GetNullableString(reader, "TenKhachHang") ?? "khach hang",
            workText,
            GetNullableString(reader, "DiaChi") ?? "chua co dia chi",
            FirstNotEmpty(GetNullableString(reader, "DienThoai"), GetNullableString(reader, "SoDienThoai")),
            GetNullableString(reader, "ZaloID"));
    }

    private async Task<ZaloTokenRecord?> LoadLatestTokenAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1)
                Id, OaId, AccessToken, RefreshToken, AccessTokenExpiresAtUtc,
                LastRefreshSuccessAtUtc, LastError
            FROM [{TokenTableName}]
            WHERE (@OaId IS NULL OR OaId = @OaId)
            ORDER BY CreatedAtUtc DESC
            """;
        command.Parameters.Add(new SqlParameter("@OaId", SqlDbType.NVarChar, 100) { Value = ToDbValue(_zaloOptions.OaId) });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ZaloTokenRecord(
            reader.GetGuid(reader.GetOrdinal("Id")),
            GetNullableString(reader, "OaId"),
            GetNullableString(reader, "AccessToken") ?? string.Empty,
            GetNullableString(reader, "RefreshToken") ?? string.Empty,
            GetNullableDateTime(reader, "AccessTokenExpiresAtUtc") ?? DateTime.MinValue,
            GetNullableDateTime(reader, "LastRefreshSuccessAtUtc"),
            GetNullableString(reader, "LastError"));
    }

    private async Task UpdateTokenAsync(SqlConnection connection, Guid id, string accessToken, string refreshToken, DateTime expiresAtUtc, string responseJson, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE [{TokenTableName}]
            SET AccessToken = @AccessToken,
                RefreshToken = @RefreshToken,
                AccessTokenExpiresAtUtc = @AccessTokenExpiresAtUtc,
                RefreshTokenUpdatedAtUtc = SYSUTCDATETIME(),
                LastRefreshAttemptAtUtc = SYSUTCDATETIME(),
                LastRefreshSuccessAtUtc = SYSUTCDATETIME(),
                LastError = NULL,
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE Id = @Id
            """;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = id });
        command.Parameters.Add(new SqlParameter("@AccessToken", SqlDbType.NVarChar, -1) { Value = accessToken });
        command.Parameters.Add(new SqlParameter("@RefreshToken", SqlDbType.NVarChar, -1) { Value = refreshToken });
        command.Parameters.Add(new SqlParameter("@AccessTokenExpiresAtUtc", SqlDbType.DateTime2) { Value = expiresAtUtc });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool IsInvalidRefreshTokenError(string? error)
    {
        return !string.IsNullOrWhiteSpace(error) &&
            error.Contains("invalid refresh token", StringComparison.OrdinalIgnoreCase);
    }
    private static string BuildZaloTokenErrorMessage(JsonElement root, string responseJson)
    {
        var errorCode = FirstNotEmpty(GetJsonString(root, "error"), GetJsonString(root, "error_code"), GetJsonString(root, "code"));
        var errorName = FirstNotEmpty(GetJsonString(root, "error_name"), GetJsonString(root, "error_type"));
        var message = FirstNotEmpty(GetJsonString(root, "message"), GetJsonString(root, "error_description"), GetJsonString(root, "description"));
        var detail = string.Join(" - ", new[] { errorCode, errorName, message }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(detail))
        {
            detail = TrimTo(responseJson, 1000) ?? "empty response";
        }

        return $"Zalo refresh response missing access_token. Response: {detail}";
    }
    private async Task SaveTokenErrorAsync(SqlConnection connection, Guid id, string error, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE [{TokenTableName}]
            SET LastRefreshAttemptAtUtc = SYSUTCDATETIME(),
                LastError = @LastError,
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE Id = @Id
            """;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = id });
        command.Parameters.Add(new SqlParameter("@LastError", SqlDbType.NVarChar, 2000) { Value = TrimTo(error, 2000) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<string?> FindZaloUserIdAsync(SqlConnection connection, int customerId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1) ZaloUserId
            FROM [{MappingTableName}]
            WHERE CustomerId = @CustomerId AND (@OaId IS NULL OR OaId = @OaId)
            ORDER BY LastSeenAtUtc DESC
            """;
        command.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.Int) { Value = customerId });
        command.Parameters.Add(new SqlParameter("@OaId", SqlDbType.NVarChar, 100) { Value = ToDbValue(_zaloOptions.OaId) });
        return (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
    }

    private async Task<ZaloCustomerLinkView?> LoadLinkAsync(SqlConnection connection, SqlTransaction? transaction, string token, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT TOP (1)
                l.CustomerId, l.BookingId, l.Token, l.UserExternalId, l.Purpose,
                l.ExpiresAtUtc, kh.TenKhachHang
            FROM [{LinkTableName}] AS l
            LEFT JOIN [{CustomerTableName}] AS kh ON kh.ID = l.CustomerId
            WHERE l.Token = @Token
            """;
        command.Parameters.Add(new SqlParameter("@Token", SqlDbType.NVarChar, 200) { Value = token });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ZaloCustomerLinkView(
            GetNullableInt32(reader, "CustomerId") ?? 0,
            GetNullableInt32(reader, "BookingId"),
            GetNullableString(reader, "Token") ?? token,
            GetNullableString(reader, "UserExternalId") ?? string.Empty,
            GetNullableString(reader, "Purpose") ?? "ConnectZalo",
            GetNullableDateTime(reader, "ExpiresAtUtc"),
            GetNullableString(reader, "TenKhachHang"));
    }

    private async Task UpsertMappingFromExternalIdAsync(SqlConnection connection, string userExternalId, string zaloUserId, string? oaId, string source, CancellationToken cancellationToken)
    {
        await using var linkCommand = connection.CreateCommand();
        linkCommand.CommandText = $"""
            SELECT TOP (1) CustomerId
            FROM [{LinkTableName}]
            WHERE UserExternalId = @UserExternalId
            """;
        linkCommand.Parameters.Add(new SqlParameter("@UserExternalId", SqlDbType.NVarChar, 200) { Value = userExternalId });
        var customerId = Convert.ToInt32(await linkCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
        if (customerId <= 0)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF EXISTS (SELECT 1 FROM [{MappingTableName}] WHERE OaId = @OaId AND ZaloUserId = @ZaloUserId)
            BEGIN
                UPDATE [{MappingTableName}]
                SET CustomerId = @CustomerId,
                    UserExternalId = @UserExternalId,
                    Source = @Source,
                    LastSeenAtUtc = SYSUTCDATETIME()
                WHERE OaId = @OaId AND ZaloUserId = @ZaloUserId;
            END
            ELSE
            BEGIN
                INSERT INTO [{MappingTableName}] (
                    Id, CustomerId, ZaloUserId, UserExternalId, OaId, Source, FirstSeenAtUtc, LastSeenAtUtc
                )
                VALUES (
                    NEWID(), @CustomerId, @ZaloUserId, @UserExternalId, @OaId, @Source, SYSUTCDATETIME(), SYSUTCDATETIME()
                );
            END;

            UPDATE [{LinkTableName}]
            SET IsUsed = 1
            WHERE UserExternalId = @UserExternalId;

            UPDATE [{CustomerTableName}]
            SET ZaloID = @ZaloUserId,
                ZaloLastUpdate = GETDATE(),
                Updated_Date = GETDATE()
            WHERE ID = @CustomerId;
            """;
        command.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.Int) { Value = customerId });
        command.Parameters.Add(new SqlParameter("@ZaloUserId", SqlDbType.NVarChar, 200) { Value = zaloUserId });
        command.Parameters.Add(new SqlParameter("@UserExternalId", SqlDbType.NVarChar, 200) { Value = userExternalId });
        command.Parameters.Add(new SqlParameter("@OaId", SqlDbType.NVarChar, 100) { Value = string.IsNullOrWhiteSpace(oaId) ? "default" : oaId });
        command.Parameters.Add(new SqlParameter("@Source", SqlDbType.NVarChar, 80) { Value = source });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<Guid> SaveWebhookEventAsync(SqlConnection connection, string eventName, string? oaId, string? appId, string rawJson, string? signature, bool isSignatureValid, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO [{WebhookTableName}] (
                Id, EventName, OaId, AppId, RawJson, Signature, IsSignatureValid, CreatedAtUtc
            )
            VALUES (
                @Id, @EventName, @OaId, @AppId, @RawJson, @Signature, @IsSignatureValid, SYSUTCDATETIME()
            );
            """;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = id });
        command.Parameters.Add(new SqlParameter("@EventName", SqlDbType.NVarChar, 150) { Value = TrimTo(eventName, 150) });
        command.Parameters.Add(new SqlParameter("@OaId", SqlDbType.NVarChar, 100) { Value = ToDbValue(oaId) });
        command.Parameters.Add(new SqlParameter("@AppId", SqlDbType.NVarChar, 100) { Value = ToDbValue(appId) });
        command.Parameters.Add(new SqlParameter("@RawJson", SqlDbType.NVarChar, -1) { Value = rawJson });
        command.Parameters.Add(new SqlParameter("@Signature", SqlDbType.NVarChar, 500) { Value = ToDbValue(signature) });
        command.Parameters.Add(new SqlParameter("@IsSignatureValid", SqlDbType.Bit) { Value = isSignatureValid });
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    private static async Task MarkWebhookProcessedAsync(SqlConnection connection, Guid id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE [{WebhookTableName}]
            SET ProcessedAtUtc = SYSUTCDATETIME()
            WHERE Id = @Id
            """;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = id });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SaveMessageLogAsync(SqlConnection connection, int? customerId, int? bookingId, string? zaloUserId, string? phone, string messageType, string requestJson, string? responseJson, bool success, string? error, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO [{MessageLogTableName}] (
                Id, CustomerId, BookingId, ZaloUserId, PhoneNumber, MessageType,
                RequestJson, ResponseJson, IsSuccess, ErrorMessage, CreatedAtUtc
            )
            VALUES (
                NEWID(), @CustomerId, @BookingId, @ZaloUserId, @PhoneNumber, @MessageType,
                @RequestJson, @ResponseJson, @IsSuccess, @ErrorMessage, SYSUTCDATETIME()
            )
            """;
        command.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.Int) { Value = customerId.HasValue ? customerId.Value : DBNull.Value });
        command.Parameters.Add(new SqlParameter("@BookingId", SqlDbType.Int) { Value = bookingId.HasValue ? bookingId.Value : DBNull.Value });
        command.Parameters.Add(new SqlParameter("@ZaloUserId", SqlDbType.NVarChar, 200) { Value = ToDbValue(zaloUserId) });
        command.Parameters.Add(new SqlParameter("@PhoneNumber", SqlDbType.NVarChar, 50) { Value = ToDbValue(phone) });
        command.Parameters.Add(new SqlParameter("@MessageType", SqlDbType.NVarChar, 50) { Value = messageType });
        command.Parameters.Add(new SqlParameter("@RequestJson", SqlDbType.NVarChar, -1) { Value = requestJson });
        command.Parameters.Add(new SqlParameter("@ResponseJson", SqlDbType.NVarChar, -1) { Value = ToDbValue(responseJson) });
        command.Parameters.Add(new SqlParameter("@IsSuccess", SqlDbType.Bit) { Value = success });
        command.Parameters.Add(new SqlParameter("@ErrorMessage", SqlDbType.NVarChar, 2000) { Value = ToDbValue(TrimTo(error, 2000)) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private bool IsWebhookSignatureValid(string rawJson, string? signature)
    {
        if (!_zaloOptions.EnableSignatureValidation)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(_zaloOptions.OaSecretKey))
        {
            _logger.LogWarning("Zalo webhook signature validation is enabled but OA Secret Key is missing. Accepting webhook without signature validation.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_zaloOptions.OaSecretKey));
        var hash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawJson))).ToLowerInvariant();
        var cleanedSignature = signature.Trim();
        if (cleanedSignature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            cleanedSignature = cleanedSignature["sha256=".Length..];
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(hash),
            Encoding.UTF8.GetBytes(cleanedSignature.ToLowerInvariant()));
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

    private async Task EnsureSchemaAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF OBJECT_ID('dbo.{TokenTableName}', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[{TokenTableName}] (
                    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [OaId] NVARCHAR(100) NULL,
                    [AccessToken] NVARCHAR(MAX) NOT NULL,
                    [RefreshToken] NVARCHAR(MAX) NOT NULL,
                    [AccessTokenExpiresAtUtc] DATETIME2 NOT NULL,
                    [RefreshTokenUpdatedAtUtc] DATETIME2 NOT NULL,
                    [LastRefreshAttemptAtUtc] DATETIME2 NULL,
                    [LastRefreshSuccessAtUtc] DATETIME2 NULL,
                    [LastError] NVARCHAR(2000) NULL,
                    [CreatedAtUtc] DATETIME2 NOT NULL,
                    [UpdatedAtUtc] DATETIME2 NOT NULL
                );
            END;

            IF OBJECT_ID('dbo.{LinkTableName}', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[{LinkTableName}] (
                    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
                    [CustomerId] INT NOT NULL,
                    [BookingId] INT NULL,
                    [Token] NVARCHAR(200) NOT NULL,
                    [UserExternalId] NVARCHAR(200) NOT NULL,
                    [Purpose] NVARCHAR(50) NOT NULL,
                    [TargetUrl] NVARCHAR(1000) NULL,
                    [ClickCount] INT NOT NULL DEFAULT 0,
                    [FirstClickedAtUtc] DATETIME2 NULL,
                    [LastClickedAtUtc] DATETIME2 NULL,
                    [ExpiresAtUtc] DATETIME2 NULL,
                    [IsUsed] BIT NOT NULL DEFAULT 0,
                    [CreatedAtUtc] DATETIME2 NOT NULL
                );
                CREATE UNIQUE INDEX [UX_TblCustomerInteractionLinks_Token] ON [dbo].[{LinkTableName}] ([Token]);
                CREATE UNIQUE INDEX [UX_TblCustomerInteractionLinks_UserExternalId] ON [dbo].[{LinkTableName}] ([UserExternalId]);
            END;

            IF OBJECT_ID('dbo.{MappingTableName}', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[{MappingTableName}] (
                    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [CustomerId] INT NOT NULL,
                    [ZaloUserId] NVARCHAR(200) NOT NULL,
                    [UserExternalId] NVARCHAR(200) NULL,
                    [OaId] NVARCHAR(100) NOT NULL,
                    [Source] NVARCHAR(80) NOT NULL,
                    [FirstSeenAtUtc] DATETIME2 NOT NULL,
                    [LastSeenAtUtc] DATETIME2 NOT NULL
                );
                CREATE UNIQUE INDEX [UX_TblZaloUserMappings_Oa_User] ON [dbo].[{MappingTableName}] ([OaId], [ZaloUserId]);
            END;

            IF OBJECT_ID('dbo.{WebhookTableName}', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[{WebhookTableName}] (
                    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [EventName] NVARCHAR(150) NOT NULL,
                    [OaId] NVARCHAR(100) NULL,
                    [AppId] NVARCHAR(100) NULL,
                    [RawJson] NVARCHAR(MAX) NOT NULL,
                    [Signature] NVARCHAR(500) NULL,
                    [IsSignatureValid] BIT NOT NULL,
                    [ProcessedAtUtc] DATETIME2 NULL,
                    [CreatedAtUtc] DATETIME2 NOT NULL
                );
            END;

            IF OBJECT_ID('dbo.{MessageLogTableName}', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[{MessageLogTableName}] (
                    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    [CustomerId] INT NULL,
                    [BookingId] INT NULL,
                    [ZaloUserId] NVARCHAR(200) NULL,
                    [PhoneNumber] NVARCHAR(50) NULL,
                    [MessageType] NVARCHAR(50) NOT NULL,
                    [RequestJson] NVARCHAR(MAX) NOT NULL,
                    [ResponseJson] NVARCHAR(MAX) NULL,
                    [IsSuccess] BIT NOT NULL,
                    [ErrorMessage] NVARCHAR(2000) NULL,
                    [CreatedAtUtc] DATETIME2 NOT NULL
                );
            END;

            IF OBJECT_ID('dbo.{RatingTableName}', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[{RatingTableName}] (
                    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
                    [BookingId] INT NULL,
                    [CustomerId] INT NOT NULL,
                    [Token] NVARCHAR(200) NOT NULL,
                    [Score] INT NOT NULL,
                    [Comment] NVARCHAR(1000) NULL,
                    [Source] NVARCHAR(50) NOT NULL,
                    [CreatedAtUtc] DATETIME2 NOT NULL
                );
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private Uri BuildApiUri(string path) => BuildUri(_zaloOptions.ApiBaseUrl, path);

    private Uri BuildOAuthUri(string path) => BuildUri(_zaloOptions.OAuthBaseUrl, path);

    private static Uri BuildUri(string baseUrl, string path)
    {
        var normalizedBase = string.IsNullOrWhiteSpace(baseUrl) ? "https://openapi.zalo.me" : baseUrl.TrimEnd('/');
        var normalizedPath = string.IsNullOrWhiteSpace(path) ? "/" : path.StartsWith('/') ? path : "/" + path;
        return new Uri(normalizedBase + normalizedPath);
    }

    private string BuildPublicUrl(string path)
    {
        var configured = _zaloOptions.PublicBaseUrl ?? configuration["APP_PUBLIC_BASE_URL"];
        var baseUrl = string.IsNullOrWhiteSpace(configured) ? "https://yourdomain.com" : configured.TrimEnd('/');
        var normalizedPath = path.StartsWith('/') ? path : "/" + path;
        return baseUrl + normalizedPath;
    }

    private static string SecureToken(int bytes = 32)
    {
        var data = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string NormalizePurpose(string? value)
    {
        return value?.Trim() switch
        {
            "BookingReminder" => "BookingReminder",
            "Rating" => "Rating",
            _ => "ConnectZalo"
        };
    }

    private static string? GetJsonString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var value) &&
            value.ValueKind != JsonValueKind.Null &&
            value.ValueKind != JsonValueKind.Undefined
            ? value.ToString()
            : null;
    }

    private static string? FindJsonString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                        ? null
                        : property.Value.ToString();
                }

                var child = FindJsonString(property.Value, name);
                if (!string.IsNullOrWhiteSpace(child))
                {
                    return child;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var childElement in element.EnumerateArray())
            {
                var child = FindJsonString(childElement, name);
                if (!string.IsNullOrWhiteSpace(child))
                {
                    return child;
                }
            }
        }

        return null;
    }

    private static async Task<bool> TableExistsAsync(SqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@TableName, 'U') IS NULL THEN 0 ELSE 1 END";
        command.Parameters.Add(new SqlParameter("@TableName", SqlDbType.NVarChar, 256) { Value = "dbo." + tableName });
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0) == 1;
    }

    private static string? FindZaloUserIdFromWebhook(JsonElement root)
    {
        return FirstNotEmpty(
            FindNestedString(root, "sender", "id", "user_id", "from_id"),
            FindNestedString(root, "from", "id", "user_id"),
            FindJsonString(root, "user_id"),
            FindJsonString(root, "from_id"),
            FindJsonString(root, "sender_id"));
    }

    private static string? FindIncomingMessageText(JsonElement root)
    {
        return FirstNotEmpty(
            FindNestedString(root, "message", "text", "content"),
            FindNestedString(root, "text", "content"),
            FindJsonString(root, "message_text"),
            FindJsonString(root, "text"),
            FindJsonString(root, "content"));
    }

    private static string? FindNestedString(JsonElement element, string objectName, params string[] propertyNames)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, objectName, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var propertyName in propertyNames)
                    {
                        var value = GetJsonString(property.Value, propertyName);
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }
                }

                var child = FindNestedString(property.Value, objectName, propertyNames);
                if (!string.IsNullOrWhiteSpace(child))
                {
                    return child;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var childElement in element.EnumerateArray())
            {
                var child = FindNestedString(childElement, objectName, propertyNames);
                if (!string.IsNullOrWhiteSpace(child))
                {
                    return child;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractMessageCandidates(string messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            return [];
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var separators = messageText.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : ' ').ToArray();
        foreach (var token in new string(separators).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length >= 8)
            {
                candidates.Add(token);
            }
        }

        var trimmed = messageText.Trim();
        if (trimmed.Length >= 8 && trimmed.Length <= 300)
        {
            candidates.Add(trimmed);
        }

        return candidates.ToArray();
    }

    private static object ToDbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static string? FirstNotEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static string? TrimTo(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? GetNullableString(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? GetNullableInt32(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static DateTime? GetNullableDateTime(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDateTime(reader.GetValue(ordinal));
    }

    private sealed record ZaloCustomerContext(int CustomerId, int? BookingId);

    private sealed record ZaloWebhookProfile(string? DisplayName, string? AvatarUrl, string? PhoneNumber)
    {
        public static ZaloWebhookProfile Empty { get; } = new(null, null, null);
    }

    private sealed record ZaloTokenRecord(
        Guid Id,
        string? OaId,
        string AccessToken,
        string RefreshToken,
        DateTime AccessTokenExpiresAtUtc,
        DateTime? LastRefreshSuccessAtUtc,
        string? LastError);

    private sealed record ZaloBookingInfo(
        int RequestId,
        int CustomerId,
        string CustomerName,
        string WorkTimeText,
        string Address,
        string? PhoneNumber,
        string? CustomerZaloId);
}

public sealed record ZaloSendResult(bool Succeeded, string Message)
{
    public static ZaloSendResult Ok(string message) => new(true, message);
    public static ZaloSendResult Fail(string message) => new(false, message);
}

public sealed record ZaloCustomerLinkResult(string Link, string Token, string UserExternalId);

public sealed record ZaloCustomerLinkView(
    int CustomerId,
    int? BookingId,
    string Token,
    string UserExternalId,
    string Purpose,
    DateTime? ExpiresAtUtc,
    string? CustomerName);

public sealed record ZaloWebhookProcessResult(bool Accepted, bool Processed, string Message);

public sealed record ZaloTokenStatus(
    string? OaId,
    DateTime? AccessTokenExpiresAtUtc,
    int? MinutesUntilExpiry,
    DateTime? LastRefreshSuccessAtUtc,
    string? LastError);
