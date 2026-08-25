using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IDanhMucDichVuService
{
    Task<(IReadOnlyList<DanhMucDichVuListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        bool? statusFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<DanhMucDichVuListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        DanhMucDichVuFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        DanhMucDichVuFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(
        int id,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DanhMucDichVuOption>> GetActiveOptionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DanhMucDichVuWorkItem>> GetWorkOptionsAsync(
        int? serviceId = null,
        CancellationToken cancellationToken = default);

    Task<(DanhMucDichVuOption? Service, IReadOnlyList<DanhMucDichVuWorkItem> Works)> GetWorksAsync(
        int serviceId,
        CancellationToken cancellationToken = default);
}

public sealed class DanhMucDichVuService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<DanhMucDichVuService> logger) : IDanhMucDichVuService
{
    private const string TableName = "TblDanhMucDichVu";
    private const string MappingTableName = "TblDanhMucDichVuCongViec";
    private const string WorkTableName = "TblCongViec";
    private const string WorkChecklistTableName = "TblCongViecChecklist";
    private const string RequestTableName = "TblYeuCau";
    private const string SearchCollation = "Latin1_General_100_CI_AI";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<DanhMucDichVuService> _logger = logger;

    public async Task<(IReadOnlyList<DanhMucDichVuListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
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
                FROM [{TableName}] AS dv
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
                    dv.ID,
                    dv.TenDichVu,
                    dv.MieuTa,
                    CAST(ISNULL(dv.TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung,
                    dv.Created_Date,
                    dv.Created_By,
                    dv.Updated_Date,
                    dv.Updated_By,
                    ISNULL(workStats.SoCongViec, 0) AS SoCongViec
                FROM [{TableName}] AS dv
                OUTER APPLY (
                    SELECT COUNT(1) AS SoCongViec
                    FROM [{MappingTableName}] AS map
                    WHERE map.IDDanhMucDichVu = dv.ID
                ) AS workStats
                WHERE {whereClause}
                ORDER BY ISNULL(dv.Updated_Date, dv.Created_Date) DESC, dv.ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                """;
            AddFilterParameters(listCommand, normalizedKeyword, statusFilter);
            listCommand.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = offset });
            listCommand.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = pageSize });

            var items = new List<DanhMucDichVuListItem>();
            await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapListItem(reader));
            }

            return (items, totalCount, currentPage, totalPages, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load service catalog list.");
            return ([], 0, 1, 0, pageSize);
        }
    }

    public async Task<DanhMucDichVuListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
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
                    dv.ID,
                    dv.TenDichVu,
                    dv.MieuTa,
                    CAST(ISNULL(dv.TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung,
                    dv.Created_Date,
                    dv.Created_By,
                    dv.Updated_Date,
                    dv.Updated_By,
                    ISNULL(workStats.SoCongViec, 0) AS SoCongViec
                FROM [{TableName}] AS dv
                OUTER APPLY (
                    SELECT COUNT(1) AS SoCongViec
                    FROM [{MappingTableName}] AS map
                    WHERE map.IDDanhMucDichVu = dv.ID
                ) AS workStats
                WHERE dv.ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var item = MapListItem(reader);
            await reader.CloseAsync();
            item.CongViecs = await GetWorkOptionsAsync(id, cancellationToken);
            item.SoCongViec = item.CongViecs.Count;
            return item;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load service catalog {Id}.", id);
            return null;
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        DanhMucDichVuFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            var validationError = await ValidateAsync(connection, transaction, model, excludeId: null, cancellationToken);
            if (validationError is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, validationError, null);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO [{TableName}] (
                    TenDichVu,
                    MieuTa,
                    TrangThaiSuDung,
                    Created_Date,
                    Created_By,
                    Updated_Date,
                    Updated_By
                )
                VALUES (
                    @TenDichVu,
                    @MieuTa,
                    @TrangThaiSuDung,
                    GETDATE(),
                    @CreatedBy,
                    GETDATE(),
                    @UpdatedBy
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
                return (false, "Không thể thêm dịch vụ.", null);
            }

            await SyncWorksAsync(connection, transaction, newId, model.CongViecs, currentUser, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (true, null, newId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create service catalog.");
            return (false, "Không thể thêm dịch vụ lúc này.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        DanhMucDichVuFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (model.Id is null or <= 0)
        {
            return (false, "Không xác định được dịch vụ cần cập nhật.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            var validationError = await ValidateAsync(connection, transaction, model, model.Id, cancellationToken);
            if (validationError is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, validationError);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                UPDATE [{TableName}]
                SET
                    TenDichVu = @TenDichVu,
                    MieuTa = @MieuTa,
                    TrangThaiSuDung = @TrangThaiSuDung,
                    Updated_Date = GETDATE(),
                    Updated_By = @UpdatedBy
                WHERE ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id.Value });
            FillSaveParameters(command, model);
            command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy dịch vụ để cập nhật.");
            }

            await SyncWorksAsync(connection, transaction, model.Id.Value, model.CongViecs, currentUser, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update service catalog {Id}.", model.Id);
            return (false, "Không thể cập nhật dịch vụ lúc này.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(
        int id,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return (false, "Không xác định được dịch vụ cần xóa.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            if (await IsReferencedByRequestAsync(connection, transaction, id, cancellationToken))
            {
                await using var disableCommand = connection.CreateCommand();
                disableCommand.Transaction = transaction;
                disableCommand.CommandText = $"""
                    UPDATE [{TableName}]
                    SET TrangThaiSuDung = 0,
                        Updated_Date = GETDATE(),
                        Updated_By = @UpdatedBy
                    WHERE ID = @Id
                    """;
                disableCommand.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
                disableCommand.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
                await disableCommand.ExecuteNonQueryAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return (true, "Dịch vụ đã được sử dụng trong phiếu yêu cầu nên hệ thống chuyển sang Ngưng sử dụng.");
            }

            await using (var deleteMapCommand = connection.CreateCommand())
            {
                deleteMapCommand.Transaction = transaction;
                deleteMapCommand.CommandText = $"""
                    DELETE FROM [{MappingTableName}]
                    WHERE IDDanhMucDichVu = @Id
                    """;
                deleteMapCommand.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
                await deleteMapCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = $"""
                DELETE FROM [{TableName}]
                WHERE ID = @Id
                """;
            deleteCommand.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
            var affectedRows = await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy dịch vụ để xóa.");
            }

            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete service catalog {Id}.", id);
            return (false, "Không thể xóa dịch vụ lúc này.");
        }
    }

    public async Task<IReadOnlyList<DanhMucDichVuOption>> GetActiveOptionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT
                    dv.ID,
                    dv.TenDichVu,
                    ISNULL(workStats.SoCongViec, 0) AS SoCongViec
                FROM [{TableName}] AS dv
                OUTER APPLY (
                    SELECT COUNT(1) AS SoCongViec
                    FROM [{MappingTableName}] AS map
                    WHERE map.IDDanhMucDichVu = dv.ID
                ) AS workStats
                WHERE ISNULL(dv.TrangThaiSuDung, 1) = 1
                ORDER BY dv.TenDichVu, dv.ID
                """;

            var items = new List<DanhMucDichVuOption>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new DanhMucDichVuOption
                {
                    Id = reader.GetInt32(reader.GetOrdinal("ID")),
                    TenDichVu = GetNullableString(reader, "TenDichVu") ?? string.Empty,
                    SoCongViec = GetNullableInt32(reader, "SoCongViec") ?? 0
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load active service catalog options.");
            return [];
        }
    }

    public async Task<IReadOnlyList<DanhMucDichVuWorkItem>> GetWorkOptionsAsync(
        int? serviceId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            return await LoadWorkOptionsAsync(connection, serviceId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load service catalog work options.");
            return [];
        }
    }

    public async Task<(DanhMucDichVuOption? Service, IReadOnlyList<DanhMucDichVuWorkItem> Works)> GetWorksAsync(
        int serviceId,
        CancellationToken cancellationToken = default)
    {
        if (serviceId <= 0)
        {
            return (null, []);
        }

        try
        {
            var service = await GetOptionByIdAsync(serviceId, cancellationToken);
            if (service is null)
            {
                return (null, []);
            }

            var works = await GetWorkOptionsAsync(serviceId, cancellationToken);
            return (service, works);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load service catalog works for {Id}.", serviceId);
            return (null, []);
        }
    }

    private async Task<DanhMucDichVuOption?> GetOptionByIdAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1)
                dv.ID,
                dv.TenDichVu,
                ISNULL(workStats.SoCongViec, 0) AS SoCongViec
            FROM [{TableName}] AS dv
            OUTER APPLY (
                SELECT COUNT(1) AS SoCongViec
                FROM [{MappingTableName}] AS map
                WHERE map.IDDanhMucDichVu = dv.ID
            ) AS workStats
            WHERE dv.ID = @Id
            """;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new DanhMucDichVuOption
        {
            Id = reader.GetInt32(reader.GetOrdinal("ID")),
            TenDichVu = GetNullableString(reader, "TenDichVu") ?? string.Empty,
            SoCongViec = GetNullableInt32(reader, "SoCongViec") ?? 0
        };
    }

    private static async Task<IReadOnlyList<DanhMucDichVuWorkItem>> LoadWorkOptionsAsync(
        SqlConnection connection,
        int? serviceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        if (serviceId.HasValue && serviceId.Value > 0)
        {
            command.CommandText = $"""
                SELECT
                    cv.ID,
                    cv.TenCongViec,
                    cv.MieuTa,
                    cv.DonGia,
                    cv.SoLuongAnhCheckIn,
                    cv.SoLuongAnhCheckOut,
                    CAST(ISNULL(cv.TrangThaiSuDung, 1) AS bit) AS TrangThaiSuDung,
                    ISNULL(map.ThuTu, 0) AS ThuTu,
                    ckt.ID AS ChecklistId,
                    ckt.TenCheckList,
                    ISNULL(ckt.ViTri, 0) AS ViTri
                FROM [{MappingTableName}] AS map
                INNER JOIN [{WorkTableName}] AS cv ON cv.ID = map.IDCongViec
                LEFT JOIN [{WorkChecklistTableName}] AS ckt
                    ON ckt.IDCongViec = cv.ID
                    AND ISNULL(ckt.TrangThaiSuDung, 1) = 1
                WHERE map.IDDanhMucDichVu = @ServiceId
                ORDER BY
                    CASE WHEN ISNULL(map.ThuTu, 0) <= 0 THEN 2147483647 ELSE map.ThuTu END,
                    map.ID,
                    CASE WHEN ckt.ViTri IS NULL OR ckt.ViTri <= 0 THEN 2147483647 ELSE ckt.ViTri END,
                    ckt.ID
                """;
            command.Parameters.Add(new SqlParameter("@ServiceId", SqlDbType.Int) { Value = serviceId.Value });
        }
        else
        {
            command.CommandText = $"""
                SELECT
                    cv.ID,
                    cv.TenCongViec,
                    cv.MieuTa,
                    cv.DonGia,
                    cv.SoLuongAnhCheckIn,
                    cv.SoLuongAnhCheckOut,
                    CAST(ISNULL(cv.TrangThaiSuDung, 1) AS bit) AS TrangThaiSuDung,
                    0 AS ThuTu,
                    ckt.ID AS ChecklistId,
                    ckt.TenCheckList,
                    ISNULL(ckt.ViTri, 0) AS ViTri
                FROM [{WorkTableName}] AS cv
                LEFT JOIN [{WorkChecklistTableName}] AS ckt
                    ON ckt.IDCongViec = cv.ID
                    AND ISNULL(ckt.TrangThaiSuDung, 1) = 1
                WHERE ISNULL(cv.TrangThaiSuDung, 1) = 1
                ORDER BY cv.TenCongViec,
                    CASE WHEN ckt.ViTri IS NULL OR ckt.ViTri <= 0 THEN 2147483647 ELSE ckt.ViTri END,
                    ckt.ID
                """;
        }

        var lookup = new Dictionary<int, DanhMucDichVuWorkItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var workId = reader.GetInt32(reader.GetOrdinal("ID"));
            if (!lookup.TryGetValue(workId, out var item))
            {
                item = new DanhMucDichVuWorkItem
                {
                    IDCongViec = workId,
                    TenCongViec = GetNullableString(reader, "TenCongViec") ?? "Công việc",
                    MieuTa = GetNullableString(reader, "MieuTa"),
                    DonGia = GetNullableDecimal(reader, "DonGia"),
                    SoLuongAnhCheckIn = Math.Max(0, GetNullableInt32(reader, "SoLuongAnhCheckIn") ?? 0),
                    SoLuongAnhCheckOut = Math.Max(0, GetNullableInt32(reader, "SoLuongAnhCheckOut") ?? 0),
                    ThuTu = GetNullableInt32(reader, "ThuTu") ?? 0,
                    TrangThaiSuDung = GetNullableBoolean(reader, "TrangThaiSuDung") ?? true,
                    Checklists = []
                };
                lookup[workId] = item;
            }

            var checklistId = GetNullableInt32(reader, "ChecklistId");
            if (checklistId.HasValue && checklistId.Value > 0)
            {
                var current = item.Checklists.ToList();
                current.Add(new YeuCauCongViecChecklistFormItem
                {
                    ChecklistId = checklistId.Value,
                    TenChecklist = GetNullableString(reader, "TenCheckList") ?? string.Empty,
                    ViTri = GetNullableInt32(reader, "ViTri") ?? 0
                });
                item.Checklists = current;
            }
        }

        return lookup.Values
            .OrderBy(item => serviceId.HasValue ? item.ThuTu <= 0 ? int.MaxValue : item.ThuTu : 0)
            .ThenBy(item => item.TenCongViec)
            .ToList();
    }

    private async Task<string?> ValidateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DanhMucDichVuFormModel model,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        var name = model.TenDichVu?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Vui lòng nhập tên dịch vụ.";
        }

        await using (var duplicateCommand = connection.CreateCommand())
        {
            duplicateCommand.Transaction = transaction;
            duplicateCommand.CommandText = $"""
                SELECT TOP (1) ID
                FROM [{TableName}]
                WHERE TenDichVu COLLATE {SearchCollation} = @TenDichVu COLLATE {SearchCollation}
                {(excludeId.HasValue ? "AND ID <> @ExcludeId" : string.Empty)}
                """;
            duplicateCommand.Parameters.Add(new SqlParameter("@TenDichVu", SqlDbType.NVarChar, 250) { Value = name });
            if (excludeId.HasValue)
            {
                duplicateCommand.Parameters.Add(new SqlParameter("@ExcludeId", SqlDbType.Int) { Value = excludeId.Value });
            }

            if (await duplicateCommand.ExecuteScalarAsync(cancellationToken) is not null)
            {
                return "Tên dịch vụ đã tồn tại.";
            }
        }

        var workIds = NormalizeWorks(model.CongViecs).Select(item => item.IDCongViec).ToList();
        if (workIds.Count == 0)
        {
            return "Dịch vụ phải có tối thiểu 1 công việc.";
        }

        if (workIds.Count != workIds.Distinct().Count())
        {
            return "Không được chọn trùng công việc trong cùng một dịch vụ.";
        }

        var validIds = await LoadValidWorkIdsAsync(connection, transaction, workIds, cancellationToken);
        var invalidIds = workIds.Except(validIds).ToList();
        if (invalidIds.Count > 0)
        {
            return "Danh sách công việc có mục không tồn tại hoặc đã ngưng sử dụng.";
        }

        return null;
    }

    private static async Task<HashSet<int>> LoadValidWorkIdsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyList<int> workIds,
        CancellationToken cancellationToken)
    {
        if (workIds.Count == 0)
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var placeholders = new List<string>();
        for (var index = 0; index < workIds.Count; index++)
        {
            var parameterName = $"@WorkId{index}";
            placeholders.Add(parameterName);
            command.Parameters.Add(new SqlParameter(parameterName, SqlDbType.Int) { Value = workIds[index] });
        }

        command.CommandText = $"""
            SELECT ID
            FROM [{WorkTableName}]
            WHERE ISNULL(TrangThaiSuDung, 1) = 1
              AND ID IN ({string.Join(", ", placeholders)})
            """;

        var result = new HashSet<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetInt32(reader.GetOrdinal("ID")));
        }

        return result;
    }

    private static async Task SyncWorksAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int serviceId,
        IEnumerable<DanhMucDichVuFormWorkItem>? works,
        string currentUser,
        CancellationToken cancellationToken)
    {
        var normalizedWorks = NormalizeWorks(works);
        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = $"""
                DELETE FROM [{MappingTableName}]
                WHERE IDDanhMucDichVu = @ServiceId
                """;
            deleteCommand.Parameters.Add(new SqlParameter("@ServiceId", SqlDbType.Int) { Value = serviceId });
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var auditUser = TrimToLength(currentUser, 50);
        foreach (var work in normalizedWorks)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = $"""
                INSERT INTO [{MappingTableName}] (
                    IDDanhMucDichVu,
                    IDCongViec,
                    ThuTu,
                    Created_Date,
                    Created_By
                )
                VALUES (
                    @ServiceId,
                    @WorkId,
                    @ThuTu,
                    GETDATE(),
                    @CreatedBy
                )
                """;
            insertCommand.Parameters.Add(new SqlParameter("@ServiceId", SqlDbType.Int) { Value = serviceId });
            insertCommand.Parameters.Add(new SqlParameter("@WorkId", SqlDbType.Int) { Value = work.IDCongViec });
            insertCommand.Parameters.Add(new SqlParameter("@ThuTu", SqlDbType.Int) { Value = work.ThuTu });
            insertCommand.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = auditUser });
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static IReadOnlyList<DanhMucDichVuFormWorkItem> NormalizeWorks(IEnumerable<DanhMucDichVuFormWorkItem>? works)
    {
        if (works is null)
        {
            return [];
        }

        return works
            .Where(item => item.IDCongViec > 0)
            .GroupBy(item => item.IDCongViec)
            .Select((group, index) => new DanhMucDichVuFormWorkItem
            {
                IDCongViec = group.Key,
                ThuTu = group.First().ThuTu > 0 ? group.First().ThuTu : index + 1
            })
            .OrderBy(item => item.ThuTu)
            .ToList();
    }

    private static async Task<bool> IsReferencedByRequestAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int serviceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT TOP (1) 1
            FROM [{RequestTableName}]
            WHERE IDDanhMucDichVu = @ServiceId
            """;
        command.Parameters.Add(new SqlParameter("@ServiceId", SqlDbType.Int) { Value = serviceId });
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static DanhMucDichVuListItem MapListItem(SqlDataReader reader)
    {
        return new DanhMucDichVuListItem
        {
            Id = reader.GetInt32(reader.GetOrdinal("ID")),
            TenDichVu = GetNullableString(reader, "TenDichVu") ?? string.Empty,
            MieuTa = GetNullableString(reader, "MieuTa"),
            TrangThaiSuDung = GetNullableBoolean(reader, "TrangThaiSuDung") ?? false,
            CreatedDate = GetNullableDateTime(reader, "Created_Date"),
            CreatedBy = GetNullableString(reader, "Created_By"),
            UpdatedDate = GetNullableDateTime(reader, "Updated_Date"),
            UpdatedBy = GetNullableString(reader, "Updated_By"),
            SoCongViec = GetNullableInt32(reader, "SoCongViec") ?? 0
        };
    }

    private static string BuildWhereClause(string? keyword, bool? statusFilter)
    {
        var filters = new List<string> { "1 = 1" };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filters.Add($"""
                (
                    dv.TenDichVu COLLATE {SearchCollation} LIKE @Keyword OR
                    dv.MieuTa COLLATE {SearchCollation} LIKE @Keyword
                )
                """);
        }

        if (statusFilter.HasValue)
        {
            filters.Add("ISNULL(dv.TrangThaiSuDung, 0) = @TrangThaiSuDung");
        }

        return string.Join(" AND ", filters);
    }

    private static void AddFilterParameters(SqlCommand command, string? keyword, bool? statusFilter)
    {
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            command.Parameters.Add(new SqlParameter("@Keyword", SqlDbType.NVarChar, 250) { Value = $"%{keyword}%" });
        }

        if (statusFilter.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@TrangThaiSuDung", SqlDbType.Bit) { Value = statusFilter.Value });
        }
    }

    private static void FillSaveParameters(SqlCommand command, DanhMucDichVuFormModel model)
    {
        command.Parameters.Add(new SqlParameter("@TenDichVu", SqlDbType.NVarChar, 250) { Value = model.TenDichVu.Trim() });
        command.Parameters.Add(new SqlParameter("@MieuTa", SqlDbType.NVarChar, 1000) { Value = ToDbValue(model.MieuTa) });
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

    private static bool? GetNullableBoolean(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetValue(ordinal) switch
        {
            bool typedBool => typedBool,
            int typedInt => typedInt != 0,
            byte typedByte => typedByte != 0,
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
