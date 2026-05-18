using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IPhongBanService
{
    Task<(IReadOnlyList<PhongBanListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        bool? statusFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PhongBanListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        PhongBanFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        PhongBanFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<PhongBanImportResult> ImportAsync(
        IReadOnlyList<PhongBanImportRow> rows,
        string currentUser,
        CancellationToken cancellationToken = default);
}

public sealed class PhongBanService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<PhongBanService> logger) : IPhongBanService
{
    private const string TableName = "TblPhongBan";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<PhongBanService> _logger = logger;

    public async Task<(IReadOnlyList<PhongBanListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
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
            var normalizedKeyword = NormalizeKeyword(keyword);
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
                    TenPhongBan,
                    TenVietTat,
                    CAST(ISNULL(TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung,
                    NguoiTao,
                    NgayTao,
                    NguoiCapNhat,
                    NgayCapNhat
                FROM [{TableName}]
                WHERE {whereClause}
                ORDER BY ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                """;
            AddFilterParameters(listCommand, normalizedKeyword, statusFilter);
            listCommand.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = offset });
            listCommand.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = pageSize });

            var items = new List<PhongBanListItem>();
            await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapItem(reader));
            }

            return (items, totalCount, currentPage, totalPages, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblPhongBan list.");
            return ([], 0, 1, 0, pageSize);
        }
    }

    public async Task<PhongBanListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
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
                    TenPhongBan,
                    TenVietTat,
                    CAST(ISNULL(TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung,
                    NguoiTao,
                    NgayTao,
                    NguoiCapNhat,
                    NgayCapNhat
                FROM [{TableName}]
                WHERE ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? MapItem(reader) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblPhongBan item {Id}.", id);
            return null;
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        PhongBanFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var duplicateError = await ValidateDuplicateNameAsync(connection, model.TenPhongBan, null, cancellationToken);
            if (duplicateError is not null)
            {
                return (false, duplicateError, null);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO [{TableName}] (
                    TenPhongBan,
                    TenVietTat,
                    TrangThaiSuDung,
                    NguoiTao,
                    NgayTao,
                    NguoiCapNhat,
                    NgayCapNhat
                )
                VALUES (
                    @TenPhongBan,
                    @TenVietTat,
                    @TrangThaiSuDung,
                    @NguoiTao,
                    GETDATE(),
                    @NguoiCapNhat,
                    GETDATE()
                );

                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;

            FillSaveParameters(command, model);
            command.Parameters.Add(new SqlParameter("@NguoiTao", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentUser, 100) });
            command.Parameters.Add(new SqlParameter("@NguoiCapNhat", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentUser, 100) });

            var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
            return newId > 0
                ? (true, null, newId)
                : (false, "Không thể thêm mới phòng ban.", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TblPhongBan.");
            return (false, "Không thể thêm mới phòng ban lúc này.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        PhongBanFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (model.Id is null or <= 0)
        {
            return (false, "Không xác định được phòng ban cần cập nhật.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var duplicateError = await ValidateDuplicateNameAsync(connection, model.TenPhongBan, model.Id, cancellationToken);
            if (duplicateError is not null)
            {
                return (false, duplicateError);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                UPDATE [{TableName}]
                SET
                    TenPhongBan = @TenPhongBan,
                    TenVietTat = @TenVietTat,
                    TrangThaiSuDung = @TrangThaiSuDung,
                    NguoiCapNhat = @NguoiCapNhat,
                    NgayCapNhat = GETDATE()
                WHERE ID = @Id
                """;

            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id.Value });
            FillSaveParameters(command, model);
            command.Parameters.Add(new SqlParameter("@NguoiCapNhat", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentUser, 100) });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            return affectedRows > 0
                ? (true, null)
                : (false, "Không tìm thấy phòng ban để cập nhật.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update TblPhongBan {Id}.", model.Id);
            return (false, "Không thể cập nhật phòng ban lúc này.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return (false, "Không xác định được phòng ban cần xóa.");
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
                : (false, "Không tìm thấy phòng ban để xóa.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete TblPhongBan {Id}.", id);
            return (false, "Không thể xóa phòng ban lúc này.");
        }
    }

    public async Task<PhongBanImportResult> ImportAsync(
        IReadOnlyList<PhongBanImportRow> rows,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
        {
            return new PhongBanImportResult();
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"""
                    SELECT UPPER(LTRIM(RTRIM(TenPhongBan)))
                    FROM [{TableName}]
                    WHERE TenPhongBan IS NOT NULL AND LTRIM(RTRIM(TenPhongBan)) <> ''
                    """;

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    existingNames.Add(reader.GetString(0));
                }
            }

            var result = new PhongBanImportResult();
            foreach (var row in rows)
            {
                var tenPhongBan = row.TenPhongBan?.Trim();
                if (string.IsNullOrWhiteSpace(tenPhongBan))
                {
                    result.SkippedCount++;
                    continue;
                }

                var normalizedName = tenPhongBan.ToUpperInvariant();
                if (!existingNames.Add(normalizedName))
                {
                    result.SkippedCount++;
                    continue;
                }

                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"""
                    INSERT INTO [{TableName}] (
                        TenPhongBan,
                        TenVietTat,
                        TrangThaiSuDung,
                        NguoiTao,
                        NgayTao,
                        NguoiCapNhat,
                        NgayCapNhat
                    )
                    VALUES (
                        @TenPhongBan,
                        @TenVietTat,
                        1,
                        @NguoiTao,
                        GETDATE(),
                        @NguoiCapNhat,
                        GETDATE()
                    )
                    """;

                command.Parameters.Add(new SqlParameter("@TenPhongBan", SqlDbType.NVarChar, 300) { Value = tenPhongBan });
                command.Parameters.Add(new SqlParameter("@TenVietTat", SqlDbType.NVarChar, 40) { Value = ToDbValue(row.MaPhongBan) });
                command.Parameters.Add(new SqlParameter("@NguoiTao", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentUser, 100) });
                command.Parameters.Add(new SqlParameter("@NguoiCapNhat", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentUser, 100) });

                await command.ExecuteNonQueryAsync(cancellationToken);
                result.ImportedCount++;
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import TblPhongBan from Excel.");
            return new PhongBanImportResult();
        }
    }

    private async Task<string?> ValidateDuplicateNameAsync(
        SqlConnection connection,
        string tenPhongBan,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1) ID
            FROM [{TableName}]
            WHERE UPPER(LTRIM(RTRIM(TenPhongBan))) = UPPER(@TenPhongBan)
            {(excludeId.HasValue ? "AND ID <> @ExcludeId" : string.Empty)}
            """;
        command.Parameters.Add(new SqlParameter("@TenPhongBan", SqlDbType.NVarChar, 300) { Value = tenPhongBan.Trim() });

        if (excludeId.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@ExcludeId", SqlDbType.Int) { Value = excludeId.Value });
        }

        var existingId = await command.ExecuteScalarAsync(cancellationToken);
        return existingId is null
            ? null
            : "Tên phòng ban đã tồn tại.";
    }

    private static PhongBanListItem MapItem(SqlDataReader reader)
    {
        return new PhongBanListItem
        {
            Id = reader.GetInt32(reader.GetOrdinal("ID")),
            TenPhongBan = GetNullableString(reader, "TenPhongBan") ?? string.Empty,
            TenVietTat = GetNullableString(reader, "TenVietTat"),
            TrangThaiSuDung = reader.GetBoolean(reader.GetOrdinal("TrangThaiSuDung")),
            NguoiTao = GetNullableString(reader, "NguoiTao"),
            NgayTao = GetNullableDateTime(reader, "NgayTao"),
            NguoiCapNhat = GetNullableString(reader, "NguoiCapNhat"),
            NgayCapNhat = GetNullableDateTime(reader, "NgayCapNhat")
        };
    }

    private static string BuildWhereClause(string? keyword, bool? statusFilter)
    {
        var filters = new List<string> { "1 = 1" };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filters.Add("(TenPhongBan LIKE @Keyword OR TenVietTat LIKE @Keyword)");
        }

        if (statusFilter.HasValue)
        {
            filters.Add("TrangThaiSuDung = @TrangThaiSuDung");
        }

        return string.Join(" AND ", filters);
    }

    private static void AddFilterParameters(SqlCommand command, string? keyword, bool? statusFilter)
    {
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            command.Parameters.Add(new SqlParameter("@Keyword", SqlDbType.NVarChar, 300)
            {
                Value = $"%{keyword}%"
            });
        }

        if (statusFilter.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@TrangThaiSuDung", SqlDbType.Bit)
            {
                Value = statusFilter.Value
            });
        }
    }

    private static void FillSaveParameters(SqlCommand command, PhongBanFormModel model)
    {
        command.Parameters.Add(new SqlParameter("@TenPhongBan", SqlDbType.NVarChar, 300)
        {
            Value = model.TenPhongBan.Trim()
        });
        command.Parameters.Add(new SqlParameter("@TenVietTat", SqlDbType.NVarChar, 40)
        {
            Value = ToDbValue(model.TenVietTat)
        });
        command.Parameters.Add(new SqlParameter("@TrangThaiSuDung", SqlDbType.Bit)
        {
            Value = model.TrangThaiSuDung
        });
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

    private static string? NormalizeKeyword(string? keyword)
    {
        return string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
    }

    private static object ToDbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static string TrimToLength(string value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "system" : value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? GetNullableString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal)?.ToString()?.Trim();
    }

    private static DateTime? GetNullableDateTime(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetValue(ordinal) switch
        {
            DateTime typedDate => typedDate,
            string typedString when DateTime.TryParse(typedString, out var parsedDate) => parsedDate,
            _ => null
        };
    }
}
