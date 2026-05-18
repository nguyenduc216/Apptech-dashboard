using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface ICongViecService
{
    Task<(IReadOnlyList<CongViecListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        bool? statusFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<CongViecListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        CongViecFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        CongViecFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<CongViecImportResult> ImportAsync(
        IReadOnlyList<CongViecImportRow> rows,
        string currentUser,
        CancellationToken cancellationToken = default);
}

public sealed class CongViecService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<CongViecService> logger) : ICongViecService
{
    private const string TableName = "TblCongViec";
    private const string ChecklistTableName = "TblCongViecChecklist";
    private const string SearchCollation = "Latin1_General_100_CI_AI";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<CongViecService> _logger = logger;

    public async Task<(IReadOnlyList<CongViecListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
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
                    TenCongViec,
                    MieuTa,
                    CAST(ISNULL(TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung,
                    DonGia,
                    SoLuongAnhCheckIn,
                    SoLuongAnhCheckOut,
                    Created_Date,
                    Created_By,
                    Updated_Date,
                    Updated_By
                FROM [{TableName}]
                WHERE {whereClause}
                ORDER BY ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                """;
            AddFilterParameters(listCommand, normalizedKeyword, statusFilter);
            listCommand.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = offset });
            listCommand.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = pageSize });

            var items = new List<CongViecListItem>();
            await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapItem(reader));
            }

            return (items, totalCount, currentPage, totalPages, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblCongViec list.");
            return ([], 0, 1, 0, pageSize);
        }
    }

    public async Task<CongViecListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
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
                    TenCongViec,
                    MieuTa,
                    CAST(ISNULL(TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung,
                    DonGia,
                    SoLuongAnhCheckIn,
                    SoLuongAnhCheckOut,
                    Created_Date,
                    Created_By,
                    Updated_Date,
                    Updated_By
                FROM [{TableName}]
                WHERE ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var item = MapItem(reader);
            await reader.CloseAsync();
            item.DanhSachChecklist = await LoadChecklistAsync(connection, transaction: null, item.Id, cancellationToken);

            return item;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblCongViec item {Id}.", id);
            return null;
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        CongViecFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            var duplicateError = await ValidateDuplicateNameAsync(connection, transaction, model.TenCongViec, excludeId: null, cancellationToken);
            if (duplicateError is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, duplicateError, null);
            }

            var normalizedChecklistItems = NormalizeChecklistItems(model.DanhSachChecklist);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO [{TableName}] (
                    TenCongViec,
                    MieuTa,
                    TrangThaiSuDung,
                    DonGia,
                    Created_Date,
                    Created_By,
                    Updated_Date,
                    Updated_By,
                    SoLuongAnhCheckIn,
                    SoLuongAnhCheckOut
                )
                VALUES (
                    @TenCongViec,
                    @MieuTa,
                    @TrangThaiSuDung,
                    @DonGia,
                    GETDATE(),
                    @CreatedBy,
                    GETDATE(),
                    @UpdatedBy,
                    @SoLuongAnhCheckIn,
                    @SoLuongAnhCheckOut
                );

                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;

            FillSaveParameters(command, model);
            command.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
            command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });

            var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
            if (newId <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không thể thêm mới công việc.", null);
            }

            await SyncChecklistAsync(connection, transaction, newId, normalizedChecklistItems, currentUser, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return (true, null, newId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TblCongViec.");
            return (false, "Không thể thêm mới công việc lúc này.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        CongViecFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (model.Id is null or <= 0)
        {
            return (false, "Không xác định được công việc cần cập nhật.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            var duplicateError = await ValidateDuplicateNameAsync(connection, transaction, model.TenCongViec, model.Id, cancellationToken);
            if (duplicateError is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, duplicateError);
            }

            var normalizedChecklistItems = NormalizeChecklistItems(model.DanhSachChecklist);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                UPDATE [{TableName}]
                SET
                    TenCongViec = @TenCongViec,
                    MieuTa = @MieuTa,
                    TrangThaiSuDung = @TrangThaiSuDung,
                    DonGia = @DonGia,
                    Updated_Date = GETDATE(),
                    Updated_By = @UpdatedBy,
                    SoLuongAnhCheckIn = @SoLuongAnhCheckIn,
                    SoLuongAnhCheckOut = @SoLuongAnhCheckOut
                WHERE ID = @Id
                """;

            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id.Value });
            FillSaveParameters(command, model);
            command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy công việc để cập nhật.");
            }

