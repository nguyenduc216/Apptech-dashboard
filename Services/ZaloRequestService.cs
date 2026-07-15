using System.Data;
using System.Security.Cryptography;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using QRCoder;

namespace ApptechDashboard.Services;

public interface IZaloRequestService
{
    Task<ZaloRequestLinkResult?> CreateLinkAsync(int requestId, CancellationToken cancellationToken = default);
    Task<ZaloRequestLinkStatus?> GetStatusAsync(string token, CancellationToken cancellationToken = default);
    Task<ZaloRequestLandingView?> OpenAsync(string token, CancellationToken cancellationToken = default);
    Task<ZaloRequestLandingView?> GetRatingViewAsync(string token, CancellationToken cancellationToken = default);
    Task<CustomerZaloProfileInfo> GetCustomerZaloProfileAsync(int customerId, CancellationToken cancellationToken = default);
    Task<RequestRatingInfo> GetRequestRatingAsync(int requestId, CancellationToken cancellationToken = default);
    Task<(ZaloRequestRatingResult? Result, string? Error)> SubmitRatingAsync(
        ZaloRequestRatingSubmit request,
        CancellationToken cancellationToken = default);
    Task MapWebhookUserAsync(
        string userExternalId,
        string zaloUserId,
        string? oaId,
        string source,
        string? displayName,
        string? avatarUrl,
        string? phoneNumber,
        CancellationToken cancellationToken = default);
}

