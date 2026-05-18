using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IKhachHangService
{
    Task<(IReadOnlyList<KhachHangListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<KhachHangListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        KhachHangFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        KhachHangFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, KhachHangDiaDiemFormItem? Item)> SaveLocationAsync(
        KhachHangDiaDiemSaveModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> DeleteLocationAsync(
        int khachHangId,
        int locationId,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class KhachHangService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<KhachHangService> logger) : IKhachHangService
{
    private const string TableName = "TblKhachHang";
    private const string LocationTableName = "TblKhachHangDiaDiem";
    private const string SearchCollation = "Latin1_General_100_CI_AI";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<KhachHangService> _logger = logger;

    public async Task<(IReadOnlyList<KhachHangListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
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
            var whereClause = BuildWhereClause(normalizedKeyword);

            await using var countCommand = connection.CreateCommand();
            countCommand.CommandText = $"""
                SELECT COUNT(1)
                FROM [{TableName}] AS kh
                WHERE {whereClause}
                """;
            AddFilterParameters(countCommand, normalizedKeyword);

            var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
            var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            var currentPage = totalPages == 0 ? 1 : Math.Min(page, totalPages);
            var offset = (currentPage - 1) * pageSize;

            await using var listCommand = connection.CreateCommand();
            listCommand.CommandText = $"""
                SELECT
                    ID,
                    TenKhachHang,
                    MaKhachHang,
                    DiaChi,
                    SoDienThoai,
                    NguoiDaiDien,
                    NganhNghe,
                    ZaloID,
                    GhiChu,
                    Created_By,
                    Created_Date,
                    Updated_By,
                    Updated_Date
                FROM [{TableName}] AS kh
                WHERE {whereClause}
                ORDER BY ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                """;
            AddFilterParameters(listCommand, normalizedKeyword);
            listCommand.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = offset });
            listCommand.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = pageSize });

            var items = new List<KhachHangListItem>();
            await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapItem(reader));
            }

            return (items, totalCount, currentPage, totalPages, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblKhachHang list.");
            return ([], 0, 1, 0, pageSize);
        }
    }

    public async Task<KhachHangListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
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
                    TenKhachHang,
                    MaKhachHang,
                    DiaChi,
                    SoDienThoai,
                    NguoiDaiDien,
                    NganhNghe,
                    ZaloID,
                    GhiChu,
                    Created_By,
                    Created_Date,
                    Updated_By,
                    Updated_Date
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
            item.DiaDiemLamViec = await LoadDiaDiemLamViecAsync(connection, transaction: null, item.Id, cancellationToken);

            return item;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblKhachHang item {Id}.", id);
            return null;
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        KhachHangFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            var duplicateError = await ValidateDuplicateCodeAsync(connection, transaction, model.MaKhachHang, excludeId: null, cancellationToken);
            if (duplicateError is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, duplicateError, null);
            }

            var normalizedLocations = NormalizeDiaDiemLamViec(model.DiaDiemLamViec);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO [{TableName}] (
                    TenKhachHang,
                    MaKhachHang,
                    DiaChi,
                    SoDienThoai,
                    NguoiDaiDien,
                    NganhNghe,
                    ZaloID,
                    ZaloLastUpdate,
                    GhiChu,
                    Created_Date,
                    Created_By,
                    Updated_Date,
                    Updated_By
                )
                VALUES (
                    @TenKhachHang,
                    @MaKhachHang,
                    @DiaChi,
                    @SoDienThoai,
                    @NguoiDaiDien,
                    @NganhNghe,
                    @ZaloID,
                    CASE WHEN NULLIF(LTRIM(RTRIM(@ZaloID)), '') IS NULL THEN NULL ELSE GETDATE() END,
                    @GhiChu,
                    GETDATE(),
                    @CreatedBy,
                    GETDATE(),
                    @UpdatedBy
                );

                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;

            FillSaveParameters(command, model);
            command.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentUser, 100) });
            command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentUser, 100) });

            var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
            if (newId <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không thể thêm mới khách hàng.", null);
            }

            await SyncDiaDiemLamViecAsync(connection, transaction, newId, normalizedLocations, currentUser, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return (true, null, newId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TblKhachHang.");
            return (false, "Không thể thêm mới khách hàng lúc này.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        KhachHangFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (model.Id is null or <= 0)
        {
            return (false, "Không xác định được khách hàng cần cập nhật.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            var duplicateError = await ValidateDuplicateCodeAsync(connection, transaction, model.MaKhachHang, model.Id, cancellationToken);
            if (duplicateError is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, duplicateError);
            }

            var normalizedLocations = NormalizeDiaDiemLamViec(model.DiaDiemLamViec);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                UPDATE [{TableName}]
                SET
                    TenKhachHang = @TenKhachHang,
                    MaKhachHang = @MaKhachHang,
                    DiaChi = @DiaChi,
                    SoDienThoai = @SoDienThoai,
                    NguoiDaiDien = @NguoiDaiDien,
                    NganhNghe = @NganhNghe,
                    ZaloID = @ZaloID,
                    ZaloLastUpdate = CASE WHEN NULLIF(LTRIM(RTRIM(@ZaloID)), '') IS NULL THEN NULL ELSE GETDATE() END,
                    GhiChu = @GhiChu,
                    Updated_By = @UpdatedBy,
                    Updated_Date = GETDATE()
                WHERE ID = @Id
                """;

            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id.Value });
            FillSaveParameters(command, model);
            command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentUser, 100) });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy khách hàng để cập nhật.");
            }

            await SyncDiaDiemLamViecAsync(connection, transaction, model.Id.Value, normalizedLocations, currentUser, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update TblKhachHang {Id}.", model.Id);
            return (false, "Không thể cập nhật khách hàng lúc này.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, KhachHangDiaDiemFormItem? Item)> SaveLocationAsync(
        KhachHangDiaDiemSaveModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (!model.IDKhachHang.HasValue || model.IDKhachHang.Value <= 0)
        {
            return (false, "Không xác định được khách hàng cần lưu địa điểm.", null);
        }

        var normalized = NormalizeLocation(model);
        if (normalized is null)
        {
            return (false, "Vui lòng nhập ít nhất một thông tin cho địa điểm làm việc.", null);
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            var customerExists = await CustomerExistsAsync(connection, transaction, model.IDKhachHang.Value, cancellationToken);
            if (!customerExists)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy khách hàng để lưu địa điểm.", null);
            }

            int locationId;
            if (model.Id.HasValue && model.Id.Value > 0)
            {
                var locationExists = await LocationExistsAsync(connection, transaction, model.IDKhachHang.Value, model.Id.Value, cancellationToken);
                if (!locationExists)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return (false, "Không tìm thấy địa điểm làm việc để cập nhật.", null);
                }

                await using var updateCommand = connection.CreateCommand();
                updateCommand.Transaction = transaction;
                updateCommand.CommandText = $"""
                    UPDATE [{LocationTableName}]
                    SET
                        DiaChi = @DiaChi,
                        NguoiLienHe = @NguoiLienHe,
                        DienThoai = @DienThoai,
                        LongAddress = @LongAddress,
                        LatAddress = @LatAddress,
                        TrangThaiSuDung = @TrangThaiSuDung,
                        Updated_Date = GETDATE(),
                        Updated_by = @UpdatedBy,
                        Updated_LongLat_Date = CASE WHEN @LongAddress IS NULL OR @LatAddress IS NULL THEN NULL ELSE GETDATE() END,
                        Updated_LongLat_By = CASE WHEN @LongAddress IS NULL OR @LatAddress IS NULL THEN NULL ELSE @UpdatedLongLatBy END
                    WHERE ID = @Id AND IDKhachHang = @IDKhachHang
                    """;
                FillLocationParameters(updateCommand, model.IDKhachHang.Value, normalized, currentUser);
                updateCommand.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id.Value });

                var affectedRows = await updateCommand.ExecuteNonQueryAsync(cancellationToken);
                if (affectedRows <= 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return (false, "Không thể cập nhật địa điểm làm việc.", null);
                }

                locationId = model.Id.Value;
            }
            else
            {
                await using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = transaction;
                insertCommand.CommandText = $"""
                    INSERT INTO [{LocationTableName}] (
                        IDKhachHang,
                        DiaChi,
                        NguoiLienHe,
                        DienThoai,
                        LongAddress,
                        LatAddress,
                        TrangThaiSuDung,
                        Created_Date,
                        Created_By,
                        Updated_Date,
                        Updated_by,
                        Updated_LongLat_Date,
                        Updated_LongLat_By,
                        isRoot
                    )
                    VALUES (
                        @IDKhachHang,
                        @DiaChi,
                        @NguoiLienHe,
                        @DienThoai,
                        @LongAddress,
                        @LatAddress,
                        @TrangThaiSuDung,
                        GETDATE(),
                        @CreatedBy,
                        GETDATE(),
                        @UpdatedBy,
                        CASE WHEN @LongAddress IS NULL OR @LatAddress IS NULL THEN NULL ELSE GETDATE() END,
                        CASE WHEN @LongAddress IS NULL OR @LatAddress IS NULL THEN NULL ELSE @UpdatedLongLatBy END,
                        CASE WHEN EXISTS (SELECT 1 FROM [{LocationTableName}] WHERE IDKhachHang = @IDKhachHang) THEN 0 ELSE 1 END
                    );

                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """;
                FillLocationParameters(insertCommand, model.IDKhachHang.Value, normalized, currentUser);

                locationId = Convert.ToInt32(await insertCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
                if (locationId <= 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return (false, "Không thể thêm địa điểm làm việc.", null);
                }
            }

            await EnsureSingleRootLocationAsync(connection, transaction, model.IDKhachHang.Value, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return (true, null, new KhachHangDiaDiemFormItem
            {
                Id = locationId,
                DiaChi = normalized.DiaChi,
                NguoiLienHe = normalized.NguoiLienHe,
                DienThoai = normalized.DienThoai,
                LongAddress = normalized.LongAddress,
                LatAddress = normalized.LatAddress,
                TrangThaiSuDung = normalized.TrangThaiSuDung
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save location for TblKhachHang {Id}.", model.IDKhachHang);
            return (false, "Không thể lưu địa điểm làm việc lúc này.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteLocationAsync(
        int khachHangId,
        int locationId,
        CancellationToken cancellationToken = default)
    {
        if (khachHangId <= 0 || locationId <= 0)
        {
            return (false, "Không xác định được địa điểm làm việc cần xóa.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                DELETE FROM [{LocationTableName}]
                WHERE ID = @Id AND IDKhachHang = @IDKhachHang
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = locationId });
            command.Parameters.Add(new SqlParameter("@IDKhachHang", SqlDbType.Int) { Value = khachHangId });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy địa điểm làm việc để xóa.");
            }

            await EnsureSingleRootLocationAsync(connection, transaction, khachHangId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete location {LocationId} for TblKhachHang {CustomerId}.", locationId, khachHangId);
            return (false, "Không thể xóa địa điểm làm việc lúc này.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return (false, "Không xác định được khách hàng cần xóa.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await using (var deleteLocationCommand = connection.CreateCommand())
            {
                deleteLocationCommand.Transaction = transaction;
                deleteLocationCommand.CommandText = $"""
                    DELETE FROM [{LocationTableName}]
                    WHERE IDKhachHang = @Id
                    """;
                deleteLocationCommand.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
                await deleteLocationCommand.ExecuteNonQueryAsync(cancellationToken);
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
                return (false, "Không tìm thấy khách hàng để xóa.");
            }

            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete TblKhachHang {Id}.", id);
            return (false, "Không thể xóa khách hàng lúc này.");
        }
    }

    private async Task<string?> ValidateDuplicateCodeAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string? maKhachHang,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(maKhachHang))
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT TOP (1) ID
            FROM [{TableName}]
            WHERE UPPER(LTRIM(RTRIM(MaKhachHang))) = UPPER(@MaKhachHang)
            {(excludeId.HasValue ? "AND ID <> @ExcludeId" : string.Empty)}
            """;
        command.Parameters.Add(new SqlParameter("@MaKhachHang", SqlDbType.NVarChar, 50) { Value = maKhachHang.Trim() });

        if (excludeId.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@ExcludeId", SqlDbType.Int) { Value = excludeId.Value });
        }

        var existingId = await command.ExecuteScalarAsync(cancellationToken);
        return existingId is null
            ? null
            : "Mã khách hàng đã tồn tại.";
    }

    private static async Task<IReadOnlyList<KhachHangDiaDiemFormItem>> LoadDiaDiemLamViecAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int khachHangId,
        CancellationToken cancellationToken)
    {
        if (khachHangId <= 0)
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                ID,
                DiaChi,
                NguoiLienHe,
                DienThoai,
                LongAddress,
                LatAddress,
                CAST(ISNULL(TrangThaiSuDung, 1) AS bit) AS TrangThaiSuDung
            FROM [{LocationTableName}]
            WHERE IDKhachHang = @IDKhachHang
            ORDER BY
                CASE WHEN ISNULL(isRoot, 0) = 1 THEN 0 ELSE 1 END,
                ID
            """;
        command.Parameters.Add(new SqlParameter("@IDKhachHang", SqlDbType.Int) { Value = khachHangId });

        var items = new List<KhachHangDiaDiemFormItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new KhachHangDiaDiemFormItem
            {
                Id = GetNullableInt32(reader, "ID"),
                DiaChi = GetNullableString(reader, "DiaChi"),
                NguoiLienHe = GetNullableString(reader, "NguoiLienHe"),
                DienThoai = GetNullableString(reader, "DienThoai"),
                LongAddress = GetNullableDecimal(reader, "LongAddress"),
                LatAddress = GetNullableDecimal(reader, "LatAddress"),
                TrangThaiSuDung = GetNullableBoolean(reader, "TrangThaiSuDung") ?? true
            });
        }

        return items;
    }

    private static async Task SyncDiaDiemLamViecAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int khachHangId,
        IReadOnlyList<KhachHangDiaDiemFormItem> items,
        string currentUser,
        CancellationToken cancellationToken)
    {
        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = $"""
                DELETE FROM [{LocationTableName}]
                WHERE IDKhachHang = @IDKhachHang
                """;
            deleteCommand.Parameters.Add(new SqlParameter("@IDKhachHang", SqlDbType.Int) { Value = khachHangId });
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (items.Count == 0)
        {
            return;
        }

        var auditUser = TrimToLength(currentUser, 50);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];

            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = $"""
                INSERT INTO [{LocationTableName}] (
                    IDKhachHang,
                    DiaChi,
                    NguoiLienHe,
                    DienThoai,
                    LongAddress,
                    LatAddress,
                    TrangThaiSuDung,
                    Created_Date,
                    Created_By,
                    Updated_Date,
                    Updated_by,
                    Updated_LongLat_Date,
                    Updated_LongLat_By,
                    isRoot
                )
                VALUES (
                    @IDKhachHang,
                    @DiaChi,
                    @NguoiLienHe,
                    @DienThoai,
                    @LongAddress,
                    @LatAddress,
                    @TrangThaiSuDung,
                    GETDATE(),
                    @CreatedBy,
                    GETDATE(),
                    @UpdatedBy,
                    CASE WHEN @LongAddress IS NULL OR @LatAddress IS NULL THEN NULL ELSE GETDATE() END,
                    CASE WHEN @LongAddress IS NULL OR @LatAddress IS NULL THEN NULL ELSE @UpdatedLongLatBy END,
                    @IsRoot
                )
                """;

            insertCommand.Parameters.Add(new SqlParameter("@IDKhachHang", SqlDbType.Int) { Value = khachHangId });
            insertCommand.Parameters.Add(new SqlParameter("@DiaChi", SqlDbType.NVarChar, 250) { Value = ToDbValue(item.DiaChi) });
            insertCommand.Parameters.Add(new SqlParameter("@NguoiLienHe", SqlDbType.NVarChar, 150) { Value = ToDbValue(item.NguoiLienHe) });
            insertCommand.Parameters.Add(new SqlParameter("@DienThoai", SqlDbType.NVarChar, 50) { Value = ToDbValue(item.DienThoai) });
            insertCommand.Parameters.Add(new SqlParameter("@LongAddress", SqlDbType.Decimal)
            {
                Precision = 18,
                Scale = 5,
                Value = ToDbValue(item.LongAddress)
            });
            insertCommand.Parameters.Add(new SqlParameter("@LatAddress", SqlDbType.Decimal)
            {
                Precision = 18,
                Scale = 5,
                Value = ToDbValue(item.LatAddress)
            });
            insertCommand.Parameters.Add(new SqlParameter("@TrangThaiSuDung", SqlDbType.Bit) { Value = item.TrangThaiSuDung });
            insertCommand.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = auditUser });
            insertCommand.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = auditUser });
            insertCommand.Parameters.Add(new SqlParameter("@UpdatedLongLatBy", SqlDbType.NVarChar, 50) { Value = auditUser });
            insertCommand.Parameters.Add(new SqlParameter("@IsRoot", SqlDbType.Bit) { Value = index == 0 });

            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static IReadOnlyList<KhachHangDiaDiemFormItem> NormalizeDiaDiemLamViec(IEnumerable<KhachHangDiaDiemFormItem>? items)
    {
        if (items is null)
        {
            return [];
        }

        var normalized = new List<KhachHangDiaDiemFormItem>();
        foreach (var item in items)
        {
            var diaChi = TrimNullable(item.DiaChi, 250);
            var nguoiLienHe = TrimNullable(item.NguoiLienHe, 150);
            var dienThoai = TrimNullable(item.DienThoai, 50);
            var longAddress = NormalizeCoordinate(item.LongAddress, -180m, 180m);
            var latAddress = NormalizeCoordinate(item.LatAddress, -90m, 90m);

            var hasValue =
                diaChi is not null ||
                nguoiLienHe is not null ||
                dienThoai is not null ||
                longAddress.HasValue ||
                latAddress.HasValue;

            if (!hasValue)
            {
                continue;
            }

            normalized.Add(new KhachHangDiaDiemFormItem
            {
                Id = item.Id,
                DiaChi = diaChi,
                NguoiLienHe = nguoiLienHe,
                DienThoai = dienThoai,
                LongAddress = longAddress,
                LatAddress = latAddress,
                TrangThaiSuDung = item.TrangThaiSuDung
            });
        }

        return normalized;
    }

    private static KhachHangListItem MapItem(SqlDataReader reader)
    {
        return new KhachHangListItem
        {
            Id = reader.GetInt32(reader.GetOrdinal("ID")),
            TenKhachHang = GetNullableString(reader, "TenKhachHang") ?? string.Empty,
            MaKhachHang = GetNullableString(reader, "MaKhachHang"),
            DiaChi = GetNullableString(reader, "DiaChi"),
            SoDienThoai = GetNullableString(reader, "SoDienThoai"),
            NguoiDaiDien = GetNullableString(reader, "NguoiDaiDien"),
            NganhNghe = GetNullableString(reader, "NganhNghe"),
            ZaloID = GetNullableString(reader, "ZaloID"),
            GhiChu = GetNullableString(reader, "GhiChu"),
            CreatedBy = GetNullableString(reader, "Created_By"),
            CreatedDate = GetNullableDateTime(reader, "Created_Date"),
            UpdatedBy = GetNullableString(reader, "Updated_By"),
            UpdatedDate = GetNullableDateTime(reader, "Updated_Date")
        };
    }

    private async Task<bool> CustomerExistsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int khachHangId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT COUNT(1)
            FROM [{TableName}]
            WHERE ID = @Id
            """;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = khachHangId });
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0) > 0;
    }

    private async Task<bool> LocationExistsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int khachHangId,
        int locationId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT COUNT(1)
            FROM [{LocationTableName}]
            WHERE ID = @Id AND IDKhachHang = @IDKhachHang
            """;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = locationId });
        command.Parameters.Add(new SqlParameter("@IDKhachHang", SqlDbType.Int) { Value = khachHangId });
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0) > 0;
    }

    private static KhachHangDiaDiemFormItem? NormalizeLocation(KhachHangDiaDiemSaveModel model)
    {
        var normalizedItems = NormalizeDiaDiemLamViec(
        [
            new KhachHangDiaDiemFormItem
            {
                Id = model.Id,
                DiaChi = model.DiaChi,
                NguoiLienHe = model.NguoiLienHe,
                DienThoai = model.DienThoai,
                LongAddress = model.LongAddress,
                LatAddress = model.LatAddress,
                TrangThaiSuDung = model.TrangThaiSuDung
            }
        ]);

        return normalizedItems.FirstOrDefault();
    }

    private static void FillLocationParameters(
        SqlCommand command,
        int khachHangId,
        KhachHangDiaDiemFormItem item,
        string currentUser)
    {
        var auditUser = TrimToLength(currentUser, 50);

        command.Parameters.Add(new SqlParameter("@IDKhachHang", SqlDbType.Int) { Value = khachHangId });
        command.Parameters.Add(new SqlParameter("@DiaChi", SqlDbType.NVarChar, 250) { Value = ToDbValue(item.DiaChi) });
        command.Parameters.Add(new SqlParameter("@NguoiLienHe", SqlDbType.NVarChar, 150) { Value = ToDbValue(item.NguoiLienHe) });
        command.Parameters.Add(new SqlParameter("@DienThoai", SqlDbType.NVarChar, 50) { Value = ToDbValue(item.DienThoai) });
        command.Parameters.Add(new SqlParameter("@LongAddress", SqlDbType.Decimal)
        {
            Precision = 18,
            Scale = 5,
            Value = ToDbValue(item.LongAddress)
        });
        command.Parameters.Add(new SqlParameter("@LatAddress", SqlDbType.Decimal)
        {
            Precision = 18,
            Scale = 5,
            Value = ToDbValue(item.LatAddress)
        });
        command.Parameters.Add(new SqlParameter("@TrangThaiSuDung", SqlDbType.Bit) { Value = item.TrangThaiSuDung });
        command.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = auditUser });
        command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = auditUser });
        command.Parameters.Add(new SqlParameter("@UpdatedLongLatBy", SqlDbType.NVarChar, 50) { Value = auditUser });
    }

    private static async Task EnsureSingleRootLocationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int khachHangId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            ;WITH first_location AS (
                SELECT TOP (1) ID
                FROM [{LocationTableName}]
                WHERE IDKhachHang = @IDKhachHang
                ORDER BY ID
            )
            UPDATE [{LocationTableName}]
            SET isRoot = CASE WHEN ID IN (SELECT ID FROM first_location) THEN 1 ELSE 0 END
            WHERE IDKhachHang = @IDKhachHang
            """;
        command.Parameters.Add(new SqlParameter("@IDKhachHang", SqlDbType.Int) { Value = khachHangId });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string BuildWhereClause(string? keyword)
    {
        var filters = new List<string> { "1 = 1" };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filters.Add($"""
                (
                    kh.TenKhachHang COLLATE {SearchCollation} LIKE @Keyword
                    OR kh.MaKhachHang COLLATE {SearchCollation} LIKE @Keyword
                    OR kh.DiaChi COLLATE {SearchCollation} LIKE @Keyword
                    OR kh.SoDienThoai COLLATE {SearchCollation} LIKE @Keyword
                    OR kh.NguoiDaiDien COLLATE {SearchCollation} LIKE @Keyword
                    OR kh.NganhNghe COLLATE {SearchCollation} LIKE @Keyword
                    OR kh.ZaloID COLLATE {SearchCollation} LIKE @Keyword
                    OR kh.GhiChu COLLATE {SearchCollation} LIKE @Keyword
                    OR EXISTS (
                        SELECT 1
                        FROM [{LocationTableName}] AS dd
                        WHERE dd.IDKhachHang = kh.ID
                            AND (
                                dd.DiaChi COLLATE {SearchCollation} LIKE @Keyword
                                OR dd.NguoiLienHe COLLATE {SearchCollation} LIKE @Keyword
                                OR dd.DienThoai COLLATE {SearchCollation} LIKE @Keyword
                            )
                    )
                )
                """);
        }

        return string.Join(" AND ", filters);
    }

    private static void AddFilterParameters(SqlCommand command, string? keyword)
    {
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            command.Parameters.Add(new SqlParameter("@Keyword", SqlDbType.NVarChar, 250)
            {
                Value = $"%{keyword}%"
            });
        }
    }

    private static void FillSaveParameters(SqlCommand command, KhachHangFormModel model)
    {
        command.Parameters.Add(new SqlParameter("@TenKhachHang", SqlDbType.NVarChar, 250)
        {
            Value = model.TenKhachHang.Trim()
        });
        command.Parameters.Add(new SqlParameter("@MaKhachHang", SqlDbType.NVarChar, 50)
        {
            Value = ToDbValue(model.MaKhachHang)
        });
        command.Parameters.Add(new SqlParameter("@DiaChi", SqlDbType.NVarChar, 250)
        {
            Value = ToDbValue(model.DiaChi)
        });
        command.Parameters.Add(new SqlParameter("@SoDienThoai", SqlDbType.NVarChar, 50)
        {
            Value = ToDbValue(model.SoDienThoai)
        });
        command.Parameters.Add(new SqlParameter("@NguoiDaiDien", SqlDbType.NVarChar, 150)
        {
            Value = ToDbValue(model.NguoiDaiDien)
        });
        command.Parameters.Add(new SqlParameter("@NganhNghe", SqlDbType.NVarChar, 250)
        {
            Value = ToDbValue(model.NganhNghe)
        });
        command.Parameters.Add(new SqlParameter("@ZaloID", SqlDbType.NVarChar, 150)
        {
            Value = ToDbValue(model.ZaloID)
        });
        command.Parameters.Add(new SqlParameter("@GhiChu", SqlDbType.NVarChar, 250)
        {
            Value = ToDbValue(model.GhiChu)
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

    private static string TrimToLength(string value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "system" : value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? TrimNullable(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static decimal? NormalizeCoordinate(decimal? value, decimal min, decimal max)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var normalized = Math.Round(value.Value, 5, MidpointRounding.AwayFromZero);
        return normalized < min || normalized > max ? null : normalized;
    }

    private static string? GetNullableString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal)?.ToString()?.Trim();
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
            byte typedByte => typedByte != 0,
            short typedShort => typedShort != 0,
            int typedInt => typedInt != 0,
            string typedString when bool.TryParse(typedString, out var parsedBool) => parsedBool,
            string typedString when int.TryParse(typedString, out var parsedInt) => parsedInt != 0,
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