            await SyncChecklistAsync(connection, transaction, model.Id.Value, normalizedChecklistItems, currentUser, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update TblCongViec {Id}.", model.Id);
            return (false, "Không thể cập nhật công việc lúc này.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return (false, "Không xác định được công việc cần xóa.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await using (var deleteChecklistCommand = connection.CreateCommand())
            {
                deleteChecklistCommand.Transaction = transaction;
                deleteChecklistCommand.CommandText = $"""
                    DELETE FROM [{ChecklistTableName}]
                    WHERE IDCongViec = @Id
                    """;
                deleteChecklistCommand.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
                await deleteChecklistCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                DELETE FROM [{TableName}]
                WHERE ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy công việc để xóa.");
            }

            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete TblCongViec {Id}.", id);
            return (false, "Không thể xóa công việc lúc này.");
        }
    }

    public async Task<CongViecImportResult> ImportAsync(
        IReadOnlyList<CongViecImportRow> rows,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
        {
            return new CongViecImportResult();
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
                    SELECT UPPER(LTRIM(RTRIM(TenCongViec)))
                    FROM [{TableName}]
                    WHERE TenCongViec IS NOT NULL AND LTRIM(RTRIM(TenCongViec)) <> ''
                    """;

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    existingNames.Add(reader.GetString(0));
                }
            }

            var result = new CongViecImportResult();
            foreach (var row in rows)
            {
                var tenCongViec = row.TenCongViec?.Trim();
                if (string.IsNullOrWhiteSpace(tenCongViec))
                {
                    result.SkippedCount++;
                    continue;
                }

                var normalizedName = tenCongViec.ToUpperInvariant();
                if (!existingNames.Add(normalizedName))
                {
                    result.SkippedCount++;
                    continue;
                }

                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"""
                    INSERT INTO [{TableName}] (
                        TenCongViec,
                        MieuTa,
                        TrangThaiSuDung,
                        DonGia,
                        Created_Date,
                        Created_By,
                        Updated_Date,
                        Updated_By,
                        SoLuongAnhCheckIn,
                        SoLuongAnhCheckOut
                    )
                    VALUES (
                        @TenCongViec,
                        @MieuTa,
                        1,
                        @DonGia,
                        GETDATE(),
                        @CreatedBy,
                        GETDATE(),
                        @UpdatedBy,
                        @SoLuongAnhCheckIn,
                        @SoLuongAnhCheckOut
                    )
                    """;

                command.Parameters.Add(new SqlParameter("@TenCongViec", SqlDbType.NVarChar, 250) { Value = tenCongViec });
                command.Parameters.Add(new SqlParameter("@MieuTa", SqlDbType.NVarChar, 250) { Value = ToDbValue(row.MieuTa) });
                command.Parameters.Add(new SqlParameter("@DonGia", SqlDbType.Decimal)
                {
                    Precision = 18,
                    Scale = 0,
                    Value = ToDbValue(row.DonGia)
                });
                command.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
                command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
                command.Parameters.Add(new SqlParameter("@SoLuongAnhCheckIn", SqlDbType.Int) { Value = ToDbValue(row.SoLuongAnhCheckIn) });
                command.Parameters.Add(new SqlParameter("@SoLuongAnhCheckOut", SqlDbType.Int) { Value = ToDbValue(row.SoLuongAnhCheckOut) });

                await command.ExecuteNonQueryAsync(cancellationToken);
                result.ImportedCount++;
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import TblCongViec from Excel.");
            return new CongViecImportResult();
        }
    }

    private async Task<string?> ValidateDuplicateNameAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string tenCongViec,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT TOP (1) ID
            FROM [{TableName}]
            WHERE UPPER(LTRIM(RTRIM(TenCongViec))) = UPPER(@TenCongViec)
            {(excludeId.HasValue ? "AND ID <> @ExcludeId" : string.Empty)}
            """;
        command.Parameters.Add(new SqlParameter("@TenCongViec", SqlDbType.NVarChar, 250) { Value = tenCongViec.Trim() });

        if (excludeId.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@ExcludeId", SqlDbType.Int) { Value = excludeId.Value });
        }

        var existingId = await command.ExecuteScalarAsync(cancellationToken);
        return existingId is null ? null : "Tên công việc đã tồn tại.";
    }

    private static async Task<IReadOnlyList<CongViecChecklistFormItem>> LoadChecklistAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int congViecId,
        CancellationToken cancellationToken)
    {
        if (congViecId <= 0)
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                TenCheckList,
                ISNULL(ViTri, 0) AS ViTri,
                CAST(ISNULL(TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung
            FROM [{ChecklistTableName}]
            WHERE IDCongViec = @IDCongViec
            ORDER BY
                CASE
                    WHEN ViTri IS NULL OR ViTri <= 0 THEN 2147483647
                    ELSE ViTri
                END,
                ID
            """;
        command.Parameters.Add(new SqlParameter("@IDCongViec", SqlDbType.Int) { Value = congViecId });

        var items = new List<CongViecChecklistFormItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CongViecChecklistFormItem
            {
                TenChecklist = GetNullableString(reader, "TenCheckList") ?? string.Empty,
                ViTri = GetNullableInt32(reader, "ViTri") ?? 0,
                TrangThaiSuDung = reader.GetBoolean(reader.GetOrdinal("TrangThaiSuDung"))
            });
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].ViTri <= 0)
            {
                items[index].ViTri = index + 1;
            }
        }

        return items;
    }

    private static async Task SyncChecklistAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int congViecId,
        IReadOnlyList<CongViecChecklistFormItem> items,
        string currentUser,
        CancellationToken cancellationToken)
    {
        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = $"""
                DELETE FROM [{ChecklistTableName}]
                WHERE IDCongViec = @IDCongViec
                """;
            deleteCommand.Parameters.Add(new SqlParameter("@IDCongViec", SqlDbType.Int) { Value = congViecId });
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (items.Count == 0)
        {
            return;
        }

        var auditUser = TrimToLength(currentUser, 50);
        foreach (var item in items)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = $"""
                INSERT INTO [{ChecklistTableName}] (
                    TenCheckList,
                    ViTri,
                    IDCongViec,
                    Created_Date,
                    Created_By,
                    Updated_Date,
                    Updated_By,
                    TrangThaiSuDung
                )
                VALUES (
                    @TenCheckList,
                    @ViTri,
                    @IDCongViec,
                    GETDATE(),
                    @CreatedBy,
                    GETDATE(),
                    @UpdatedBy,
                    @TrangThaiSuDung
                )
                """;

            insertCommand.Parameters.Add(new SqlParameter("@TenCheckList", SqlDbType.NVarChar, 250) { Value = item.TenChecklist });
            insertCommand.Parameters.Add(new SqlParameter("@ViTri", SqlDbType.Int) { Value = item.ViTri });
            insertCommand.Parameters.Add(new SqlParameter("@IDCongViec", SqlDbType.Int) { Value = congViecId });
            insertCommand.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = auditUser });
            insertCommand.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = auditUser });
            insertCommand.Parameters.Add(new SqlParameter("@TrangThaiSuDung", SqlDbType.Bit) { Value = item.TrangThaiSuDung });

            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static IReadOnlyList<CongViecChecklistFormItem> NormalizeChecklistItems(IEnumerable<CongViecChecklistFormItem>? items)
    {
        if (items is null)
        {
            return [];
        }

        var normalized = new List<CongViecChecklistFormItem>();
        foreach (var item in items)
        {
            var tenChecklist = item.TenChecklist?.Trim();
            if (string.IsNullOrWhiteSpace(tenChecklist))
            {
                continue;
            }

            normalized.Add(new CongViecChecklistFormItem
            {
                TenChecklist = tenChecklist.Length <= 250 ? tenChecklist : tenChecklist[..250],
                TrangThaiSuDung = item.TrangThaiSuDung
            });
        }

        for (var index = 0; index < normalized.Count; index++)
        {
            normalized[index].ViTri = index + 1;
        }

        return normalized;
    }

    private static CongViecListItem MapItem(SqlDataReader reader)
    {
        return new CongViecListItem
        {
            Id = reader.GetInt32(reader.GetOrdinal("ID")),
            TenCongViec = GetNullableString(reader, "TenCongViec") ?? string.Empty,
            MieuTa = GetNullableString(reader, "MieuTa"),
            TrangThaiSuDung = reader.GetBoolean(reader.GetOrdinal("TrangThaiSuDung")),
            DonGia = GetNullableDecimal(reader, "DonGia"),
            SoLuongAnhCheckIn = GetNullableInt32(reader, "SoLuongAnhCheckIn"),
            SoLuongAnhCheckOut = GetNullableInt32(reader, "SoLuongAnhCheckOut"),
            CreatedDate = GetNullableDateTime(reader, "Created_Date"),
            CreatedBy = GetNullableString(reader, "Created_By"),
            UpdatedDate = GetNullableDateTime(reader, "Updated_Date"),
            UpdatedBy = GetNullableString(reader, "Updated_By")
        };
    }

    private static string BuildWhereClause(string? keyword, bool? statusFilter)
    {
        var filters = new List<string> { "1 = 1" };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filters.Add($"""
                (
                    TenCongViec COLLATE {SearchCollation} LIKE @Keyword OR
                    MieuTa COLLATE {SearchCollation} LIKE @Keyword
                )
                """);
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
            command.Parameters.Add(new SqlParameter("@Keyword", SqlDbType.NVarChar, 250)
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

    private static void FillSaveParameters(SqlCommand command, CongViecFormModel model)
    {
        command.Parameters.Add(new SqlParameter("@TenCongViec", SqlDbType.NVarChar, 250)
        {
            Value = model.TenCongViec.Trim()
        });
        command.Parameters.Add(new SqlParameter("@MieuTa", SqlDbType.NVarChar, 250)
        {
            Value = ToDbValue(model.MieuTa)
        });
        command.Parameters.Add(new SqlParameter("@TrangThaiSuDung", SqlDbType.Bit)
        {
            Value = model.TrangThaiSuDung
        });
        command.Parameters.Add(new SqlParameter("@DonGia", SqlDbType.Decimal)
        {
            Precision = 18,
            Scale = 0,
            Value = ToDbValue(model.DonGia)
        });
        command.Parameters.Add(new SqlParameter("@SoLuongAnhCheckIn", SqlDbType.Int)
        {
            Value = ToDbValue(model.SoLuongAnhCheckIn)
        });
        command.Parameters.Add(new SqlParameter("@SoLuongAnhCheckOut", SqlDbType.Int)
        {
            Value = ToDbValue(model.SoLuongAnhCheckOut)
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

    private static object ToDbValue(decimal? value)
    {
        return value.HasValue ? value.Value : DBNull.Value;
    }

    private static object ToDbValue(int? value)
    {
        return value.HasValue ? value.Value : DBNull.Value;
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

    private static int? GetNullableInt32(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetValue(ordinal) switch
        {
            int typedInt => typedInt,
            decimal typedDecimal => Convert.ToInt32(typedDecimal),
            long typedLong => Convert.ToInt32(typedLong),
            string typedString when int.TryParse(typedString, out var parsed) => parsed,
            _ => null
        };
    }

    private static decimal? GetNullableDecimal(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetValue(ordinal) switch
        {
            decimal typedDecimal => typedDecimal,
            double typedDouble => Convert.ToDecimal(typedDouble),
            float typedFloat => Convert.ToDecimal(typedFloat),
            int typedInt => typedInt,
            long typedLong => typedLong,
            string typedString when decimal.TryParse(typedString, out var parsed) => parsed,
            _ => null
        };
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