public sealed class ZaloRequestService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    IZaloSettingsService zaloSettings,
    ILogger<ZaloRequestService> logger) : IZaloRequestService
{
    private const string LinkTable = "TblZaloRequestLinks";
    private const string ProfileTable = "TblCustomerZaloProfiles";
    private const string RatingTable = "TblRequestWorkRatings";
    private const string RatingItemTable = "TblRequestWorkRatingItems";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");

    public async Task<ZaloRequestLinkResult?> CreateLinkAsync(int requestId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        var requestInfo = await LoadRequestAsync(connection, requestId, cancellationToken);
        if (requestInfo is null)
        {
            return null;
        }

        var existing = await LoadActiveLinkByRequestAsync(connection, requestId, cancellationToken);
        if (existing is not null)
        {
            return await BuildLinkResultAsync(connection, existing, requestInfo, cancellationToken);
        }

        var token = SecureToken();
        var externalId = $"yc-{requestId}-{SecureToken(14)}";
        var qrUrl = BuildPublicUrl($"/zalo/request/{token}");
        var expiresAt = DateTime.UtcNow.AddDays(30);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO [{LinkTable}] (
                Id, RequestId, CustomerId, Token, UserExternalId, QrUrl, Purpose,
                Status, OpenCount, ExpiresAtUtc, CreatedAtUtc, UpdatedAtUtc
            )
            VALUES (
                NEWID(), @RequestId, @CustomerId, @Token, @UserExternalId, @QrUrl,
                N'RequestRating', N'Created', 0, @ExpiresAtUtc, SYSUTCDATETIME(), SYSUTCDATETIME()
            )
            """;
        command.Parameters.Add(new SqlParameter("@RequestId", SqlDbType.Int) { Value = requestId });
        command.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.Int) { Value = requestInfo.CustomerId.HasValue ? requestInfo.CustomerId.Value : DBNull.Value });
        command.Parameters.Add(new SqlParameter("@Token", SqlDbType.NVarChar, 200) { Value = token });
        command.Parameters.Add(new SqlParameter("@UserExternalId", SqlDbType.NVarChar, 200) { Value = externalId });
        command.Parameters.Add(new SqlParameter("@QrUrl", SqlDbType.NVarChar, 1000) { Value = qrUrl });
        command.Parameters.Add(new SqlParameter("@ExpiresAtUtc", SqlDbType.DateTime2) { Value = expiresAt });
        await command.ExecuteNonQueryAsync(cancellationToken);

        var link = new RequestLinkRecord(requestId, requestInfo.CustomerId, token, externalId, "Created", expiresAt);
        return await BuildLinkResultAsync(connection, link, requestInfo, cancellationToken);
    }

    public async Task<ZaloRequestLinkStatus?> GetStatusAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1)
                link.RequestId,
                link.CustomerId,
                link.Status,
                link.OpenCount,
                link.LastOpenedAtUtc,
                link.ExpiresAtUtc,
                profile.ZaloDisplayName,
                COALESCE(profile.ZaloPhoneNumber, profile.PhoneNumber) AS ZaloPhoneNumber,
                rating.RatingScore,
                rating.SubmittedAtUtc AS RatingSubmittedAtUtc
            FROM [{LinkTable}] link
            OUTER APPLY (
                SELECT TOP (1) ZaloDisplayName, ZaloPhoneNumber, PhoneNumber
                FROM [{ProfileTable}] p
                WHERE p.CustomerId = link.CustomerId
                  AND NULLIF(LTRIM(RTRIM(p.ZaloUserId)), N'') IS NOT NULL
                ORDER BY p.UpdatedAtUtc DESC, p.CreatedAtUtc DESC
            ) profile
            OUTER APPLY (
                SELECT TOP (1) RatingScore, SubmittedAtUtc
                FROM [{RatingTable}] r
                WHERE r.RequestId = link.RequestId
                ORDER BY r.SubmittedAtUtc DESC
            ) rating
            WHERE link.Token = @Token
            """;
        command.Parameters.Add(new SqlParameter("@Token", SqlDbType.NVarChar, 200) { Value = token.Trim() });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var status = GetString(reader, "Status") ?? "Created";
        var expiresAt = GetDateTime(reader, "ExpiresAtUtc");
        if (expiresAt.HasValue && expiresAt.Value < DateTime.UtcNow && status != "Rated")
        {
            status = "Expired";
        }

        var ratingScore = reader["RatingScore"] == DBNull.Value ? null : (int?)Convert.ToInt32(reader["RatingScore"]);
        var ratingSubmittedAt = GetDateTime(reader, "RatingSubmittedAtUtc");
        var zaloConnected = !string.IsNullOrWhiteSpace(GetString(reader, "ZaloDisplayName")) ||
            !string.IsNullOrWhiteSpace(GetString(reader, "ZaloPhoneNumber")) ||
            status is "ZaloConnected" or "Rated";

        return new ZaloRequestLinkStatus(
            status,
            Convert.ToInt32(reader["OpenCount"]),
            zaloConnected,
            status == "Rated" || ratingScore.HasValue,
            GetDateTime(reader, "LastOpenedAtUtc"),
            expiresAt,
            GetString(reader, "ZaloDisplayName"),
            GetString(reader, "ZaloPhoneNumber"),
            ratingScore,
            ratingSubmittedAt);
    }

    public async Task<ZaloRequestLandingView?> OpenAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        var link = await LoadLinkAsync(connection, token, cancellationToken);
        if (link is null || link.ExpiresAtUtc < DateTime.UtcNow)
        {
            return null;
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                UPDATE [{LinkTable}]
                SET OpenCount = OpenCount + 1,
                    FirstOpenedAtUtc = ISNULL(FirstOpenedAtUtc, SYSUTCDATETIME()),
                    LastOpenedAtUtc = SYSUTCDATETIME(),
                    Status = CASE WHEN Status = N'Created' THEN N'Opened' ELSE Status END,
                    UpdatedAtUtc = SYSUTCDATETIME()
                WHERE Token = @Token
                """;
            command.Parameters.Add(new SqlParameter("@Token", SqlDbType.NVarChar, 200) { Value = token.Trim() });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var landing = await LoadLandingAsync(connection, link, cancellationToken);
        if (landing.ZaloConnected)
        {
            landing.IsRated = link.Status == "Rated";
        }

        return landing;
    }

    public async Task<CustomerZaloProfileInfo> GetCustomerZaloProfileAsync(int customerId, CancellationToken cancellationToken = default)
    {
        if (customerId <= 0)
        {
            return new CustomerZaloProfileInfo();
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        return await LoadCustomerZaloProfileAsync(connection, customerId, cancellationToken) ?? new CustomerZaloProfileInfo
        {
            CustomerId = customerId,
            Connected = false
        };
    }

    public async Task<RequestRatingInfo> GetRequestRatingAsync(int requestId, CancellationToken cancellationToken = default)
    {
        if (requestId <= 0)
        {
            return new RequestRatingInfo();
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        return await LoadRequestRatingAsync(connection, requestId, cancellationToken) ?? new RequestRatingInfo
        {
            RequestId = requestId,
            HasRating = false
        };
    }

    public async Task<ZaloRequestLandingView?> GetRatingViewAsync(string token, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        var link = await LoadLinkAsync(connection, token, cancellationToken);
        return link is null || link.ExpiresAtUtc < DateTime.UtcNow
            ? null
            : await LoadLandingAsync(connection, link, cancellationToken);
    }

    public async Task<(ZaloRequestRatingResult? Result, string? Error)> SubmitRatingAsync(
        ZaloRequestRatingSubmit request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || request.RatingScore is < 1 or > 5)
        {
            return (null, "Dữ liệu đánh giá không hợp lệ.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        var link = await LoadLinkAsync(connection, request.Token.Trim(), cancellationToken);
        if (link is null || link.ExpiresAtUtc < DateTime.UtcNow)
        {
            return (null, "Link đánh giá không hợp lệ hoặc đã hết hạn.");
        }

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var duplicateCommand = connection.CreateCommand();
            duplicateCommand.Transaction = transaction;
            duplicateCommand.CommandText = $"SELECT COUNT(1) FROM [{RatingTable}] WHERE Token = @Token";
            duplicateCommand.Parameters.Add(new SqlParameter("@Token", SqlDbType.NVarChar, 200) { Value = request.Token.Trim() });
            if (Convert.ToInt32(await duplicateCommand.ExecuteScalarAsync(cancellationToken) ?? 0) > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (null, "Phiếu yêu cầu này đã được đánh giá.");
            }

            var zaloUserId = await FindZaloUserIdAsync(connection, transaction, link.CustomerId, cancellationToken);
            var ratingId = Guid.NewGuid();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"""
                    INSERT INTO [{RatingTable}] (
                        Id, RequestId, CustomerId, ZaloUserId, Token, RatingScore,
                        Note, CustomerComment, SubmittedAtUtc, CreatedAtUtc, UpdatedAtUtc, Source
                    )
                    VALUES (
                        @Id, @RequestId, @CustomerId, @ZaloUserId, @Token, @RatingScore,
                        @Note, @CustomerComment, SYSUTCDATETIME(), SYSUTCDATETIME(), NULL, N'ZaloQr'
                    )
                    """;
                command.Parameters.Add(new SqlParameter("@Id", SqlDbType.UniqueIdentifier) { Value = ratingId });
                command.Parameters.Add(new SqlParameter("@RequestId", SqlDbType.Int) { Value = link.RequestId });
                command.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.Int) { Value = link.CustomerId.HasValue ? link.CustomerId.Value : DBNull.Value });
                AddString(command, "@ZaloUserId", zaloUserId);
                command.Parameters.Add(new SqlParameter("@Token", SqlDbType.NVarChar, 200) { Value = request.Token.Trim() });
                command.Parameters.Add(new SqlParameter("@RatingScore", SqlDbType.Int) { Value = request.RatingScore });
                AddString(command, "@Note", request.Note);
                AddString(command, "@CustomerComment", request.CustomerComment);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var item in request.Items.Where(item => item.RatingScore is >= 1 and <= 5))
            {
                await using var itemCommand = connection.CreateCommand();
                itemCommand.Transaction = transaction;
                itemCommand.CommandText = $"""
                    INSERT INTO [{RatingItemTable}] (
                        Id, RatingId, RequestWorkItemId, WorkName, RatingScore, Note, CreatedAtUtc
                    )
                    VALUES (NEWID(), @RatingId, @RequestWorkItemId, @WorkName, @RatingScore, @Note, SYSUTCDATETIME())
                    """;
                itemCommand.Parameters.Add(new SqlParameter("@RatingId", SqlDbType.UniqueIdentifier) { Value = ratingId });
                itemCommand.Parameters.Add(new SqlParameter("@RequestWorkItemId", SqlDbType.Int) { Value = item.RequestWorkItemId.HasValue ? item.RequestWorkItemId.Value : DBNull.Value });
                AddString(itemCommand, "@WorkName", item.WorkName ?? "Công việc");
                itemCommand.Parameters.Add(new SqlParameter("@RatingScore", SqlDbType.Int) { Value = item.RatingScore });
                AddString(itemCommand, "@Note", item.Note);
                await itemCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var updateCommand = connection.CreateCommand())
            {
                updateCommand.Transaction = transaction;
                updateCommand.CommandText = $"""
                    UPDATE [{LinkTable}]
                    SET Status = N'Rated', UpdatedAtUtc = SYSUTCDATETIME()
                    WHERE Token = @Token
                    """;
                updateCommand.Parameters.Add(new SqlParameter("@Token", SqlDbType.NVarChar, 200) { Value = request.Token.Trim() });
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return (new ZaloRequestRatingResult(ratingId, link.RequestId, link.CustomerId, request.RatingScore), null);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(ex, "Failed to save Zalo request rating for token {Token}.", request.Token);
            return (null, "Không thể lưu đánh giá lúc này.");
        }
    }

    public async Task MapWebhookUserAsync(
        string userExternalId,
        string zaloUserId,
        string? oaId,
        string source,
        string? displayName,
        string? avatarUrl,
        string? phoneNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userExternalId) || string.IsNullOrWhiteSpace(zaloUserId))
        {
            return;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureSchemaAsync(connection, cancellationToken);
        var link = await LoadLinkByExternalIdAsync(connection, userExternalId, cancellationToken);
        if (link is null)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        var isFollowing = !string.Equals(source, "user_unfollow_oa", StringComparison.OrdinalIgnoreCase);
        command.CommandText = $"""
            MERGE [{ProfileTable}] AS target
            USING (SELECT @ZaloUserId AS ZaloUserId, @OaId AS OaId) AS source
                ON target.ZaloUserId = source.ZaloUserId AND target.OaId = source.OaId
            WHEN MATCHED THEN UPDATE SET
                CustomerId = @CustomerId, RequestId = @RequestId, Source = @Source,
                ZaloDisplayName = COALESCE(@ZaloDisplayName, ZaloDisplayName),
                ZaloAvatarUrl = COALESCE(@ZaloAvatarUrl, ZaloAvatarUrl),
                ZaloPhoneNumber = COALESCE(@ZaloPhoneNumber, ZaloPhoneNumber),
                IsFollowingOa = @IsFollowingOa,
                LastInteractionAtUtc = SYSUTCDATETIME(),
                UpdatedAtUtc = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (
                Id, CustomerId, RequestId, ZaloUserId, OaId, Source,
                ZaloDisplayName, ZaloAvatarUrl, ZaloPhoneNumber, IsFollowingOa,
                ConnectedAtUtc, LastInteractionAtUtc, CreatedAtUtc, UpdatedAtUtc
            ) VALUES (
                NEWID(), @CustomerId, @RequestId, @ZaloUserId, @OaId, @Source,
                @ZaloDisplayName, @ZaloAvatarUrl, @ZaloPhoneNumber, @IsFollowingOa,
                SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME()
            );

            UPDATE [{LinkTable}]
            SET Status = CASE WHEN Status = N'Rated' THEN Status ELSE N'ZaloConnected' END,
                UpdatedAtUtc = SYSUTCDATETIME()
            WHERE UserExternalId = @UserExternalId;

            UPDATE [TblKhachHang]
            SET ZaloID = @ZaloUserId, ZaloLastUpdate = GETDATE(), Updated_Date = GETDATE()
            WHERE ID = @CustomerId;
            """;
        command.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.Int) { Value = link.CustomerId.HasValue ? link.CustomerId.Value : DBNull.Value });
        command.Parameters.Add(new SqlParameter("@RequestId", SqlDbType.Int) { Value = link.RequestId });
        command.Parameters.Add(new SqlParameter("@ZaloUserId", SqlDbType.NVarChar, 200) { Value = zaloUserId.Trim() });
        command.Parameters.Add(new SqlParameter("@UserExternalId", SqlDbType.NVarChar, 200) { Value = userExternalId.Trim() });
        command.Parameters.Add(new SqlParameter("@OaId", SqlDbType.NVarChar, 100) { Value = string.IsNullOrWhiteSpace(oaId) ? "default" : oaId.Trim() });
        command.Parameters.Add(new SqlParameter("@Source", SqlDbType.NVarChar, 80) { Value = source });
        AddString(command, "@ZaloDisplayName", displayName);
        AddString(command, "@ZaloAvatarUrl", avatarUrl);
        AddString(command, "@ZaloPhoneNumber", phoneNumber);
        command.Parameters.Add(new SqlParameter("@IsFollowingOa", SqlDbType.Bit) { Value = isFollowing });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<ZaloRequestLinkResult> BuildLinkResultAsync(
        SqlConnection connection,
        RequestLinkRecord link,
        RequestRecord request,
        CancellationToken cancellationToken)
    {
        var url = BuildPublicUrl($"/zalo/request/{link.Token}");
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data).GetGraphic(8);
        var profile = link.CustomerId.HasValue
            ? await LoadCustomerZaloProfileAsync(connection, link.CustomerId.Value, cancellationToken)
            : null;
        var rating = await LoadRequestRatingAsync(connection, link.RequestId, cancellationToken);
        return new ZaloRequestLinkResult(
            link.Token,
            url,
            $"data:image/png;base64,{Convert.ToBase64String(png)}",
            link.Status,
            link.ExpiresAtUtc,
            profile?.Connected == true,
            profile?.ZaloDisplayName,
            profile?.ZaloPhoneNumber,
            rating?.HasRating == true,
            rating?.RatingScore,
            rating?.SubmittedAtUtc,
            request.CustomerName,
            request.PhoneNumber);
    }

    private async Task<ZaloRequestLandingView> LoadLandingAsync(
        SqlConnection connection,
        RequestLinkRecord link,
        CancellationToken cancellationToken)
    {
        var request = await LoadRequestAsync(connection, link.RequestId, cancellationToken)
            ?? new RequestRecord(link.RequestId, null, $"YC-{link.RequestId}", "Khách hàng", null, null);
        var profile = request.CustomerId.HasValue
            ? await LoadCustomerZaloProfileAsync(connection, request.CustomerId.Value, cancellationToken)
            : null;
        var rating = await LoadRequestRatingAsync(connection, link.RequestId, cancellationToken);
        var works = await LoadWorksAsync(connection, link.RequestId, cancellationToken);
        return new ZaloRequestLandingView
        {
            Token = link.Token,
            UserExternalId = link.UserExternalId,
            RequestCode = request.RequestCode,
            CustomerName = request.CustomerName,
            PhoneNumber = request.PhoneNumber,
            ExecutionDate = request.ExecutionDate,
            OaId = zaloSettings.Current.OaId,
            IsRated = link.Status == "Rated" || rating?.HasRating == true,
            ZaloConnected = profile?.Connected == true,
            ZaloDisplayName = profile?.ZaloDisplayName,
            ZaloPhoneNumber = profile?.ZaloPhoneNumber,
            Works = works
        };
    }

    private static async Task<RequestRecord?> LoadRequestAsync(SqlConnection connection, int requestId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1)
                yc.ID, yc.IDKhachHang, yc.MaYeuCau, yc.NgayThucHien,
                kh.TenKhachHang, kh.SoDienThoai
            FROM [TblYeuCau] yc
            LEFT JOIN [TblKhachHang] kh ON kh.ID = yc.IDKhachHang
            WHERE yc.ID = @Id
            """;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = requestId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new RequestRecord(
            Convert.ToInt32(reader["ID"]),
            reader["IDKhachHang"] == DBNull.Value ? null : Convert.ToInt32(reader["IDKhachHang"]),
            GetString(reader, "MaYeuCau") ?? $"YC-{requestId}",
            GetString(reader, "TenKhachHang") ?? "Khách hàng",
            GetString(reader, "SoDienThoai"),
            GetDateTime(reader, "NgayThucHien"));
    }

    private static async Task<IReadOnlyList<ZaloRequestWorkItem>> LoadWorksAsync(SqlConnection connection, int requestId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ycvc.ID, cv.TenCongViec, ycvc.TrangThaiCongViec
            FROM [TblYeuCauCongViec] ycvc
            LEFT JOIN [TblCongViec] cv ON cv.ID = ycvc.IDCongViec
            WHERE ycvc.IDYeuCau = @RequestId
            ORDER BY ycvc.ID
            """;
        command.Parameters.Add(new SqlParameter("@RequestId", SqlDbType.Int) { Value = requestId });
        var items = new List<ZaloRequestWorkItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ZaloRequestWorkItem
            {
                RequestWorkItemId = Convert.ToInt32(reader["ID"]),
                WorkName = GetString(reader, "TenCongViec") ?? "Công việc",
                Status = GetString(reader, "TrangThaiCongViec")
            });
        }

        return items;
    }

    private static async Task<RequestLinkRecord?> LoadActiveLinkByRequestAsync(SqlConnection connection, int requestId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1) RequestId, CustomerId, Token, UserExternalId, Status, ExpiresAtUtc
            FROM [{LinkTable}]
            WHERE RequestId = @RequestId AND ExpiresAtUtc > SYSUTCDATETIME()
            ORDER BY CreatedAtUtc DESC
            """;
        command.Parameters.Add(new SqlParameter("@RequestId", SqlDbType.Int) { Value = requestId });
        return await ReadLinkAsync(command, cancellationToken);
    }

    private static async Task<RequestLinkRecord?> LoadLinkAsync(SqlConnection connection, string token, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1) RequestId, CustomerId, Token, UserExternalId, Status, ExpiresAtUtc
            FROM [{LinkTable}]
            WHERE Token = @Token
            """;
        command.Parameters.Add(new SqlParameter("@Token", SqlDbType.NVarChar, 200) { Value = token.Trim() });
        return await ReadLinkAsync(command, cancellationToken);
    }

    private static async Task<RequestLinkRecord?> LoadLinkByExternalIdAsync(SqlConnection connection, string externalId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1) RequestId, CustomerId, Token, UserExternalId, Status, ExpiresAtUtc
            FROM [{LinkTable}]
            WHERE UserExternalId = @UserExternalId
            """;
        command.Parameters.Add(new SqlParameter("@UserExternalId", SqlDbType.NVarChar, 200) { Value = externalId.Trim() });
        return await ReadLinkAsync(command, cancellationToken);
    }

    private static async Task<RequestLinkRecord?> ReadLinkAsync(SqlCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new RequestLinkRecord(
            Convert.ToInt32(reader["RequestId"]),
            reader["CustomerId"] == DBNull.Value ? null : Convert.ToInt32(reader["CustomerId"]),
            GetString(reader, "Token") ?? string.Empty,
            GetString(reader, "UserExternalId") ?? string.Empty,
            GetString(reader, "Status") ?? "Created",
            GetDateTime(reader, "ExpiresAtUtc") ?? DateTime.MinValue);
    }

    private static async Task<string?> FindZaloUserIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int? customerId,
        CancellationToken cancellationToken)
    {
        if (!customerId.HasValue)
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT TOP (1) ZaloUserId
            FROM [{ProfileTable}]
            WHERE CustomerId = @CustomerId
            ORDER BY UpdatedAtUtc DESC
            """;
        command.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.Int) { Value = customerId.Value });
        return (await command.ExecuteScalarAsync(cancellationToken))?.ToString();
    }

    private static async Task<CustomerZaloProfileInfo?> LoadCustomerZaloProfileAsync(
        SqlConnection connection,
        int customerId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1)
                CustomerId,
                ZaloUserId,
                ZaloDisplayName,
                ZaloAvatarUrl,
                COALESCE(ZaloPhoneNumber, PhoneNumber) AS ZaloPhoneNumber,
                IsFollowingOa,
                ConnectedAtUtc,
                LastInteractionAtUtc,
                Source
            FROM [{ProfileTable}]
            WHERE CustomerId = @CustomerId
              AND NULLIF(LTRIM(RTRIM(ZaloUserId)), N'') IS NOT NULL
            ORDER BY UpdatedAtUtc DESC, CreatedAtUtc DESC
            """;
        command.Parameters.Add(new SqlParameter("@CustomerId", SqlDbType.Int) { Value = customerId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CustomerZaloProfileInfo
        {
            Connected = true,
            CustomerId = reader["CustomerId"] == DBNull.Value ? null : Convert.ToInt32(reader["CustomerId"]),
            ZaloUserId = GetString(reader, "ZaloUserId"),
            ZaloDisplayName = GetString(reader, "ZaloDisplayName"),
            ZaloAvatarUrl = GetString(reader, "ZaloAvatarUrl"),
            ZaloPhoneNumber = GetString(reader, "ZaloPhoneNumber"),
            IsFollowingOa = reader["IsFollowingOa"] == DBNull.Value ? null : Convert.ToBoolean(reader["IsFollowingOa"]),
            ConnectedAtUtc = GetDateTime(reader, "ConnectedAtUtc"),
            LastInteractionAtUtc = GetDateTime(reader, "LastInteractionAtUtc"),
            Source = GetString(reader, "Source")
        };
    }

    private static async Task<RequestRatingInfo?> LoadRequestRatingAsync(
        SqlConnection connection,
        int requestId,
        CancellationToken cancellationToken)
    {
        Guid ratingId;
        var rating = new RequestRatingInfo();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT TOP (1)
                    Id,
                    RequestId,
                    CustomerId,
                    RatingScore,
                    Note,
                    CustomerComment,
                    SubmittedAtUtc,
                    Source
                FROM [{RatingTable}]
                WHERE RequestId = @RequestId
                ORDER BY SubmittedAtUtc DESC, CreatedAtUtc DESC
                """;
            command.Parameters.Add(new SqlParameter("@RequestId", SqlDbType.Int) { Value = requestId });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            ratingId = (Guid)reader["Id"];
            rating = new RequestRatingInfo
            {
                HasRating = true,
                RatingId = ratingId,
                RequestId = Convert.ToInt32(reader["RequestId"]),
                CustomerId = reader["CustomerId"] == DBNull.Value ? null : Convert.ToInt32(reader["CustomerId"]),
                RatingScore = Convert.ToInt32(reader["RatingScore"]),
                Note = GetString(reader, "Note"),
                CustomerComment = GetString(reader, "CustomerComment"),
                SubmittedAtUtc = GetDateTime(reader, "SubmittedAtUtc"),
                Source = GetString(reader, "Source") ?? "ZaloQr"
            };
        }

        await using (var itemCommand = connection.CreateCommand())
        {
            itemCommand.CommandText = $"""
                SELECT RequestWorkItemId, WorkName, RatingScore, Note
                FROM [{RatingItemTable}]
                WHERE RatingId = @RatingId
                ORDER BY WorkName
                """;
            itemCommand.Parameters.Add(new SqlParameter("@RatingId", SqlDbType.UniqueIdentifier) { Value = ratingId });
            var items = new List<RequestRatingItemInfo>();
            await using var itemReader = await itemCommand.ExecuteReaderAsync(cancellationToken);
            while (await itemReader.ReadAsync(cancellationToken))
            {
                items.Add(new RequestRatingItemInfo
                {
                    RequestWorkItemId = itemReader["RequestWorkItemId"] == DBNull.Value ? null : Convert.ToInt32(itemReader["RequestWorkItemId"]),
                    WorkName = GetString(itemReader, "WorkName") ?? string.Empty,
                    RatingScore = Convert.ToInt32(itemReader["RatingScore"]),
                    Note = GetString(itemReader, "Note")
                });
            }

            rating.Items = items;
        }

        return rating;
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

    private static async Task EnsureSchemaAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF OBJECT_ID('dbo.{LinkTable}', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[{LinkTable}] (
                    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    RequestId INT NOT NULL,
                    CustomerId INT NULL,
                    Token NVARCHAR(200) NOT NULL,
                    UserExternalId NVARCHAR(200) NOT NULL,
                    QrUrl NVARCHAR(1000) NOT NULL,
                    Purpose NVARCHAR(50) NOT NULL,
                    Status NVARCHAR(40) NOT NULL,
                    OpenCount INT NOT NULL DEFAULT 0,
                    FirstOpenedAtUtc DATETIME2 NULL,
                    LastOpenedAtUtc DATETIME2 NULL,
                    ExpiresAtUtc DATETIME2 NOT NULL,
                    CreatedAtUtc DATETIME2 NOT NULL,
                    UpdatedAtUtc DATETIME2 NULL
                );
                CREATE UNIQUE INDEX UX_TblZaloRequestLinks_Token ON [dbo].[{LinkTable}](Token);
                CREATE UNIQUE INDEX UX_TblZaloRequestLinks_ExternalId ON [dbo].[{LinkTable}](UserExternalId);
            END;

            IF OBJECT_ID('dbo.{ProfileTable}', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[{ProfileTable}] (
                    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    CustomerId INT NULL,
                    RequestId INT NULL,
                    ZaloUserId NVARCHAR(200) NOT NULL,
                    ZaloDisplayName NVARCHAR(250) NULL,
                    ZaloAvatarUrl NVARCHAR(1000) NULL,
                    PhoneNumber NVARCHAR(50) NULL,
                    ZaloPhoneNumber NVARCHAR(50) NULL,
                    OaId NVARCHAR(100) NOT NULL,
                    IsFollowingOa BIT NULL,
                    ConnectedAtUtc DATETIME2 NULL,
                    LastInteractionAtUtc DATETIME2 NULL,
                    Source NVARCHAR(80) NOT NULL,
                    CreatedAtUtc DATETIME2 NOT NULL,
                    UpdatedAtUtc DATETIME2 NULL
                );
                CREATE UNIQUE INDEX UX_TblCustomerZaloProfiles_UserOa ON [dbo].[{ProfileTable}](ZaloUserId, OaId);
            END;
            ELSE
            BEGIN
                IF COL_LENGTH('dbo.{ProfileTable}', 'ZaloDisplayName') IS NULL ALTER TABLE [dbo].[{ProfileTable}] ADD ZaloDisplayName NVARCHAR(250) NULL;
                IF COL_LENGTH('dbo.{ProfileTable}', 'ZaloAvatarUrl') IS NULL ALTER TABLE [dbo].[{ProfileTable}] ADD ZaloAvatarUrl NVARCHAR(1000) NULL;
                IF COL_LENGTH('dbo.{ProfileTable}', 'PhoneNumber') IS NULL ALTER TABLE [dbo].[{ProfileTable}] ADD PhoneNumber NVARCHAR(50) NULL;
                IF COL_LENGTH('dbo.{ProfileTable}', 'ZaloPhoneNumber') IS NULL ALTER TABLE [dbo].[{ProfileTable}] ADD ZaloPhoneNumber NVARCHAR(50) NULL;
                IF COL_LENGTH('dbo.{ProfileTable}', 'IsFollowingOa') IS NULL ALTER TABLE [dbo].[{ProfileTable}] ADD IsFollowingOa BIT NULL;
                IF COL_LENGTH('dbo.{ProfileTable}', 'ConnectedAtUtc') IS NULL ALTER TABLE [dbo].[{ProfileTable}] ADD ConnectedAtUtc DATETIME2 NULL;
                IF COL_LENGTH('dbo.{ProfileTable}', 'LastInteractionAtUtc') IS NULL ALTER TABLE [dbo].[{ProfileTable}] ADD LastInteractionAtUtc DATETIME2 NULL;
            END;

            IF OBJECT_ID('dbo.{RatingTable}', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[{RatingTable}] (
                    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    RequestId INT NOT NULL,
                    CustomerId INT NULL,
                    ZaloUserId NVARCHAR(200) NULL,
                    Token NVARCHAR(200) NOT NULL,
                    RatingScore INT NOT NULL,
                    Note NVARCHAR(1000) NULL,
                    CustomerComment NVARCHAR(2000) NULL,
                    SubmittedAtUtc DATETIME2 NOT NULL,
                    CreatedAtUtc DATETIME2 NOT NULL,
                    UpdatedAtUtc DATETIME2 NULL,
                    Source NVARCHAR(80) NOT NULL DEFAULT N'ZaloQr'
                );
                CREATE UNIQUE INDEX UX_TblRequestWorkRatings_Token ON [dbo].[{RatingTable}](Token);
            END;
            ELSE
            BEGIN
                IF COL_LENGTH('dbo.{RatingTable}', 'UpdatedAtUtc') IS NULL ALTER TABLE [dbo].[{RatingTable}] ADD UpdatedAtUtc DATETIME2 NULL;
                IF COL_LENGTH('dbo.{RatingTable}', 'Source') IS NULL ALTER TABLE [dbo].[{RatingTable}] ADD Source NVARCHAR(80) NOT NULL CONSTRAINT DF_{RatingTable}_Source DEFAULT N'ZaloQr';
            END;

            IF OBJECT_ID('dbo.{RatingItemTable}', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[{RatingItemTable}] (
                    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                    RatingId UNIQUEIDENTIFIER NOT NULL,
                    RequestWorkItemId INT NULL,
                    WorkName NVARCHAR(500) NOT NULL,
                    RatingScore INT NOT NULL,
                    Note NVARCHAR(1000) NULL,
                    CreatedAtUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;
            ELSE
            BEGIN
                IF COL_LENGTH('dbo.{RatingItemTable}', 'CreatedAtUtc') IS NULL ALTER TABLE [dbo].[{RatingItemTable}] ADD CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_{RatingItemTable}_Created DEFAULT SYSUTCDATETIME();
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string BuildPublicUrl(string path)
    {
        var baseUrl = zaloSettings.Current.PublicBaseUrl?.TrimEnd('/') ?? "https://apptech.ddns.net";
        return baseUrl + (path.StartsWith('/') ? path : "/" + path);
    }

    private static string SecureToken(int bytes = 32) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void AddString(SqlCommand command, string name, string? value) =>
        command.Parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, -1)
        {
            Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim()
        });

    private static string? GetString(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTime? GetDateTime(SqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDateTime(reader.GetValue(ordinal));
    }

    private sealed record RequestRecord(
        int RequestId,
        int? CustomerId,
        string RequestCode,
        string CustomerName,
        string? PhoneNumber,
        DateTime? ExecutionDate);

    private sealed record RequestLinkRecord(
        int RequestId,
        int? CustomerId,
        string Token,
        string UserExternalId,
        string Status,
        DateTime ExpiresAtUtc);
}
