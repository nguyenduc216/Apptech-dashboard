using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface INhapKhoService
{
    Task<(IReadOnlyList<NhapKhoListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        string? statusFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<NhapKhoListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NhapKhoDetailItem>> GetDetailsAsync(int phieuId, CancellationToken cancellationToken = default);
    Task<string> GenerateNextMaPhieuAsync(DateTime ngayNhapKho, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<NhapKhoLookupOption> KhoOptions, IReadOnlyList<NhapKhoLookupOption> HangHoaOptions, IReadOnlyList<NhapKhoLookupOption> DonViTinhOptions, IReadOnlyList<NhapKhoLookupOption> NhaCungCapOptions)> GetLookupDataAsync(CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(NhapKhoFormModel model, string currentUser, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(NhapKhoFormModel model, string currentUser, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class NhapKhoService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<NhapKhoService> logger,
    ICommonAuditService commonAuditService) : INhapKhoService
{
    private const string HeaderTableName = "TblPhieuNhapKho";
    private const string DetailTableName = "TblPhieuNhapKhoChiTiet";
    private const string SearchCollation = "Latin1_General_100_CI_AI";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<NhapKhoService> _logger = logger;
    private readonly ICommonAuditService _commonAuditService = commonAuditService;

    public async Task<(IReadOnlyList<NhapKhoListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        string? statusFilter,
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
            var normalizedStatus = NormalizeStatusFilter(statusFilter);
            var filters = new List<string> { "1 = 1" };

            await using var countCommand = connection.CreateCommand();
            if (!string.IsNullOrWhiteSpace(normalizedKeyword))
            {
                filters.Add($"""
                    (
                        pn.MaPhieu COLLATE {SearchCollation} LIKE @Keyword OR
                        pn.NoiDungNhapKho COLLATE {SearchCollation} LIKE @Keyword OR
                        pn.NguoiNhapKho COLLATE {SearchCollation} LIKE @Keyword OR
                        kho.TenKho COLLATE {SearchCollation} LIKE @Keyword OR
                        ncc.TenNhaCungCap COLLATE {SearchCollation} LIKE @Keyword
                    )
                    """);
                countCommand.Parameters.Add(new SqlParameter("@Keyword", SqlDbType.NVarChar, 250) { Value = $"%{normalizedKeyword}%" });
            }

            if (!string.IsNullOrWhiteSpace(normalizedStatus))
            {
                filters.Add("LOWER(LTRIM(RTRIM(pn.TrangThaiPhieu))) = @StatusFilter");
                countCommand.Parameters.Add(new SqlParameter("@StatusFilter", SqlDbType.NVarChar, 50) { Value = normalizedStatus });
            }

            countCommand.CommandText = $"""
                SELECT COUNT(1)
                FROM [{HeaderTableName}] pn
                LEFT JOIN [TblKho] kho ON kho.ID = pn.IDKho
                LEFT JOIN [TblNhaCungCap] ncc ON ncc.ID = pn.IDNhaCungCap
                WHERE {string.Join(" AND ", filters)}
                """;

            var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
            var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            var currentPage = totalPages == 0 ? 1 : Math.Min(page, totalPages);
            var offset = (currentPage - 1) * pageSize;

            await using var listCommand = connection.CreateCommand();
            CopyParameters(countCommand, listCommand);
            listCommand.CommandText = $"""
                SELECT
                    pn.ID,
                    pn.NgayNhapKho,
                    pn.MaPhieu,
                    pn.NoiDungNhapKho,
                    pn.NguoiNhapKho,
                    pn.IDKho,
                    pn.IDNhaCungCap,
                    pn.TrangThaiPhieu,
                    kho.TenKho,
                    ncc.TenNhaCungCap,
                    COUNT(ct.ID) AS DetailCount,
                    ISNULL(SUM(ct.SoLuongNhap), 0) AS TotalQuantity
                FROM [{HeaderTableName}] pn
                LEFT JOIN [{DetailTableName}] ct ON ct.IDPhieuNhapKho = pn.ID
                LEFT JOIN [TblKho] kho ON kho.ID = pn.IDKho
                LEFT JOIN [TblNhaCungCap] ncc ON ncc.ID = pn.IDNhaCungCap
                WHERE {string.Join(" AND ", filters)}
                GROUP BY
                    pn.ID,
                    pn.NgayNhapKho,
                    pn.MaPhieu,
                    pn.NoiDungNhapKho,
                    pn.NguoiNhapKho,
                    pn.IDKho,
                    pn.IDNhaCungCap,
                    pn.TrangThaiPhieu,
                    kho.TenKho,
                    ncc.TenNhaCungCap
                ORDER BY pn.NgayNhapKho DESC, pn.ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                """;
            listCommand.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = offset });
            listCommand.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = pageSize });

            var items = new List<NhapKhoListItem>();
            await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapListItem(reader));
            }

            return (items, totalCount, currentPage, totalPages, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblPhieuNhapKho list.");
            return ([], 0, 1, 0, pageSize);
        }
    }

    public async Task<NhapKhoListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
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
                    pn.ID,
                    pn.NgayNhapKho,
                    pn.MaPhieu,
                    pn.NoiDungNhapKho,
                    pn.NguoiNhapKho,
                    pn.IDKho,
                    pn.IDNhaCungCap,
                    pn.TrangThaiPhieu,
                    kho.TenKho,
                    ncc.TenNhaCungCap,
                    COUNT(ct.ID) AS DetailCount,
                    ISNULL(SUM(ct.SoLuongNhap), 0) AS TotalQuantity
                FROM [{HeaderTableName}] pn
                LEFT JOIN [{DetailTableName}] ct ON ct.IDPhieuNhapKho = pn.ID
                LEFT JOIN [TblKho] kho ON kho.ID = pn.IDKho
                LEFT JOIN [TblNhaCungCap] ncc ON ncc.ID = pn.IDNhaCungCap
                WHERE pn.ID = @Id
                GROUP BY
                    pn.ID,
                    pn.NgayNhapKho,
                    pn.MaPhieu,
                    pn.NoiDungNhapKho,
                    pn.NguoiNhapKho,
                    pn.IDKho,
                    pn.IDNhaCungCap,
                    pn.TrangThaiPhieu,
                    kho.TenKho,
                    ncc.TenNhaCungCap
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? MapListItem(reader) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblPhieuNhapKho {Id}.", id);
            return null;
        }
    }

    public async Task<IReadOnlyList<NhapKhoDetailItem>> GetDetailsAsync(int phieuId, CancellationToken cancellationToken = default)
    {
        if (phieuId <= 0)
        {
            return [];
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            return await LoadDetailsAsync(connection, transaction: null, phieuId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblPhieuNhapKhoChiTiet for {Id}.", phieuId);
            return [];
        }
    }

    public async Task<string> GenerateNextMaPhieuAsync(DateTime ngayNhapKho, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await GenerateNextMaPhieuAsync(connection, transaction: null, ngayNhapKho, cancellationToken);
    }

    public async Task<(IReadOnlyList<NhapKhoLookupOption> KhoOptions, IReadOnlyList<NhapKhoLookupOption> HangHoaOptions, IReadOnlyList<NhapKhoLookupOption> DonViTinhOptions, IReadOnlyList<NhapKhoLookupOption> NhaCungCapOptions)> GetLookupDataAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var kho = await TryLoadOptionsAsync(() => LoadKhoOptionsAsync(connection, cancellationToken), "Kho");
            var hangHoa = await TryLoadOptionsAsync(() => LoadHangHoaOptionsAsync(connection, cancellationToken), "HangHoa");
            var donViTinh = await TryLoadOptionsAsync(() => LoadDonViTinhOptionsAsync(connection, cancellationToken), "DonViTinh");
            var nhaCungCap = await TryLoadOptionsAsync(() => LoadNhaCungCapOptionsAsync(connection, cancellationToken), "NhaCungCap");
            return (kho, hangHoa, donViTinh, nhaCungCap);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load NhapKho lookup data.");
            return ([], [], [], []);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(NhapKhoFormModel model, string currentUser, CancellationToken cancellationToken = default)
    {
        var details = NormalizeDetails(model.Details);
        if (details.Count == 0)
        {
            return (false, "Vui lòng thêm ít nhất một hàng hóa cần nhập.", null);
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await EnsureNhapKhoDetailSchemaAsync(connection, transaction, cancellationToken);

            var validationError = ValidateHeaderAndDetails(model, details);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, validationError, null);
            }

            var ngayNhapKho = (model.NgayNhapKho ?? DateTime.Today).Date;
            var maPhieu = await GenerateNextMaPhieuAsync(connection, transaction, DateTime.Today, cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO [{HeaderTableName}] (
                    MaPhieu,
                    NgayNhapKho,
                    NguoiNhapKho,
                    NoiDungNhapKho,
                    IDNhaCungCap,
                    TrangThaiPhieu,
                    IDKho
                )
                VALUES (
                    @MaPhieu,
                    @NgayNhapKho,
                    @NguoiNhapKho,
                    @NoiDungNhapKho,
                    @IDNhaCungCap,
                    @TrangThaiPhieu,
                    @IDKho
                );

                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;
            FillHeaderParameters(command, maPhieu, ngayNhapKho, currentUser, model.NoiDungNhapKho, model.NhaCungCapId, NhapKhoPhieuStatus.Draft, model.KhoId);

            var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
            if (newId <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không thể tạo phiếu nhập kho.", null);
            }

            await ReplaceDetailsAsync(connection, transaction, newId, details, cancellationToken);
            await _commonAuditService.WriteAsync(
                connection,
                transaction,
                new CommonAuditEntry(
                    "NHAP_KHO",
                    "CREATE",
                    "PHIEU_NHAP",
                    newId.ToString(),
                    maPhieu,
                    "Tao phieu nhap kho nhap.",
                    currentUser,
                    Data: new
                    {
                        Id = newId,
                        MaPhieu = maPhieu,
                        NgayNhapKho = ngayNhapKho,
                        model.KhoId,
                        model.NhaCungCapId,
                        Details = details
                    }),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (true, null, newId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TblPhieuNhapKho.");
            return (false, "Không thể tạo phiếu nhập kho lúc này.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(NhapKhoFormModel model, string currentUser, CancellationToken cancellationToken = default)
    {
        if (model.Id is null or <= 0)
        {
            return (false, "Không xác định được phiếu nhập kho cần cập nhật.");
        }

        var details = NormalizeDetails(model.Details);
        if (details.Count == 0)
        {
            return (false, "Vui lòng thêm ít nhất một hàng hóa cần nhập.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await EnsureNhapKhoDetailSchemaAsync(connection, transaction, cancellationToken);

            var currentStatus = await LoadStatusAsync(connection, transaction, model.Id.Value, cancellationToken);
            if (currentStatus is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy phiếu nhập kho cần cập nhật.");
            }

            currentStatus = NhapKhoPhieuStatus.Normalize(currentStatus);
            if (currentStatus != NhapKhoPhieuStatus.Draft)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Chỉ phiếu nháp mới được cập nhật.");
            }

            var validationError = ValidateHeaderAndDetails(model, details);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, validationError);
            }

            var targetStatus = NhapKhoPhieuStatus.Normalize(model.TrangThaiPhieu);
            var shouldImport = targetStatus == NhapKhoPhieuStatus.Imported;
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                UPDATE [{HeaderTableName}]
                SET
                    NgayNhapKho = @NgayNhapKho,
                    NguoiNhapKho = @NguoiNhapKho,
                    NoiDungNhapKho = @NoiDungNhapKho,
                    IDNhaCungCap = @IDNhaCungCap,
                    TrangThaiPhieu = @TrangThaiPhieu,
                    IDKho = @IDKho
                WHERE ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id.Value });
            FillHeaderParameters(command, model.MaPhieu ?? string.Empty, (model.NgayNhapKho ?? DateTime.Today).Date, string.IsNullOrWhiteSpace(model.NguoiNhapKho) ? currentUser : model.NguoiNhapKho, model.NoiDungNhapKho, model.NhaCungCapId, targetStatus, model.KhoId, includeMaPhieu: false);

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy phiếu nhập kho cần cập nhật.");
            }

            await ReplaceDetailsAsync(connection, transaction, model.Id.Value, details, cancellationToken);

            if (shouldImport)
            {
                await CreateVatTuFromImportDetailsAsync(
                    connection,
                    transaction,
                    model.Id.Value,
                    (model.NgayNhapKho ?? DateTime.Today).Date,
                    model.KhoId!.Value,
                    currentUser,
                    _commonAuditService,
                    cancellationToken);
            }

            await _commonAuditService.WriteAsync(
                connection,
                transaction,
                new CommonAuditEntry(
                    "NHAP_KHO",
                    shouldImport ? "IMPORT" : "UPDATE",
                    "PHIEU_NHAP",
                    model.Id.Value.ToString(),
                    model.MaPhieu,
                    shouldImport ? "Nhap phieu nhap kho va tao chi tiet vat tu." : "Cap nhat phieu nhap kho.",
                    currentUser,
                    Data: new
                    {
                        model.Id,
                        model.MaPhieu,
                        model.NgayNhapKho,
                        model.KhoId,
                        model.NhaCungCapId,
                        model.TrangThaiPhieu,
                        Details = details
                    }),
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update TblPhieuNhapKho {Id}.", model.Id);
            return (false, "Không thể cập nhật phiếu nhập kho lúc này.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return (false, "Không xác định được phiếu nhập kho cần xóa.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            var currentStatus = NhapKhoPhieuStatus.Normalize(await LoadStatusAsync(connection, transaction, id, cancellationToken));
            if (currentStatus == NhapKhoPhieuStatus.Imported)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không thể xóa phiếu đã nhập.");
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                DELETE FROM [{DetailTableName}]
                WHERE IDPhieuNhapKho = @Id;

                DELETE FROM [{HeaderTableName}]
                WHERE ID = @Id;
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy phiếu nhập kho cần xóa.");
            }

            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete TblPhieuNhapKho {Id}.", id);
            return (false, "Không thể xóa phiếu nhập kho lúc này.");
        }
    }

    private static async Task<string> GenerateNextMaPhieuAsync(SqlConnection connection, SqlTransaction? transaction, DateTime ngayNhapKho, CancellationToken cancellationToken)
    {
        var prefix = $"N{ngayNhapKho:yyMM}";
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT ISNULL(MAX(TRY_CONVERT(int, RIGHT(MaPhieu, 3))), 0)
            FROM [{HeaderTableName}]
            WHERE MaPhieu LIKE @Prefix + '[0-9][0-9][0-9]'
            """;
        command.Parameters.Add(new SqlParameter("@Prefix", SqlDbType.NVarChar, 5) { Value = prefix });
        var maxSequence = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
        return $"{prefix}{maxSequence + 1:000}";
    }

    private static async Task<IReadOnlyList<NhapKhoDetailItem>> LoadDetailsAsync(SqlConnection connection, SqlTransaction? transaction, int phieuId, CancellationToken cancellationToken)
    {
        var hasDetailDonViTinhColumn = await HasColumnAsync(connection, transaction, DetailTableName, "IDDonViTinh", cancellationToken);
        var hasDetailMaSoLoColumn = await HasColumnAsync(connection, transaction, DetailTableName, "MaSoLo", cancellationToken);
        var hasDetailDonGiaNhapColumn = await HasColumnAsync(connection, transaction, DetailTableName, "DonGiaNhap", cancellationToken);
        var hasDetailDonGiaBanLeColumn = await HasColumnAsync(connection, transaction, DetailTableName, "DonGiaBanLe", cancellationToken);
        var hasDetailDonViNhapColumn = await HasColumnAsync(connection, transaction, DetailTableName, "IDDonViNhap", cancellationToken);
        var hasDetailSoLuongQuyDoiColumn = await HasColumnAsync(connection, transaction, DetailTableName, "SoLuongQuyDoi", cancellationToken);
        var hasDetailSoChungTuColumn = await HasColumnAsync(connection, transaction, DetailTableName, "SoChungTu", cancellationToken);
        var detailDonViTinhExpression = hasDetailDonViTinhColumn ? "ct.IDDonViTinh" : "CAST(NULL AS int)";
        var detailDonViTinhSelect = $"{detailDonViTinhExpression} AS IDDonViTinh";
        var detailMaSoLoSelect = hasDetailMaSoLoColumn ? "ct.MaSoLo" : "CAST(NULL AS nvarchar(50)) AS MaSoLo";
        var detailDonGiaNhapSelect = hasDetailDonGiaNhapColumn ? "ct.DonGiaNhap" : "CAST(0 AS decimal(18,2)) AS DonGiaNhap";
        var detailDonGiaBanLeSelect = hasDetailDonGiaBanLeColumn ? "ct.DonGiaBanLe" : "CAST(0 AS decimal(18,2)) AS DonGiaBanLe";
        var detailDonViNhapExpression = hasDetailDonViNhapColumn ? "ct.IDDonViNhap" : "CAST(NULL AS int)";
        var detailDonViNhapSelect = $"{detailDonViNhapExpression} AS IDDonViNhap";
        var detailSoLuongQuyDoiSelect = hasDetailSoLuongQuyDoiColumn ? "ct.SoLuongQuyDoi" : "CAST(1 AS decimal(18,4)) AS SoLuongQuyDoi";
        var detailSoChungTuSelect = hasDetailSoChungTuColumn ? "ct.SoChungTu" : "CAST(NULL AS nvarchar(50)) AS SoChungTu";
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                ct.ID,
                ct.IDHangHoa,
                {detailDonViTinhSelect},
                {detailMaSoLoSelect},
                {detailSoChungTuSelect},
                ct.SoLuongNhap,
                {detailSoLuongQuyDoiSelect},
                {detailDonViNhapSelect},
                {detailDonGiaNhapSelect},
                {detailDonGiaBanLeSelect},
                ct.LoaiHinhNhap,
                hh.TenHangHoa,
                hh.MaHangHoa,
                dvt.TenDonVi,
                dvt.TenVietTat,
                dvn.TenDonVi AS TenDonViNhap,
                dvn.TenVietTat AS TenVietTatDonViNhap
            FROM [{DetailTableName}] ct
            LEFT JOIN [TblHangHoa] hh ON hh.ID = ct.IDHangHoa
            LEFT JOIN [TblDonViTinh] dvt ON dvt.ID = {detailDonViTinhExpression}
            LEFT JOIN [TblDonViTinh] dvn ON dvn.ID = {detailDonViNhapExpression}
            WHERE ct.IDPhieuNhapKho = @PhieuId
            ORDER BY ct.ID ASC
            """;
        command.Parameters.Add(new SqlParameter("@PhieuId", SqlDbType.Int) { Value = phieuId });

        var items = new List<NhapKhoDetailItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(MapDetailItem(reader));
        }

        return items;
    }

    private static async Task ReplaceDetailsAsync(SqlConnection connection, SqlTransaction transaction, int phieuId, IReadOnlyList<NhapKhoDetailItem> details, CancellationToken cancellationToken)
    {
        var hasDetailDonViTinhColumn = await HasColumnAsync(connection, transaction, DetailTableName, "IDDonViTinh", cancellationToken);
        var hasDetailMaSoLoColumn = await HasColumnAsync(connection, transaction, DetailTableName, "MaSoLo", cancellationToken);
        var hasDetailDonGiaNhapColumn = await HasColumnAsync(connection, transaction, DetailTableName, "DonGiaNhap", cancellationToken);
        var hasDetailDonGiaBanLeColumn = await HasColumnAsync(connection, transaction, DetailTableName, "DonGiaBanLe", cancellationToken);
        var hasDetailDonViNhapColumn = await HasColumnAsync(connection, transaction, DetailTableName, "IDDonViNhap", cancellationToken);
        var hasDetailSoLuongQuyDoiColumn = await HasColumnAsync(connection, transaction, DetailTableName, "SoLuongQuyDoi", cancellationToken);
        var hasDetailSoChungTuColumn = await HasColumnAsync(connection, transaction, DetailTableName, "SoChungTu", cancellationToken);
        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = $"""
            DELETE FROM [{DetailTableName}]
            WHERE IDPhieuNhapKho = @PhieuId
            """;
        deleteCommand.Parameters.Add(new SqlParameter("@PhieuId", SqlDbType.Int) { Value = phieuId });
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var detail in details)
        {
            var donViTinhColumn = hasDetailDonViTinhColumn ? "IDDonViTinh," : string.Empty;
            var donViTinhValue = hasDetailDonViTinhColumn ? "@IDDonViTinh," : string.Empty;
            var maSoLoColumn = hasDetailMaSoLoColumn ? "MaSoLo," : string.Empty;
            var maSoLoValue = hasDetailMaSoLoColumn ? "@MaSoLo," : string.Empty;
            var soChungTuColumn = hasDetailSoChungTuColumn ? "SoChungTu," : string.Empty;
            var soChungTuValue = hasDetailSoChungTuColumn ? "@SoChungTu," : string.Empty;
            var donGiaNhapColumn = hasDetailDonGiaNhapColumn ? "DonGiaNhap," : string.Empty;
            var donGiaNhapValue = hasDetailDonGiaNhapColumn ? "@DonGiaNhap," : string.Empty;
            var donGiaBanLeColumn = hasDetailDonGiaBanLeColumn ? "DonGiaBanLe," : string.Empty;
            var donGiaBanLeValue = hasDetailDonGiaBanLeColumn ? "@DonGiaBanLe," : string.Empty;
            var donViNhapColumn = hasDetailDonViNhapColumn ? "IDDonViNhap," : string.Empty;
            var donViNhapValue = hasDetailDonViNhapColumn ? "@IDDonViNhap," : string.Empty;
            var soLuongQuyDoiColumn = hasDetailSoLuongQuyDoiColumn ? "SoLuongQuyDoi," : string.Empty;
            var soLuongQuyDoiValue = hasDetailSoLuongQuyDoiColumn ? "@SoLuongQuyDoi," : string.Empty;
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = $"""
                INSERT INTO [{DetailTableName}] (
                    IDPhieuNhapKho,
                    IDHangHoa,
                    {donViTinhColumn}
                    {maSoLoColumn}
                    {soChungTuColumn}
                    SoLuongNhap,
                    {soLuongQuyDoiColumn}
                    {donViNhapColumn}
                    {donGiaNhapColumn}
                    {donGiaBanLeColumn}
                    LoaiHinhNhap
                )
                VALUES (
                    @IDPhieuNhapKho,
                    @IDHangHoa,
                    {donViTinhValue}
                    {maSoLoValue}
                    {soChungTuValue}
                    @SoLuongNhap,
                    {soLuongQuyDoiValue}
                    {donViNhapValue}
                    {donGiaNhapValue}
                    {donGiaBanLeValue}
                    @LoaiHinhNhap
                )
                """;
            insertCommand.Parameters.Add(new SqlParameter("@IDPhieuNhapKho", SqlDbType.Int) { Value = phieuId });
            insertCommand.Parameters.Add(new SqlParameter("@IDHangHoa", SqlDbType.Int) { Value = detail.HangHoaId });
            if (hasDetailDonViTinhColumn)
            {
                insertCommand.Parameters.Add(new SqlParameter("@IDDonViTinh", SqlDbType.Int) { Value = detail.DonViTinhId.HasValue && detail.DonViTinhId.Value > 0 ? detail.DonViTinhId.Value : DBNull.Value });
            }
            if (hasDetailMaSoLoColumn)
            {
                insertCommand.Parameters.Add(new SqlParameter("@MaSoLo", SqlDbType.NVarChar, 50) { Value = ToDbValue(detail.MaSoLo) });
            }
            if (hasDetailSoChungTuColumn)
            {
                insertCommand.Parameters.Add(new SqlParameter("@SoChungTu", SqlDbType.NVarChar, 50) { Value = ToDbValue(detail.SoChungTu) });
            }
            insertCommand.Parameters.Add(new SqlParameter("@SoLuongNhap", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = detail.SoLuongNhap });
            if (hasDetailSoLuongQuyDoiColumn)
            {
                insertCommand.Parameters.Add(new SqlParameter("@SoLuongQuyDoi", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = detail.SoLuongQuyDoi });
            }
            if (hasDetailDonViNhapColumn)
            {
                insertCommand.Parameters.Add(new SqlParameter("@IDDonViNhap", SqlDbType.Int) { Value = detail.DonViNhapId.HasValue && detail.DonViNhapId.Value > 0 ? detail.DonViNhapId.Value : DBNull.Value });
            }
            if (hasDetailDonGiaNhapColumn)
            {
                insertCommand.Parameters.Add(new SqlParameter("@DonGiaNhap", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = detail.DonGiaNhap });
            }
            if (hasDetailDonGiaBanLeColumn)
            {
                insertCommand.Parameters.Add(new SqlParameter("@DonGiaBanLe", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = detail.DonGiaBanLe });
            }
            insertCommand.Parameters.Add(new SqlParameter("@LoaiHinhNhap", SqlDbType.NVarChar, 100) { Value = ToDbValue(detail.LoaiHinhNhap) });
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<string?> LoadStatusAsync(SqlConnection connection, SqlTransaction transaction, int id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT TOP (1) TrangThaiPhieu
            FROM [{HeaderTableName}]
            WHERE ID = @Id
            """;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : result.ToString();
    }

    private static string? ValidateHeaderAndDetails(NhapKhoFormModel model, IReadOnlyList<NhapKhoDetailItem> details)
    {
        if (model.KhoId is null or <= 0)
        {
            return "Vui lòng chọn kho nhập.";
        }

        foreach (var detail in details)
        {
            if (detail.HangHoaId <= 0)
            {
                return "Danh sách hàng hóa nhập không hợp lệ.";
            }

            if (detail.SoLuongNhap <= 0)
            {
                return "Số lượng nhập phải lớn hơn 0.";

            }
            if (detail.SoLuongQuyDoi <= 0)
            {
                return "So luong quy doi phai lon hon 0.";
            }
            if (detail.DonViNhapId is null or <= 0)
            {
                return "Vui long chon don vi nhap.";
            }
            if (detail.DonGiaNhap < 0)
            {
                return "Don gia nhap khong hop le.";
            }
            if (detail.DonGiaBanLe < 0)
            {
                return "Don gia ban le khong hop le.";
            }
            if (detail.DonViTinhId is null or <= 0)
            {
                return "Vui lòng chọn đơn vị tính.";
            }

            var loaiHinhNhap = NhapKhoLoaiHinh.Normalize(detail.LoaiHinhNhap);
            if (string.IsNullOrWhiteSpace(loaiHinhNhap))
            {
                return "Vui lòng chọn loại hình nhập.";
            }

            if (loaiHinhNhap == NhapKhoLoaiHinh.NhapTungVatTu && detail.SoLuongNhap % 1 != 0)
            {
                return "Nhập từng vật tư yêu cầu số lượng nhập là số nguyên.";
            }
        }

        return null;
    }

    private static List<NhapKhoDetailItem> NormalizeDetails(IEnumerable<NhapKhoDetailItem>? details)
    {
        var result = new List<NhapKhoDetailItem>();
        foreach (var detail in details ?? [])
        {
            if (detail.HangHoaId <= 0)
            {
                continue;
            }

            result.Add(new NhapKhoDetailItem
            {
                Id = detail.Id,
                HangHoaId = detail.HangHoaId,
                TenHangHoa = detail.TenHangHoa,
                MaHangHoa = detail.MaHangHoa,
                DonViTinhId = detail.DonViTinhId,
                TenDonViTinh = detail.TenDonViTinh,
                TenVietTatDonViTinh = detail.TenVietTatDonViTinh,
                MaSoLo = string.IsNullOrWhiteSpace(detail.MaSoLo) ? null : detail.MaSoLo.Trim(),
                SoChungTu = string.IsNullOrWhiteSpace(detail.SoChungTu) ? null : detail.SoChungTu.Trim(),
                LoaiHinhNhap = NhapKhoLoaiHinh.Normalize(detail.LoaiHinhNhap),
                SoLuongNhap = detail.SoLuongNhap,
                SoLuongQuyDoi = detail.SoLuongQuyDoi <= 0 ? 1 : detail.SoLuongQuyDoi,
                DonViNhapId = detail.DonViNhapId,
                DonGiaNhap = detail.DonGiaNhap < 0 ? 0 : detail.DonGiaNhap,
                DonGiaBanLe = detail.DonGiaBanLe < 0 ? 0 : detail.DonGiaBanLe
            });
        }

        return result;
    }

    private static async Task CreateVatTuFromImportDetailsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int phieuId,
        DateTime ngayNhapKho,
        int khoId,
        string currentUser,
        ICommonAuditService commonAuditService,
        CancellationToken cancellationToken)
    {
        await EnsureChiTietHangHoaImportSchemaAsync(connection, transaction, cancellationToken);

        var hasTrangThaiSuDungColumn = await HasColumnAsync(connection, transaction, "TblChiTietHangHoa", "TrangThaiSuDung", cancellationToken);
        var hasNgayNhapColumn = await HasColumnAsync(connection, transaction, "TblChiTietHangHoa", "NgayNhap", cancellationToken);
        var hasPhieuNhapChiTietColumn = await HasColumnAsync(connection, transaction, "TblChiTietHangHoa", "IDPhieuNhapChiTiet", cancellationToken);
        var hasDetailDonViTinhColumn = await HasColumnAsync(connection, transaction, DetailTableName, "IDDonViTinh", cancellationToken);
        var hasDetailMaSoLoColumn = await HasColumnAsync(connection, transaction, DetailTableName, "MaSoLo", cancellationToken);
        var hasDetailDonGiaNhapColumn = await HasColumnAsync(connection, transaction, DetailTableName, "DonGiaNhap", cancellationToken);
        var hasDetailDonGiaBanLeColumn = await HasColumnAsync(connection, transaction, DetailTableName, "DonGiaBanLe", cancellationToken);
        var hasDetailDonViNhapColumn = await HasColumnAsync(connection, transaction, DetailTableName, "IDDonViNhap", cancellationToken);
        var hasDetailSoLuongQuyDoiColumn = await HasColumnAsync(connection, transaction, DetailTableName, "SoLuongQuyDoi", cancellationToken);
        var hasDetailSoChungTuColumn = await HasColumnAsync(connection, transaction, DetailTableName, "SoChungTu", cancellationToken);
        var detailDonViTinhSelect = hasDetailDonViTinhColumn
            ? "ct.IDDonViTinh"
            : "CAST(NULL AS int) AS IDDonViTinh";
        var detailMaSoLoSelect = hasDetailMaSoLoColumn ? "ct.MaSoLo" : "CAST(NULL AS nvarchar(50)) AS MaSoLo";
        var detailDonGiaNhapSelect = hasDetailDonGiaNhapColumn ? "ct.DonGiaNhap" : "CAST(0 AS decimal(18,2)) AS DonGiaNhap";
        var detailDonGiaBanLeSelect = hasDetailDonGiaBanLeColumn ? "ct.DonGiaBanLe" : "CAST(0 AS decimal(18,2)) AS DonGiaBanLe";
        var detailDonViNhapSelect = hasDetailDonViNhapColumn ? "ct.IDDonViNhap" : "CAST(NULL AS int) AS IDDonViNhap";
        var detailSoLuongQuyDoiSelect = hasDetailSoLuongQuyDoiColumn ? "ct.SoLuongQuyDoi" : "CAST(1 AS decimal(18,4)) AS SoLuongQuyDoi";
        var detailSoChungTuSelect = hasDetailSoChungTuColumn ? "ct.SoChungTu" : "CAST(NULL AS nvarchar(50)) AS SoChungTu";

        await using var loadCommand = connection.CreateCommand();
        loadCommand.Transaction = transaction;
        loadCommand.CommandText = $"""
            SELECT
                ct.ID,
                ct.IDHangHoa,
                {detailDonViTinhSelect},
                {detailMaSoLoSelect},
                {detailSoChungTuSelect},
                ct.SoLuongNhap,
                {detailSoLuongQuyDoiSelect},
                {detailDonViNhapSelect},
                {detailDonGiaNhapSelect},
                {detailDonGiaBanLeSelect},
                ct.LoaiHinhNhap,
                hh.TenHangHoa
            FROM [{DetailTableName}] ct
            LEFT JOIN [TblHangHoa] hh ON hh.ID = ct.IDHangHoa
            WHERE ct.IDPhieuNhapKho = @PhieuId
            ORDER BY ct.ID ASC
            """;
        loadCommand.Parameters.Add(new SqlParameter("@PhieuId", SqlDbType.Int) { Value = phieuId });

        var details = new List<NhapKhoImportMaterialSource>();
        await using (var reader = await loadCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                details.Add(new NhapKhoImportMaterialSource
                {
                    Id = reader.GetInt32(reader.GetOrdinal("ID")),
                    HangHoaId = GetNullableInt32(reader, "IDHangHoa") ?? 0,
                    DonViTinhId = GetNullableInt32(reader, "IDDonViTinh"),
                    MaSoLo = GetNullableString(reader, "MaSoLo"),
                    SoChungTu = GetNullableString(reader, "SoChungTu"),
                    TenHangHoa = GetNullableString(reader, "TenHangHoa"),
                    SoLuongNhap = GetNullableDecimal(reader, "SoLuongNhap") ?? 0,
                    SoLuongQuyDoi = GetNullableDecimal(reader, "SoLuongQuyDoi") ?? 1,
                    DonViNhapId = GetNullableInt32(reader, "IDDonViNhap"),
                    DonGiaNhap = GetNullableDecimal(reader, "DonGiaNhap") ?? 0,
                    DonGiaBanLe = GetNullableDecimal(reader, "DonGiaBanLe") ?? 0,
                    LoaiHinhNhap = NhapKhoLoaiHinh.Normalize(GetNullableString(reader, "LoaiHinhNhap"))
                });
            }
        }

        foreach (var detail in details)
        {
            if (detail.HangHoaId <= 0 || detail.SoLuongNhap <= 0)
            {
                continue;
            }

            if (detail.LoaiHinhNhap == NhapKhoLoaiHinh.NhapTungVatTu)
            {
                var itemCount = decimal.ToInt32(decimal.Truncate(detail.SoLuongNhap));
                for (var index = 0; index < itemCount; index++)
                {
                    await InsertVatTuFromImportAsync(connection, transaction, detail, 1, khoId, ngayNhapKho, currentUser, commonAuditService, hasTrangThaiSuDungColumn, hasNgayNhapColumn, hasPhieuNhapChiTietColumn, cancellationToken);
                }
            }
            else
            {
                await InsertVatTuFromImportAsync(connection, transaction, detail, detail.SoLuongNhap, khoId, ngayNhapKho, currentUser, commonAuditService, hasTrangThaiSuDungColumn, hasNgayNhapColumn, hasPhieuNhapChiTietColumn, cancellationToken);
            }
        }
    }

    private static async Task InsertVatTuFromImportAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        NhapKhoImportMaterialSource detail,
        decimal quantity,
        int khoId,
        DateTime ngayNhapKho,
        string currentUser,
        ICommonAuditService commonAuditService,
        bool hasTrangThaiSuDungColumn,
        bool hasNgayNhapColumn,
        bool hasPhieuNhapChiTietColumn,
        CancellationToken cancellationToken)
    {
        var extraColumns = new List<string>();
        var extraValues = new List<string>();

        if (hasTrangThaiSuDungColumn)
        {
            extraColumns.Add("TrangThaiSuDung");
            extraValues.Add("1");
        }

        if (hasNgayNhapColumn)
        {
            extraColumns.Add("NgayNhap");
            extraValues.Add("@NgayNhap");
        }

        if (hasPhieuNhapChiTietColumn)
        {
            extraColumns.Add("IDPhieuNhapChiTiet");
            extraValues.Add("@IDPhieuNhapChiTiet");
        }

        var extraColumnSql = extraColumns.Count == 0 ? string.Empty : $"{string.Join(",\n                    ", extraColumns)},";
        var extraValueSql = extraValues.Count == 0 ? string.Empty : $"{string.Join(",\n                    ", extraValues)},";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO [TblChiTietHangHoa] (
                {extraColumnSql}
                IDKho,
                IDHangHoa,
                IDDonVinTinh,
                IDDonViNhap,
                TenChiTiet,
                SoLuongNhap,
                SoLuongQuyDoi,
                SoLuongTon,
                DonGiaNhap,
                DonGiaBanLe,
                MaSoLo,
                SoChungTu,
                Created_Date,
                Created_By,
                Updated_Date,
                Updated_By
            )
            VALUES (
                {extraValueSql}
                @IDKho,
                @IDHangHoa,
                @IDDonVinTinh,
                @IDDonViNhap,
                @TenChiTiet,
                @SoLuongNhap,
                @SoLuongQuyDoi,
                @SoLuongTon,
                @DonGiaNhap,
                @DonGiaBanLe,
                @MaSoLo,
                @SoChungTu,
                GETDATE(),
                @CreatedBy,
                GETDATE(),
                @UpdatedBy
            );

            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;

        if (hasNgayNhapColumn)
        {
            command.Parameters.Add(new SqlParameter("@NgayNhap", SqlDbType.DateTime) { Value = ngayNhapKho });
        }

        if (hasPhieuNhapChiTietColumn)
        {
            command.Parameters.Add(new SqlParameter("@IDPhieuNhapChiTiet", SqlDbType.Int) { Value = detail.Id });
        }

        command.Parameters.Add(new SqlParameter("@IDKho", SqlDbType.Int) { Value = khoId });
        command.Parameters.Add(new SqlParameter("@IDHangHoa", SqlDbType.Int) { Value = detail.HangHoaId });
        command.Parameters.Add(new SqlParameter("@IDDonVinTinh", SqlDbType.Int) { Value = detail.DonViTinhId.HasValue && detail.DonViTinhId.Value > 0 ? detail.DonViTinhId.Value : DBNull.Value });
        command.Parameters.Add(new SqlParameter("@TenChiTiet", SqlDbType.NVarChar, 250) { Value = TrimToLength(string.IsNullOrWhiteSpace(detail.TenHangHoa) ? $"Hàng hóa #{detail.HangHoaId}" : detail.TenHangHoa, 250) });
        command.Parameters.Add(new SqlParameter("@IDDonViNhap", SqlDbType.Int) { Value = detail.DonViNhapId.HasValue && detail.DonViNhapId.Value > 0 ? detail.DonViNhapId.Value : DBNull.Value });
        command.Parameters.Add(new SqlParameter("@SoLuongNhap", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = quantity });
        command.Parameters.Add(new SqlParameter("@SoLuongQuyDoi", SqlDbType.Decimal) { Precision = 18, Scale = 4, Value = detail.SoLuongQuyDoi });
        command.Parameters.Add(new SqlParameter("@SoLuongTon", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = quantity * detail.SoLuongQuyDoi });
        command.Parameters.Add(new SqlParameter("@DonGiaNhap", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = detail.DonGiaNhap });
        command.Parameters.Add(new SqlParameter("@DonGiaBanLe", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = detail.DonGiaBanLe });
        command.Parameters.Add(new SqlParameter("@MaSoLo", SqlDbType.NVarChar, 50) { Value = ToDbValue(detail.MaSoLo) });
        command.Parameters.Add(new SqlParameter("@SoChungTu", SqlDbType.NVarChar, 50) { Value = ToDbValue(detail.SoChungTu) });
        command.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
        command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });

        var vatTuId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
        if (vatTuId > 0)
        {
            await commonAuditService.WriteAsync(
                connection,
                transaction,
                new CommonAuditEntry(
                    "VAT_TU",
                    "CREATE_FROM_NHAP_KHO",
                    "VAT_TU",
                    vatTuId.ToString(),
                    null,
                    "Tao chi tiet vat tu tu phieu nhap kho.",
                    currentUser,
                    Data: new
                    {
                        VatTuId = vatTuId,
                        detail.Id,
                        detail.HangHoaId,
                        detail.TenHangHoa,
                        LoaiHinhNhap = detail.LoaiHinhNhap,
                        detail.MaSoLo,
                        detail.SoChungTu,
                        SoLuongNhap = quantity,
                        detail.SoLuongQuyDoi,
                        detail.DonViNhapId,
                        detail.DonGiaNhap,
                        detail.DonGiaBanLe,
                        SoLuongTon = quantity * detail.SoLuongQuyDoi,
                        KhoId = khoId,
                        NgayNhap = ngayNhapKho
                    }),
                cancellationToken);
        }
    }

    private static void FillHeaderParameters(SqlCommand command, string maPhieu, DateTime ngayNhapKho, string? nguoiNhapKho, string? noiDungNhapKho, int? nhaCungCapId, string trangThaiPhieu, int? khoId, bool includeMaPhieu = true)
    {
        if (includeMaPhieu)
        {
            command.Parameters.Add(new SqlParameter("@MaPhieu", SqlDbType.NVarChar, 50) { Value = maPhieu });
        }

        command.Parameters.Add(new SqlParameter("@NgayNhapKho", SqlDbType.DateTime) { Value = ngayNhapKho });
        command.Parameters.Add(new SqlParameter("@NguoiNhapKho", SqlDbType.NVarChar, 50) { Value = TrimToLength(nguoiNhapKho, 50) });
        command.Parameters.Add(new SqlParameter("@NoiDungNhapKho", SqlDbType.NVarChar, 550) { Value = ToDbValue(noiDungNhapKho) });
        command.Parameters.Add(new SqlParameter("@IDNhaCungCap", SqlDbType.Int) { Value = nhaCungCapId.HasValue && nhaCungCapId.Value > 0 ? nhaCungCapId.Value : DBNull.Value });
        command.Parameters.Add(new SqlParameter("@TrangThaiPhieu", SqlDbType.NVarChar, 50) { Value = NhapKhoPhieuStatus.Normalize(trangThaiPhieu) });
        command.Parameters.Add(new SqlParameter("@IDKho", SqlDbType.Int) { Value = khoId.HasValue && khoId.Value > 0 ? khoId.Value : DBNull.Value });
    }

    private static async Task<IReadOnlyList<NhapKhoLookupOption>> LoadKhoOptionsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT ID, TenKho, MaKho
            FROM [TblKho]
            WHERE ISNULL(TrangThaiSuDung, 1) = 1
            ORDER BY TenKho ASC, ID ASC
            """;
        return await LoadOptionsAsync(connection, sql, "TenKho", "MaKho", cancellationToken);
    }

    private static async Task<IReadOnlyList<NhapKhoLookupOption>> LoadHangHoaOptionsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var hasDonViTinhColumn = await HasColumnAsync(connection, null, "TblHangHoa", "IDDonViTinh", cancellationToken);
        var hasDonVinTinhColumn = !hasDonViTinhColumn && await HasColumnAsync(connection, null, "TblHangHoa", "IDDonVinTinh", cancellationToken);
        var donViTinhSelect = hasDonViTinhColumn
            ? "IDDonViTinh"
            : hasDonVinTinhColumn ? "IDDonVinTinh AS IDDonViTinh" : "CAST(NULL AS int) AS IDDonViTinh";

        var sql = $"""
            SELECT ID, TenHangHoa, MaHangHoa, {donViTinhSelect}
            FROM [TblHangHoa]
            WHERE ISNULL(TrangThaiSuDung, 1) = 1
            ORDER BY TenHangHoa ASC, ID ASC
            """;
        return await LoadOptionsAsync(connection, sql, "TenHangHoa", "MaHangHoa", cancellationToken, "IDDonViTinh");
    }

    private static async Task<IReadOnlyList<NhapKhoLookupOption>> LoadDonViTinhOptionsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT ID, TenDonVi, TenVietTat
            FROM [TblDonViTinh]
            WHERE ISNULL(TrangThaiSuDung, 1) = 1
            ORDER BY TenDonVi ASC, ID ASC
            """;
        return await LoadOptionsAsync(connection, sql, "TenDonVi", "TenVietTat", cancellationToken);
    }

    private static async Task<IReadOnlyList<NhapKhoLookupOption>> LoadNhaCungCapOptionsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT ID, TenNhaCungCap
            FROM [TblNhaCungCap]
            WHERE ISNULL(TrangThaiSuDung, 1) = 1
            ORDER BY TenNhaCungCap ASC, ID ASC
            """;
        return await LoadOptionsAsync(connection, sql, "TenNhaCungCap", null, cancellationToken);
    }

    private async Task<IReadOnlyList<NhapKhoLookupOption>> TryLoadOptionsAsync(
        Func<Task<IReadOnlyList<NhapKhoLookupOption>>> loader,
        string name)
    {
        try
        {
            return await loader();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load NhapKho lookup {Name}.", name);
            return [];
        }
    }

    private static async Task<IReadOnlyList<NhapKhoLookupOption>> LoadOptionsAsync(SqlConnection connection, string sql, string nameColumn, string? codeColumn, CancellationToken cancellationToken, string? donViTinhColumn = null)
    {
        var items = new List<NhapKhoLookupOption>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = GetNullableString(reader, nameColumn) ?? $"#{reader.GetInt32(reader.GetOrdinal("ID"))}";
            var code = string.IsNullOrWhiteSpace(codeColumn) ? null : GetNullableString(reader, codeColumn);
            items.Add(new NhapKhoLookupOption
            {
                Id = reader.GetInt32(reader.GetOrdinal("ID")),
                Label = string.IsNullOrWhiteSpace(code) ? name : $"{name} ({code})",
                DonViTinhId = string.IsNullOrWhiteSpace(donViTinhColumn) ? null : GetNullableInt32(reader, donViTinhColumn)
            });
        }

        return items;
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

    private static NhapKhoListItem MapListItem(SqlDataReader reader)
    {
        return new NhapKhoListItem
        {
            Id = reader.GetInt32(reader.GetOrdinal("ID")),
            NgayNhapKho = GetNullableDateTime(reader, "NgayNhapKho"),
            MaPhieu = GetNullableString(reader, "MaPhieu") ?? string.Empty,
            NoiDungNhapKho = GetNullableString(reader, "NoiDungNhapKho"),
            NguoiNhapKho = GetNullableString(reader, "NguoiNhapKho"),
            KhoId = GetNullableInt32(reader, "IDKho"),
            NhaCungCapId = GetNullableInt32(reader, "IDNhaCungCap"),
            TenKho = GetNullableString(reader, "TenKho"),
            TenNhaCungCap = GetNullableString(reader, "TenNhaCungCap"),
            TrangThaiPhieu = NhapKhoPhieuStatus.Normalize(GetNullableString(reader, "TrangThaiPhieu")),
            DetailCount = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("DetailCount"))),
            TotalQuantity = GetNullableDecimal(reader, "TotalQuantity") ?? 0
        };
    }

    private static NhapKhoDetailItem MapDetailItem(SqlDataReader reader)
    {
        return new NhapKhoDetailItem
        {
            Id = GetNullableInt32(reader, "ID"),
            HangHoaId = GetNullableInt32(reader, "IDHangHoa") ?? 0,
            DonViTinhId = GetNullableInt32(reader, "IDDonViTinh"),
            DonViNhapId = GetNullableInt32(reader, "IDDonViNhap"),
            MaSoLo = GetNullableString(reader, "MaSoLo"),
            SoChungTu = GetNullableString(reader, "SoChungTu"),
            SoLuongNhap = GetNullableDecimal(reader, "SoLuongNhap") ?? 1,
            SoLuongQuyDoi = GetNullableDecimal(reader, "SoLuongQuyDoi") ?? 1,
            DonGiaNhap = GetNullableDecimal(reader, "DonGiaNhap") ?? 0,
            DonGiaBanLe = GetNullableDecimal(reader, "DonGiaBanLe") ?? 0,
            LoaiHinhNhap = GetNullableString(reader, "LoaiHinhNhap"),
            TenHangHoa = GetNullableString(reader, "TenHangHoa"),
            MaHangHoa = GetNullableString(reader, "MaHangHoa"),
            TenDonViTinh = GetNullableString(reader, "TenDonVi"),
            TenVietTatDonViTinh = GetNullableString(reader, "TenVietTat"),
            TenDonViNhap = GetNullableString(reader, "TenDonViNhap"),
            TenVietTatDonViNhap = GetNullableString(reader, "TenVietTatDonViNhap")
        };
    }

    private static void CopyParameters(SqlCommand source, SqlCommand target)
    {
        foreach (SqlParameter parameter in source.Parameters)
        {
            target.Parameters.Add(new SqlParameter(parameter.ParameterName, parameter.SqlDbType, parameter.Size)
            {
                Value = parameter.Value
            });
        }
    }

    private static string? NormalizeKeyword(string? keyword) => string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();

    private static string? NormalizeStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return NhapKhoPhieuStatus.Normalize(status);
    }

    private static object ToDbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string TrimToLength(string? value, int maxLength)
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
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static decimal? GetNullableDecimal(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetValue(ordinal));
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

    private static async Task<bool> HasColumnAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CASE
                WHEN COL_LENGTH(@TableName, @ColumnName) IS NULL THEN 0
                ELSE 1
            END
            """;
        command.Parameters.Add(new SqlParameter("@TableName", SqlDbType.NVarChar, 128) { Value = $"dbo.{tableName}" });
        command.Parameters.Add(new SqlParameter("@ColumnName", SqlDbType.NVarChar, 128) { Value = columnName });

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0) == 1;
    }

    private static async Task EnsureNhapKhoDetailSchemaAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(connection, transaction, DetailTableName, "DonGiaNhap", "decimal(18,2) NOT NULL CONSTRAINT DF_TblPhieuNhapKhoChiTiet_DonGiaNhap DEFAULT(0)", cancellationToken);
        await EnsureColumnAsync(connection, transaction, DetailTableName, "DonGiaBanLe", "decimal(18,2) NOT NULL CONSTRAINT DF_TblPhieuNhapKhoChiTiet_DonGiaBanLe DEFAULT(0)", cancellationToken);
        await EnsureColumnAsync(connection, transaction, DetailTableName, "IDDonViNhap", "int NULL", cancellationToken);
        await EnsureColumnAsync(connection, transaction, DetailTableName, "SoLuongQuyDoi", "decimal(18,4) NOT NULL CONSTRAINT DF_TblPhieuNhapKhoChiTiet_SoLuongQuyDoi DEFAULT(1)", cancellationToken);
        await EnsureColumnAsync(connection, transaction, DetailTableName, "SoChungTu", "nvarchar(50) NULL", cancellationToken);
    }

    private static async Task EnsureChiTietHangHoaImportSchemaAsync(SqlConnection connection, SqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(connection, transaction, "TblChiTietHangHoa", "DonGiaNhap", "decimal(18,2) NOT NULL CONSTRAINT DF_TblChiTietHangHoa_DonGiaNhap DEFAULT(0)", cancellationToken);
        await EnsureColumnAsync(connection, transaction, "TblChiTietHangHoa", "DonGiaBanLe", "decimal(18,2) NOT NULL CONSTRAINT DF_TblChiTietHangHoa_DonGiaBanLe DEFAULT(0)", cancellationToken);
        await EnsureColumnAsync(connection, transaction, "TblChiTietHangHoa", "IDDonViNhap", "int NULL", cancellationToken);
        await EnsureColumnAsync(connection, transaction, "TblChiTietHangHoa", "SoLuongQuyDoi", "decimal(18,4) NOT NULL CONSTRAINT DF_TblChiTietHangHoa_SoLuongQuyDoi DEFAULT(1)", cancellationToken);
        await EnsureColumnAsync(connection, transaction, "TblChiTietHangHoa", "SoChungTu", "nvarchar(50) NULL", cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string tableName,
        string columnName,
        string definition,
        CancellationToken cancellationToken)
    {
        if (await HasColumnAsync(connection, transaction, tableName, columnName, cancellationToken))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"ALTER TABLE [dbo].[{tableName}] ADD [{columnName}] {definition};";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed class NhapKhoImportMaterialSource
    {
        public int Id { get; set; }
        public int HangHoaId { get; set; }
        public int? DonViTinhId { get; set; }
        public int? DonViNhapId { get; set; }
        public string? TenHangHoa { get; set; }
        public string? MaSoLo { get; set; }
        public string? SoChungTu { get; set; }
        public decimal SoLuongNhap { get; set; }
        public decimal SoLuongQuyDoi { get; set; } = 1;
        public decimal DonGiaNhap { get; set; }
        public decimal DonGiaBanLe { get; set; }
        public string LoaiHinhNhap { get; set; } = string.Empty;
    }
}
