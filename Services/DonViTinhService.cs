using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IDonViTinhService
{
    Task<(IReadOnlyList<DonViTinhListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        bool? statusFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<DonViTinhListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        DonViTinhFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        DonViTinhFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<DonViTinhImportResult> ImportAsync(
        IReadOnlyList<DonViTinhImportRow> rows,
        string currentUser,
        CancellationToken cancellationToken = default);
}

public sealed class DonViTinhService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<DonViTinhService> logger) : IDonViTinhService
{
    private const string TableName = "TblDonViTinh";
    private const string DefaultType = "DV";
    private const string SearchCollation = "Latin1_General_100_CI_AI";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<DonViTinhService> _logger = logger;

    public async Task<(IReadOnlyList<DonViTinhListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
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
                    TenDonVi,
                    TenVietTat,
                    CAST(ISNULL(TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung,
                    NguoiTao,
                    NgayTao,
                    NguoiCapNhap,
                    NgayCapNhat
                FROM [{TableName}]
                WHERE {whereClause}
                ORDER BY ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                """;
            AddFilterParameters(listCommand, normalizedKeyword, statusFilter);
            listCommand.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = offset });
            listCommand.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = pageSize });

            var items = new List<DonViTinhListItem>();
            await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapItem(reader));
            }

            return (items, totalCount, currentPage, totalPages, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblDonViTinh list.");
            return ([], 0, 1, 0, pageSize);
        }
    }

    public async Task<DonViTinhListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
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
                    TenDonVi,
                    TenVietTat,
                    CAST(ISNULL(TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung,
                    NguoiTao,
                    NgayTao,
                    NguoiCapNhap,
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
            _logger.LogError(ex, "Failed to load TblDonViTinh item {Id}.", id);
            return null;
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        DonViTinhFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var duplicateError = await ValidateDuplicateNameAsync(connection, model.TenDonVi, null, cancellationToken);
            if (duplicateError is not null)
            {
                return (false, duplicateError, null);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO [{TableName}] (
                    TenDonVi,
                    TenVietTat,
                    TrangThaiSuDung,
                    NguoiTao,
                    NgayTao,
                    NguoiCapNhap,
                    NgayCapNhat,
                    [Type]
                )
                VALUES (
                    @TenDonVi,
                    @TenVietTat,
                    @TrangThaiSuDung,
                    @NguoiTao,
                    GETDATE(),
                    @NguoiCapNhap,
                    GETDATE(),
                    @Type
                );

                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;

            FillSaveParameters(command, model);
            command.Parameters.Add(new SqlParameter("@NguoiTao", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentUser, 100) });
            command.Parameters.Add(new SqlParameter("@NguoiCapNhap", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentUser, 100) });
            command.Parameters.Add(new SqlParameter("@Type", SqlDbType.NVarChar, 100) { Value = DefaultType });

            var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
            return newId > 0
                ? (true, null, newId)
                : (false, "Không thể thêm mới đơn vị tính.", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TblDonViTinh.");
            return (false, "Không thể thêm mới đơn vị tính lúc này.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        DonViTinhFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (model.Id is null or <= 0)
        {
            return (false, "Không xác định được đơn vị tính cần cập nhật.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var duplicateError = await ValidateDuplicateNameAsync(connection, model.TenDonVi, model.Id, cancellationToken);
            if (duplicateError is not null)
            {
                return (false, duplicateError);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                UPDATE [{TableName}]
                SET
                    TenDonVi = @TenDonVi,
                    TenVietTat = @TenVietTat,
                    TrangThaiSuDung = @TrangThaiSuDung,
                    NguoiCapNhap = @NguoiCapNhap,
                    NgayCapNhat = GETDATE()
                WHERE ID = @Id
                """;

            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id.Value });
            FillSaveParameters(command, model);
            command.Parameters.Add(new SqlParameter("@NguoiCapNhap", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentUser, 100) });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            return affectedRows > 0
                ? (true, null)
                : (false, "Không tìm thấy đơn vị tính để cập nhật.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update TblDonViTinh {Id}.", model.Id);
            return (false, "Không thể cập nhật đơn vị tính lúc này.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return (false, "Không xác định được đơn vị tính cần xóa.");
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
                : (false, "Không tìm thấy đơn vị tính để xóa.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete TblDonViTinh {Id}.", id);
            return (false, "Không thể xóa đơn vị tính lúc này.");
        }
    }

    public async Task<DonViTinhImportResult> ImportAsync(
        IReadOnlyList<DonViTinhImportRow> rows,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
        {
            return new DonViTinhImportResult();
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
                    SELECT UPPER(LTRIM(RTRIM(TenDonVi)))
                    FROM [{TableName}]
                    WHERE TenDonVi IS NOT NULL AND LTRIM(RTRIM(TenDonVi)) <> ''
                    """;

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    existingNames.Add(reader.GetString(0));
                }
            }

            var result = new DonViTinhImportResult();
            foreach (var row in rows)
            {
                var tenDonVi = row.TenDonVi?.Trim();
                if (string.IsNullOrWhiteSpace(tenDonVi))
                {
                    result.SkippedCount++;
                    continue;
                }

                var normalizedName = tenDonVi.ToUpperInvariant();
                if (!existingNames.Add(normalizedName))
                {
                    result.SkippedCount++;
                    continue;
                }

                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"""
                    INSERT INTO [{TableName}] (
                        TenDonVi,
                        TenVietTat,
                        TrangThaiSuDung,
                        NguoiTao,
                        NgayTao,
                        NguoiCapNhap,
                        NgayCapNhat,
                        [Type]
                    )
                    VALUES (
                        @TenDonVi,
                        @TenVietTat,
                        1,
                        @NguoiTao,
                        GETDATE(),
                        @NguoiCapNhap,
                        GETDATE(),
                        @Type
                    )
                    """;

                command.Parameters.Add(new SqlParameter("@TenDonVi", SqlDbType.NVarChar, 300) { Value = tenDonVi });
                command.Parameters.Add(new SqlParameter("@TenVietTat", SqlDbType.NVarChar, 40) { Value = ToDbValue(row.MaDonVi) });
                command.Parameters.Add(new SqlParameter("@NguoiTao", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentUser, 100) });
                command.Parameters.Add(new SqlParameter("@NguoiCapNhap", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentUser, 100) });
                command.Parameters.Add(new SqlParameter("@Type", SqlDbType.NVarChar, 100) { Value = DefaultType });

                await command.ExecuteNonQueryAsync(cancellationToken);
                result.ImportedCount++;
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import TblDonViTinh from Excel.");
            return new DonViTinhImportResult();
        }
    }

    private async Task<string?> ValidateDuplicateNameAsync(
        SqlConnection connection,
        string tenDonVi,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1) ID
            FROM [{TableName}]
            WHERE UPPER(LTRIM(RTRIM(TenDonVi))) = UPPER(@TenDonVi)
            {(excludeId.HasValue ? "AND ID <> @ExcludeId" : string.Empty)}
            """;
        command.Parameters.Add(new SqlParameter("@TenDonVi", SqlDbType.NVarChar, 300) { Value = tenDonVi.Trim() });

        if (excludeId.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@ExcludeId", SqlDbType.Int) { Value = excludeId.Value });
        }

        var existingId = await command.ExecuteScalarAsync(cancellationToken);
        return existingId is null
            ? null
            : "Tên đơn vị đã tồn tại.";
    }

    private static DonViTinhListItem MapItem(SqlDataReader reader)
    {
        return new DonViTinhListItem
        {
            Id = reader.GetInt32(reader.GetOrdinal("ID")),
            TenDonVi = GetNullableString(reader, "TenDonVi") ?? string.Empty,
            TenVietTat = GetNullableString(reader, "TenVietTat"),
            TrangThaiSuDung = reader.GetBoolean(reader.GetOrdinal("TrangThaiSuDung")),
            NguoiTao = GetNullableString(reader, "NguoiTao"),
            NgayTao = GetNullableDateTime(reader, "NgayTao"),
            NguoiCapNhap = GetNullableString(reader, "NguoiCapNhap"),
            NgayCapNhat = GetNullableDateTime(reader, "NgayCapNhat")
        };
    }

    private static string BuildWhereClause(string? keyword, bool? statusFilter)
    {
        var filters = new List<string> { "1 = 1" };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filters.Add($"TenDonVi COLLATE {SearchCollation} LIKE @Keyword");
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

    private static void FillSaveParameters(SqlCommand command, DonViTinhFormModel model)
    {
        command.Parameters.Add(new SqlParameter("@TenDonVi", SqlDbType.NVarChar, 300)
        {
            Value = model.TenDonVi.Trim()
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
