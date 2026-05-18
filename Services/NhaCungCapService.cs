using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface INhaCungCapService
{
    Task<(IReadOnlyList<NhaCungCapListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        bool? statusFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<NhaCungCapListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(NhaCungCapFormModel model, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(NhaCungCapFormModel model, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class NhaCungCapService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<NhaCungCapService> logger) : INhaCungCapService
{
    private const string TableName = "TblNhaCungCap";
    private const string SearchCollation = "Latin1_General_100_CI_AI";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<NhaCungCapService> _logger = logger;

    public async Task<(IReadOnlyList<NhaCungCapListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        bool? statusFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 5, 100);
        page = Math.Max(page, 1);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var normalizedKeyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
            var whereClause = BuildWhereClause(normalizedKeyword, statusFilter);

            await using var countCommand = connection.CreateCommand();
            countCommand.CommandText = $"""
                SELECT COUNT(1)
                FROM [{TableName}]
                WHERE {whereClause}
                """;
            AddFilterParameters(countCommand, normalizedKeyword, statusFilter);

            var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
            var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            var currentPage = totalPages == 0 ? 1 : Math.Min(page, totalPages);
            var offset = (currentPage - 1) * pageSize;

            await using var listCommand = connection.CreateCommand();
            listCommand.CommandText = $"""
                SELECT
                    ID,
                    TenNhaCungCap,
                    SoDienThoai,
                    Email,
                    DiaChi,
                    CAST(ISNULL(TrangThaiSuDung, 1) AS bit) AS TrangThaiSuDung
                FROM [{TableName}]
                WHERE {whereClause}
                ORDER BY ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                """;
            AddFilterParameters(listCommand, normalizedKeyword, statusFilter);
            listCommand.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = offset });
            listCommand.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = pageSize });

            var items = new List<NhaCungCapListItem>();
            await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapItem(reader));
            }

            return (items, totalCount, currentPage, totalPages, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblNhaCungCap list.");
            return ([], 0, 1, 0, pageSize);
        }
    }

    public async Task<NhaCungCapListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return null;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT TOP (1)
                    ID,
                    TenNhaCungCap,
                    SoDienThoai,
                    Email,
                    DiaChi,
                    CAST(ISNULL(TrangThaiSuDung, 1) AS bit) AS TrangThaiSuDung
                FROM [{TableName}]
                WHERE ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? MapItem(reader) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblNhaCungCap item {Id}.", id);
            return null;
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        NhaCungCapFormModel model,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var duplicateError = await ValidateDuplicateNameAsync(connection, model.TenNhaCungCap, null, cancellationToken);
            if (duplicateError is not null)
            {
                return (false, duplicateError, null);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO [{TableName}] (
                    TenNhaCungCap,
                    SoDienThoai,
                    Email,
                    DiaChi,
                    TrangThaiSuDung
                )
                VALUES (
                    @TenNhaCungCap,
                    @SoDienThoai,
                    @Email,
                    @DiaChi,
                    @TrangThaiSuDung
                );

                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;
            FillSaveParameters(command, model);

            var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
            return newId > 0
                ? (true, null, newId)
                : (false, "Không thể thêm mới nhà cung cấp.", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TblNhaCungCap.");
            return (false, "Không thể thêm mới nhà cung cấp lúc này.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        NhaCungCapFormModel model,
        CancellationToken cancellationToken = default)
    {
        if (model.Id is null or <= 0)
        {
            return (false, "Không xác định được nhà cung cấp cần cập nhật.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var duplicateError = await ValidateDuplicateNameAsync(connection, model.TenNhaCungCap, model.Id, cancellationToken);
            if (duplicateError is not null)
            {
                return (false, duplicateError);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                UPDATE [{TableName}]
                SET
                    TenNhaCungCap = @TenNhaCungCap,
                    SoDienThoai = @SoDienThoai,
                    Email = @Email,
                    DiaChi = @DiaChi,
                    TrangThaiSuDung = @TrangThaiSuDung
                WHERE ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id.Value });
            FillSaveParameters(command, model);

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            return affectedRows > 0
                ? (true, null)
                : (false, "Không tìm thấy nhà cung cấp để cập nhật.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update TblNhaCungCap {Id}.", model.Id);
            return (false, "Không thể cập nhật nhà cung cấp lúc này.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return (false, "Không xác định được nhà cung cấp cần xóa.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                DELETE FROM [{TableName}]
                WHERE ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            return affectedRows > 0
                ? (true, null)
                : (false, "Không tìm thấy nhà cung cấp để xóa.");
        }
        catch (SqlException ex) when (ex.Number is 547)
        {
            _logger.LogWarning(ex, "Cannot delete TblNhaCungCap {Id} because it is referenced.", id);
            return (false, "Nhà cung cấp đã được sử dụng, không thể xóa.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete TblNhaCungCap {Id}.", id);
            return (false, "Không thể xóa nhà cung cấp lúc này.");
        }
    }

    private static async Task<string?> ValidateDuplicateNameAsync(
        SqlConnection connection,
        string tenNhaCungCap,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1) ID
            FROM [{TableName}]
            WHERE UPPER(LTRIM(RTRIM(TenNhaCungCap))) = UPPER(@TenNhaCungCap)
            {(excludeId.HasValue ? "AND ID <> @ExcludeId" : string.Empty)}
            """;
        command.Parameters.Add(new SqlParameter("@TenNhaCungCap", SqlDbType.NVarChar, 250) { Value = tenNhaCungCap.Trim() });

        if (excludeId.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@ExcludeId", SqlDbType.Int) { Value = excludeId.Value });
        }

        var existingId = await command.ExecuteScalarAsync(cancellationToken);
        return existingId is null ? null : "Tên nhà cung cấp đã tồn tại.";
    }

    private static string BuildWhereClause(string? keyword, bool? statusFilter)
    {
        var filters = new List<string> { "1 = 1" };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filters.Add($"""
                (
                    TenNhaCungCap COLLATE {SearchCollation} LIKE @Keyword OR
                    SoDienThoai COLLATE {SearchCollation} LIKE @Keyword OR
                    Email COLLATE {SearchCollation} LIKE @Keyword OR
                    DiaChi COLLATE {SearchCollation} LIKE @Keyword
                )
                """);
        }

        if (statusFilter.HasValue)
        {
            filters.Add("ISNULL(TrangThaiSuDung, 1) = @TrangThaiSuDung");
        }

        return string.Join(" AND ", filters);
    }

    private static void AddFilterParameters(SqlCommand command, string? keyword, bool? statusFilter)
    {
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            command.Parameters.Add(new SqlParameter("@Keyword", SqlDbType.NVarChar, 300) { Value = $"%{keyword}%" });
        }

        if (statusFilter.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@TrangThaiSuDung", SqlDbType.Bit) { Value = statusFilter.Value });
        }
    }

    private static void FillSaveParameters(SqlCommand command, NhaCungCapFormModel model)
    {
        command.Parameters.Add(new SqlParameter("@TenNhaCungCap", SqlDbType.NVarChar, 250) { Value = model.TenNhaCungCap.Trim() });
        command.Parameters.Add(new SqlParameter("@SoDienThoai", SqlDbType.NVarChar, 50) { Value = ToDbValue(model.SoDienThoai) });
        command.Parameters.Add(new SqlParameter("@Email", SqlDbType.NVarChar, 250) { Value = ToDbValue(model.Email) });
        command.Parameters.Add(new SqlParameter("@DiaChi", SqlDbType.NVarChar, 550) { Value = ToDbValue(model.DiaChi) });
        command.Parameters.Add(new SqlParameter("@TrangThaiSuDung", SqlDbType.Bit) { Value = model.TrangThaiSuDung });
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

    private static NhaCungCapListItem MapItem(SqlDataReader reader)
    {
        return new NhaCungCapListItem
        {
            Id = reader.GetInt32(reader.GetOrdinal("ID")),
            TenNhaCungCap = GetNullableString(reader, "TenNhaCungCap") ?? string.Empty,
            SoDienThoai = GetNullableString(reader, "SoDienThoai"),
            Email = GetNullableString(reader, "Email"),
            DiaChi = GetNullableString(reader, "DiaChi"),
            TrangThaiSuDung = reader.GetBoolean(reader.GetOrdinal("TrangThaiSuDung"))
        };
    }

    private static object ToDbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string? GetNullableString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal)?.ToString()?.Trim();
    }
}
