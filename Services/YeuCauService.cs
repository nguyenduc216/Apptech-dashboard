using System.Data;
using System.Globalization;
using System.Net;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IYeuCauService
{
    Task<(IReadOnlyList<YeuCauListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        string? statusFilter,
        DateTime? requestDateFrom,
        DateTime? requestDateTo,
        DateTime? executionDateFrom,
        DateTime? executionDateTo,
        string? assigneeKeyword,
        string? workStatusFilter,
        int? assignedEmployeeId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<YeuCauListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<YeuCauNhanVienOption>> GetNhanVienOptionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<YeuCauCongViecOption>> GetWorkOptionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<YeuCauCongViecFormItem>> GetAssignedWorksAsync(int yeuCauId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<YeuCauCheckinItem>> GetCheckinsAsync(int yeuCauId, CancellationToken cancellationToken = default);

    Task<decimal?> GetCheckinDistanceLimitMetersAsync(CancellationToken cancellationToken = default);

    Task<bool> IsEmployeeAssignedToRequestAsync(int yeuCauId, int employeeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<YeuCauLocationOption>> SearchLocationsAsync(string? keyword, int limit = 12, CancellationToken cancellationToken = default);

    Task<YeuCauLocationOption?> GetLocationByIdAsync(int idDiaDiem, CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> UpdateLocationCoordinatesAsync(
        int idDiaDiem,
        decimal longAddress,
        decimal latAddress,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<string> GenerateNextCodeAsync(DateTime? requestDate, CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        YeuCauFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        YeuCauFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateCheckinAsync(
        YeuCauCheckinCreateModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> CreateCheckoutAsync(
        YeuCauCheckoutCreateModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateWorkImageAsync(
        int yeuCauCongViecId,
        string imagePath,
        string imageType,
        string currentUser,
        Guid? currentAccountId,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, YeuCauCongViecChecklistFormItem? Checklist)> CreateWorkChecklistAsync(
        int congViecId,
        string? tenChecklist,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, string? ImagePath)> DeleteWorkImageAsync(
        int imageId,
        Guid? currentAccountId,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<bool> IsWorkCompletedAsync(int yeuCauCongViecId, CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, string? ImgPath)> DeleteCheckinAsync(
        int id,
        int yeuCauId,
        CancellationToken cancellationToken = default);
}

public sealed class YeuCauService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<YeuCauService> logger,
    ICommonAuditService commonAuditService) : IYeuCauService
{
    private const int RequestCodeMaxLength = 10;
    private const int RequestCodeSequenceDigits = 5;
    private const string TableName = "TblYeuCau";
    private const string AssignmentTableName = "TblYeuCauCongViecNhanVien";
    private const string WorkTableName = "TblYeuCauCongViec";
    private const string WorkImageTableName = "TblYeuCauCongViecImages";
    private const string WorkChecklistTableName = "TblYeuCauCongViecCheckList";
    private const string CheckinHistoryTableName = "TblCheckinHistory";
    private const string ChecklistTemplateTableName = "TblCongViecChecklist";
    private const string CustomerTableName = "TblKhachHang";
    private const string LocationTableName = "TblKhachHangDiaDiem";
    private const string EmployeeTableName = "TblNhanVien";
    private const string SystemConfigTableName = "TblCauHinhHeThong";
    private const string SearchCollation = "Latin1_General_100_CI_AI";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<YeuCauService> _logger = logger;
    private readonly ICommonAuditService _commonAuditService = commonAuditService;

    public async Task<(IReadOnlyList<YeuCauListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        string? statusFilter,
        DateTime? requestDateFrom,
        DateTime? requestDateTo,
        DateTime? executionDateFrom,
        DateTime? executionDateTo,
        string? assigneeKeyword,
        string? workStatusFilter,
        int? assignedEmployeeId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 5, 100);
        page = Math.Max(page, 1);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureWorkMetadataColumnsAsync(connection, cancellationToken);
            var normalizedKeyword = NormalizeKeyword(keyword);
            var normalizedAssigneeKeyword = NormalizeKeyword(assigneeKeyword);
            var normalizedStatus = NormalizeStatus(statusFilter);
            var normalizedWorkStatus = YeuCauCongViecTrangThaiFilter.Normalize(workStatusFilter);
            var statusValues = YeuCauTrangThaiCatalog.GetFilterValues(normalizedStatus);
            var whereClause = BuildWhereClause(
                normalizedKeyword,
                statusValues,
                requestDateFrom,
                requestDateTo,
                executionDateFrom,
                executionDateTo,
                normalizedAssigneeKeyword,
                normalizedWorkStatus,
                assignedEmployeeId);

            await using var countCommand = connection.CreateCommand();
            countCommand.CommandText = $"""
                SELECT COUNT(1)
                FROM [{TableName}] AS yc
                LEFT JOIN [{CustomerTableName}] AS kh ON kh.ID = yc.IDKhachHang
                LEFT JOIN [{LocationTableName}] AS dd ON dd.ID = yc.IDDiaDiem
                WHERE {whereClause}
                """;
            AddFilterParameters(countCommand, normalizedKeyword, statusValues, requestDateFrom, requestDateTo, executionDateFrom, executionDateTo, normalizedAssigneeKeyword, assignedEmployeeId);

            var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
            var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            var currentPage = totalPages == 0 ? 1 : Math.Min(page, totalPages);
            var offset = (currentPage - 1) * pageSize;

            await using var listCommand = connection.CreateCommand();
            listCommand.CommandText = $"""
                SELECT
                    yc.ID,
                    yc.MaYeuCau,
                    yc.IDKhachHang,
                    yc.NgayYeuCau,
                    yc.IDDiaDiem,
                    yc.GhiChu,
                    yc.NhanVienThucHien,
                    yc.TrangThaiYeuCau,
                    yc.NgayThucHien,
                    yc.NgayHetHan,
                    yc.NgayHoanThanh,
                    yc.NgayHenTiepTheo,
                    CAST(ISNULL(yc.CheckinTheoKhoangCach, 0) AS bit) AS CheckinTheoKhoangCach,
                    yc.Created_Date,
                    yc.Created_By,
                    yc.Updated_Date,
                    yc.Updated_By,
                    kh.TenKhachHang,
                    dd.DiaChi,
                    dd.NguoiLienHe,
                    dd.DienThoai,
                    dd.LongAddress,
                    dd.LatAddress,
                    ISNULL(workStats.SoCongViec, 0) AS SoCongViec,
                    ISNULL(workStats.SoCongViecHoanThanh, 0) AS SoCongViecHoanThanh
                FROM [{TableName}] AS yc
                LEFT JOIN [{CustomerTableName}] AS kh ON kh.ID = yc.IDKhachHang
                LEFT JOIN [{LocationTableName}] AS dd ON dd.ID = yc.IDDiaDiem
                OUTER APPLY (
                    SELECT
                        COUNT(1) AS SoCongViec,
                        SUM(CASE WHEN ycvc.TrangThaiCongViec = @CompletedWorkStatus THEN 1 ELSE 0 END) AS SoCongViecHoanThanh
                    FROM [{WorkTableName}] AS ycvc
                    WHERE ycvc.IDYeuCau = yc.ID
                ) AS workStats
                WHERE {whereClause}
                ORDER BY ISNULL(yc.NgayYeuCau, yc.Created_Date) DESC, yc.ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                """;
            AddFilterParameters(listCommand, normalizedKeyword, statusValues, requestDateFrom, requestDateTo, executionDateFrom, executionDateTo, normalizedAssigneeKeyword, assignedEmployeeId);
            listCommand.Parameters.Add(new SqlParameter("@CompletedWorkStatus", SqlDbType.NVarChar, 50) { Value = YeuCauCongViecTrangThaiCatalog.HoanThanh });
            listCommand.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = offset });
            listCommand.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = pageSize });

            var items = new List<YeuCauListItem>();
            await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapItem(reader));
            }

            return (items, totalCount, currentPage, totalPages, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblYeuCau list.");
            return ([], 0, 1, 0, pageSize);
        }
    }

    public async Task<YeuCauListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return null;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureWorkMetadataColumnsAsync(connection, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT TOP (1)
                    yc.ID,
                    yc.MaYeuCau,
                    yc.IDKhachHang,
                    yc.NgayYeuCau,
                    yc.IDDiaDiem,
                    yc.GhiChu,
                    yc.NhanVienThucHien,
                    yc.TrangThaiYeuCau,
                    yc.NgayThucHien,
                    yc.NgayHetHan,
                    yc.NgayHoanThanh,
                    yc.NgayHenTiepTheo,
                    CAST(ISNULL(yc.CheckinTheoKhoangCach, 0) AS bit) AS CheckinTheoKhoangCach,
                    yc.Created_Date,
                    yc.Created_By,
                    yc.Updated_Date,
                    yc.Updated_By,
                    kh.TenKhachHang,
                    dd.DiaChi,
                    dd.NguoiLienHe,
                    dd.DienThoai,
                    dd.LongAddress,
                    dd.LatAddress,
                    ISNULL(workStats.SoCongViec, 0) AS SoCongViec,
                    ISNULL(workStats.SoCongViecHoanThanh, 0) AS SoCongViecHoanThanh
                FROM [{TableName}] AS yc
                LEFT JOIN [{CustomerTableName}] AS kh ON kh.ID = yc.IDKhachHang
                LEFT JOIN [{LocationTableName}] AS dd ON dd.ID = yc.IDDiaDiem
                OUTER APPLY (
                    SELECT
                        COUNT(1) AS SoCongViec,
                        SUM(CASE WHEN ycvc.TrangThaiCongViec = @CompletedWorkStatus THEN 1 ELSE 0 END) AS SoCongViecHoanThanh
                    FROM [{WorkTableName}] AS ycvc
                    WHERE ycvc.IDYeuCau = yc.ID
                ) AS workStats
                WHERE yc.ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
            command.Parameters.Add(new SqlParameter("@CompletedWorkStatus", SqlDbType.NVarChar, 50) { Value = YeuCauCongViecTrangThaiCatalog.HoanThanh });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var item = MapItem(reader);
            await reader.CloseAsync();
            return item;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblYeuCau item {Id}.", id);
            return null;
        }
    }

    public async Task<IReadOnlyList<YeuCauNhanVienOption>> GetNhanVienOptionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT
                    ID,
                    Ho,
                    Ten,
                    ChucVu,
                    Avatar,
                    CAST(ISNULL(TrangThaiSuDung, 1) AS bit) AS TrangThaiSuDung
                FROM [{EmployeeTableName}]
                WHERE ISNULL(TrangThaiSuDung, 1) = 1
                ORDER BY
                    LTRIM(RTRIM(Ho)),
                    LTRIM(RTRIM(Ten)),
                    ID
                """;

            var items = new List<YeuCauNhanVienOption>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var ho = GetNullableString(reader, "Ho") ?? string.Empty;
                var ten = GetNullableString(reader, "Ten") ?? string.Empty;
                var hoTen = string.Join(" ", new[] { ho, ten }.Where(static part => !string.IsNullOrWhiteSpace(part))).Trim();

                if (string.IsNullOrWhiteSpace(hoTen))
                {
                    continue;
                }

                items.Add(new YeuCauNhanVienOption
                {
                    Id = reader.GetInt32(reader.GetOrdinal("ID")),
                    HoTen = hoTen,
                    ChucVu = GetNullableString(reader, "ChucVu"),
                    Avatar = GetNullableString(reader, "Avatar"),
                    TrangThaiSuDung = GetNullableBoolean(reader, "TrangThaiSuDung") ?? true
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load employee options for TblYeuCau.");
            return [];
        }
    }

    public async Task<IReadOnlyList<YeuCauCongViecOption>> GetWorkOptionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    cv.ID,
                    cv.TenCongViec,
                    cv.SoLuongAnhCheckIn,
                    cv.SoLuongAnhCheckOut,
                    ckt.ID AS ChecklistId,
                    ckt.TenCheckList,
                    ISNULL(ckt.ViTri, 0) AS ViTri
                FROM [TblCongViec] AS cv
                LEFT JOIN [TblCongViecChecklist] AS ckt
                    ON ckt.IDCongViec = cv.ID
                    AND ISNULL(ckt.TrangThaiSuDung, 1) = 1
                WHERE ISNULL(cv.TrangThaiSuDung, 1) = 1
                ORDER BY cv.TenCongViec, CASE WHEN ckt.ViTri IS NULL OR ckt.ViTri <= 0 THEN 2147483647 ELSE ckt.ViTri END, ckt.ID
                """;

            var lookup = new Dictionary<int, YeuCauCongViecOption>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var workId = reader.GetInt32(reader.GetOrdinal("ID"));
                if (!lookup.TryGetValue(workId, out var option))
                {
                    option = new YeuCauCongViecOption
                    {
                        Id = workId,
                        TenCongViec = GetNullableString(reader, "TenCongViec") ?? "Công việc"
                    };
                    option.SoLuongAnhCheckIn = Math.Max(0, GetNullableInt32(reader, "SoLuongAnhCheckIn") ?? 0);
                    option.SoLuongAnhCheckOut = Math.Max(0, GetNullableInt32(reader, "SoLuongAnhCheckOut") ?? 0);
                    lookup[workId] = option;
                }

                var checklistId = GetNullableInt32(reader, "ChecklistId");
                if (checklistId.HasValue && checklistId.Value > 0)
                {
                    option.Checklists.Add(new YeuCauCongViecChecklistFormItem
                    {
                        ChecklistId = checklistId.Value,
                        TenChecklist = GetNullableString(reader, "TenCheckList") ?? string.Empty,
                        ViTri = GetNullableInt32(reader, "ViTri") ?? 0
                    });
                }
            }

            return lookup.Values
                .OrderBy(option => option.TenCongViec)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load work options for TblYeuCau.");
            return [];
        }
    }

    public async Task<IReadOnlyList<YeuCauLocationOption>> SearchLocationsAsync(string? keyword, int limit = 12, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 50);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            var normalizedKeyword = NormalizeKeyword(keyword);
            var whereClause = BuildLocationWhereClause(normalizedKeyword);

            command.CommandText = $"""
                SELECT TOP (@Limit)
                    dd.ID,
                    dd.IDKhachHang,
                    kh.TenKhachHang,
                    dd.DiaChi,
                    dd.NguoiLienHe,
                    dd.DienThoai,
                    dd.LongAddress,
                    dd.LatAddress,
                    CAST(ISNULL(dd.TrangThaiSuDung, 1) AS bit) AS TrangThaiSuDung
                FROM [{LocationTableName}] AS dd
                LEFT JOIN [{CustomerTableName}] AS kh ON kh.ID = dd.IDKhachHang
                WHERE {whereClause}
                ORDER BY
                    CASE WHEN ISNULL(dd.TrangThaiSuDung, 1) = 1 THEN 0 ELSE 1 END,
                    kh.TenKhachHang,
                    dd.DiaChi,
                    dd.ID
                """;
            command.Parameters.Add(new SqlParameter("@Limit", SqlDbType.Int) { Value = limit });

            if (!string.IsNullOrWhiteSpace(normalizedKeyword))
            {
                command.Parameters.Add(new SqlParameter("@Keyword", SqlDbType.NVarChar, 250) { Value = $"%{normalizedKeyword}%" });
            }

            var items = new List<YeuCauLocationOption>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapLocationOption(reader));
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search customer locations for TblYeuCau.");
            return [];
        }
    }

    public async Task<YeuCauLocationOption?> GetLocationByIdAsync(int idDiaDiem, CancellationToken cancellationToken = default)
    {
        if (idDiaDiem <= 0)
        {
            return null;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT TOP (1)
                    dd.ID,
                    dd.IDKhachHang,
                    kh.TenKhachHang,
                    dd.DiaChi,
                    dd.NguoiLienHe,
                    dd.DienThoai,
                    dd.LongAddress,
                    dd.LatAddress,
                    CAST(ISNULL(dd.TrangThaiSuDung, 1) AS bit) AS TrangThaiSuDung
                FROM [{LocationTableName}] AS dd
                LEFT JOIN [{CustomerTableName}] AS kh ON kh.ID = dd.IDKhachHang
                WHERE dd.ID = @IDDiaDiem
                """;
            command.Parameters.Add(new SqlParameter("@IDDiaDiem", SqlDbType.Int) { Value = idDiaDiem });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? MapLocationOption(reader) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load location {IDDiaDiem} for TblYeuCau.", idDiaDiem);
            return null;
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> UpdateLocationCoordinatesAsync(
        int idDiaDiem,
        decimal longAddress,
        decimal latAddress,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (idDiaDiem <= 0)
        {
            return (false, "Khong xac dinh duoc dia diem can cap nhat toa do.");
        }

        if (latAddress < -90m || latAddress > 90m || longAddress < -180m || longAddress > 180m)
        {
            return (false, "Toa do khong hop le.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                UPDATE [{LocationTableName}]
                SET
                    LongAddress = @LongAddress,
                    LatAddress = @LatAddress,
                    Updated_LongLat_Date = GETDATE(),
                    Updated_LongLat_By = @UpdatedLongLatBy
                WHERE ID = @IDDiaDiem
                  AND LongAddress IS NULL
                  AND LatAddress IS NULL
                """;
            command.Parameters.Add(new SqlParameter("@IDDiaDiem", SqlDbType.Int) { Value = idDiaDiem });
            command.Parameters.Add(new SqlParameter("@LongAddress", SqlDbType.Decimal) { Precision = 18, Scale = 10, Value = longAddress });
            command.Parameters.Add(new SqlParameter("@LatAddress", SqlDbType.Decimal) { Precision = 18, Scale = 10, Value = latAddress });
            command.Parameters.Add(new SqlParameter("@UpdatedLongLatBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            return affectedRows > 0
                ? (true, null)
                : (false, "Dia diem da co toa do hoac khong ton tai.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update coordinates for TblKhachHangDiaDiem {Id}.", idDiaDiem);
            return (false, "Khong the cap nhat toa do dia diem.");
        }
    }

    public async Task<string> GenerateNextCodeAsync(DateTime? requestDate, CancellationToken cancellationToken = default)
    {
        var targetDate = requestDate?.Date ?? DateTime.Today;
        var yearSuffix = targetDate.ToString("yy");
        var prefix = BuildRequestCodePrefix(yearSuffix);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var nextSequence = await GetNextSequenceAsync(connection, transaction: null, prefix, cancellationToken);
            return BuildRequestCode(prefix, nextSequence);
        }
        catch (Exception ex) when (ex is not YeuCauBusinessRuleException)
        {
            _logger.LogError(ex, "Failed to generate next TblYeuCau code for prefix {Prefix}.", prefix);
            return BuildRequestCode(prefix, 1);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        YeuCauFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureWorkMetadataColumnsAsync(connection, cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            var validationError = await ValidateBusinessRulesAsync(connection, transaction, model, cancellationToken);
            if (validationError is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, validationError, null);
            }

            var yearSuffix = (model.NgayYeuCau ?? DateTime.Today).ToString("yy");
            var codePrefix = BuildRequestCodePrefix(yearSuffix);
            var nextSequence = await GetNextSequenceAsync(connection, transaction, codePrefix, cancellationToken);
            var generatedCode = BuildRequestCode(codePrefix, nextSequence);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO [{TableName}] (
                    MaYeuCau,
                    IDKhachHang,
                    NgayYeuCau,
                    IDDiaDiem,
                    GhiChu,
                    NhanVienThucHien,
                    TrangThaiYeuCau,
                    NgayThucHien,
                    NgayHetHan,
                    NgayHoanThanh,
                    NgayHenTiepTheo,
                    CheckinTheoKhoangCach,
                    Created_Date,
                    Created_By,
                    Updated_Date,
                    Updated_By
                )
                VALUES (
                    @MaYeuCau,
                    @IDKhachHang,
                    @NgayYeuCau,
                    @IDDiaDiem,
                    @GhiChu,
                    @NhanVienThucHien,
                    @TrangThaiYeuCau,
                    @NgayThucHien,
                    @NgayHetHan,
                    @NgayHoanThanh,
                    @NgayHenTiepTheo,
                    @CheckinTheoKhoangCach,
                    GETDATE(),
                    @CreatedBy,
                    GETDATE(),
                    @UpdatedBy
                );

                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;

            var requestEmployeeSummary = string.Empty;
            FillSaveParameters(command, model, generatedCode, requestEmployeeSummary, currentUser);

            var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
            if (newId <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không thể thêm mới yêu cầu.", null);
            }

            await SyncAssignedWorksAsync(connection, transaction, newId, model.CongViecs, currentUser, cancellationToken);
            requestEmployeeSummary = await RefreshRequestEmployeeSummaryAsync(connection, transaction, newId, currentUser, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            model.MaYeuCau = generatedCode;
            model.NhanVienThucHienText = requestEmployeeSummary;

            return (true, null, newId);
        }
        catch (YeuCauBusinessRuleException ex)
        {
            _logger.LogWarning(ex, "Business rule failed while creating TblYeuCau.");
            return (false, ex.Message, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TblYeuCau.");
            return (false, "Không thể thêm mới yêu cầu lúc này.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        YeuCauFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (model.Id is null or <= 0)
        {
            return (false, "Không xác định được yêu cầu cần cập nhật.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureWorkMetadataColumnsAsync(connection, cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            var existingCode = await GetExistingCodeAsync(connection, transaction, model.Id.Value, cancellationToken);
            if (existingCode is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy yêu cầu để cập nhật.");
            }

            var validationError = await ValidateBusinessRulesAsync(connection, transaction, model, cancellationToken);
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
                    IDKhachHang = @IDKhachHang,
                    NgayYeuCau = @NgayYeuCau,
                    IDDiaDiem = @IDDiaDiem,
                    GhiChu = @GhiChu,
                    NhanVienThucHien = @NhanVienThucHien,
                    TrangThaiYeuCau = @TrangThaiYeuCau,
                    NgayThucHien = @NgayThucHien,
                    NgayHetHan = @NgayHetHan,
                    NgayHoanThanh = @NgayHoanThanh,
                    NgayHenTiepTheo = @NgayHenTiepTheo,
                    CheckinTheoKhoangCach = @CheckinTheoKhoangCach,
                    Updated_Date = GETDATE(),
                    Updated_By = @UpdatedBy
                WHERE ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id.Value });
            var requestEmployeeSummary = string.Empty;
            FillSaveParameters(command, model, existingCode, requestEmployeeSummary, currentUser, includeCreatedFields: false);

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy yêu cầu để cập nhật.");
            }

            await SyncAssignedWorksAsync(connection, transaction, model.Id.Value, model.CongViecs, currentUser, cancellationToken);
            requestEmployeeSummary = await RefreshRequestEmployeeSummaryAsync(connection, transaction, model.Id.Value, currentUser, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            model.MaYeuCau = existingCode;
            model.NhanVienThucHienText = requestEmployeeSummary;

            return (true, null);
        }
        catch (YeuCauBusinessRuleException ex)
        {
            _logger.LogWarning(ex, "Business rule failed while updating TblYeuCau {Id}.", model.Id);
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update TblYeuCau {Id}.", model.Id);
            return (false, "Không thể cập nhật yêu cầu lúc này.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return (false, "Không xác định được yêu cầu cần xóa.");
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
                : (false, "Không tìm thấy yêu cầu để xóa.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete TblYeuCau {Id}.", id);
            return (false, "Không thể xóa yêu cầu lúc này.");
        }
    }

    private async Task<string?> ValidateBusinessRulesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        YeuCauFormModel model,
        CancellationToken cancellationToken)
    {
        if (!model.IDDiaDiem.HasValue || model.IDDiaDiem.Value <= 0)
        {
            return "Vui lòng chọn địa điểm khách hàng.";
        }

        var location = await GetLocationByIdAsync(connection, transaction, model.IDDiaDiem.Value, cancellationToken);
        if (location is null)
        {
            return "Không tìm thấy địa điểm khách hàng đã chọn.";
        }

        if (!location.IDKhachHang.HasValue || location.IDKhachHang.Value <= 0)
        {
            return "Địa điểm đã chọn chưa liên kết khách hàng.";
        }

        if (!model.IDKhachHang.HasValue || model.IDKhachHang.Value != location.IDKhachHang.Value)
        {
            model.IDKhachHang = location.IDKhachHang;
        }

        model.NgayYeuCau ??= DateTime.Today;
        model.TrangThaiYeuCau = YeuCauTrangThaiCatalog.Normalize(model.TrangThaiYeuCau);

        model.NhanVienThucHienText = BuildRequestEmployeeSummary(model);
        return null;
    }

    private async Task<YeuCauLocationOption?> GetLocationByIdAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int idDiaDiem,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT TOP (1)
                dd.ID,
                dd.IDKhachHang,
                kh.TenKhachHang,
                dd.DiaChi,
                dd.NguoiLienHe,
                dd.DienThoai,
                dd.LongAddress,
                dd.LatAddress,
                CAST(ISNULL(dd.TrangThaiSuDung, 1) AS bit) AS TrangThaiSuDung
            FROM [{LocationTableName}] AS dd
            LEFT JOIN [{CustomerTableName}] AS kh ON kh.ID = dd.IDKhachHang
            WHERE dd.ID = @IDDiaDiem
            """;
        command.Parameters.Add(new SqlParameter("@IDDiaDiem", SqlDbType.Int) { Value = idDiaDiem });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapLocationOption(reader) : null;
    }

    private async Task<string?> ResolveEmployeeNameAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int employeeId,
        CancellationToken cancellationToken)
    {
        if (employeeId <= 0)
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT TOP (1) Ho, Ten
            FROM [{EmployeeTableName}]
            WHERE ID = @Id
            """;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = employeeId });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var ho = GetNullableString(reader, "Ho") ?? string.Empty;
        var ten = GetNullableString(reader, "Ten") ?? string.Empty;
        var hoTen = string.Join(" ", new[] { ho, ten }.Where(static part => !string.IsNullOrWhiteSpace(part))).Trim();
        return string.IsNullOrWhiteSpace(hoTen) ? null : hoTen;
    }

    public async Task<IReadOnlyList<YeuCauNhanVienLienKetItem>> GetAssignedEmployeesAsync(
        int yeuCauId,
        CancellationToken cancellationToken = default)
    {
        if (yeuCauId <= 0)
        {
            return [];
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            return await LoadAssignedEmployeesAsync(connection, transaction: null, yeuCauId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load assigned employees for TblYeuCau {Id}.", yeuCauId);
            return [];
        }
    }

    public async Task<IReadOnlyList<YeuCauCongViecFormItem>> GetAssignedWorksAsync(
        int yeuCauId,
        CancellationToken cancellationToken = default)
    {
        if (yeuCauId <= 0)
        {
            return [];
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureWorkMetadataColumnsAsync(connection, cancellationToken);
            return await LoadAssignedWorksAsync(connection, transaction: null, yeuCauId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load assigned works for TblYeuCau {Id}.", yeuCauId);
            return [];
        }
    }

    public async Task<IReadOnlyList<YeuCauCheckinItem>> GetCheckinsAsync(
        int yeuCauId,
        CancellationToken cancellationToken = default)
    {
        if (yeuCauId <= 0)
        {
            return [];
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT
                    ch.ID,
                    ch.IDYeuCau,
                    ch.IDKhachHang,
                    kh.TenKhachHang,
                    ch.IDNhanVien,
                    nv.Ho,
                    nv.Ten,
                    ch.IDDiaDiem,
                    ch.ThoiDiem,
                    ch.ThoiDiemCheckOut,
                    CAST(ISNULL(ch.IsCheckIn, 0) AS bit) AS IsCheckIn,
                    ch.LongAddress,
                    ch.LatAddress,
                    ch.LongAddressCheckOut,
                    ch.LatAddressCheckOut,
                    ch.ImgPath,
                    ch.ImgPathCheckOut,
                    ch.GhiChuNhanVien,
                    ch.GhiChuCheckOut
                FROM [{CheckinHistoryTableName}] AS ch
                LEFT JOIN [{CustomerTableName}] AS kh ON kh.ID = ch.IDKhachHang
                LEFT JOIN [{EmployeeTableName}] AS nv ON nv.ID = ch.IDNhanVien
                WHERE ch.IDYeuCau = @IDYeuCau
                ORDER BY ch.ThoiDiem DESC, ch.ID DESC
                """;
            command.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = yeuCauId });

            var items = new List<YeuCauCheckinItem>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var ho = GetNullableString(reader, "Ho") ?? string.Empty;
                var ten = GetNullableString(reader, "Ten") ?? string.Empty;
                var hoTen = string.Join(" ", new[] { ho, ten }.Where(static part => !string.IsNullOrWhiteSpace(part))).Trim();

                items.Add(new YeuCauCheckinItem
                {
                    Id = GetNullableInt32(reader, "ID") ?? 0,
                    IDYeuCau = GetNullableInt32(reader, "IDYeuCau"),
                    IDKhachHang = GetNullableInt32(reader, "IDKhachHang"),
                    TenKhachHang = GetNullableString(reader, "TenKhachHang"),
                    IDNhanVien = GetNullableInt32(reader, "IDNhanVien"),
                    TenNhanVien = string.IsNullOrWhiteSpace(hoTen) ? null : hoTen,
                    IDDiaDiem = GetNullableInt32(reader, "IDDiaDiem"),
                    ThoiDiem = GetNullableDateTime(reader, "ThoiDiem"),
                    ThoiDiemCheckOut = GetNullableDateTime(reader, "ThoiDiemCheckOut"),
                    IsCheckIn = GetNullableBoolean(reader, "IsCheckIn") ?? false,
                    LongAddress = GetNullableDecimal(reader, "LongAddress"),
                    LatAddress = GetNullableDecimal(reader, "LatAddress"),
                    LongAddressCheckOut = GetNullableDecimal(reader, "LongAddressCheckOut"),
                    LatAddressCheckOut = GetNullableDecimal(reader, "LatAddressCheckOut"),
                    ImgPath = GetNullableString(reader, "ImgPath"),
                    ImgPathCheckOut = GetNullableString(reader, "ImgPathCheckOut"),
                    GhiChuNhanVien = GetNullableString(reader, "GhiChuNhanVien"),
                    GhiChuCheckOut = GetNullableString(reader, "GhiChuCheckOut")
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load checkin history for TblYeuCau {Id}.", yeuCauId);
            return [];
        }
    }

    public async Task<decimal?> GetCheckinDistanceLimitMetersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT TOP (1) GiaTri
                FROM [{SystemConfigTableName}]
                WHERE MaCauHinh = @MaCauHinh
                """;
            command.Parameters.Add(new SqlParameter("@MaCauHinh", SqlDbType.NVarChar, 100)
            {
                Value = "KM_limit"
            });

            var rawValue = await command.ExecuteScalarAsync(cancellationToken);
            return ParseNullableDecimal(rawValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load KM_limit from TblCauHinhHeThong.");
            return null;
        }
    }

    public async Task<bool> IsEmployeeAssignedToRequestAsync(
        int yeuCauId,
        int employeeId,
        CancellationToken cancellationToken = default)
    {
        if (yeuCauId <= 0 || employeeId <= 0)
        {
            return false;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT TOP (1) 1
                FROM [{WorkTableName}] AS ycvc
                INNER JOIN [{AssignmentTableName}] AS lknv
                    ON lknv.IDYeuCauCongViec = ycvc.ID
                WHERE ycvc.IDYeuCau = @IDYeuCau
                  AND lknv.IDNhanVien = @IDNhanVien
                """;
            command.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = yeuCauId });
            command.Parameters.Add(new SqlParameter("@IDNhanVien", SqlDbType.Int) { Value = employeeId });

            return await command.ExecuteScalarAsync(cancellationToken) is not null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check employee {EmployeeId} assignment for TblYeuCau {Id}.", employeeId, yeuCauId);
            return false;
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateCheckinAsync(
        YeuCauCheckinCreateModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (model.IDYeuCau <= 0)
        {
            return (false, "Không xác định được yêu cầu checkin.", null);
        }

        if (!model.IDNhanVien.HasValue || model.IDNhanVien.Value <= 0)
        {
            return (false, "Không xác định được nhân viên checkin.", null);
        }

        if (!model.ThoiDiem.HasValue)
        {
            return (false, "Vui lòng chọn thời điểm checkin.", null);
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await using (var openCheckinCommand = connection.CreateCommand())
            {
                openCheckinCommand.Transaction = transaction;
                openCheckinCommand.CommandText = $"""
                    SELECT TOP (1) ID
                    FROM [{CheckinHistoryTableName}] WITH (UPDLOCK, HOLDLOCK)
                    WHERE IDYeuCau = @IDYeuCau
                      AND IDNhanVien = @IDNhanVien
                      AND ThoiDiemCheckOut IS NULL
                    ORDER BY ThoiDiem DESC, ID DESC
                    """;
                openCheckinCommand.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = model.IDYeuCau });
                openCheckinCommand.Parameters.Add(new SqlParameter("@IDNhanVien", SqlDbType.Int) { Value = model.IDNhanVien.Value });

                if (await openCheckinCommand.ExecuteScalarAsync(cancellationToken) is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return (false, "Nhan vien nay con checkin chua checkout. Vui long checkout truoc khi checkin tiep.", null);
                }
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO [{CheckinHistoryTableName}] (
                    IDKhachHang,
                    IDNhanVien,
                    ThoiDiem,
                    IDYeuCau,
                    IDDiaDiem,
                    IsCheckIn,
                    LongAddress,
                    LatAddress,
                    ImgPath,
                    GhiChuNhanVien
                )
                VALUES (
                    @IDKhachHang,
                    @IDNhanVien,
                    @ThoiDiem,
                    @IDYeuCau,
                    @IDDiaDiem,
                    @IsCheckIn,
                    @LongAddress,
                    @LatAddress,
                    @ImgPath,
                    @GhiChuNhanVien
                );

                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;
            command.Parameters.Add(new SqlParameter("@IDKhachHang", SqlDbType.Int) { Value = ToDbValue(model.IDKhachHang) });
            command.Parameters.Add(new SqlParameter("@IDNhanVien", SqlDbType.Int) { Value = model.IDNhanVien.Value });
            command.Parameters.Add(new SqlParameter("@ThoiDiem", SqlDbType.DateTime) { Value = model.ThoiDiem.Value });
            command.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = model.IDYeuCau });
            command.Parameters.Add(new SqlParameter("@IDDiaDiem", SqlDbType.Int) { Value = ToDbValue(model.IDDiaDiem) });
            command.Parameters.Add(new SqlParameter("@IsCheckIn", SqlDbType.Bit) { Value = true });
            command.Parameters.Add(new SqlParameter("@LongAddress", SqlDbType.Decimal) { Precision = 18, Scale = 10, Value = ToDbValue(model.LongAddress) });
            command.Parameters.Add(new SqlParameter("@LatAddress", SqlDbType.Decimal) { Precision = 18, Scale = 10, Value = ToDbValue(model.LatAddress) });
            command.Parameters.Add(new SqlParameter("@ImgPath", SqlDbType.NVarChar, 500) { Value = ToDbValue(model.ImgPath) });
            command.Parameters.Add(new SqlParameter("@GhiChuNhanVien", SqlDbType.NVarChar, 1000) { Value = ToDbValue(model.GhiChuNhanVien) });

            var id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
            await transaction.CommitAsync(cancellationToken);
            return (true, null, id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create checkin for TblYeuCau {Id} by {User}.", model.IDYeuCau, currentUser);
            return (false, "Không thể lưu thông tin checkin.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> CreateCheckoutAsync(
        YeuCauCheckoutCreateModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (model.Id <= 0 || model.IDYeuCau <= 0)
        {
            return (false, "Khong xac dinh duoc checkin can checkout.");
        }

        if (!model.IDNhanVien.HasValue || model.IDNhanVien.Value <= 0)
        {
            return (false, "Khong xac dinh duoc nhan vien checkout.");
        }

        if (!model.ThoiDiemCheckOut.HasValue)
        {
            return (false, "Vui long chon thoi diem checkout.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                UPDATE [{CheckinHistoryTableName}]
                SET
                    ThoiDiemCheckOut = @ThoiDiemCheckOut,
                    LongAddressCheckOut = @LongAddressCheckOut,
                    LatAddressCheckOut = @LatAddressCheckOut,
                    ImgPathCheckOut = @ImgPathCheckOut,
                    GhiChuCheckOut = @GhiChuCheckOut
                WHERE ID = @Id
                  AND IDYeuCau = @IDYeuCau
                  AND IDNhanVien = @IDNhanVien
                  AND ThoiDiemCheckOut IS NULL
                  AND ThoiDiem IS NOT NULL
                  AND @ThoiDiemCheckOut >= ThoiDiem
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id });
            command.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = model.IDYeuCau });
            command.Parameters.Add(new SqlParameter("@IDNhanVien", SqlDbType.Int) { Value = model.IDNhanVien.Value });
            command.Parameters.Add(new SqlParameter("@ThoiDiemCheckOut", SqlDbType.DateTime) { Value = model.ThoiDiemCheckOut.Value });
            command.Parameters.Add(new SqlParameter("@LongAddressCheckOut", SqlDbType.Decimal) { Precision = 18, Scale = 10, Value = ToDbValue(model.LongAddressCheckOut) });
            command.Parameters.Add(new SqlParameter("@LatAddressCheckOut", SqlDbType.Decimal) { Precision = 18, Scale = 10, Value = ToDbValue(model.LatAddressCheckOut) });
            command.Parameters.Add(new SqlParameter("@ImgPathCheckOut", SqlDbType.NVarChar, 500) { Value = ToDbValue(model.ImgPathCheckOut) });
            command.Parameters.Add(new SqlParameter("@GhiChuCheckOut", SqlDbType.NVarChar, 1000) { Value = ToDbValue(model.GhiChuCheckOut) });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            return affectedRows > 0
                ? (true, null)
                : (false, "Khong tim thay checkin dang mo hoac thoi diem checkout khong hop le.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create checkout for checkin {CheckinId} / TblYeuCau {Id} by {User}.", model.Id, model.IDYeuCau, currentUser);
            return (false, "Khong the luu thong tin checkout.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateWorkImageAsync(
        int yeuCauCongViecId,
        string imagePath,
        string imageType,
        string currentUser,
        Guid? currentAccountId,
        CancellationToken cancellationToken = default)
    {
        if (yeuCauCongViecId <= 0)
        {
            return (false, "Khong xac dinh duoc cong viec can luu anh.", null);
        }

        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return (false, "Khong xac dinh duoc duong dan anh.", null);
        }

        if (await IsWorkCompletedAsync(yeuCauCongViecId, cancellationToken))
        {
            return (false, "Cong viec da hoan thanh nen khong the them hinh anh.", null);
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureWorkImageMetadataColumnsAsync(connection, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO [{WorkImageTableName}] (
                    IDCongViec,
                    ImagePath,
                    ImageType,
                    Created_Date,
                    Created_By,
                    IDTaiKhoanNguoiDung
                )
                VALUES (
                    @IDCongViec,
                    @ImagePath,
                    @ImageType,
                    GETDATE(),
                    @CreatedBy,
                    @IDTaiKhoanNguoiDung
                );

                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;
            command.Parameters.Add(new SqlParameter("@IDCongViec", SqlDbType.Int) { Value = yeuCauCongViecId });
            command.Parameters.Add(new SqlParameter("@ImagePath", SqlDbType.NVarChar, 500) { Value = imagePath.Trim() });
            command.Parameters.Add(new SqlParameter("@ImageType", SqlDbType.NVarChar, 50) { Value = YeuCauCongViecImageTypes.Normalize(imageType) });
            command.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
            command.Parameters.Add(new SqlParameter("@IDTaiKhoanNguoiDung", SqlDbType.UniqueIdentifier) { Value = ToDbValue(currentAccountId) });

            var id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
            await _commonAuditService.WriteAsync(
                connection,
                null,
                new CommonAuditEntry(
                    "YEU_CAU",
                    "WORK_IMAGE_UPLOAD",
                    "YEU_CAU_CONG_VIEC",
                    yeuCauCongViecId.ToString(CultureInfo.InvariantCulture),
                    null,
                    "Upload anh cong viec yeu cau.",
                    currentUser,
                    Data: new { YeuCauCongViecId = yeuCauCongViecId, ImageId = id, ImagePath = imagePath, ImageType = imageType, CurrentAccountId = currentAccountId }),
                cancellationToken);
            return (true, null, id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create work image for TblYeuCauCongViec {Id} ({ImageType}).", yeuCauCongViecId, imageType);
            return (false, "Khong the luu anh cong viec.", null);
        }
    }

    public async Task<bool> IsWorkCompletedAsync(int yeuCauCongViecId, CancellationToken cancellationToken = default)
    {
        if (yeuCauCongViecId <= 0)
        {
            return false;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT TOP 1 TrangThaiCongViec
                FROM [{WorkTableName}]
                WHERE ID = @ID;
                """;
            command.Parameters.Add(new SqlParameter("@ID", SqlDbType.Int) { Value = yeuCauCongViecId });

            var status = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            return YeuCauCongViecTrangThaiCatalog.IsCompleted(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read work status for TblYeuCauCongViec {Id}.", yeuCauCongViecId);
            return false;
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, YeuCauCongViecChecklistFormItem? Checklist)> CreateWorkChecklistAsync(
        int congViecId,
        string? tenChecklist,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (congViecId <= 0)
        {
            return (false, "Khong xac dinh duoc cong viec can them checklist.", null);
        }

        var normalizedName = TrimNullableToLength(tenChecklist, 250);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return (false, "Vui long nhap ten checklist.", null);
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                DECLARE @NextPosition int;

                SELECT @NextPosition = ISNULL(MAX(ISNULL(ViTri, 0)), 0) + 1
                FROM [{ChecklistTemplateTableName}]
                WHERE IDCongViec = @IDCongViec;

                INSERT INTO [{ChecklistTemplateTableName}] (
                    TenCheckList,
                    ViTri,
                    IDCongViec,
                    Created_Date,
                    Created_By,
                    TrangThaiSuDung
                )
                VALUES (
                    @TenCheckList,
                    @NextPosition,
                    @IDCongViec,
                    GETDATE(),
                    @CreatedBy,
                    1
                );

                SELECT CAST(SCOPE_IDENTITY() AS int), @NextPosition;
                """;
            command.Parameters.Add(new SqlParameter("@IDCongViec", SqlDbType.Int) { Value = congViecId });
            command.Parameters.Add(new SqlParameter("@TenCheckList", SqlDbType.NVarChar, 250) { Value = normalizedName });
            command.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return (false, "Khong the them checklist cong viec.", null);
            }

            var checklist = new YeuCauCongViecChecklistFormItem
            {
                ChecklistId = Convert.ToInt32(reader.GetValue(0)),
                TenChecklist = normalizedName,
                ViTri = Convert.ToInt32(reader.GetValue(1))
            };

            return (true, null, checklist);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create checklist for TblCongViec {Id}.", congViecId);
            return (false, "Khong the them checklist cong viec.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, string? ImagePath)> DeleteWorkImageAsync(
        int imageId,
        Guid? currentAccountId,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (imageId <= 0)
        {
            return (false, "Khong xac dinh duoc anh can xoa.", null);
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureWorkImageMetadataColumnsAsync(connection, cancellationToken);

            int workId;
            string? imagePath;
            Guid? ownerAccountId;
            string? createdBy;
            await using (var selectCommand = connection.CreateCommand())
            {
                selectCommand.CommandText = $"""
                    SELECT TOP (1)
                        IDCongViec,
                        ImagePath,
                        IDTaiKhoanNguoiDung,
                        Created_By
                    FROM [{WorkImageTableName}]
                    WHERE ID = @Id
                    """;
                selectCommand.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = imageId });

                await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return (false, "Khong tim thay anh can xoa.", null);
                }

                workId = GetNullableInt32(reader, "IDCongViec") ?? 0;
                imagePath = GetNullableString(reader, "ImagePath");
                ownerAccountId = GetNullableGuid(reader, "IDTaiKhoanNguoiDung");
                createdBy = GetNullableString(reader, "Created_By");
            }

            if (!CanDeleteWorkImage(ownerAccountId, createdBy, currentAccountId, currentUser))
            {
                return (false, "Chi nguoi da them hinh anh moi duoc xoa hinh anh nay.", null);
            }

            await using var deleteCommand = connection.CreateCommand();
            deleteCommand.CommandText = $"""
                DELETE FROM [{WorkImageTableName}]
                WHERE ID = @Id
                """;
            deleteCommand.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = imageId });
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

            await _commonAuditService.WriteAsync(
                connection,
                null,
                new CommonAuditEntry(
                    "YEU_CAU",
                    "WORK_IMAGE_DELETE",
                    "YEU_CAU_CONG_VIEC",
                    workId.ToString(CultureInfo.InvariantCulture),
                    null,
                    "Xoa anh cong viec yeu cau.",
                    currentUser,
                    Data: new { ImageId = imageId, ImagePath = imagePath, CurrentAccountId = currentAccountId }),
                cancellationToken);

            return (true, null, imagePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete work image {Id}.", imageId);
            return (false, "Khong the xoa anh cong viec.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, string? ImgPath)> DeleteCheckinAsync(
        int id,
        int yeuCauId,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0 || yeuCauId <= 0)
        {
            return (false, "Không xác định được checkin cần xóa.", null);
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            string? imgPath;
            await using (var selectCommand = connection.CreateCommand())
            {
                selectCommand.Transaction = transaction;
                selectCommand.CommandText = $"""
                    SELECT TOP (1) ImgPath
                    FROM [{CheckinHistoryTableName}]
                    WHERE ID = @Id AND IDYeuCau = @IDYeuCau
                    """;
                selectCommand.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
                selectCommand.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = yeuCauId });

                var rawImgPath = await selectCommand.ExecuteScalarAsync(cancellationToken);
                if (rawImgPath is null || rawImgPath == DBNull.Value)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return (false, "Không tìm thấy checkin cần xóa.", null);
                }

                imgPath = rawImgPath.ToString();
            }

            await using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = $"""
                    DELETE FROM [{CheckinHistoryTableName}]
                    WHERE ID = @Id AND IDYeuCau = @IDYeuCau
                    """;
                deleteCommand.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
                deleteCommand.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = yeuCauId });

                var affectedRows = await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
                if (affectedRows <= 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return (false, "Không tìm thấy checkin cần xóa.", null);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return (true, null, imgPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete checkin {CheckinId} for TblYeuCau {YeuCauId}.", id, yeuCauId);
            return (false, "Không thể xóa thông tin checkin.", null);
        }
    }

    private static async Task<IReadOnlyList<YeuCauNhanVienLienKetItem>> LoadAssignedEmployeesAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int yeuCauId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                lknv.IDNhanVien,
                nv.Ho,
                nv.Ten,
                nv.ChucVu,
                nv.Avatar
            FROM [{AssignmentTableName}] AS lknv
            LEFT JOIN [{EmployeeTableName}] AS nv ON nv.ID = lknv.IDNhanVien
            WHERE lknv.IDYeuCauCongViec = @IDYeuCauCongViec
            ORDER BY lknv.ID DESC
            """;
        command.Parameters.Add(new SqlParameter("@IDYeuCauCongViec", SqlDbType.Int) { Value = yeuCauId });

        var items = new List<YeuCauNhanVienLienKetItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var ho = GetNullableString(reader, "Ho") ?? string.Empty;
            var ten = GetNullableString(reader, "Ten") ?? string.Empty;
            var hoTen = string.Join(" ", new[] { ho, ten }.Where(static part => !string.IsNullOrWhiteSpace(part))).Trim();

            items.Add(new YeuCauNhanVienLienKetItem
            {
                NhanVienId = GetNullableInt32(reader, "IDNhanVien"),
                HoTen = string.IsNullOrWhiteSpace(hoTen) ? "Nhân viên không xác định" : hoTen,
                ChucVu = GetNullableString(reader, "ChucVu"),
                Avatar = GetNullableString(reader, "Avatar")
            });
        }

        return items
            .GroupBy(item => item.NhanVienId)
            .Select(group => group.First())
            .ToList();
    }

    private static async Task<IReadOnlyList<YeuCauCongViecFormItem>> LoadAssignedWorksAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int yeuCauId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                ycvc.ID,
                ycvc.IDCongViec,
                cv.TenCongViec,
                cv.SoLuongAnhCheckIn,
                cv.SoLuongAnhCheckOut,
                ycvc.TrangThaiCongViec,
                ycvc.GhiChu,
                ycvc.CheckInTime,
                ycvc.CheckoutTime
            FROM [{WorkTableName}] AS ycvc
            LEFT JOIN [TblCongViec] AS cv ON cv.ID = ycvc.IDCongViec
            WHERE ycvc.IDYeuCau = @IDYeuCau
            ORDER BY ycvc.ID
            """;
        command.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = yeuCauId });

        var works = new List<YeuCauCongViecFormItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            works.Add(new YeuCauCongViecFormItem
            {
                YeuCauCongViecId = GetNullableInt32(reader, "ID"),
                CongViecId = GetNullableInt32(reader, "IDCongViec"),
                SoLuongAnhCheckIn = Math.Max(0, GetNullableInt32(reader, "SoLuongAnhCheckIn") ?? 0),
                SoLuongAnhCheckOut = Math.Max(0, GetNullableInt32(reader, "SoLuongAnhCheckOut") ?? 0),
                TenCongViec = GetNullableString(reader, "TenCongViec") ?? "Công việc",
                TrangThaiCongViec = YeuCauCongViecTrangThaiCatalog.Normalize(GetNullableString(reader, "TrangThaiCongViec")),
                GhiChu = GetNullableString(reader, "GhiChu"),
                CheckInTime = GetNullableDateTime(reader, "CheckInTime"),
                CheckOutTime = GetNullableDateTime(reader, "CheckoutTime")
            });
        }

        await reader.CloseAsync();

            foreach (var work in works)
            {
            work.Images = (await LoadWorkImagesAsync(connection, transaction, work.YeuCauCongViecId ?? 0, cancellationToken)).ToList();
            work.Checklists = (await LoadWorkChecklistsAsync(connection, transaction, work.YeuCauCongViecId ?? 0, work.CongViecId ?? 0, cancellationToken)).ToList();
            work.NhanViens = (await LoadWorkEmployeesAsync(connection, transaction, work.YeuCauCongViecId ?? 0, cancellationToken)).ToList();
            }

            return works;
        }

    private static async Task<IReadOnlyList<YeuCauCongViecImageItem>> LoadWorkImagesAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int yeuCauCongViecId,
        CancellationToken cancellationToken)
    {
        if (yeuCauCongViecId <= 0)
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                ID,
                IDCongViec,
                ImagePath,
                ImageType,
                Created_Date,
                Created_By,
                IDTaiKhoanNguoiDung
            FROM [{WorkImageTableName}]
            WHERE IDCongViec = @IDCongViec
            ORDER BY ID
            """;
        command.Parameters.Add(new SqlParameter("@IDCongViec", SqlDbType.Int) { Value = yeuCauCongViecId });

        var items = new List<YeuCauCongViecImageItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var imagePath = GetNullableString(reader, "ImagePath") ?? string.Empty;
            items.Add(new YeuCauCongViecImageItem
            {
                Id = GetNullableInt32(reader, "ID") ?? 0,
                IDCongViec = GetNullableInt32(reader, "IDCongViec") ?? yeuCauCongViecId,
                ImagePath = imagePath,
                ImageType = !string.IsNullOrWhiteSpace(GetNullableString(reader, "ImageType"))
                    ? YeuCauCongViecImageTypes.Normalize(GetNullableString(reader, "ImageType"))
                    : imagePath.Contains("/CheckOut/", StringComparison.OrdinalIgnoreCase) ||
                        imagePath.Contains("\\CheckOut\\", StringComparison.OrdinalIgnoreCase)
                            ? YeuCauCongViecImageTypes.CheckOut
                            : YeuCauCongViecImageTypes.CheckIn,
                CreatedDate = GetNullableDateTime(reader, "Created_Date"),
                CreatedBy = GetNullableString(reader, "Created_By"),
                CreatedByAccountId = GetNullableGuid(reader, "IDTaiKhoanNguoiDung")
            });
        }

        return items;
    }

    private static async Task<IReadOnlyList<YeuCauCongViecChecklistFormItem>> LoadWorkChecklistsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int yeuCauCongViecId,
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
                ckt.ID,
                ckt.TenCheckList,
                ISNULL(ckt.ViTri, 0) AS ViTri,
                ycvcck.Created_Date,
                ycvcck.Created_By,
                ycvcck.FinishDate,
                ycvcck.FinishBy
            FROM [{ChecklistTemplateTableName}] AS ckt
            LEFT JOIN [{WorkChecklistTableName}] AS ycvcck
                ON ycvcck.IDCheckList = ckt.ID
                AND ycvcck.IDCongViec = @IDYeuCauCongViec
            WHERE ckt.IDCongViec = @IDCongViec
              AND ISNULL(ckt.TrangThaiSuDung, 1) = 1
            ORDER BY
                CASE WHEN ckt.ViTri IS NULL OR ckt.ViTri <= 0 THEN 2147483647 ELSE ckt.ViTri END,
                ckt.ID
            """;
        command.Parameters.Add(new SqlParameter("@IDYeuCauCongViec", SqlDbType.Int) { Value = yeuCauCongViecId });
        command.Parameters.Add(new SqlParameter("@IDCongViec", SqlDbType.Int) { Value = congViecId });

        var items = new List<YeuCauCongViecChecklistFormItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new YeuCauCongViecChecklistFormItem
            {
                ChecklistId = reader.GetInt32(reader.GetOrdinal("ID")),
                TenChecklist = GetNullableString(reader, "TenCheckList") ?? string.Empty,
                ViTri = GetNullableInt32(reader, "ViTri") ?? 0,
                IsCompleted = GetNullableDateTime(reader, "FinishDate").HasValue,
                CreatedDate = GetNullableDateTime(reader, "Created_Date"),
                CreatedBy = GetNullableString(reader, "Created_By"),
                FinishDate = GetNullableDateTime(reader, "FinishDate"),
                FinishBy = GetNullableString(reader, "FinishBy")
            });
        }

        return items;
    }

    private static async Task<IReadOnlyList<YeuCauCongViecNhanVienFormItem>> LoadWorkEmployeesAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int yeuCauCongViecId,
        CancellationToken cancellationToken)
    {
        if (yeuCauCongViecId <= 0)
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                lknv.IDNhanVien,
                nv.Ho,
                nv.Ten,
                nv.ChucVu,
                nv.Avatar
            FROM [{AssignmentTableName}] AS lknv
            LEFT JOIN [{EmployeeTableName}] AS nv ON nv.ID = lknv.IDNhanVien
            WHERE lknv.IDYeuCauCongViec = @IDYeuCauCongViec
            ORDER BY lknv.ID DESC
            """;
        command.Parameters.Add(new SqlParameter("@IDYeuCauCongViec", SqlDbType.Int) { Value = yeuCauCongViecId });

        var items = new List<YeuCauCongViecNhanVienFormItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var ho = GetNullableString(reader, "Ho") ?? string.Empty;
            var ten = GetNullableString(reader, "Ten") ?? string.Empty;
            var hoTen = string.Join(" ", new[] { ho, ten }.Where(static part => !string.IsNullOrWhiteSpace(part))).Trim();

            items.Add(new YeuCauCongViecNhanVienFormItem
            {
                NhanVienId = GetNullableInt32(reader, "IDNhanVien"),
                HoTen = string.IsNullOrWhiteSpace(hoTen) ? "Nhân viên không xác định" : hoTen,
                ChucVu = GetNullableString(reader, "ChucVu"),
                Avatar = GetNullableString(reader, "Avatar")
            });
        }

        return items
            .GroupBy(item => item.NhanVienId)
            .Select(group => group.First())
            .ToList();
    }

    private static async Task<WorkStateSnapshot?> LoadWorkStateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int yeuCauCongViecId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT TrangThaiCongViec, CheckInTime
            FROM [{WorkTableName}]
            WHERE ID = @ID
            """;
        command.Parameters.Add(new SqlParameter("@ID", SqlDbType.Int) { Value = yeuCauCongViecId });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var status = GetNullableString(reader, "TrangThaiCongViec");
        return new WorkStateSnapshot(
            YeuCauCongViecTrangThaiCatalog.IsCompleted(status),
            GetNullableDateTime(reader, "CheckInTime"));
    }

    private sealed record WorkStateSnapshot(bool IsCompleted, DateTime? CheckInTime);

    private static async Task SyncAssignedEmployeesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int yeuCauId,
        IEnumerable<YeuCauNhanVienLienKetItem>? items,
        CancellationToken cancellationToken)
    {
        var employeeIds = items?
            .Select(item => item.NhanVienId)
            .Where(id => id.HasValue && id.Value > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList() ?? [];

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            if (employeeIds.Count == 0)
            {
                deleteCommand.CommandText = $"""
                    DELETE FROM [{AssignmentTableName}]
                    WHERE IDYeuCauCongViec = @IDYeuCauCongViec
                    """;
                deleteCommand.Parameters.Add(new SqlParameter("@IDYeuCauCongViec", SqlDbType.Int) { Value = yeuCauId });
            }
            else
            {
                var placeholders = new List<string>();
                for (var index = 0; index < employeeIds.Count; index++)
                {
                    placeholders.Add($"@EmployeeId{index}");
                    deleteCommand.Parameters.Add(new SqlParameter($"@EmployeeId{index}", SqlDbType.Int) { Value = employeeIds[index] });
                }

                deleteCommand.CommandText = $"""
                    DELETE FROM [{AssignmentTableName}]
                    WHERE IDYeuCauCongViec = @IDYeuCauCongViec
                      AND IDNhanVien NOT IN ({string.Join(", ", placeholders)})
                    """;
                deleteCommand.Parameters.Add(new SqlParameter("@IDYeuCauCongViec", SqlDbType.Int) { Value = yeuCauId });
            }

            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var employeeId in employeeIds)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                IF NOT EXISTS (
                    SELECT 1
                    FROM [{AssignmentTableName}]
                    WHERE IDYeuCauCongViec = @IDYeuCauCongViec
                      AND IDNhanVien = @IDNhanVien
                )
                BEGIN
                    INSERT INTO [{AssignmentTableName}] (
                        IDYeuCauCongViec,
                        IDNhanVien
                    )
                    VALUES (
                        @IDYeuCauCongViec,
                        @IDNhanVien
                    )
                END
                """;
            command.Parameters.Add(new SqlParameter("@IDYeuCauCongViec", SqlDbType.Int) { Value = yeuCauId });
            command.Parameters.Add(new SqlParameter("@IDNhanVien", SqlDbType.Int) { Value = employeeId });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task SyncAssignedWorksAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int yeuCauId,
        IEnumerable<YeuCauCongViecFormItem>? works,
        string currentUser,
        CancellationToken cancellationToken)
    {
        var workItems = works?
            .Where(work => work.CongViecId.HasValue && work.CongViecId.Value > 0)
            .ToList() ?? [];

        var keepIds = workItems
            .Where(work => work.YeuCauCongViecId.HasValue && work.YeuCauCongViecId.Value > 0)
            .Select(work => work.YeuCauCongViecId!.Value)
            .Distinct()
            .ToList();

        await using (var removedWorkStatusCommand = connection.CreateCommand())
        {
            removedWorkStatusCommand.Transaction = transaction;
            if (keepIds.Count == 0)
            {
                removedWorkStatusCommand.CommandText = $"""
                    SELECT TrangThaiCongViec
                    FROM [{WorkTableName}]
                    WHERE IDYeuCau = @IDYeuCau
                    """;
                removedWorkStatusCommand.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = yeuCauId });
            }
            else
            {
                var placeholders = new List<string>();
                for (var index = 0; index < keepIds.Count; index++)
                {
                    placeholders.Add($"@KeepId{index}");
                    removedWorkStatusCommand.Parameters.Add(new SqlParameter($"@KeepId{index}", SqlDbType.Int) { Value = keepIds[index] });
                }

                removedWorkStatusCommand.CommandText = $"""
                    SELECT TrangThaiCongViec
                    FROM [{WorkTableName}]
                    WHERE IDYeuCau = @IDYeuCau
                      AND ID NOT IN ({string.Join(", ", placeholders)})
                    """;
                removedWorkStatusCommand.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = yeuCauId });
            }

            await using var removedWorkStatusReader = await removedWorkStatusCommand.ExecuteReaderAsync(cancellationToken);
            while (await removedWorkStatusReader.ReadAsync(cancellationToken))
            {
                if (YeuCauCongViecTrangThaiCatalog.IsCompleted(GetNullableString(removedWorkStatusReader, "TrangThaiCongViec")))
                {
                    throw new YeuCauBusinessRuleException("Cong viec da hoan thanh nen khong the xoa khoi phieu yeu cau.");
                }
            }
        }

        await using (var deleteChecklistCommand = connection.CreateCommand())
        {
            deleteChecklistCommand.Transaction = transaction;
            if (keepIds.Count == 0)
            {
                deleteChecklistCommand.CommandText = $"""
                    DELETE ycvcck
                    FROM [{WorkChecklistTableName}] AS ycvcck
                    INNER JOIN [{WorkTableName}] AS ycvc ON ycvc.ID = ycvcck.IDCongViec
                    WHERE ycvc.IDYeuCau = @IDYeuCau
                    """;
                deleteChecklistCommand.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = yeuCauId });
            }
            else
            {
                var placeholders = new List<string>();
                for (var index = 0; index < keepIds.Count; index++)
                {
                    placeholders.Add($"@KeepId{index}");
                    deleteChecklistCommand.Parameters.Add(new SqlParameter($"@KeepId{index}", SqlDbType.Int) { Value = keepIds[index] });
                }

                deleteChecklistCommand.CommandText = $"""
                    DELETE ycvcck
                    FROM [{WorkChecklistTableName}] AS ycvcck
                    INNER JOIN [{WorkTableName}] AS ycvc ON ycvc.ID = ycvcck.IDCongViec
                    WHERE ycvc.IDYeuCau = @IDYeuCau
                      AND ycvc.ID NOT IN ({string.Join(", ", placeholders)})
                    """;
                deleteChecklistCommand.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = yeuCauId });
            }

            await deleteChecklistCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteEmployeeCommand = connection.CreateCommand())
        {
            deleteEmployeeCommand.Transaction = transaction;
            if (keepIds.Count == 0)
            {
                deleteEmployeeCommand.CommandText = $"""
                    DELETE lknv
                    FROM [{AssignmentTableName}] AS lknv
                    INNER JOIN [{WorkTableName}] AS ycvc ON ycvc.ID = lknv.IDYeuCauCongViec
                    WHERE ycvc.IDYeuCau = @IDYeuCau
                    """;
                deleteEmployeeCommand.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = yeuCauId });
            }
            else
            {
                var placeholders = new List<string>();
                for (var index = 0; index < keepIds.Count; index++)
                {
                    placeholders.Add($"@KeepId{index}");
                    deleteEmployeeCommand.Parameters.Add(new SqlParameter($"@KeepId{index}", SqlDbType.Int) { Value = keepIds[index] });
                }

                deleteEmployeeCommand.CommandText = $"""
                    DELETE lknv
                    FROM [{AssignmentTableName}] AS lknv
                    INNER JOIN [{WorkTableName}] AS ycvc ON ycvc.ID = lknv.IDYeuCauCongViec
                    WHERE ycvc.IDYeuCau = @IDYeuCau
                      AND ycvc.ID NOT IN ({string.Join(", ", placeholders)})
                    """;
                deleteEmployeeCommand.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = yeuCauId });
            }

            await deleteEmployeeCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteWorkCommand = connection.CreateCommand())
        {
            deleteWorkCommand.Transaction = transaction;
            if (keepIds.Count == 0)
            {
                deleteWorkCommand.CommandText = $"""
                    DELETE FROM [{WorkTableName}]
                    WHERE IDYeuCau = @IDYeuCau
                    """;
                deleteWorkCommand.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = yeuCauId });
            }
            else
            {
                var placeholders = new List<string>();
                for (var index = 0; index < keepIds.Count; index++)
                {
                    placeholders.Add($"@KeepId{index}");
                    deleteWorkCommand.Parameters.Add(new SqlParameter($"@KeepId{index}", SqlDbType.Int) { Value = keepIds[index] });
                }

                deleteWorkCommand.CommandText = $"""
                    DELETE FROM [{WorkTableName}]
                    WHERE IDYeuCau = @IDYeuCau
                      AND ID NOT IN ({string.Join(", ", placeholders)})
                    """;
                deleteWorkCommand.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = yeuCauId });
            }

            await deleteWorkCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var work in workItems)
        {
            work.TrangThaiCongViec = YeuCauCongViecTrangThaiCatalog.Normalize(work.TrangThaiCongViec);
            work.GhiChu = TrimNullableToLength(work.GhiChu, 500);
            var isNewWork = !work.YeuCauCongViecId.HasValue || work.YeuCauCongViecId.Value <= 0;
            var previousState = isNewWork
                ? null
                : await LoadWorkStateAsync(connection, transaction, work.YeuCauCongViecId!.Value, cancellationToken);
            var previousWorkEmployeeIds = isNewWork
                ? new List<int>()
                : (await LoadWorkEmployeesAsync(connection, transaction, work.YeuCauCongViecId!.Value, cancellationToken))
                    .Where(employee => employee.NhanVienId.HasValue && employee.NhanVienId.Value > 0)
                    .Select(employee => employee.NhanVienId!.Value)
                    .Distinct()
                    .ToList();
            var workEmployeeIds = work.NhanViens
                .Where(employee => employee.NhanVienId.HasValue && employee.NhanVienId.Value > 0)
                .Select(employee => employee.NhanVienId!.Value)
                .Distinct()
                .ToList();

            if (previousState?.IsCompleted == true)
            {
                var employeeChanged = previousWorkEmployeeIds.Count != workEmployeeIds.Count ||
                    previousWorkEmployeeIds.Except(workEmployeeIds).Any() ||
                    workEmployeeIds.Except(previousWorkEmployeeIds).Any();
                if (employeeChanged)
                {
                    throw new YeuCauBusinessRuleException("Cong viec da hoan thanh nen khong the thay doi nhan vien thuc hien.");
                }

                if (previousState.CheckInTime != work.CheckInTime)
                {
                    throw new YeuCauBusinessRuleException("Cong viec da hoan thanh nen khong the thay doi thoi gian bat dau.");
                }
            }

            if (!work.YeuCauCongViecId.HasValue || work.YeuCauCongViecId.Value <= 0)
            {
                work.CheckInTime ??= DateTime.Now;
                await using var insertWorkCommand = connection.CreateCommand();
                insertWorkCommand.Transaction = transaction;
                insertWorkCommand.CommandText = $"""
                    INSERT INTO [{WorkTableName}] (
                        IDYeuCau,
                        IDCongViec,
                        TrangThaiCongViec,
                        GhiChu,
                        CheckInTime,
                        CheckoutTime,
                        Created_Date,
                        Created_By,
                        Updated_Date,
                        Updated_By
                    )
                    VALUES (
                        @IDYeuCau,
                        @IDCongViec,
                        @TrangThaiCongViec,
                        @GhiChu,
                        @CheckInTime,
                        @CheckoutTime,
                        GETDATE(),
                        @CreatedBy,
                        GETDATE(),
                        @UpdatedBy
                    );

                    SELECT CAST(SCOPE_IDENTITY() AS int);
                    """;
                insertWorkCommand.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = yeuCauId });
                insertWorkCommand.Parameters.Add(new SqlParameter("@IDCongViec", SqlDbType.Int) { Value = work.CongViecId!.Value });
                insertWorkCommand.Parameters.Add(new SqlParameter("@TrangThaiCongViec", SqlDbType.NVarChar, 50) { Value = YeuCauCongViecTrangThaiCatalog.Normalize(work.TrangThaiCongViec) });
                insertWorkCommand.Parameters.Add(new SqlParameter("@GhiChu", SqlDbType.NVarChar, 500) { Value = ToDbValue(TrimNullableToLength(work.GhiChu, 500)) });
                insertWorkCommand.Parameters.Add(new SqlParameter("@CheckInTime", SqlDbType.DateTime) { Value = ToDbValue(work.CheckInTime) });
                insertWorkCommand.Parameters.Add(new SqlParameter("@CheckoutTime", SqlDbType.DateTime) { Value = ToDbValue(work.CheckOutTime) });
                insertWorkCommand.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
                insertWorkCommand.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
                work.YeuCauCongViecId = Convert.ToInt32(await insertWorkCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
            }

            await using (var updateCommand = connection.CreateCommand())
            {
                updateCommand.Transaction = transaction;
                updateCommand.CommandText = $"""
                    UPDATE [{WorkTableName}]
                    SET
                        IDCongViec = @IDCongViec,
                        TrangThaiCongViec = @TrangThaiCongViec,
                        GhiChu = @GhiChu,
                        CheckInTime = @CheckInTime,
                        CheckoutTime = @CheckoutTime,
                        Updated_Date = GETDATE(),
                        Updated_By = @UpdatedBy
                    WHERE ID = @Id
                      AND IDYeuCau = @IDYeuCau
                """;
                updateCommand.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = work.YeuCauCongViecId!.Value });
                updateCommand.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = yeuCauId });
                updateCommand.Parameters.Add(new SqlParameter("@IDCongViec", SqlDbType.Int) { Value = work.CongViecId!.Value });
                updateCommand.Parameters.Add(new SqlParameter("@TrangThaiCongViec", SqlDbType.NVarChar, 50) { Value = YeuCauCongViecTrangThaiCatalog.Normalize(work.TrangThaiCongViec) });
                updateCommand.Parameters.Add(new SqlParameter("@GhiChu", SqlDbType.NVarChar, 500) { Value = ToDbValue(TrimNullableToLength(work.GhiChu, 500)) });
                updateCommand.Parameters.Add(new SqlParameter("@CheckInTime", SqlDbType.DateTime) { Value = ToDbValue(work.CheckInTime) });
                updateCommand.Parameters.Add(new SqlParameter("@CheckoutTime", SqlDbType.DateTime) { Value = ToDbValue(work.CheckOutTime) });
                updateCommand.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await _commonAuditService.WriteAsync(
                connection,
                transaction,
                new CommonAuditEntry(
                    "YEU_CAU",
                    isNewWork ? "WORK_CREATE" : "WORK_UPDATE",
                    "YEU_CAU_CONG_VIEC",
                    work.YeuCauCongViecId!.Value.ToString(CultureInfo.InvariantCulture),
                    null,
                    isNewWork ? "Them cong viec vao phieu yeu cau." : "Cap nhat cong viec trong phieu yeu cau.",
                    currentUser,
                    Data: new
                    {
                        YeuCauId = yeuCauId,
                        work.YeuCauCongViecId,
                        work.CongViecId,
                        work.TrangThaiCongViec,
                        work.GhiChu,
                        work.CheckInTime,
                        work.CheckOutTime
                    }),
                cancellationToken);

            await using (var deleteWorkEmployeeCommand = connection.CreateCommand())
            {
                deleteWorkEmployeeCommand.Transaction = transaction;
                if (workEmployeeIds.Count == 0)
                {
                    deleteWorkEmployeeCommand.CommandText = $"""
                        DELETE FROM [{AssignmentTableName}]
                        WHERE IDYeuCauCongViec = @IDYeuCauCongViec
                        """;
                    deleteWorkEmployeeCommand.Parameters.Add(new SqlParameter("@IDYeuCauCongViec", SqlDbType.Int) { Value = work.YeuCauCongViecId!.Value });
                }
                else
                {
                    var placeholders = new List<string>();
                    for (var index = 0; index < workEmployeeIds.Count; index++)
                    {
                        placeholders.Add($"@EmployeeId{index}");
                        deleteWorkEmployeeCommand.Parameters.Add(new SqlParameter($"@EmployeeId{index}", SqlDbType.Int) { Value = workEmployeeIds[index] });
                    }

                    deleteWorkEmployeeCommand.CommandText = $"""
                        DELETE FROM [{AssignmentTableName}]
                        WHERE IDYeuCauCongViec = @IDYeuCauCongViec
                          AND IDNhanVien NOT IN ({string.Join(", ", placeholders)})
                        """;
                    deleteWorkEmployeeCommand.Parameters.Add(new SqlParameter("@IDYeuCauCongViec", SqlDbType.Int) { Value = work.YeuCauCongViecId!.Value });
                }

                await deleteWorkEmployeeCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            var addedEmployeeIds = workEmployeeIds.Except(previousWorkEmployeeIds).ToList();
            var removedEmployeeIds = previousWorkEmployeeIds.Except(workEmployeeIds).ToList();

            foreach (var addedEmployeeId in addedEmployeeIds)
            {
                await _commonAuditService.WriteAsync(
                    connection,
                    transaction,
                    new CommonAuditEntry(
                        "YEU_CAU",
                        "WORK_EMPLOYEE_ADD",
                        "YEU_CAU_CONG_VIEC",
                        work.YeuCauCongViecId!.Value.ToString(CultureInfo.InvariantCulture),
                        null,
                        "Them nhan vien vao cong viec yeu cau.",
                        currentUser,
                        Data: new { YeuCauId = yeuCauId, work.YeuCauCongViecId, EmployeeId = addedEmployeeId }),
                    cancellationToken);
            }

            foreach (var removedEmployeeId in removedEmployeeIds)
            {
                await _commonAuditService.WriteAsync(
                    connection,
                    transaction,
                    new CommonAuditEntry(
                        "YEU_CAU",
                        "WORK_EMPLOYEE_REMOVE",
                        "YEU_CAU_CONG_VIEC",
                        work.YeuCauCongViecId!.Value.ToString(CultureInfo.InvariantCulture),
                        null,
                        "Xoa nhan vien khoi cong viec yeu cau.",
                        currentUser,
                        Data: new { YeuCauId = yeuCauId, work.YeuCauCongViecId, EmployeeId = removedEmployeeId }),
                    cancellationToken);
            }

            foreach (var employeeId in workEmployeeIds)
            {
                await using var insertEmployeeCommand = connection.CreateCommand();
                insertEmployeeCommand.Transaction = transaction;
                insertEmployeeCommand.CommandText = $"""
                    IF EXISTS (
                        SELECT 1
                        FROM [{AssignmentTableName}]
                        WHERE IDYeuCauCongViec = @IDYeuCauCongViec
                          AND IDNhanVien = @IDNhanVien
                    )
                    BEGIN
                        UPDATE [{AssignmentTableName}]
                        SET
                            CheckInTime = @CheckInTime,
                            CheckOutTime = @CheckOutTime
                        WHERE IDYeuCauCongViec = @IDYeuCauCongViec
                          AND IDNhanVien = @IDNhanVien
                    END
                    ELSE
                    BEGIN
                        INSERT INTO [{AssignmentTableName}] (
                            IDYeuCauCongViec,
                            IDNhanVien,
                            CheckInTime,
                            CheckOutTime
                        )
                        VALUES (
                            @IDYeuCauCongViec,
                            @IDNhanVien,
                            @CheckInTime,
                            @CheckOutTime
                        )
                    END
                    """;
                insertEmployeeCommand.Parameters.Add(new SqlParameter("@IDYeuCauCongViec", SqlDbType.Int) { Value = work.YeuCauCongViecId!.Value });
                insertEmployeeCommand.Parameters.Add(new SqlParameter("@IDNhanVien", SqlDbType.Int) { Value = employeeId });
                insertEmployeeCommand.Parameters.Add(new SqlParameter("@CheckInTime", SqlDbType.DateTime) { Value = ToDbValue(work.CheckInTime) });
                insertEmployeeCommand.Parameters.Add(new SqlParameter("@CheckOutTime", SqlDbType.DateTime) { Value = ToDbValue(work.CheckOutTime) });
                await insertEmployeeCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await SyncWorkChecklistsAsync(connection, transaction, work.YeuCauCongViecId!.Value, work.Checklists, currentUser, cancellationToken);
        }
    }

    private static async Task SyncWorkChecklistsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int yeuCauCongViecId,
        IEnumerable<YeuCauCongViecChecklistFormItem> checklists,
        string currentUser,
        CancellationToken cancellationToken)
    {
        var postedChecklists = checklists
            .Where(checklist => checklist.ChecklistId > 0)
            .GroupBy(checklist => checklist.ChecklistId)
            .Select(group => group.First())
            .ToList();
        var postedIds = postedChecklists.Select(checklist => checklist.ChecklistId).ToHashSet();
        var normalizedCurrentUser = NormalizeAuditUser(currentUser);

        var existingRows = new Dictionary<int, string?>();
        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText = $"""
                SELECT IDCheckList, FinishBy
                FROM [{WorkChecklistTableName}]
                WHERE IDCongViec = @IDCongViec
                  AND FinishDate IS NOT NULL
                """;
            selectCommand.Parameters.Add(new SqlParameter("@IDCongViec", SqlDbType.Int) { Value = yeuCauCongViecId });

            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var checklistId = GetNullableInt32(reader, "IDCheckList");
                if (checklistId.HasValue)
                {
                    existingRows[checklistId.Value] = GetNullableString(reader, "FinishBy");
                }
            }
        }

        foreach (var existing in existingRows)
        {
            var posted = postedChecklists.FirstOrDefault(checklist => checklist.ChecklistId == existing.Key);
            var shouldUncheck = posted is null || !posted.IsCompleted;
            if (!shouldUncheck)
            {
                continue;
            }

            if (!IsSameAuditUser(existing.Value, normalizedCurrentUser))
            {
                throw new YeuCauBusinessRuleException("Chi nguoi da hoan thanh checklist moi duoc bo dau hoan thanh.");
            }
        }

        var completedIds = postedChecklists
            .Where(checklist => checklist.IsCompleted)
            .Select(checklist => checklist.ChecklistId)
            .ToHashSet();

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            if (completedIds.Count == 0)
            {
                deleteCommand.CommandText = $"""
                    DELETE FROM [{WorkChecklistTableName}]
                    WHERE IDCongViec = @IDCongViec
                    """;
            }
            else
            {
                var placeholders = completedIds.Select((_, index) => $"@ChecklistId{index}").ToList();
                deleteCommand.CommandText = $"""
                    DELETE FROM [{WorkChecklistTableName}]
                    WHERE IDCongViec = @IDCongViec
                      AND IDCheckList NOT IN ({string.Join(", ", placeholders)})
                    """;

                var index = 0;
                foreach (var checklistId in completedIds)
                {
                    deleteCommand.Parameters.Add(new SqlParameter($"@ChecklistId{index}", SqlDbType.Int) { Value = checklistId });
                    index++;
                }
            }

            deleteCommand.Parameters.Add(new SqlParameter("@IDCongViec", SqlDbType.Int) { Value = yeuCauCongViecId });
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var checklistId in completedIds)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = $"""
                IF EXISTS (
                    SELECT 1
                    FROM [{WorkChecklistTableName}]
                    WHERE IDCongViec = @IDCongViec
                      AND IDCheckList = @IDCheckList
                )
                BEGIN
                    UPDATE [{WorkChecklistTableName}]
                    SET
                        FinishDate = ISNULL(FinishDate, GETDATE()),
                        FinishBy = ISNULL(FinishBy, @FinishBy)
                    WHERE IDCongViec = @IDCongViec
                      AND IDCheckList = @IDCheckList
                END
                ELSE
                BEGIN
                    INSERT INTO [{WorkChecklistTableName}] (
                        IDCongViec,
                        IDCheckList,
                        Created_Date,
                        Created_By,
                        FinishDate,
                        FinishBy
                    )
                    VALUES (
                        @IDCongViec,
                        @IDCheckList,
                        GETDATE(),
                        @CreatedBy,
                        GETDATE(),
                        @FinishBy
                    )
                END
                """;
            insertCommand.Parameters.Add(new SqlParameter("@IDCongViec", SqlDbType.Int) { Value = yeuCauCongViecId });
            insertCommand.Parameters.Add(new SqlParameter("@IDCheckList", SqlDbType.Int) { Value = checklistId });
            insertCommand.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
            insertCommand.Parameters.Add(new SqlParameter("@FinishBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<int> GetNextSequenceAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string prefix,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT MAX(TRY_CONVERT(int, SUBSTRING(MaYeuCau, LEN(@Prefix) + 1, @SequenceDigits)))
            FROM [{TableName}]
            WHERE MaYeuCau LIKE @PrefixLike
              AND LEN(MaYeuCau) = @MaxLength
              AND SUBSTRING(MaYeuCau, LEN(@Prefix) + 1, @SequenceDigits) NOT LIKE '%[^0-9]%'
            """;
        command.Parameters.Add(new SqlParameter("@Prefix", SqlDbType.NVarChar, 20) { Value = prefix });
        command.Parameters.Add(new SqlParameter("@PrefixLike", SqlDbType.NVarChar, 20) { Value = $"{prefix}%" });
        command.Parameters.Add(new SqlParameter("@SequenceDigits", SqlDbType.Int) { Value = RequestCodeSequenceDigits });
        command.Parameters.Add(new SqlParameter("@MaxLength", SqlDbType.Int) { Value = RequestCodeMaxLength });

        var currentMax = await command.ExecuteScalarAsync(cancellationToken);
        var lastSequence = currentMax is null || currentMax == DBNull.Value ? 0 : Convert.ToInt32(currentMax);
        return Math.Max(lastSequence, 0) + 1;
    }

    private static string BuildRequestCodePrefix(string yearSuffix)
    {
        return $"YC-{yearSuffix}";
    }

    private static string BuildRequestCode(string prefix, int sequence)
    {
        if (sequence <= 0)
        {
            sequence = 1;
        }

        var maxSequence = (int)Math.Pow(10, RequestCodeSequenceDigits) - 1;
        if (sequence > maxSequence)
        {
            throw new YeuCauBusinessRuleException($"Da het dai so ma yeu cau cho nam {prefix[^2..]}. Vui long dieu chinh cau truc ma truoc khi tao them phieu.");
        }

        var code = $"{prefix}{sequence.ToString($"D{RequestCodeSequenceDigits}", CultureInfo.InvariantCulture)}";
        if (code.Length > RequestCodeMaxLength)
        {
            throw new YeuCauBusinessRuleException($"Ma yeu cau {code} vuot qua {RequestCodeMaxLength} ky tu.");
        }

        return code;
    }

    private static string BuildRequestEmployeeSummary(YeuCauFormModel model)
    {
        var employeeNames = model.CongViecs
            .SelectMany(work => work.NhanViens)
            .Where(item => item.NhanVienId.HasValue && item.NhanVienId.Value > 0)
            .Select(item => item.HoTen?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (employeeNames.Count == 0)
        {
            return string.Empty;
        }

        return BuildEmployeeSummaryHtml(employeeNames);
    }

    private static async Task<string> RefreshRequestEmployeeSummaryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int yeuCauId,
        string currentUser,
        CancellationToken cancellationToken)
    {
        var employeeNames = new List<string>();
        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText = $"""
                SELECT DISTINCT
                    lknv.IDNhanVien,
                    nv.Ho,
                    nv.Ten
                FROM [{WorkTableName}] AS ycvc
                INNER JOIN [{AssignmentTableName}] AS lknv ON lknv.IDYeuCauCongViec = ycvc.ID
                LEFT JOIN [{EmployeeTableName}] AS nv ON nv.ID = lknv.IDNhanVien
                WHERE ycvc.IDYeuCau = @IDYeuCau
                  AND lknv.IDNhanVien IS NOT NULL
                ORDER BY lknv.IDNhanVien
                """;
            selectCommand.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = yeuCauId });

            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var ho = GetNullableString(reader, "Ho") ?? string.Empty;
                var ten = GetNullableString(reader, "Ten") ?? string.Empty;
                var hoTen = string.Join(" ", new[] { ho, ten }.Where(static part => !string.IsNullOrWhiteSpace(part))).Trim();
                if (!string.IsNullOrWhiteSpace(hoTen))
                {
                    employeeNames.Add(hoTen);
                }
            }
        }

        var employeeSummary = BuildEmployeeSummaryHtml(employeeNames);
        await using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = $"""
            UPDATE [{TableName}]
            SET
                NhanVienThucHien = @NhanVienThucHien,
                Updated_Date = GETDATE(),
                Updated_By = @UpdatedBy
            WHERE ID = @IDYeuCau
            """;
        updateCommand.Parameters.Add(new SqlParameter("@IDYeuCau", SqlDbType.Int) { Value = yeuCauId });
        updateCommand.Parameters.Add(new SqlParameter("@NhanVienThucHien", SqlDbType.NVarChar, 550) { Value = employeeSummary });
        updateCommand.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);

        return employeeSummary;
    }

    private static string BuildEmployeeSummaryHtml(IEnumerable<string?> employeeNames)
    {
        var distinctNames = employeeNames
            .Select(name => name?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctNames.Count == 0)
        {
            return string.Empty;
        }

        return $"<ul>{string.Concat(distinctNames.Select(name => $"<li>{WebUtility.HtmlEncode(name)}</li>"))}</ul>";
    }

    private static void FillSaveParameters(
        SqlCommand command,
        YeuCauFormModel model,
        string maYeuCau,
        string nhanVienThucHien,
        string currentUser,
        bool includeCreatedFields = true)
    {
        command.Parameters.Add(new SqlParameter("@MaYeuCau", SqlDbType.NVarChar, 10) { Value = maYeuCau });
        command.Parameters.Add(new SqlParameter("@IDKhachHang", SqlDbType.Int) { Value = model.IDKhachHang!.Value });
        command.Parameters.Add(new SqlParameter("@NgayYeuCau", SqlDbType.DateTime) { Value = model.NgayYeuCau!.Value.Date });
        command.Parameters.Add(new SqlParameter("@IDDiaDiem", SqlDbType.Int) { Value = model.IDDiaDiem!.Value });
        command.Parameters.Add(new SqlParameter("@GhiChu", SqlDbType.NVarChar, 50) { Value = ToDbValue(model.GhiChu) });
        command.Parameters.Add(new SqlParameter("@NhanVienThucHien", SqlDbType.NVarChar, 550) { Value = nhanVienThucHien });
        command.Parameters.Add(new SqlParameter("@TrangThaiYeuCau", SqlDbType.NVarChar, 250) { Value = YeuCauTrangThaiCatalog.Normalize(model.TrangThaiYeuCau) });
        command.Parameters.Add(new SqlParameter("@NgayThucHien", SqlDbType.DateTime) { Value = ToDbValue(model.NgayThucHien?.Date) });
        command.Parameters.Add(new SqlParameter("@NgayHetHan", SqlDbType.DateTime) { Value = ToDbValue(model.NgayHetHan?.Date) });
        command.Parameters.Add(new SqlParameter("@NgayHoanThanh", SqlDbType.DateTime) { Value = ToDbValue(model.NgayHoanThanh?.Date) });
        command.Parameters.Add(new SqlParameter("@NgayHenTiepTheo", SqlDbType.DateTime) { Value = ToDbValue(model.NgayHenTiepTheo?.Date) });
        command.Parameters.Add(new SqlParameter("@CheckinTheoKhoangCach", SqlDbType.Bit) { Value = model.CheckinTheoKhoangCach });
        command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });

        if (includeCreatedFields)
        {
            command.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
        }
    }

    private async Task<string?> GetExistingCodeAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT TOP (1) MaYeuCau
            FROM [{TableName}]
            WHERE ID = @Id
            """;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result == DBNull.Value ? null : result.ToString()?.Trim();
    }

    private static YeuCauListItem MapItem(SqlDataReader reader)
    {
        return new YeuCauListItem
        {
            Id = reader.GetInt32(reader.GetOrdinal("ID")),
            MaYeuCau = GetNullableString(reader, "MaYeuCau") ?? string.Empty,
            IDKhachHang = GetNullableInt32(reader, "IDKhachHang"),
            TenKhachHang = GetNullableString(reader, "TenKhachHang"),
            NgayYeuCau = GetNullableDateTime(reader, "NgayYeuCau"),
            IDDiaDiem = GetNullableInt32(reader, "IDDiaDiem"),
            DiaChi = GetNullableString(reader, "DiaChi"),
            NguoiLienHe = GetNullableString(reader, "NguoiLienHe"),
            DienThoai = GetNullableString(reader, "DienThoai"),
            LongAddress = GetNullableDecimal(reader, "LongAddress"),
            LatAddress = GetNullableDecimal(reader, "LatAddress"),
            CheckinTheoKhoangCach = GetNullableBoolean(reader, "CheckinTheoKhoangCach") ?? false,
            GhiChu = GetNullableString(reader, "GhiChu"),
            NhanVienThucHien = GetNullableString(reader, "NhanVienThucHien"),
            TrangThaiYeuCau = GetNullableString(reader, "TrangThaiYeuCau"),
            NgayThucHien = GetNullableDateTime(reader, "NgayThucHien"),
            NgayHetHan = GetNullableDateTime(reader, "NgayHetHan"),
            NgayHoanThanh = GetNullableDateTime(reader, "NgayHoanThanh"),
            NgayHenTiepTheo = GetNullableDateTime(reader, "NgayHenTiepTheo"),
            CreatedBy = GetNullableString(reader, "Created_By"),
            CreatedDate = GetNullableDateTime(reader, "Created_Date"),
            UpdatedBy = GetNullableString(reader, "Updated_By"),
            UpdatedDate = GetNullableDateTime(reader, "Updated_Date"),
            SoCongViec = GetNullableInt32(reader, "SoCongViec") ?? 0,
            SoCongViecHoanThanh = GetNullableInt32(reader, "SoCongViecHoanThanh") ?? 0
        };
    }

    private static YeuCauLocationOption MapLocationOption(SqlDataReader reader)
    {
        return new YeuCauLocationOption
        {
            IDDiaDiem = reader.GetInt32(reader.GetOrdinal("ID")),
            IDKhachHang = GetNullableInt32(reader, "IDKhachHang"),
            TenKhachHang = GetNullableString(reader, "TenKhachHang"),
            DiaChi = GetNullableString(reader, "DiaChi"),
            NguoiLienHe = GetNullableString(reader, "NguoiLienHe"),
            DienThoai = GetNullableString(reader, "DienThoai"),
            LongAddress = GetNullableDecimal(reader, "LongAddress"),
            LatAddress = GetNullableDecimal(reader, "LatAddress"),
            TrangThaiSuDung = GetNullableBoolean(reader, "TrangThaiSuDung") ?? true
        };
    }

    private static string BuildWhereClause(
        string? keyword,
        IReadOnlyList<string> statusValues,
        DateTime? requestDateFrom,
        DateTime? requestDateTo,
        DateTime? executionDateFrom,
        DateTime? executionDateTo,
        string? assigneeKeyword,
        string? workStatusFilter,
        int? assignedEmployeeId)
    {
        var filters = new List<string> { "1 = 1" };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filters.Add($"""
                (
                    yc.MaYeuCau COLLATE {SearchCollation} LIKE @Keyword
                    OR kh.TenKhachHang COLLATE {SearchCollation} LIKE @Keyword
                    OR dd.DiaChi COLLATE {SearchCollation} LIKE @Keyword
                    OR dd.NguoiLienHe COLLATE {SearchCollation} LIKE @Keyword
                    OR dd.DienThoai COLLATE {SearchCollation} LIKE @Keyword
                    OR yc.NhanVienThucHien COLLATE {SearchCollation} LIKE @Keyword
                    OR yc.GhiChu COLLATE {SearchCollation} LIKE @Keyword
                    OR {BuildSearchExpression("yc.MaYeuCau")} LIKE @KeywordNoAccent
                    OR {BuildSearchExpression("kh.TenKhachHang")} LIKE @KeywordNoAccent
                    OR {BuildSearchExpression("dd.DiaChi")} LIKE @KeywordNoAccent
                    OR {BuildSearchExpression("dd.NguoiLienHe")} LIKE @KeywordNoAccent
                    OR {BuildSearchExpression("dd.DienThoai")} LIKE @KeywordNoAccent
                    OR {BuildSearchExpression("yc.NhanVienThucHien")} LIKE @KeywordNoAccent
                    OR {BuildSearchExpression("yc.GhiChu")} LIKE @KeywordNoAccent
                )
                """);
        }

        if (statusValues.Count > 0)
        {
            var placeholders = new List<string>();
            for (var index = 0; index < statusValues.Count; index++)
            {
                placeholders.Add($"@TrangThaiYeuCau{index}");
            }

            filters.Add($"yc.TrangThaiYeuCau IN ({string.Join(", ", placeholders)})");
        }

        if (requestDateFrom.HasValue)
        {
            filters.Add("yc.NgayYeuCau >= @RequestDateFrom");
        }

        if (requestDateTo.HasValue)
        {
            filters.Add("yc.NgayYeuCau < DATEADD(day, 1, @RequestDateTo)");
        }

        if (executionDateFrom.HasValue)
        {
            filters.Add("yc.NgayThucHien >= @ExecutionDateFrom");
        }

        if (executionDateTo.HasValue)
        {
            filters.Add("yc.NgayThucHien < DATEADD(day, 1, @ExecutionDateTo)");
        }

        if (!string.IsNullOrWhiteSpace(assigneeKeyword))
        {
            filters.Add($"""
                (
                    yc.NhanVienThucHien COLLATE {SearchCollation} LIKE @AssigneeKeyword
                    OR {BuildSearchExpression("yc.NhanVienThucHien")} LIKE @AssigneeKeywordNoAccent
                    OR EXISTS (
                        SELECT 1
                        FROM [{WorkTableName}] AS filterWork
                        INNER JOIN [{AssignmentTableName}] AS filterAssign ON filterAssign.IDYeuCauCongViec = filterWork.ID
                        LEFT JOIN [{EmployeeTableName}] AS filterEmployee ON filterEmployee.ID = filterAssign.IDNhanVien
                        WHERE filterWork.IDYeuCau = yc.ID
                          AND (
                            LTRIM(RTRIM(CONCAT(ISNULL(filterEmployee.Ho, N''), N' ', ISNULL(filterEmployee.Ten, N'')))) COLLATE {SearchCollation} LIKE @AssigneeKeyword
                            OR {BuildSearchExpression("LTRIM(RTRIM(CONCAT(ISNULL(filterEmployee.Ho, N''), N' ', ISNULL(filterEmployee.Ten, N''))))")} LIKE @AssigneeKeywordNoAccent
                          )
                    )
                )
                """);
        }

        if (assignedEmployeeId.HasValue && assignedEmployeeId.Value > 0)
        {
            filters.Add($"""
                EXISTS (
                    SELECT 1
                    FROM [{WorkTableName}] AS assignedWork
                    INNER JOIN [{AssignmentTableName}] AS assignedEmployee ON assignedEmployee.IDYeuCauCongViec = assignedWork.ID
                    WHERE assignedWork.IDYeuCau = yc.ID
                      AND assignedEmployee.IDNhanVien = @AssignedEmployeeId
                )
                """);
        }

        if (string.Equals(workStatusFilter, YeuCauCongViecTrangThaiFilter.HoanThanh, StringComparison.OrdinalIgnoreCase))
        {
            filters.Add(assignedEmployeeId.HasValue && assignedEmployeeId.Value > 0
                ? $"""
                    EXISTS (
                        SELECT 1
                        FROM [{WorkTableName}] AS anyAssignedWork
                        INNER JOIN [{AssignmentTableName}] AS anyAssignedEmployee ON anyAssignedEmployee.IDYeuCauCongViec = anyAssignedWork.ID
                        WHERE anyAssignedWork.IDYeuCau = yc.ID
                          AND anyAssignedEmployee.IDNhanVien = @AssignedEmployeeId
                    )
                    AND NOT EXISTS (
                        SELECT 1
                        FROM [{WorkTableName}] AS incompleteAssignedCompletedWork
                        INNER JOIN [{AssignmentTableName}] AS incompleteAssignedCompletedEmployee ON incompleteAssignedCompletedEmployee.IDYeuCauCongViec = incompleteAssignedCompletedWork.ID
                        WHERE incompleteAssignedCompletedWork.IDYeuCau = yc.ID
                          AND incompleteAssignedCompletedEmployee.IDNhanVien = @AssignedEmployeeId
                          AND {BuildWorkStatusExpression("incompleteAssignedCompletedWork")} <> @CompletedWorkStatusFilter
                    )
                    """
                : $"""
                    EXISTS (
                        SELECT 1
                        FROM [{WorkTableName}] AS anyWork
                        WHERE anyWork.IDYeuCau = yc.ID
                    )
                    AND NOT EXISTS (
                        SELECT 1
                        FROM [{WorkTableName}] AS incompleteCompletedWork
                        WHERE incompleteCompletedWork.IDYeuCau = yc.ID
                          AND {BuildWorkStatusExpression("incompleteCompletedWork")} <> @CompletedWorkStatusFilter
                    )
                    """);
        }
        else if (string.Equals(workStatusFilter, YeuCauCongViecTrangThaiFilter.ChuaHoanThanh, StringComparison.OrdinalIgnoreCase))
        {
            filters.Add(assignedEmployeeId.HasValue && assignedEmployeeId.Value > 0
                ? $"""
                    EXISTS (
                        SELECT 1
                        FROM [{WorkTableName}] AS incompleteAssignedWork
                        INNER JOIN [{AssignmentTableName}] AS incompleteAssignedEmployee ON incompleteAssignedEmployee.IDYeuCauCongViec = incompleteAssignedWork.ID
                        WHERE incompleteAssignedWork.IDYeuCau = yc.ID
                          AND incompleteAssignedEmployee.IDNhanVien = @AssignedEmployeeId
                          AND {BuildWorkStatusExpression("incompleteAssignedWork")} <> @CompletedWorkStatusFilter
                    )
                    """
                : $"""
                    EXISTS (
                        SELECT 1
                        FROM [{WorkTableName}] AS incompleteWork
                        WHERE incompleteWork.IDYeuCau = yc.ID
                          AND {BuildWorkStatusExpression("incompleteWork")} <> @CompletedWorkStatusFilter
                    )
                    """);
        }

        return string.Join(" AND ", filters);
    }

    private static string BuildWorkStatusExpression(string alias)
    {
        return $"ISNULL(NULLIF(LTRIM(RTRIM({alias}.TrangThaiCongViec)), N''), @DefaultWorkStatusFilter)";
    }

    private static string BuildLocationWhereClause(string? keyword)
    {
        var filters = new List<string> { "1 = 1" };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filters.Add($"""
                (
                    kh.TenKhachHang COLLATE {SearchCollation} LIKE @Keyword
                    OR dd.DiaChi COLLATE {SearchCollation} LIKE @Keyword
                    OR dd.NguoiLienHe COLLATE {SearchCollation} LIKE @Keyword
                    OR dd.DienThoai COLLATE {SearchCollation} LIKE @Keyword
                )
                """);
        }

        return string.Join(" AND ", filters);
    }

    private static void AddFilterParameters(
        SqlCommand command,
        string? keyword,
        IReadOnlyList<string> statusValues,
        DateTime? requestDateFrom,
        DateTime? requestDateTo,
        DateTime? executionDateFrom,
        DateTime? executionDateTo,
        string? assigneeKeyword,
        int? assignedEmployeeId)
    {
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            command.Parameters.Add(new SqlParameter("@Keyword", SqlDbType.NVarChar, 250)
            {
                Value = $"%{keyword}%"
            });
            command.Parameters.Add(new SqlParameter("@KeywordNoAccent", SqlDbType.NVarChar, 250)
            {
                Value = $"%{NormalizeSearchPattern(keyword)}%"
            });
        }

        for (var index = 0; index < statusValues.Count; index++)
        {
            command.Parameters.Add(new SqlParameter($"@TrangThaiYeuCau{index}", SqlDbType.NVarChar, 250)
            {
                Value = statusValues[index]
            });
        }

        if (requestDateFrom.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@RequestDateFrom", SqlDbType.DateTime) { Value = requestDateFrom.Value.Date });
        }

        if (requestDateTo.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@RequestDateTo", SqlDbType.DateTime) { Value = requestDateTo.Value.Date });
        }

        if (executionDateFrom.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@ExecutionDateFrom", SqlDbType.DateTime) { Value = executionDateFrom.Value.Date });
        }

        if (executionDateTo.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@ExecutionDateTo", SqlDbType.DateTime) { Value = executionDateTo.Value.Date });
        }

        if (!string.IsNullOrWhiteSpace(assigneeKeyword))
        {
            command.Parameters.Add(new SqlParameter("@AssigneeKeyword", SqlDbType.NVarChar, 250)
            {
                Value = $"%{assigneeKeyword}%"
            });
            command.Parameters.Add(new SqlParameter("@AssigneeKeywordNoAccent", SqlDbType.NVarChar, 250)
            {
                Value = $"%{NormalizeSearchPattern(assigneeKeyword)}%"
            });
        }

        if (assignedEmployeeId.HasValue && assignedEmployeeId.Value > 0)
        {
            command.Parameters.Add(new SqlParameter("@AssignedEmployeeId", SqlDbType.Int) { Value = assignedEmployeeId.Value });
        }

        command.Parameters.Add(new SqlParameter("@DefaultWorkStatusFilter", SqlDbType.NVarChar, 50) { Value = YeuCauCongViecTrangThaiCatalog.TaoMoi });
        command.Parameters.Add(new SqlParameter("@CompletedWorkStatusFilter", SqlDbType.NVarChar, 50) { Value = YeuCauCongViecTrangThaiCatalog.HoanThanh });
    }

    private static string BuildSearchExpression(string sqlExpression)
    {
        return $"LOWER(REPLACE(REPLACE({sqlExpression} COLLATE {SearchCollation}, N'đ', N'd'), N'Đ', N'd'))";
    }

    private static string NormalizeSearchPattern(string value)
    {
        return value.Trim().Replace('đ', 'd').Replace('Đ', 'd').ToLowerInvariant();
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

    private static async Task EnsureWorkMetadataColumnsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var schemaCommand = connection.CreateCommand();
        schemaCommand.CommandText = $"""
            IF COL_LENGTH('dbo.{WorkTableName}', 'TrangThaiCongViec') IS NULL
            BEGIN
                ALTER TABLE [dbo].[{WorkTableName}] ADD [TrangThaiCongViec] NVARCHAR(50) NULL;
            END;

            IF COL_LENGTH('dbo.{WorkTableName}', 'GhiChu') IS NULL
            BEGIN
                ALTER TABLE [dbo].[{WorkTableName}] ADD [GhiChu] NVARCHAR(500) NULL;
            END;
            """;
        await schemaCommand.ExecuteNonQueryAsync(cancellationToken);

        await using var dataCommand = connection.CreateCommand();
        dataCommand.CommandText = $"""
            UPDATE [dbo].[{WorkTableName}]
            SET [TrangThaiCongViec] = CASE
                WHEN [CheckInTime] IS NOT NULL AND [CheckoutTime] IS NOT NULL AND [CheckoutTime] > [CheckInTime] THEN @CompletedStatus
                WHEN [CheckInTime] IS NOT NULL THEN @InProgressStatus
                ELSE @NewStatus
            END
            WHERE [TrangThaiCongViec] IS NULL OR LTRIM(RTRIM([TrangThaiCongViec])) = N'';
            """;
        dataCommand.Parameters.Add(new SqlParameter("@NewStatus", SqlDbType.NVarChar, 50) { Value = YeuCauCongViecTrangThaiCatalog.TaoMoi });
        dataCommand.Parameters.Add(new SqlParameter("@InProgressStatus", SqlDbType.NVarChar, 50) { Value = YeuCauCongViecTrangThaiCatalog.DangThucHien });
        dataCommand.Parameters.Add(new SqlParameter("@CompletedStatus", SqlDbType.NVarChar, 50) { Value = YeuCauCongViecTrangThaiCatalog.HoanThanh });
        await dataCommand.ExecuteNonQueryAsync(cancellationToken);

        await EnsureWorkImageMetadataColumnsAsync(connection, cancellationToken);
    }

    private static async Task EnsureWorkImageMetadataColumnsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF COL_LENGTH('dbo.{WorkImageTableName}', 'Created_Date') IS NULL
            BEGIN
                ALTER TABLE [dbo].[{WorkImageTableName}] ADD [Created_Date] DATETIME NULL;
            END;

            IF COL_LENGTH('dbo.{WorkImageTableName}', 'Created_By') IS NULL
            BEGIN
                ALTER TABLE [dbo].[{WorkImageTableName}] ADD [Created_By] NVARCHAR(50) NULL;
            END;

            IF COL_LENGTH('dbo.{WorkImageTableName}', 'ImageType') IS NULL
            BEGIN
                ALTER TABLE [dbo].[{WorkImageTableName}] ADD [ImageType] NVARCHAR(50) NULL;
            END;

            IF COL_LENGTH('dbo.{WorkImageTableName}', 'IDTaiKhoanNguoiDung') IS NULL
            BEGIN
                ALTER TABLE [dbo].[{WorkImageTableName}] ADD [IDTaiKhoanNguoiDung] UNIQUEIDENTIFIER NULL;
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? NormalizeKeyword(string? keyword)
    {
        return string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
    }

    private static string? NormalizeStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status) ? null : YeuCauTrangThaiCatalog.Normalize(status);
    }

    private static object ToDbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static object ToDbValue(DateTime? value)
    {
        return value.HasValue ? value.Value : DBNull.Value;
    }

    private static object ToDbValue(Guid? value)
    {
        return value.HasValue && value.Value != Guid.Empty ? value.Value : DBNull.Value;
    }

    private static object ToDbValue(int? value)
    {
        return value.HasValue && value.Value > 0 ? value.Value : DBNull.Value;
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

    private static string? TrimNullableToLength(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string NormalizeAuditUser(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "system" : value.Trim();
    }

    private static bool IsSameAuditUser(string? left, string right)
    {
        return string.Equals(NormalizeAuditUser(left), NormalizeAuditUser(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanDeleteWorkImage(Guid? ownerAccountId, string? createdBy, Guid? currentAccountId, string currentUser)
    {
        if (ownerAccountId.HasValue && ownerAccountId.Value != Guid.Empty)
        {
            return currentAccountId.HasValue && ownerAccountId.Value == currentAccountId.Value;
        }

        return IsSameAuditUser(createdBy, NormalizeAuditUser(currentUser));
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
            long typedLong => Convert.ToInt32(typedLong),
            decimal typedDecimal => Convert.ToInt32(typedDecimal),
            string typedString when int.TryParse(typedString, out var parsed) => parsed,
            _ => null
        };
    }

    private static Guid? GetNullableGuid(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetValue(ordinal) switch
        {
            Guid typedGuid => typedGuid,
            string typedString when Guid.TryParse(typedString, out var parsed) => parsed,
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

    private static decimal? ParseNullableDecimal(object? value)
    {
        if (value is null || value == DBNull.Value)
        {
            return null;
        }

        return value switch
        {
            decimal typedDecimal => typedDecimal,
            double typedDouble => Convert.ToDecimal(typedDouble),
            float typedFloat => Convert.ToDecimal(typedFloat),
            int typedInt => typedInt,
            long typedLong => typedLong,
            string typedString when decimal.TryParse(typedString, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariantParsed) => invariantParsed,
            string typedString when decimal.TryParse(typedString, NumberStyles.Number, CultureInfo.CurrentCulture, out var cultureParsed) => cultureParsed,
            _ => null
        };
    }
}

public sealed class YeuCauBusinessRuleException(string message) : Exception(message);
