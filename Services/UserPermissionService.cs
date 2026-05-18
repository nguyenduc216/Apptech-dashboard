using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IUserPermissionService
{
    Task<IReadOnlyList<UserPermission>> GetPermissionsAsync(Guid userAccountId, CancellationToken cancellationToken = default);
}

public sealed class UserPermissionService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<UserPermissionService> logger) : IUserPermissionService
{
    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<UserPermissionService> _logger = logger;

    public async Task<IReadOnlyList<UserPermission>> GetPermissionsAsync(
        Guid userAccountId,
        CancellationToken cancellationToken = default)
    {
        if (userAccountId == Guid.Empty ||
            (string.IsNullOrWhiteSpace(_connectionString) && !_sqlOptions.IsConfigured))
        {
            return [];
        }

        try
        {
            var connectionString = !string.IsNullOrWhiteSpace(_connectionString)
                ? _connectionString
                : _sqlOptions.BuildConnectionString();

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                SELECT DISTINCT
                    cn.ID AS FunctionId,
                    cn.MaChucNang AS FunctionCode,
                    cn.TenChucNang AS FunctionName,
                    cn.URL AS FunctionUrl,
                    q.ID AS PermissionId,
                    q.TenQuyen AS PermissionName,
                    q.MaQuyen AS PermissionCode
                FROM TblVaiTroVaNguoiDung AS vtnd
                INNER JOIN TblVaiTro AS vt ON vt.ID = vtnd.IDVaiTro
                INNER JOIN TblVaiTroVaQuyen AS vtq ON vtq.IDVaiTro = vt.ID
                INNER JOIN TblQuyen AS q ON q.ID = vtq.IDQuyen
                INNER JOIN TblChucNang AS cn ON cn.ID = q.IDChucNang
                WHERE vtnd.IDTaiKhoanNguoiDung = @IDTaiKhoanNguoiDung
                  AND ISNULL(vt.TrangThaiSuDung, 0) = 1
                  AND ISNULL(cn.TrangThaiSuDung, 0) = 1
                ORDER BY cn.TenChucNang, q.TenQuyen
                """;

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add(new SqlParameter("@IDTaiKhoanNguoiDung", System.Data.SqlDbType.UniqueIdentifier)
            {
                Value = userAccountId
            });

            var permissions = new List<UserPermission>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                permissions.Add(new UserPermission
                {
                    FunctionId = GetInt32(reader, "FunctionId"),
                    FunctionCode = GetString(reader, "FunctionCode"),
                    FunctionName = GetString(reader, "FunctionName"),
                    FunctionUrl = GetString(reader, "FunctionUrl"),
                    PermissionId = GetInt32(reader, "PermissionId"),
                    PermissionName = GetString(reader, "PermissionName"),
                    PermissionCode = GetString(reader, "PermissionCode")
                });
            }

            return permissions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load permissions for user account {UserAccountId}.", userAccountId);
            return [];
        }
    }

    private static int GetInt32(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static string GetString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetValue(ordinal)?.ToString()?.Trim() ?? string.Empty;
    }
}
