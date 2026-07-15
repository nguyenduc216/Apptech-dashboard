using System.Data;
using ApptechDashboard.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IPermissionCatalogService
{
    Task EnsureYeuCauWorkEmployeePermissionsAsync(CancellationToken cancellationToken = default);
    Task EnsureYeuCauCheckinDistancePermissionsAsync(CancellationToken cancellationToken = default);
    Task EnsureYeuCauCheckinProxyPermissionsAsync(CancellationToken cancellationToken = default);
    Task EnsureCongViecReportPermissionsAsync(CancellationToken cancellationToken = default);
    Task EnsureZaloManagementPermissionsAsync(CancellationToken cancellationToken = default);
}

public sealed class PermissionCatalogService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<PermissionCatalogService> logger) : IPermissionCatalogService
{
    public const string AddWorkEmployeePermissionCode = "YeuCau_WorkEmployee_Insert";
    public const string DeleteWorkEmployeePermissionCode = "YeuCau_WorkEmployee_Delete";
    public const string ToggleCheckinDistancePermissionCode = "YeuCau_CheckinDistance_Update";
    public const string CheckinProxyManagePermissionCode = "YeuCau_CheckinProxy_Manage";
    public const string WorkReportViewPermissionCode = "Report_Work_View";
    public const string ZaloManagementViewPermissionCode = "Zalo_Manage_View";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<PermissionCatalogService> _logger = logger;

