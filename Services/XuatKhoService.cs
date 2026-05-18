using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IXuatKhoService
{
    Task<(IReadOnlyList<XuatKhoListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        string? statusFilter,
        DateTime? fromDate,
        DateTime? toDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<XuatKhoListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<XuatKhoDetailItem>> GetDetailsAsync(int phieuId, CancellationToken cancellationToken = default);
    Task<string> GenerateNextMaPhieuAsync(DateTime ngayXuatKho, CancellationToken cancellationToken = default);
    Task<XuatKhoDetailItem?> FindVatTuByQrCodeAsync(string qrCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<XuatKhoDetailItem>> SearchVatTuForExportAsync(string? keyword, CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        XuatKhoFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        XuatKhoFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class XuatKhoService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<XuatKhoService> logger,
    ICommonAuditService commonAuditService) : IXuatKhoService
{
    private const string HeaderTableName = "TblPhieuXuatKho";
    private const string DetailTableName = "TblPhieuXuatKhoChiTiet";
    private const string VatTuTableName = "TblChiTietHangHoa";
    private const string SearchCollation = "Latin1_General_100_CI_AI";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<XuatKhoService> _logger = logger;
    private readonly ICommonAuditService _commonAuditService = commonAuditService;

    public async Task<(IReadOnlyList<XuatKhoListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        string? statusFilter,
        DateTime? fromDate,
        DateTime? toDate,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 5, 100);
        page = Math.Max(page, 1);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, transaction: null, cancellationToken);
            var normalizedKeyword = NormalizeKeyword(keyword);
            var normalizedStatus = NormalizeStatusFilter(statusFilter);
            var normalizedFromDate = fromDate?.Date;
            var normalizedToDateExclusive = toDate?.Date.AddDays(1);
            var filters = new List<string> { "1 = 1" };

            await using var countCommand = connection.CreateCommand();
            if (!string.IsNullOrWhiteSpace(normalizedKeyword))
            {
                filters.Add($"""
                    (
                        px.MaPhieu COLLATE {SearchCollation} LIKE @Keyword OR
                        px.NoiDungXuatKho COLLATE {SearchCollation} LIKE @Keyword OR
                        px.MucDichXuat COLLATE {SearchCollation} LIKE @Keyword OR
                        px.NguoiXuatKho COLLATE {SearchCollation} LIKE @Keyword OR
                        px.NguoiNhanHang COLLATE {SearchCollation} LIKE @Keyword OR
                        px.DiaChiNguoiNhanHang COLLATE {SearchCollation} LIKE @Keyword
                    )
                    """);
                countCommand.Parameters.Add(new SqlParameter("@Keyword", SqlDbType.NVarChar, 250) { Value = $"%{normalizedKeyword}%" });
            }

            if (!string.IsNullOrWhiteSpace(normalizedStatus))
            {
                if (normalizedStatus == XuatKhoPhieuStatus.Exported)
                {
                    filters.Add("LOWER(LTRIM(RTRIM(px.TrangThaiPhieu))) IN (@StatusFilter, @LegacyExportedStatus)");
                    countCommand.Parameters.Add(new SqlParameter("@LegacyExportedStatus", SqlDbType.NVarChar, 50) { Value = "da-xuat" });
                }
                else
                {
                    filters.Add("LOWER(LTRIM(RTRIM(px.TrangThaiPhieu))) = @StatusFilter");
                }

                countCommand.Parameters.Add(new SqlParameter("@StatusFilter", SqlDbType.NVarChar, 50) { Value = normalizedStatus });
            }

            if (normalizedFromDate.HasValue)
            {
                filters.Add("px.NgayXuatKho >= @FromDate");
                countCommand.Parameters.Add(new SqlParameter("@FromDate", SqlDbType.DateTime) { Value = normalizedFromDate.Value });
            }

            if (normalizedToDateExclusive.HasValue)
            {
                filters.Add("px.NgayXuatKho < @ToDateExclusive");
                countCommand.Parameters.Add(new SqlParameter("@ToDateExclusive", SqlDbType.DateTime) { Value = normalizedToDateExclusive.Value });
            }

            countCommand.CommandText = $"""
                SELECT COUNT(1)
                FROM [{HeaderTableName}] px
                WHERE {string.Join(" AND ", filters)}
                """;

            var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
            var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            var currentPage = totalPages == 0 ? 1 : Math.Min(page, totalPages);
            var offset = (currentPage - 1) * pageSize;

            await using var listCommand = connection.CreateCommand();
            foreach (SqlParameter parameter in countCommand.Parameters)
            {
                listCommand.Parameters.Add(new SqlParameter(parameter.ParameterName, parameter.SqlDbType, parameter.Size)
                {
                    Value = parameter.Value
                });
            }

            listCommand.CommandText = $"""
                SELECT
                    px.ID,
                    px.NgayXuatKho,
                    px.MaPhieu,
                    px.NoiDungXuatKho,
                    px.MucDichXuat,
                    px.NguoiXuatKho,
                    px.NguoiNhanHang,
                    px.DiaChiNguoiNhanHang,
                    px.TrangThaiPhieu,
                    COUNT(ct.ID) AS DetailCount,
                    ISNULL(SUM(ct.SoLuongXuat), 0) AS TotalQuantity
                FROM [{HeaderTableName}] px
                LEFT JOIN [{DetailTableName}] ct ON ct.IDPhieuXuatKho = px.ID
                WHERE {string.Join(" AND ", filters)}
                GROUP BY
                    px.ID,
                    px.NgayXuatKho,
                    px.MaPhieu,
                    px.NoiDungXuatKho,
                    px.MucDichXuat,
                    px.NguoiXuatKho,
                    px.NguoiNhanHang,
                    px.DiaChiNguoiNhanHang,
                    px.TrangThaiPhieu
                ORDER BY px.NgayXuatKho DESC, px.ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                """;
            listCommand.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = offset });
            listCommand.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = pageSize });

            var items = new List<XuatKhoListItem>();
            await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapListItem(reader));
            }

            return (items, totalCount, currentPage, totalPages, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblPhieuXuatKho list.");
            return ([], 0, 1, 0, pageSize);
        }
    }

    public async Task<XuatKhoListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return null;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, transaction: null, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT TOP (1)
                    px.ID,
                    px.NgayXuatKho,
                    px.MaPhieu,
                    px.NoiDungXuatKho,
                    px.MucDichXuat,
                    px.NguoiXuatKho,
                    px.NguoiNhanHang,
                    px.DiaChiNguoiNhanHang,
                    px.TrangThaiPhieu,
                    COUNT(ct.ID) AS DetailCount,
                    ISNULL(SUM(ct.SoLuongXuat), 0) AS TotalQuantity
                FROM [{HeaderTableName}] px
                LEFT JOIN [{DetailTableName}] ct ON ct.IDPhieuXuatKho = px.ID
                WHERE px.ID = @Id
                GROUP BY
                    px.ID,
                    px.NgayXuatKho,
                    px.MaPhieu,
                    px.NoiDungXuatKho,
                    px.MucDichXuat,
                    px.NguoiXuatKho,
                    px.NguoiNhanHang,
                    px.DiaChiNguoiNhanHang,
                    px.TrangThaiPhieu
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? MapListItem(reader) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblPhieuXuatKho {Id}.", id);
            return null;
        }
    }

    public async Task<IReadOnlyList<XuatKhoDetailItem>> GetDetailsAsync(int phieuId, CancellationToken cancellationToken = default)
    {
        if (phieuId <= 0)
        {
            return [];
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, transaction: null, cancellationToken);
            return await LoadDetailsAsync(connection, transaction: null, phieuId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblPhieuXuatKhoChiTiet for {Id}.", phieuId);
            return [];
        }
    }

    public async Task<string> GenerateNextMaPhieuAsync(DateTime ngayXuatKho, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await GenerateNextMaPhieuAsync(connection, transaction: null, ngayXuatKho, cancellationToken);
    }

    public async Task<XuatKhoDetailItem?> FindVatTuByQrCodeAsync(string qrCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(qrCode))
        {
            return null;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, transaction: null, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT TOP (1)
                    ct.ID,
                    ct.IDHangHoa,
                    ct.TenChiTiet,
                    ct.QRCode,
                    ct.SoLuongNhap,
                    ct.SoLuongTon,
                    ct.DonGiaNhap,
                    ct.DonGiaBanLe,
                    ct.MaSoLo,
                    ct.NgayNhap,
                    ct.LuuTaiKho,
                    ct.Image,
                    hh.TenHangHoa,
                    hh.MaHangHoa,
                    kho.TenKho,
                    kho.MaKho,
                    dvt.TenDonVi,
                    dvt.TenVietTat
                FROM [{VatTuTableName}] ct
                LEFT JOIN [TblHangHoa] hh ON hh.ID = ct.IDHangHoa
                LEFT JOIN [TblKho] kho ON kho.ID = ct.IDKho
                LEFT JOIN [TblDonViTinh] dvt ON dvt.ID = ct.IDDonVinTinh
                WHERE UPPER(LTRIM(RTRIM(ct.QRCode))) = UPPER(@QRCode)
                  AND ISNULL(ct.SoLuongTon, 0) > 0
                ORDER BY ISNULL(ct.SoLuongTon, 0) ASC, ISNULL(ct.NgayNhap, '99991231') ASC, ct.ID ASC
                """;
            command.Parameters.Add(new SqlParameter("@QRCode", SqlDbType.NVarChar, 50) { Value = qrCode.Trim() });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? MapVatTuLookupItem(reader) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to lookup VatTu by QR for XuatKho.");
            return null;
        }
    }

    public async Task<IReadOnlyList<XuatKhoDetailItem>> SearchVatTuForExportAsync(string? keyword, CancellationToken cancellationToken = default)
    {
        var normalizedKeyword = NormalizeKeyword(keyword);
        if (string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            return [];
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, transaction: null, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT TOP (12)
                    ct.ID,
                    ct.IDHangHoa,
                    ct.TenChiTiet,
                    ct.QRCode,
                    ct.SoLuongNhap,
                    ct.SoLuongTon,
                    ct.DonGiaNhap,
                    ct.DonGiaBanLe,
                    ct.MaSoLo,
                    ct.NgayNhap,
                    ct.LuuTaiKho,
                    ct.Image,
                    hh.TenHangHoa,
                    hh.MaHangHoa,
                    kho.TenKho,
                    kho.MaKho,
                    dvt.TenDonVi,
                    dvt.TenVietTat
                FROM [{VatTuTableName}] ct
                LEFT JOIN [TblHangHoa] hh ON hh.ID = ct.IDHangHoa
                LEFT JOIN [TblKho] kho ON kho.ID = ct.IDKho
                LEFT JOIN [TblDonViTinh] dvt ON dvt.ID = ct.IDDonVinTinh
                WHERE ISNULL(ct.SoLuongTon, 0) > 0
                  AND (
                      ct.TenChiTiet COLLATE {SearchCollation} LIKE @Keyword OR
                      ct.MaSoLo COLLATE {SearchCollation} LIKE @Keyword OR
                      ct.LuuTaiKho COLLATE {SearchCollation} LIKE @Keyword OR
                      ct.QRCode COLLATE {SearchCollation} LIKE @Keyword OR
                      hh.TenHangHoa COLLATE {SearchCollation} LIKE @Keyword OR
                      hh.MaHangHoa COLLATE {SearchCollation} LIKE @Keyword OR
                      kho.TenKho COLLATE {SearchCollation} LIKE @Keyword OR
                      kho.MaKho COLLATE {SearchCollation} LIKE @Keyword
                  )
                ORDER BY
                    ISNULL(ct.SoLuongTon, 0) ASC,
                    ISNULL(ct.NgayNhap, '99991231') ASC,
                    ct.ID ASC
                """;
            command.Parameters.Add(new SqlParameter("@Keyword", SqlDbType.NVarChar, 250) { Value = $"%{normalizedKeyword}%" });

            var items = new List<XuatKhoDetailItem>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapVatTuLookupItem(reader));
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search VatTu for XuatKho.");
            return [];
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        XuatKhoFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        var normalizedDetails = NormalizeDetails(model.Details);
        if (normalizedDetails.Count == 0)
        {
            return (false, "Vui lòng scan hoặc thêm ít nhất một vật tư cần xuất.", null);
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, transaction, cancellationToken);

            var validationError = await ValidateDetailsAsync(connection, transaction, normalizedDetails, cancellationToken);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, validationError, null);
            }

            var dateValidation = NormalizeNgayXuatKho(model.NgayXuatKho);
            if (!string.IsNullOrWhiteSpace(dateValidation.ErrorMessage))
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, dateValidation.ErrorMessage, null);
            }

            var ngayXuatKho = dateValidation.NgayXuatKho;
            var maPhieu = await GenerateNextMaPhieuAsync(connection, transaction, DateTime.Today, cancellationToken);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO [{HeaderTableName}] (
                    NgayTaoPhieu,
                    NgayXuatKho,
                    MaPhieu,
                    NoiDungXuatKho,
                    MucDichXuat,
                    NguoiXuatKho,
                    NguoiNhanHang,
                    DiaChiNguoiNhanHang,
                    TrangThaiPhieu
                )
                VALUES (
                    @NgayTaoPhieu,
                    @NgayXuatKho,
                    @MaPhieu,
                    @NoiDungXuatKho,
                    @MucDichXuat,
                    @NguoiXuatKho,
                    @NguoiNhanHang,
                    @DiaChiNguoiNhanHang,
                    @TrangThaiPhieu
                );

                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;
            FillHeaderParameters(command, ngayXuatKho, maPhieu, model.NoiDungXuatKho, model.MucDichXuat, currentUser, model.NguoiNhanHang, model.DiaChiNguoiNhanHang, XuatKhoPhieuStatus.Draft);
            command.Parameters.Add(new SqlParameter("@NgayTaoPhieu", SqlDbType.DateTime) { Value = DateTime.Now });

            var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
            if (newId <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không thể tạo phiếu xuất kho.", null);
            }

            await ReplaceDetailsAsync(connection, transaction, newId, normalizedDetails, cancellationToken);
            await _commonAuditService.WriteAsync(
                connection,
                transaction,
                new CommonAuditEntry(
                    "XUAT_KHO",
                    "CREATE",
                    "PHIEU_XUAT",
                    newId.ToString(),
                    maPhieu,
                    "Tao phieu xuat kho nhap.",
                    currentUser,
                    Data: new { Id = newId, MaPhieu = maPhieu, NgayXuatKho = ngayXuatKho, Details = normalizedDetails }),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return (true, null, newId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TblPhieuXuatKho.");
            return (false, "Không thể tạo phiếu xuất kho lúc này.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        XuatKhoFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (model.Id is null or <= 0)
        {
            return (false, "Không xác định được phiếu xuất kho cần cập nhật.");
        }

        var normalizedDetails = NormalizeDetails(model.Details);
        if (normalizedDetails.Count == 0)
        {
            return (false, "Vui lòng scan hoặc thêm ít nhất một vật tư cần xuất.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await EnsureSchemaAsync(connection, transaction, cancellationToken);

            var currentStatus = await LoadStatusAsync(connection, transaction, model.Id.Value, cancellationToken);
            if (currentStatus is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy phiếu xuất kho cần cập nhật.");
            }

            currentStatus = XuatKhoPhieuStatus.Normalize(currentStatus);
            if (currentStatus != XuatKhoPhieuStatus.Draft)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Chỉ phiếu nháp mới được cập nhật.");
            }

            var targetStatus = XuatKhoPhieuStatus.Normalize(model.TrangThaiPhieu);
            var dateValidation = NormalizeNgayXuatKho(model.NgayXuatKho);
            if (!string.IsNullOrWhiteSpace(dateValidation.ErrorMessage))
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, dateValidation.ErrorMessage);
            }

            var validationError = await ValidateDetailsAsync(connection, transaction, normalizedDetails, cancellationToken);
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, validationError);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                UPDATE [{HeaderTableName}]
                SET
                    NgayXuatKho = @NgayXuatKho,
                    NoiDungXuatKho = @NoiDungXuatKho,
                    MucDichXuat = @MucDichXuat,
                    NguoiXuatKho = @NguoiXuatKho,
                    NguoiNhanHang = @NguoiNhanHang,
                    DiaChiNguoiNhanHang = @DiaChiNguoiNhanHang,
                    TrangThaiPhieu = @TrangThaiPhieu
                WHERE ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id.Value });
            FillHeaderParameters(
                command,
                dateValidation.NgayXuatKho,
                model.MaPhieu ?? string.Empty,
                model.NoiDungXuatKho,
                model.MucDichXuat,
                string.IsNullOrWhiteSpace(model.NguoiXuatKho) ? currentUser : model.NguoiXuatKho,
                model.NguoiNhanHang,
                model.DiaChiNguoiNhanHang,
                targetStatus,
                includeMaPhieu: false);

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy phiếu xuất kho cần cập nhật.");
            }

            await ReplaceDetailsAsync(connection, transaction, model.Id.Value, normalizedDetails, cancellationToken);

            if (targetStatus == XuatKhoPhieuStatus.Exported)
            {
                await DecreaseInventoryAsync(connection, transaction, normalizedDetails, cancellationToken);
            }

            await _commonAuditService.WriteAsync(
                connection,
                transaction,
                new CommonAuditEntry(
                    "XUAT_KHO",
                    targetStatus == XuatKhoPhieuStatus.Exported ? "EXPORT" : "UPDATE",
                    "PHIEU_XUAT",
                    model.Id.Value.ToString(),
                    model.MaPhieu,
                    targetStatus == XuatKhoPhieuStatus.Exported ? "Xuat phieu xuat kho va tru ton vat tu." : "Cap nhat phieu xuat kho.",
                    currentUser,
                    Data: new { model.Id, model.MaPhieu, NgayXuatKho = dateValidation.NgayXuatKho, TrangThaiPhieu = targetStatus, Details = normalizedDetails }),
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update TblPhieuXuatKho {Id}.", model.Id);
            return (false, "Không thể cập nhật phiếu xuất kho lúc này.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return (false, "Không xác định được phiếu xuất kho cần xóa.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            var currentStatus = XuatKhoPhieuStatus.Normalize(await LoadStatusAsync(connection, transaction, id, cancellationToken));
            if (currentStatus == XuatKhoPhieuStatus.Exported)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không thể xóa phiếu đã xuất.");
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                DELETE FROM [{DetailTableName}]
                WHERE IDPhieuXuatKho = @Id;

                DELETE FROM [{HeaderTableName}]
                WHERE ID = @Id;
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy phiếu xuất kho cần xóa.");
            }

            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete TblPhieuXuatKho {Id}.", id);
            return (false, "Không thể xóa phiếu xuất kho lúc này.");
        }
    }

    private async Task<string> GenerateNextMaPhieuAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        DateTime ngayXuatKho,
        CancellationToken cancellationToken)
    {
        var prefix = $"X{ngayXuatKho:yyMM}";
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

    private static async Task<IReadOnlyList<XuatKhoDetailItem>> LoadDetailsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int phieuId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                pxct.ID,
                pxct.IDChiTietHangHoa,
                pxct.IDHangHoa,
                pxct.SoLuongXuat,
                pxct.DonGiaXuat,
                pxct.TongTienXuat,
                ct.TenChiTiet,
                ct.QRCode,
                ct.SoLuongNhap,
                ct.SoLuongTon,
                ct.DonGiaNhap,
                ct.DonGiaBanLe,
                ct.MaSoLo,
                ct.SoChungTu,
                ct.NgayNhap,
                ct.LuuTaiKho,
                ct.Image,
                hh.TenHangHoa,
                hh.MaHangHoa,
                kho.TenKho,
                kho.MaKho,
                dvt.TenDonVi,
                dvt.TenVietTat
            FROM [{DetailTableName}] pxct
            LEFT JOIN [{VatTuTableName}] ct ON ct.ID = pxct.IDChiTietHangHoa
            LEFT JOIN [TblHangHoa] hh ON hh.ID = pxct.IDHangHoa
            LEFT JOIN [TblKho] kho ON kho.ID = ct.IDKho
            LEFT JOIN [TblDonViTinh] dvt ON dvt.ID = ct.IDDonVinTinh
            WHERE pxct.IDPhieuXuatKho = @PhieuId
            ORDER BY pxct.ID ASC
            """;
        command.Parameters.Add(new SqlParameter("@PhieuId", SqlDbType.Int) { Value = phieuId });

        var items = new List<XuatKhoDetailItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(MapDetailItem(reader));
        }

        return items;
    }

    private static async Task<string?> LoadStatusAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int id,
        CancellationToken cancellationToken)
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

    private static async Task<string?> ValidateDetailsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyList<XuatKhoDetailItem> details,
        CancellationToken cancellationToken)
    {
        foreach (var detail in details)
        {
            if (detail.VatTuId <= 0)
            {
                return "Danh sách vật tư xuất không hợp lệ.";
            }

            if (detail.SoLuongXuat < 1)
            {
                return "Số lượng xuất không được nhỏ hơn 1.";
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                SELECT TOP (1)
                    SoLuongTon,
                    SoLuongNhap,
                    DonGiaNhap,
                    DonGiaBanLe,
                    IDHangHoa
                FROM [{VatTuTableName}] WITH (UPDLOCK, ROWLOCK)
                WHERE ID = @VatTuId
                """;
            command.Parameters.Add(new SqlParameter("@VatTuId", SqlDbType.Int) { Value = detail.VatTuId });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return "Không tìm thấy vật tư trong danh sách xuất.";
            }

            var stock = GetNullableDecimal(reader, "SoLuongTon") ?? 0;
            var originalQuantity = GetNullableDecimal(reader, "SoLuongNhap") ?? 0;
            var importPrice = GetNullableDecimal(reader, "DonGiaNhap") ?? 0;
            var retailPrice = GetNullableDecimal(reader, "DonGiaBanLe") ?? 0;
            var hangHoaId = GetNullableInt32(reader, "IDHangHoa");
            await reader.CloseAsync();

            detail.SoLuongTon = stock;
            detail.SoLuongNhap = originalQuantity;
            detail.DonGiaNhap = importPrice;
            detail.DonGiaBanLe = retailPrice;
            detail.HangHoaId = hangHoaId;
            ApplyExportPrice(detail);

            if (detail.SoLuongXuat > stock)
            {
                return $"Số lượng xuất của {detail.TenChiTiet} không được vượt quá tồn kho ({stock:0.##}).";
            }
        }

        return null;
    }

    private static async Task ReplaceDetailsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int phieuId,
        IReadOnlyList<XuatKhoDetailItem> details,
        CancellationToken cancellationToken)
    {
        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = $"""
            DELETE FROM [{DetailTableName}]
            WHERE IDPhieuXuatKho = @PhieuId
            """;
        deleteCommand.Parameters.Add(new SqlParameter("@PhieuId", SqlDbType.Int) { Value = phieuId });
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var detail in details)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = $"""
                INSERT INTO [{DetailTableName}] (
                    IDPhieuXuatKho,
                    IDChiTietHangHoa,
                    IDHangHoa,
                    SoLuongXuat,
                    DonGiaXuat,
                    TongTienXuat
                )
                VALUES (
                    @IDPhieuXuatKho,
                    @IDChiTietHangHoa,
                    @IDHangHoa,
                    @SoLuongXuat,
                    @DonGiaXuat,
                    @TongTienXuat
                )
                """;
            insertCommand.Parameters.Add(new SqlParameter("@IDPhieuXuatKho", SqlDbType.Int) { Value = phieuId });
            insertCommand.Parameters.Add(new SqlParameter("@IDChiTietHangHoa", SqlDbType.Int) { Value = detail.VatTuId });
            insertCommand.Parameters.Add(new SqlParameter("@IDHangHoa", SqlDbType.Int) { Value = detail.HangHoaId.HasValue ? detail.HangHoaId.Value : DBNull.Value });
            insertCommand.Parameters.Add(new SqlParameter("@SoLuongXuat", SqlDbType.Decimal)
            {
                Precision = 18,
                Scale = 2,
                Value = detail.SoLuongXuat
            });
            insertCommand.Parameters.Add(new SqlParameter("@DonGiaXuat", SqlDbType.Decimal)
            {
                Precision = 18,
                Scale = 2,
                Value = detail.DonGiaXuat
            });
            insertCommand.Parameters.Add(new SqlParameter("@TongTienXuat", SqlDbType.Decimal)
            {
                Precision = 18,
                Scale = 2,
                Value = detail.TongTienXuat
            });
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task DecreaseInventoryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyList<XuatKhoDetailItem> details,
        CancellationToken cancellationToken)
    {
        foreach (var detail in details)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                UPDATE [{VatTuTableName}]
                SET SoLuongTon = SoLuongTon - @SoLuongXuat
                WHERE ID = @VatTuId
                  AND SoLuongTon >= @SoLuongXuat
                """;
            command.Parameters.Add(new SqlParameter("@VatTuId", SqlDbType.Int) { Value = detail.VatTuId });
            command.Parameters.Add(new SqlParameter("@SoLuongXuat", SqlDbType.Decimal)
            {
                Precision = 18,
                Scale = 2,
                Value = detail.SoLuongXuat
            });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows <= 0)
            {
                throw new InvalidOperationException($"Inventory is insufficient for VatTu {detail.VatTuId}.");
            }
        }
    }

    private static List<XuatKhoDetailItem> NormalizeDetails(IEnumerable<XuatKhoDetailItem>? details)
    {
        var result = new List<XuatKhoDetailItem>();
        foreach (var detail in details ?? [])
        {
            if (detail.VatTuId <= 0)
            {
                continue;
            }

            var existing = result.FirstOrDefault(item => item.VatTuId == detail.VatTuId);
            if (existing is not null)
            {
                existing.SoLuongXuat = detail.SoLuongXuat;
                continue;
            }

            result.Add(new XuatKhoDetailItem
            {
                Id = detail.Id,
                VatTuId = detail.VatTuId,
                HangHoaId = detail.HangHoaId,
                TenChiTiet = string.IsNullOrWhiteSpace(detail.TenChiTiet) ? $"Vật tư #{detail.VatTuId}" : detail.TenChiTiet.Trim(),
                TenHangHoa = detail.TenHangHoa,
                MaHangHoa = detail.MaHangHoa,
                TenKho = detail.TenKho,
                MaKho = detail.MaKho,
                DonViTinh = detail.DonViTinh,
                QRCode = detail.QRCode,
                SoChungTu = detail.SoChungTu,
                NgayNhapKho = detail.NgayNhapKho,
                SoLuongNhap = detail.SoLuongNhap,
                SoLuongTon = detail.SoLuongTon,
                DonGiaNhap = detail.DonGiaNhap,
                DonGiaBanLe = detail.DonGiaBanLe,
                DonGiaXuat = detail.DonGiaXuat,
                TongTienXuat = detail.TongTienXuat,
                SoLuongXuat = detail.SoLuongXuat
            });
        }

        return result;
    }

    private static void FillHeaderParameters(
        SqlCommand command,
        DateTime ngayXuatKho,
        string maPhieu,
        string? noiDungXuatKho,
        string? mucDichXuat,
        string? nguoiXuatKho,
        string? nguoiNhanHang,
        string? diaChiNguoiNhanHang,
        string trangThaiPhieu,
        bool includeMaPhieu = true)
    {
        command.Parameters.Add(new SqlParameter("@NgayXuatKho", SqlDbType.DateTime) { Value = ngayXuatKho });
        if (includeMaPhieu)
        {
            command.Parameters.Add(new SqlParameter("@MaPhieu", SqlDbType.NVarChar, 50) { Value = maPhieu });
        }

        command.Parameters.Add(new SqlParameter("@NoiDungXuatKho", SqlDbType.NVarChar, 550) { Value = ToDbValue(noiDungXuatKho) });
        command.Parameters.Add(new SqlParameter("@MucDichXuat", SqlDbType.NVarChar, 50) { Value = XuatKhoMucDich.Normalize(mucDichXuat) });
        command.Parameters.Add(new SqlParameter("@NguoiXuatKho", SqlDbType.NVarChar, 50) { Value = TrimToLength(nguoiXuatKho, 50) });
        command.Parameters.Add(new SqlParameter("@NguoiNhanHang", SqlDbType.NVarChar, 250) { Value = ToDbValue(nguoiNhanHang) });
        command.Parameters.Add(new SqlParameter("@DiaChiNguoiNhanHang", SqlDbType.NVarChar, 550) { Value = ToDbValue(diaChiNguoiNhanHang) });
        command.Parameters.Add(new SqlParameter("@TrangThaiPhieu", SqlDbType.NVarChar, 50) { Value = XuatKhoPhieuStatus.Normalize(trangThaiPhieu) });
    }

    private static XuatKhoListItem MapListItem(SqlDataReader reader)
    {
        return new XuatKhoListItem
        {
            Id = reader.GetInt32(reader.GetOrdinal("ID")),
            NgayXuatKho = GetNullableDateTime(reader, "NgayXuatKho"),
            MaPhieu = GetNullableString(reader, "MaPhieu") ?? string.Empty,
            NoiDungXuatKho = GetNullableString(reader, "NoiDungXuatKho"),
            MucDichXuat = XuatKhoMucDich.Normalize(GetNullableString(reader, "MucDichXuat")),
            NguoiXuatKho = GetNullableString(reader, "NguoiXuatKho"),
            NguoiNhanHang = GetNullableString(reader, "NguoiNhanHang"),
            DiaChiNguoiNhanHang = GetNullableString(reader, "DiaChiNguoiNhanHang"),
            TrangThaiPhieu = XuatKhoPhieuStatus.Normalize(GetNullableString(reader, "TrangThaiPhieu")),
            DetailCount = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("DetailCount"))),
            TotalQuantity = GetNullableDecimal(reader, "TotalQuantity") ?? 0
        };
    }

    private static XuatKhoDetailItem MapDetailItem(SqlDataReader reader)
    {
        var tenDonVi = GetNullableString(reader, "TenDonVi");
        var tenVietTat = GetNullableString(reader, "TenVietTat");
        var item = new XuatKhoDetailItem
        {
            Id = GetNullableInt32(reader, "ID"),
            VatTuId = GetNullableInt32(reader, "IDChiTietHangHoa") ?? 0,
            HangHoaId = GetNullableInt32(reader, "IDHangHoa"),
            TenChiTiet = GetNullableString(reader, "TenChiTiet") ?? string.Empty,
            QRCode = GetNullableString(reader, "QRCode"),
            SoChungTu = GetNullableString(reader, "SoChungTu"),
            NgayNhapKho = GetNullableDateTime(reader, "NgayNhap"),
            SoLuongNhap = GetNullableDecimal(reader, "SoLuongNhap") ?? 0,
            SoLuongTon = GetNullableDecimal(reader, "SoLuongTon") ?? 0,
            SoLuongXuat = GetNullableDecimal(reader, "SoLuongXuat") ?? 1,
            DonGiaNhap = GetNullableDecimal(reader, "DonGiaNhap") ?? 0,
            DonGiaBanLe = GetNullableDecimal(reader, "DonGiaBanLe") ?? 0,
            DonGiaXuat = GetNullableDecimal(reader, "DonGiaXuat") ?? 0,
            TongTienXuat = GetNullableDecimal(reader, "TongTienXuat") ?? 0,
            TenHangHoa = GetNullableString(reader, "TenHangHoa"),
            MaHangHoa = GetNullableString(reader, "MaHangHoa"),
            TenKho = GetNullableString(reader, "TenKho"),
            MaKho = GetNullableString(reader, "MaKho"),
            DonViTinh = string.IsNullOrWhiteSpace(tenVietTat) ? tenDonVi : tenVietTat,
            MaSoLo = GetNullableString(reader, "MaSoLo"),
            ViTriLuuKho = GetNullableString(reader, "LuuTaiKho"),
            ImageUrl = GetNullableString(reader, "Image")
        };
        if (item.DonGiaXuat <= 0 && item.TongTienXuat <= 0)
        {
            ApplyExportPrice(item);
        }

        return item;
    }

    private static XuatKhoDetailItem MapVatTuLookupItem(SqlDataReader reader)
    {
        var tenDonVi = GetNullableString(reader, "TenDonVi");
        var tenVietTat = GetNullableString(reader, "TenVietTat");
        var stock = GetNullableDecimal(reader, "SoLuongTon") ?? 0;
        var item = new XuatKhoDetailItem
        {
            VatTuId = reader.GetInt32(reader.GetOrdinal("ID")),
            HangHoaId = GetNullableInt32(reader, "IDHangHoa"),
            TenChiTiet = GetNullableString(reader, "TenChiTiet") ?? string.Empty,
            QRCode = GetNullableString(reader, "QRCode"),
            NgayNhapKho = GetNullableDateTime(reader, "NgayNhap"),
            SoLuongNhap = GetNullableDecimal(reader, "SoLuongNhap") ?? 0,
            SoLuongTon = stock,
            SoLuongXuat = stock < 1 ? 1 : stock,
            DonGiaNhap = GetNullableDecimal(reader, "DonGiaNhap") ?? 0,
            DonGiaBanLe = GetNullableDecimal(reader, "DonGiaBanLe") ?? 0,
            TenHangHoa = GetNullableString(reader, "TenHangHoa"),
            MaHangHoa = GetNullableString(reader, "MaHangHoa"),
            TenKho = GetNullableString(reader, "TenKho"),
            MaKho = GetNullableString(reader, "MaKho"),
            DonViTinh = string.IsNullOrWhiteSpace(tenVietTat) ? tenDonVi : tenVietTat,
            MaSoLo = GetNullableString(reader, "MaSoLo"),
            ViTriLuuKho = GetNullableString(reader, "LuuTaiKho"),
            ImageUrl = GetNullableString(reader, "Image")
        };
        ApplyExportPrice(item);
        return item;
    }

    private static void ApplyExportPrice(XuatKhoDetailItem detail)
    {
        var usesImportPrice = detail.SoLuongXuat == detail.SoLuongTon
            && detail.SoLuongTon == detail.SoLuongNhap
            && detail.DonGiaNhap > 0;
        detail.DonGiaXuat = usesImportPrice ? detail.DonGiaNhap : detail.DonGiaBanLe;
        detail.TongTienXuat = usesImportPrice
            ? detail.DonGiaNhap
            : detail.DonGiaXuat * detail.SoLuongXuat;
    }

    private static async Task EnsureSchemaAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(connection, transaction, HeaderTableName, "NguoiNhanHang", "nvarchar(250) NULL", cancellationToken);
        await EnsureColumnAsync(connection, transaction, HeaderTableName, "DiaChiNguoiNhanHang", "nvarchar(550) NULL", cancellationToken);
        await EnsureColumnAsync(connection, transaction, HeaderTableName, "MucDichXuat", "nvarchar(50) NOT NULL CONSTRAINT DF_TblPhieuXuatKho_MucDichXuat DEFAULT('xuat-ban-hang')", cancellationToken);
        await EnsureColumnAsync(connection, transaction, DetailTableName, "DonGiaXuat", "decimal(18,2) NOT NULL CONSTRAINT DF_TblPhieuXuatKhoChiTiet_DonGiaXuat DEFAULT(0)", cancellationToken);
        await EnsureColumnAsync(connection, transaction, DetailTableName, "TongTienXuat", "decimal(18,2) NOT NULL CONSTRAINT DF_TblPhieuXuatKhoChiTiet_TongTienXuat DEFAULT(0)", cancellationToken);
        await EnsureColumnAsync(connection, transaction, VatTuTableName, "DonGiaNhap", "decimal(18,2) NOT NULL CONSTRAINT DF_TblChiTietHangHoa_DonGiaNhap DEFAULT(0)", cancellationToken);
        await EnsureColumnAsync(connection, transaction, VatTuTableName, "DonGiaBanLe", "decimal(18,2) NOT NULL CONSTRAINT DF_TblChiTietHangHoa_DonGiaBanLe DEFAULT(0)", cancellationToken);
        await EnsureColumnAsync(connection, transaction, VatTuTableName, "SoChungTu", "nvarchar(50) NULL", cancellationToken);
        await EnsureColumnAsync(connection, transaction, VatTuTableName, "NgayNhap", "datetime NULL", cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string tableName,
        string columnName,
        string definition,
        CancellationToken cancellationToken)
    {
        await using var checkCommand = connection.CreateCommand();
        checkCommand.Transaction = transaction;
        checkCommand.CommandText = """
            SELECT COUNT(1)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo'
              AND TABLE_NAME = @TableName
              AND COLUMN_NAME = @ColumnName
            """;
        checkCommand.Parameters.Add(new SqlParameter("@TableName", SqlDbType.NVarChar, 128) { Value = tableName });
        checkCommand.Parameters.Add(new SqlParameter("@ColumnName", SqlDbType.NVarChar, 128) { Value = columnName });

        var exists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync(cancellationToken) ?? 0) > 0;
        if (exists)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"ALTER TABLE [dbo].[{tableName}] ADD [{columnName}] {definition};";
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static (DateTime NgayXuatKho, string? ErrorMessage) NormalizeNgayXuatKho(DateTime? ngayXuatKho)
    {
        var normalizedDate = (ngayXuatKho ?? DateTime.Today).Date;
        if (normalizedDate > DateTime.Today)
        {
            return (normalizedDate, "Ngày xuất kho không được vượt quá ngày hiện tại.");
        }

        return (normalizedDate, null);
    }

    private static string? NormalizeKeyword(string? keyword)
    {
        return string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
    }

    private static string? NormalizeStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return XuatKhoPhieuStatus.Normalize(status);
    }

    private static object ToDbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

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
}
