using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IDanhMucChamCongNgoaiService
{
    Task<IReadOnlyList<DanhMucChamCongNgoaiOption>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DanhMucChamCongNgoaiItem>> SearchAsync(string? keyword, bool? status, CancellationToken cancellationToken = default);
    Task<DanhMucChamCongNgoaiItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> SaveAsync(DanhMucChamCongNgoaiForm form, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default);
}

public sealed class DanhMucChamCongNgoaiService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<DanhMucChamCongNgoaiService> logger) : IDanhMucChamCongNgoaiService
{
    private const string TableName = "TblDanhMucChamCongNgoai";
    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<DanhMucChamCongNgoaiService> _logger = logger;

    public async Task<IReadOnlyList<DanhMucChamCongNgoaiOption>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT ID, TenNoiDung, ThuTuHienThi FROM [{TableName}] WHERE IsActive = 1 ORDER BY ThuTuHienThi, ID";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var items = new List<DanhMucChamCongNgoaiOption>();
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new DanhMucChamCongNgoaiOption { Id = reader.GetInt32(0), Name = reader.GetString(1).Trim(), SortOrder = reader.GetInt32(2) });
            }
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load active outside attendance catalog.");
            return [];
        }
    }

    public async Task<IReadOnlyList<DanhMucChamCongNgoaiItem>> SearchAsync(string? keyword, bool? status, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT ID, TenNoiDung, ThuTuHienThi, IsActive, CreatedAt, UpdatedAt
                FROM [{TableName}]
                WHERE (@Keyword IS NULL OR TenNoiDung COLLATE Latin1_General_100_CI_AI LIKE N'%' + @Keyword + N'%')
                  AND (@Status IS NULL OR IsActive = @Status)
                ORDER BY ThuTuHienThi, ID
                """;
            command.Parameters.Add(new SqlParameter("@Keyword", SqlDbType.NVarChar, 250) { Value = string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : keyword.Trim() });
            command.Parameters.Add(new SqlParameter("@Status", SqlDbType.Bit) { Value = status.HasValue ? status.Value : DBNull.Value });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var items = new List<DanhMucChamCongNgoaiItem>();
            while (await reader.ReadAsync(cancellationToken)) items.Add(Map(reader));
            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search outside attendance catalog.");
            return [];
        }
    }

    public async Task<DanhMucChamCongNgoaiItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT ID, TenNoiDung, ThuTuHienThi, IsActive, CreatedAt, UpdatedAt FROM [{TableName}] WHERE ID = @Id";
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> SaveAsync(DanhMucChamCongNgoaiForm form, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = form.Id is > 0
                ? $"UPDATE [{TableName}] SET TenNoiDung=@Name, ThuTuHienThi=@Sort, IsActive=@Active, UpdatedAt=SYSUTCDATETIME() WHERE ID=@Id"
                : $"INSERT INTO [{TableName}] (TenNoiDung, ThuTuHienThi, IsActive) VALUES (@Name, @Sort, @Active)";
            command.Parameters.Add(new SqlParameter("@Name", SqlDbType.NVarChar, 250) { Value = form.TenNoiDung.Trim() });
            command.Parameters.Add(new SqlParameter("@Sort", SqlDbType.Int) { Value = form.ThuTuHienThi });
            command.Parameters.Add(new SqlParameter("@Active", SqlDbType.Bit) { Value = form.IsActive });
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = form.Id is > 0 ? form.Id.Value : DBNull.Value });
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return affected > 0 ? (true, null) : (false, "Không tìm thấy danh mục cần cập nhật.");
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return (false, "Nội dung chấm công ngoài đã tồn tại.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save outside attendance catalog.");
            return (false, "Không thể lưu danh mục nội dung chấm công ngoài.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE [{TableName}] SET IsActive=@Active, UpdatedAt=SYSUTCDATETIME() WHERE ID=@Id";
            command.Parameters.Add(new SqlParameter("@Active", SqlDbType.Bit) { Value = isActive });
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
            return await command.ExecuteNonQueryAsync(cancellationToken) > 0 ? (true, null) : (false, "Không tìm thấy danh mục.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle outside attendance catalog {Id}.", id);
            return (false, "Không thể cập nhật trạng thái danh mục.");
        }
    }

    public static IReadOnlyList<DanhMucChamCongNgoaiOption> FilterAndOrderActive(IEnumerable<DanhMucChamCongNgoaiItem> items) =>
        items.Where(item => item.IsActive).OrderBy(item => item.ThuTuHienThi).ThenBy(item => item.Id)
            .Select(item => new DanhMucChamCongNgoaiOption { Id = item.Id, Name = item.TenNoiDung, SortOrder = item.ThuTuHienThi }).ToList();

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(!string.IsNullOrWhiteSpace(_connectionString) ? _connectionString : _sqlOptions.BuildConnectionString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static DanhMucChamCongNgoaiItem Map(SqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0), TenNoiDung = reader.GetString(1).Trim(), ThuTuHienThi = reader.GetInt32(2), IsActive = reader.GetBoolean(3),
        CreatedAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4), UpdatedAt = reader.IsDBNull(5) ? null : reader.GetDateTime(5)
    };
}