    public async Task EnsureYeuCauWorkEmployeePermissionsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString) && !_sqlOptions.IsConfigured)
        {
            return;
        }

        try
        {
            var connectionString = !string.IsNullOrWhiteSpace(_connectionString)
                ? _connectionString
                : _sqlOptions.BuildConnectionString();

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @FunctionId int;

                SELECT TOP (1) @FunctionId = ID
                FROM [TblChucNang]
                WHERE MaChucNang IN (N'YeuCau', N'YeuCau_Index', N'YeuCau_Manage')
                   OR URL LIKE N'%YeuCau%'
                   OR TenChucNang LIKE N'%yêu cầu%'
                   OR TenChucNang LIKE N'%yeu cau%'
                ORDER BY
                    CASE
                        WHEN MaChucNang = N'YeuCau' THEN 0
                        WHEN URL LIKE N'%YeuCau%' THEN 1
                        ELSE 2
                    END,
                    ID;

                IF @FunctionId IS NULL
                BEGIN
                    INSERT INTO [TblChucNang] (MaChucNang, TenChucNang, MieuTa, URL, ThuTuHienThi, TrangThaiSuDung)
                    VALUES (N'YeuCau', N'Yêu cầu', N'Quản lý phiếu yêu cầu', N'/YeuCau', N'30', 1);

                    SET @FunctionId = CONVERT(int, SCOPE_IDENTITY());
                END;

                IF NOT EXISTS (SELECT 1 FROM [TblQuyen] WHERE MaQuyen = @AddPermissionCode)
                BEGIN
                    INSERT INTO [TblQuyen] (IDChucNang, TenQuyen, MaQuyen, MieuTa)
                    VALUES (@FunctionId, @AddPermissionName, @AddPermissionCode, @AddPermissionDescription);
                END;

                IF NOT EXISTS (SELECT 1 FROM [TblQuyen] WHERE MaQuyen = @DeletePermissionCode)
                BEGIN
                    INSERT INTO [TblQuyen] (IDChucNang, TenQuyen, MaQuyen, MieuTa)
                    VALUES (@FunctionId, @DeletePermissionName, @DeletePermissionCode, @DeletePermissionDescription);
                END;
                """;
            command.Parameters.Add(new SqlParameter("@AddPermissionCode", SqlDbType.NVarChar, 250) { Value = AddWorkEmployeePermissionCode });
            command.Parameters.Add(new SqlParameter("@AddPermissionName", SqlDbType.NVarChar, 250) { Value = "Thêm nhân viên công việc" });
            command.Parameters.Add(new SqlParameter("@AddPermissionDescription", SqlDbType.NVarChar, 500) { Value = "Cho phép thêm nhân viên vào công việc của phiếu yêu cầu." });
            command.Parameters.Add(new SqlParameter("@DeletePermissionCode", SqlDbType.NVarChar, 250) { Value = DeleteWorkEmployeePermissionCode });
            command.Parameters.Add(new SqlParameter("@DeletePermissionName", SqlDbType.NVarChar, 250) { Value = "Xóa nhân viên công việc" });
            command.Parameters.Add(new SqlParameter("@DeletePermissionDescription", SqlDbType.NVarChar, 500) { Value = "Cho phép xóa nhân viên khỏi công việc của phiếu yêu cầu." });

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure YeuCau work employee permissions.");
        }
    }

    public async Task EnsureYeuCauCheckinDistancePermissionsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString) && !_sqlOptions.IsConfigured)
        {
            return;
        }

        try
        {
            var connectionString = !string.IsNullOrWhiteSpace(_connectionString)
                ? _connectionString
                : _sqlOptions.BuildConnectionString();

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @FunctionId int;

                SELECT TOP (1) @FunctionId = ID
                FROM [TblChucNang]
                WHERE MaChucNang IN (N'YeuCau', N'YeuCau_Index', N'YeuCau_Manage')
                   OR URL LIKE N'%YeuCau%'
                   OR TenChucNang LIKE N'%yêu cầu%'
                   OR TenChucNang LIKE N'%yeu cau%'
                ORDER BY
                    CASE
                        WHEN MaChucNang = N'YeuCau' THEN 0
                        WHEN URL LIKE N'%YeuCau%' THEN 1
                        ELSE 2
                    END,
                    ID;

                IF @FunctionId IS NULL
                BEGIN
                    INSERT INTO [TblChucNang] (MaChucNang, TenChucNang, MieuTa, URL, ThuTuHienThi, TrangThaiSuDung)
                    VALUES (N'YeuCau', N'Yêu cầu', N'Quản lý phiếu yêu cầu', N'/YeuCau', N'30', 1);

                    SET @FunctionId = CONVERT(int, SCOPE_IDENTITY());
                END;

                IF NOT EXISTS (SELECT 1 FROM [TblQuyen] WHERE MaQuyen = @PermissionCode)
                BEGIN
                    INSERT INTO [TblQuyen] (IDChucNang, TenQuyen, MaQuyen, MieuTa)
                    VALUES (@FunctionId, @PermissionName, @PermissionCode, @PermissionDescription);
                END;
                """;
            command.Parameters.Add(new SqlParameter("@PermissionCode", SqlDbType.NVarChar, 250) { Value = ToggleCheckinDistancePermissionCode });
            command.Parameters.Add(new SqlParameter("@PermissionName", SqlDbType.NVarChar, 250) { Value = "Bật ràng buộc khoảng cách" });
            command.Parameters.Add(new SqlParameter("@PermissionDescription", SqlDbType.NVarChar, 500) { Value = "Cho phép bật hoặc tắt ràng buộc khoảng cách khi checkin trong chi tiết phiếu yêu cầu." });

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure YeuCau checkin distance permissions.");
        }
    }

    public async Task EnsureYeuCauCheckinProxyPermissionsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString) && !_sqlOptions.IsConfigured)
        {
            return;
        }

        try
        {
            var connectionString = !string.IsNullOrWhiteSpace(_connectionString)
                ? _connectionString
                : _sqlOptions.BuildConnectionString();

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                DECLARE @FunctionId int;

                SELECT TOP (1) @FunctionId = ID
                FROM [TblChucNang]
                WHERE MaChucNang IN (N'YeuCau', N'YeuCau_Index', N'YeuCau_Manage')
                   OR URL LIKE N'%YeuCau%'
                ORDER BY
                    CASE
                        WHEN MaChucNang = N'YeuCau' THEN 0
                        WHEN URL LIKE N'%YeuCau%' THEN 1
                        ELSE 2
                    END,
                    ID;

                IF @FunctionId IS NULL
                BEGIN
                    INSERT INTO [TblChucNang] (MaChucNang, TenChucNang, MieuTa, URL, ThuTuHienThi, TrangThaiSuDung)
                    VALUES (N'YeuCau', N'Yeu cau', N'Quan ly phieu yeu cau', N'/YeuCau', N'30', 1);

                    SET @FunctionId = CONVERT(int, SCOPE_IDENTITY());
                END;

                IF NOT EXISTS (SELECT 1 FROM [TblQuyen] WHERE MaQuyen = @PermissionCode)
                BEGIN
                    INSERT INTO [TblQuyen] (IDChucNang, TenQuyen, MaQuyen, MieuTa)
                    VALUES (@FunctionId, @PermissionName, @PermissionCode, @PermissionDescription);
                END;
                """;
            command.Parameters.Add(new SqlParameter("@PermissionCode", SqlDbType.NVarChar, 250) { Value = CheckinProxyManagePermissionCode });
            command.Parameters.Add(new SqlParameter("@PermissionName", SqlDbType.NVarChar, 250) { Value = "Checkin/out lam ho" });
            command.Parameters.Add(new SqlParameter("@PermissionDescription", SqlDbType.NVarChar, 500) { Value = "Cho phep xem toan bo lich su checkin/out trong cong viec va tao checkin/out lam ho nhan vien." });

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure YeuCau checkin proxy permissions.");
        }
    }

    public async Task EnsureCongViecReportPermissionsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString) && !_sqlOptions.IsConfigured)
        {
            return;
        }

        try
        {
            var connectionString = !string.IsNullOrWhiteSpace(_connectionString)
                ? _connectionString
                : _sqlOptions.BuildConnectionString();

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                IF NOT EXISTS (SELECT 1 FROM [TblChucNang] WHERE MaChucNang = N'Report')
                BEGIN
                    INSERT INTO [TblChucNang] (MaChucNang, MaChucNangCha, TenChucNang, MieuTa, URL, ThuTuHienThi, CssClass, TrangThaiSuDung)
                    VALUES (N'Report', NULL, N'Thống kê - báo cáo', N'Nhóm báo cáo hệ thống', NULL, N'5', N'fa-solid fa-chart-column', 1);
                END;

                DECLARE @FunctionId int;

                SELECT TOP (1) @FunctionId = ID
                FROM [TblChucNang]
                WHERE MaChucNang = N'Report_CongViec';

                IF @FunctionId IS NULL
                BEGIN
                    INSERT INTO [TblChucNang] (MaChucNang, MaChucNangCha, TenChucNang, MieuTa, URL, ThuTuHienThi, CssClass, TrangThaiSuDung)
                    VALUES (N'Report_CongViec', N'Report', N'Công việc', N'Báo cáo công việc theo thời gian và nhân viên', N'/bao-cao/cong-viec', N'5.2', N'fa-solid fa-clipboard-list', 1);

                    SET @FunctionId = CONVERT(int, SCOPE_IDENTITY());
                END
                ELSE
                BEGIN
                    UPDATE [TblChucNang]
                    SET MaChucNangCha = N'Report',
                        TenChucNang = N'Công việc',
                        MieuTa = N'Báo cáo công việc theo thời gian và nhân viên',
                        URL = N'/bao-cao/cong-viec',
                        ThuTuHienThi = N'5.2',
                        CssClass = N'fa-solid fa-clipboard-list',
                        TrangThaiSuDung = 1
                    WHERE ID = @FunctionId;
                END;

                IF NOT EXISTS (SELECT 1 FROM [TblQuyen] WHERE MaQuyen = @ViewPermissionCode)
                BEGIN
                    INSERT INTO [TblQuyen] (IDChucNang, TenQuyen, MaQuyen, MieuTa)
                    VALUES (@FunctionId, N'Xem báo cáo công việc', @ViewPermissionCode, N'Cho phép xem báo cáo công việc.');
                END;
                """;
            command.Parameters.Add(new SqlParameter("@ViewPermissionCode", SqlDbType.NVarChar, 250) { Value = WorkReportViewPermissionCode });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure work report permissions.");
        }
    }

    public async Task EnsureZaloManagementPermissionsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString) && !_sqlOptions.IsConfigured)
        {
            return;
        }

        try
        {
            var connectionString = !string.IsNullOrWhiteSpace(_connectionString)
                ? _connectionString
                : _sqlOptions.BuildConnectionString();

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                IF NOT EXISTS (SELECT 1 FROM [TblChucNang] WHERE MaChucNang = N'System')
                BEGIN
                    INSERT INTO [TblChucNang] (MaChucNang, MaChucNangCha, TenChucNang, MieuTa, URL, ThuTuHienThi, CssClass, TrangThaiSuDung)
                    VALUES (N'System', NULL, N'He thong', N'Nhom cau hinh he thong', NULL, N'90', N'fa-solid fa-sliders', 1);
                END;

                DECLARE @FunctionId int;

                SELECT TOP (1) @FunctionId = ID
                FROM [TblChucNang]
                WHERE MaChucNang = N'Zalo_Manage';

                IF @FunctionId IS NULL
                BEGIN
                    INSERT INTO [TblChucNang] (MaChucNang, MaChucNangCha, TenChucNang, MieuTa, URL, ThuTuHienThi, CssClass, TrangThaiSuDung)
                    VALUES (N'Zalo_Manage', N'System', N'Quan ly Zalo OA', N'Quan ly ket noi, token, webhook va log Zalo OA', N'/quan-ly-zalo', N'90.2', N'fa-solid fa-comments', 1);

                    SET @FunctionId = CONVERT(int, SCOPE_IDENTITY());
                END
                ELSE
                BEGIN
                    UPDATE [TblChucNang]
                    SET MaChucNangCha = N'System',
                        TenChucNang = N'Quan ly Zalo OA',
                        MieuTa = N'Quan ly ket noi, token, webhook va log Zalo OA',
                        URL = N'/quan-ly-zalo',
                        ThuTuHienThi = N'90.2',
                        CssClass = N'fa-solid fa-comments',
                        TrangThaiSuDung = 1
                    WHERE ID = @FunctionId;
                END;

                IF NOT EXISTS (SELECT 1 FROM [TblQuyen] WHERE MaQuyen = @ViewPermissionCode)
                BEGIN
                    INSERT INTO [TblQuyen] (IDChucNang, TenQuyen, MaQuyen, MieuTa)
                    VALUES (@FunctionId, N'Xem quan ly Zalo OA', @ViewPermissionCode, N'Cho phep xem trang quan ly Zalo OA.');
                END;
                """;
            command.Parameters.Add(new SqlParameter("@ViewPermissionCode", SqlDbType.NVarChar, 250) { Value = ZaloManagementViewPermissionCode });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure Zalo management permissions.");
        }
    }
}
