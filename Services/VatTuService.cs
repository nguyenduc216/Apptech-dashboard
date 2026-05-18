using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IVatTuService
{
    Task<(IReadOnlyList<VatTuListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<VatTuListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        VatTuFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        VatTuFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, int SourceCount, int CreatedCount)> CopyAsync(
        IReadOnlyCollection<int> sourceIds,
        int copyQuantity,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> IsQrCodeInUseAsync(
        string qrCode,
        int? excludingId = null,
        CancellationToken cancellationToken = default);

    Task<int?> FindIdByQrCodeAsync(
        string qrCode,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<VatTuLookupOption> KhoOptions, IReadOnlyList<VatTuLookupOption> HangHoaOptions, IReadOnlyList<VatTuLookupOption> DonViTinhOptions)> GetLookupDataAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QrCodeAssignmentTargetItem>> SearchForQrAssignmentAsync(
        QrCodeAssignmentSearchModel model,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> AssignQrCodeAsync(
        int itemId,
        string qrCode,
        string currentUser,
        CancellationToken cancellationToken = default);
}

public sealed class VatTuService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<VatTuService> logger,
    ICommonAuditService commonAuditService) : IVatTuService
{
    private const string TableName = "TblChiTietHangHoa";
    private const string ImageTableName = "TblChiTietHangHoaImages";
    private const string SearchCollation = "Latin1_General_100_CI_AI";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<VatTuService> _logger = logger;
    private readonly ICommonAuditService _commonAuditService = commonAuditService;

    public async Task<(IReadOnlyList<VatTuListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 5, 1000);
        page = Math.Max(page, 1);

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var normalizedKeyword = NormalizeKeyword(keyword);
            var keywordTerms = SplitKeywordTerms(normalizedKeyword);
            var whereClause = BuildWhereClause(keywordTerms);
            var hasTrangThaiSuDungColumn = await HasTrangThaiSuDungColumnAsync(connection, transaction: null, cancellationToken);
            var trangThaiSuDungSelect = hasTrangThaiSuDungColumn
                ? "CAST(ISNULL(ct.TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung,"
                : "CAST(1 AS bit) AS TrangThaiSuDung,";
            var hasPhieuNhapChiTietColumn = await HasPhieuNhapChiTietColumnAsync(connection, transaction: null, cancellationToken);
            var phieuNhapChiTietSelect = hasPhieuNhapChiTietColumn
                ? "ct.IDPhieuNhapChiTiet,"
                : "CAST(NULL AS int) AS IDPhieuNhapChiTiet,";
            var hasDonViNhapColumn = await HasColumnAsync(connection, transaction: null, "TblChiTietHangHoa", "IDDonViNhap", cancellationToken);
            var donViNhapExpression = hasDonViNhapColumn ? "ct.IDDonViNhap" : "CAST(NULL AS int)";
            var donViNhapSelect = $"{donViNhapExpression} AS IDDonViNhap,";
            var donGiaBanLeSelect = await HasColumnAsync(connection, transaction: null, "TblChiTietHangHoa", "DonGiaBanLe", cancellationToken)
                ? "ct.DonGiaBanLe,"
                : "CAST(0 AS decimal(18,2)) AS DonGiaBanLe,";

            await using var countCommand = connection.CreateCommand();
            countCommand.CommandText = $"""
                SELECT COUNT(1)
                FROM [{TableName}] ct
                LEFT JOIN [TblKho] kho ON kho.ID = ct.IDKho
                LEFT JOIN [TblHangHoa] hh ON hh.ID = ct.IDHangHoa
                LEFT JOIN [TblDonViTinh] dvt ON dvt.ID = ct.IDDonVinTinh
                LEFT JOIN [TblPhieuNhapKhoChiTiet] pnct ON pnct.ID = ct.IDPhieuNhapChiTiet
                LEFT JOIN [TblPhieuNhapKho] pn ON pn.ID = pnct.IDPhieuNhapKho
                OUTER APPLY (
                    SELECT TOP (1)
                        px.ID,
                        px.MaPhieu
                    FROM [TblPhieuXuatKhoChiTiet] pxct
                    INNER JOIN [TblPhieuXuatKho] px ON px.ID = pxct.IDPhieuXuatKho
                    WHERE pxct.IDChiTietHangHoa = ct.ID
                    ORDER BY px.NgayXuatKho DESC, px.ID DESC
                ) pxLatest
                WHERE {whereClause}
                """;
            AddFilterParameters(countCommand, keywordTerms);

            var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
            var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            var currentPage = totalPages == 0 ? 1 : Math.Min(page, totalPages);
            var offset = (currentPage - 1) * pageSize;

            await using var listCommand = connection.CreateCommand();
            listCommand.CommandText = $"""
                SELECT
                    ct.ID,
                    {trangThaiSuDungSelect}
                    ct.IDKho,
                    ct.IDHangHoa,
                    ct.IDDonVinTinh,
                    {donViNhapSelect}
                    ct.TenChiTiet,
                    ct.QRCode,
                    ct.SoLuongTon,
                    {donGiaBanLeSelect}
                    ct.MaSoLo,
                    ct.LuuTaiKho,
                    ct.GhiChu,
                    ct.Image,
                    {phieuNhapChiTietSelect}
                    ct.Created_Date,
                    ct.Created_By,
                    ct.Updated_Date,
                    ct.Updated_By,
                    kho.TenKho,
                    kho.MaKho,
                    hh.TenHangHoa,
                    hh.MaHangHoa,
                    dvt.TenDonVi,
                    dvt.TenVietTat,
                    dvtNhap.TenDonVi AS TenDonViNhap,
                    dvtNhap.TenVietTat AS TenVietTatDonViNhap,
                    pn.ID AS IDPhieuNhapKho,
                    pn.MaPhieu AS MaPhieuNhap,
                    pxLatest.ID AS IDPhieuXuatKho,
                    pxLatest.MaPhieu AS MaPhieuXuat
                FROM [{TableName}] ct
                LEFT JOIN [TblKho] kho ON kho.ID = ct.IDKho
                LEFT JOIN [TblHangHoa] hh ON hh.ID = ct.IDHangHoa
                LEFT JOIN [TblDonViTinh] dvt ON dvt.ID = ct.IDDonVinTinh
                LEFT JOIN [TblDonViTinh] dvtNhap ON dvtNhap.ID = {donViNhapExpression}
                LEFT JOIN [TblPhieuNhapKhoChiTiet] pnct ON pnct.ID = ct.IDPhieuNhapChiTiet
                LEFT JOIN [TblPhieuNhapKho] pn ON pn.ID = pnct.IDPhieuNhapKho
                OUTER APPLY (
                    SELECT TOP (1)
                        px.ID,
                        px.MaPhieu
                    FROM [TblPhieuXuatKhoChiTiet] pxct
                    INNER JOIN [TblPhieuXuatKho] px ON px.ID = pxct.IDPhieuXuatKho
                    WHERE pxct.IDChiTietHangHoa = ct.ID
                    ORDER BY px.NgayXuatKho DESC, px.ID DESC
                ) pxLatest
                WHERE {whereClause}
                ORDER BY ct.ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                """;
            AddFilterParameters(listCommand, keywordTerms);
            listCommand.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = offset });
            listCommand.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = pageSize });

            var items = new List<VatTuListItem>();
            await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapItem(reader));
            }

            return (items, totalCount, currentPage, totalPages, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblChiTietHangHoa list.");
            return ([], 0, 1, 0, pageSize);
        }
    }

    public async Task<VatTuListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return null;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var hasTrangThaiSuDungColumn = await HasTrangThaiSuDungColumnAsync(connection, transaction: null, cancellationToken);
            var trangThaiSuDungSelect = hasTrangThaiSuDungColumn
                ? "CAST(ISNULL(ct.TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung,"
                : "CAST(1 AS bit) AS TrangThaiSuDung,";
            var hasPhieuNhapChiTietColumn = await HasPhieuNhapChiTietColumnAsync(connection, transaction: null, cancellationToken);
            var phieuNhapChiTietSelect = hasPhieuNhapChiTietColumn
                ? "ct.IDPhieuNhapChiTiet,"
                : "CAST(NULL AS int) AS IDPhieuNhapChiTiet,";
            var hasDonViNhapColumn = await HasColumnAsync(connection, transaction: null, "TblChiTietHangHoa", "IDDonViNhap", cancellationToken);
            var donViNhapExpression = hasDonViNhapColumn ? "ct.IDDonViNhap" : "CAST(NULL AS int)";
            var donViNhapSelect = $"{donViNhapExpression} AS IDDonViNhap,";
            var donGiaBanLeSelect = await HasColumnAsync(connection, transaction: null, "TblChiTietHangHoa", "DonGiaBanLe", cancellationToken)
                ? "ct.DonGiaBanLe,"
                : "CAST(0 AS decimal(18,2)) AS DonGiaBanLe,";
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT TOP (1)
                    ct.ID,
                    {trangThaiSuDungSelect}
                    ct.IDKho,
                    ct.IDHangHoa,
                    ct.IDDonVinTinh,
                    {donViNhapSelect}
                    ct.TenChiTiet,
                    ct.QRCode,
                    ct.SoLuongTon,
                    {donGiaBanLeSelect}
                    ct.MaSoLo,
                    ct.LuuTaiKho,
                    ct.GhiChu,
                    ct.Image,
                    {phieuNhapChiTietSelect}
                    ct.Created_Date,
                    ct.Created_By,
                    ct.Updated_Date,
                    ct.Updated_By,
                    kho.TenKho,
                    kho.MaKho,
                    hh.TenHangHoa,
                    hh.MaHangHoa,
                    dvt.TenDonVi,
                    dvt.TenVietTat,
                    dvtNhap.TenDonVi AS TenDonViNhap,
                    dvtNhap.TenVietTat AS TenVietTatDonViNhap,
                    pn.ID AS IDPhieuNhapKho,
                    pn.MaPhieu AS MaPhieuNhap,
                    pxLatest.ID AS IDPhieuXuatKho,
                    pxLatest.MaPhieu AS MaPhieuXuat
                FROM [{TableName}] ct
                LEFT JOIN [TblKho] kho ON kho.ID = ct.IDKho
                LEFT JOIN [TblHangHoa] hh ON hh.ID = ct.IDHangHoa
                LEFT JOIN [TblDonViTinh] dvt ON dvt.ID = ct.IDDonVinTinh
                LEFT JOIN [TblDonViTinh] dvtNhap ON dvtNhap.ID = {donViNhapExpression}
                LEFT JOIN [TblPhieuNhapKhoChiTiet] pnct ON pnct.ID = ct.IDPhieuNhapChiTiet
                LEFT JOIN [TblPhieuNhapKho] pn ON pn.ID = pnct.IDPhieuNhapKho
                OUTER APPLY (
                    SELECT TOP (1)
                        px.ID,
                        px.MaPhieu
                    FROM [TblPhieuXuatKhoChiTiet] pxct
                    INNER JOIN [TblPhieuXuatKho] px ON px.ID = pxct.IDPhieuXuatKho
                    WHERE pxct.IDChiTietHangHoa = ct.ID
                    ORDER BY px.NgayXuatKho DESC, px.ID DESC
                ) pxLatest
                WHERE ct.ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var item = MapItem(reader);
            await reader.CloseAsync();

            item.Images = await LoadImagesAsync(connection, transaction: null, item.Id, item.ImageUrl, cancellationToken);
            item.ImageUrl = item.Images.FirstOrDefault(image => image.IsPrimary)?.ImageUrl
                ?? item.Images.FirstOrDefault()?.ImageUrl
                ?? item.ImageUrl;

            return item;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblChiTietHangHoa item {Id}.", id);
            return null;
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        VatTuFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var hasTrangThaiSuDungColumn = await HasTrangThaiSuDungColumnAsync(connection, transaction, cancellationToken);
            var createStatusColumns = hasTrangThaiSuDungColumn ? "TrangThaiSuDung," : string.Empty;
            var createStatusValues = hasTrangThaiSuDungColumn ? "@TrangThaiSuDung," : string.Empty;

            if (await IsQrCodeInUseAsync(connection, transaction, model.QRCode, excludingId: null, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Mã QR đang được sử dụng trên hệ thống. Vui lòng dùng mã khác.", null);
            }

            var finalImageUrls = BuildFinalImageUrls([], null, model.RemovedImageUrls, model.UploadedImageUrls);
            model.ImageUrl = ResolvePrimaryImageUrl(finalImageUrls, model.ImageUrl);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO [{TableName}] (
                    {createStatusColumns}
                    IDKho,
                    IDHangHoa,
                    IDDonVinTinh,
                    TenChiTiet,
                    QRCode,
                    SoLuongNhap,
                    SoLuongTon,
                    MaSoLo,
                    LuuTaiKho,
                    GhiChu,
                    Image,
                    NgayCapNhatQRCode,
                    NguoiCapNhatQRCode,
                    Created_Date,
                    Created_By,
                    Updated_Date,
                    Updated_By
                )
                VALUES (
                    {createStatusValues}
                    @IDKho,
                    @IDHangHoa,
                    @IDDonVinTinh,
                    @TenChiTiet,
                    @QRCode,
                    @SoLuongNhap,
                    @SoLuongTon,
                    @MaSoLo,
                    @LuuTaiKho,
                    @GhiChu,
                    @Image,
                    CASE WHEN NULLIF(LTRIM(RTRIM(@QRCode)), '') IS NULL THEN NULL ELSE GETDATE() END,
                    CASE WHEN NULLIF(LTRIM(RTRIM(@QRCode)), '') IS NULL THEN NULL ELSE @NguoiCapNhatQRCode END,
                    GETDATE(),
                    @CreatedBy,
                    GETDATE(),
                    @UpdatedBy
                );

                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;

            FillSaveParameters(command, model, setSoLuongNhap: true, includeTrangThaiSuDung: hasTrangThaiSuDungColumn);
            command.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
            command.Parameters.Add(new SqlParameter("@NguoiCapNhatQRCode", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
            command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });

            var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
            if (newId <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không thể thêm mới vật tư.", null);
            }

            await SyncImagesAsync(
                connection,
                transaction,
                newId,
                [],
                null,
                model.RemovedImageUrls,
                model.UploadedImageUrls,
                cancellationToken);

            await _commonAuditService.WriteAsync(
                connection,
                transaction,
                new CommonAuditEntry(
                    "VAT_TU",
                    "CREATE",
                    "VAT_TU",
                    newId.ToString(),
                    model.QRCode,
                    $"Thêm chi tiết vật tư {model.TenChiTiet}.",
                    currentUser,
                    Data: new
                    {
                        VatTuId = newId,
                        model.KhoId,
                        model.HangHoaId,
                        model.DonViTinhId,
                        model.TenChiTiet,
                        model.SoLuongTon,
                        model.MaSoLo,
                        model.ViTriLuuKho,
                        model.QRCode,
                        model.TrangThaiSuDung
                    }),
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return (true, null, newId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TblChiTietHangHoa.");
            return (false, "Không thể thêm mới vật tư lúc này.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        VatTuFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (model.Id is null or <= 0)
        {
            return (false, "Khong xac dinh duoc vat tu can cap nhat.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var hasTrangThaiSuDungColumn = await HasTrangThaiSuDungColumnAsync(connection, transaction, cancellationToken);
            var updateTrangThaiSuDungClause = hasTrangThaiSuDungColumn ? "TrangThaiSuDung = @TrangThaiSuDung," : string.Empty;
            var updateState = await LoadVatTuUpdateStateAsync(connection, transaction, model.Id.Value, cancellationToken);

            if (updateState is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Khong tim thay vat tu de cap nhat.");
            }

            if (updateState.IsZeroStock && !updateState.IsRelatedToDocument)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Vat tu da het ton nen chi duoc xem thong tin.");
            }

            if (await IsQrCodeInUseAsync(connection, transaction, model.QRCode, model.Id.Value, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Ma QR dang duoc su dung tren he thong. Vui long dung ma khac.");
            }

            if (updateState.IsRelatedToDocument)
            {
                await using var limitedCommand = connection.CreateCommand();
                limitedCommand.Transaction = transaction;
                limitedCommand.CommandText = $"""
                    UPDATE [{TableName}]
                    SET
                        {updateTrangThaiSuDungClause}
                        QRCode = @QRCode,
                        LuuTaiKho = @LuuTaiKho,
                        GhiChu = @GhiChu,
                        NgayCapNhatQRCode = CASE WHEN ISNULL(LTRIM(RTRIM(@QRCode)), '') <> ISNULL(LTRIM(RTRIM(QRCode)), '') THEN GETDATE() ELSE NgayCapNhatQRCode END,
                        NguoiCapNhatQRCode = CASE WHEN ISNULL(LTRIM(RTRIM(@QRCode)), '') <> ISNULL(LTRIM(RTRIM(QRCode)), '') THEN @NguoiCapNhatQRCode ELSE NguoiCapNhatQRCode END,
                        Updated_Date = GETDATE(),
                        Updated_By = @UpdatedBy
                    WHERE ID = @Id
                    """;
                limitedCommand.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id.Value });
                if (hasTrangThaiSuDungColumn)
                {
                    limitedCommand.Parameters.Add(new SqlParameter("@TrangThaiSuDung", SqlDbType.Bit) { Value = model.TrangThaiSuDung });
                }

                limitedCommand.Parameters.Add(new SqlParameter("@QRCode", SqlDbType.NVarChar, 50) { Value = ToDbValue(model.QRCode) });
                limitedCommand.Parameters.Add(new SqlParameter("@LuuTaiKho", SqlDbType.NVarChar, 250) { Value = ToDbValue(model.ViTriLuuKho) });
                limitedCommand.Parameters.Add(new SqlParameter("@GhiChu", SqlDbType.NVarChar, 550) { Value = ToDbValue(model.GhiChu) });
                limitedCommand.Parameters.Add(new SqlParameter("@NguoiCapNhatQRCode", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
                limitedCommand.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });

                var affectedRows = await limitedCommand.ExecuteNonQueryAsync(cancellationToken);
                if (affectedRows <= 0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return (false, "Khong tim thay vat tu de cap nhat.");
                }

                await _commonAuditService.WriteAsync(
                    connection,
                    transaction,
                    new CommonAuditEntry(
                        "VAT_TU",
                        "UPDATE_LIMITED",
                        "VAT_TU",
                        model.Id.Value.ToString(),
                        model.QRCode,
                        "Cap nhat thong tin cho phep cua vat tu da lien quan phieu.",
                        currentUser,
                        OldData: updateState,
                        NewData: new { model.QRCode, model.ViTriLuuKho, model.GhiChu, model.TrangThaiSuDung }),
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                return (true, null);
            }

            var currentImageUrls = await LoadStoredImageUrlsAsync(connection, transaction, model.Id.Value, cancellationToken);
            var currentMainImageUrl = await LoadMainImageUrlAsync(connection, transaction, model.Id.Value, cancellationToken);
            var legacyImageUrl = currentImageUrls.Count == 0 ? currentMainImageUrl : null;
            var finalImageUrls = BuildFinalImageUrls(currentImageUrls, legacyImageUrl, model.RemovedImageUrls, model.UploadedImageUrls);
            model.ImageUrl = ResolvePrimaryImageUrl(finalImageUrls, model.ImageUrl);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                UPDATE [{TableName}]
                SET
                    {updateTrangThaiSuDungClause}
                    IDKho = @IDKho,
                    IDHangHoa = @IDHangHoa,
                    IDDonVinTinh = @IDDonVinTinh,
                    TenChiTiet = @TenChiTiet,
                    QRCode = @QRCode,
                    SoLuongTon = @SoLuongTon,
                    MaSoLo = @MaSoLo,
                    LuuTaiKho = @LuuTaiKho,
                    GhiChu = @GhiChu,
                    Image = @Image,
                    NgayCapNhatQRCode = CASE WHEN ISNULL(LTRIM(RTRIM(@QRCode)), '') <> ISNULL(LTRIM(RTRIM(QRCode)), '') THEN GETDATE() ELSE NgayCapNhatQRCode END,
                    NguoiCapNhatQRCode = CASE WHEN ISNULL(LTRIM(RTRIM(@QRCode)), '') <> ISNULL(LTRIM(RTRIM(QRCode)), '') THEN @NguoiCapNhatQRCode ELSE NguoiCapNhatQRCode END,
                    Updated_Date = GETDATE(),
                    Updated_By = @UpdatedBy
                WHERE ID = @Id
                """;

            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id.Value });
            FillSaveParameters(command, model, setSoLuongNhap: false, includeTrangThaiSuDung: hasTrangThaiSuDungColumn);
            command.Parameters.Add(new SqlParameter("@NguoiCapNhatQRCode", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
            command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });

            var fullAffectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (fullAffectedRows <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Khong tim thay vat tu de cap nhat.");
            }

            await SyncImagesAsync(
                connection,
                transaction,
                model.Id.Value,
                currentImageUrls,
                legacyImageUrl,
                model.RemovedImageUrls,
                model.UploadedImageUrls,
                cancellationToken);

            await _commonAuditService.WriteAsync(
                connection,
                transaction,
                new CommonAuditEntry(
                    "VAT_TU",
                    "UPDATE",
                    "VAT_TU",
                    model.Id.Value.ToString(),
                    model.QRCode,
                    "Cap nhat chi tiet vat tu.",
                    currentUser,
                    OldData: updateState,
                    NewData: new
                    {
                        model.KhoId,
                        model.HangHoaId,
                        model.DonViTinhId,
                        model.TenChiTiet,
                        model.SoLuongTon,
                        model.MaSoLo,
                        model.ViTriLuuKho,
                        model.QRCode,
                        model.TrangThaiSuDung
                    }),
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update TblChiTietHangHoa {Id}.", model.Id);
            return (false, "Khong the cap nhat vat tu luc nay.");
        }
    }
    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return (false, "Không xác định được vật tư cần xóa.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            if (await IsInventoryLockedAsync(connection, transaction, id, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Vật tư đã hết tồn sau khi xuất kho nên không được xóa.");
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                DELETE FROM [{ImageTableName}]
                WHERE IDChiTietHangHoa = @Id;

                DELETE FROM [{TableName}]
                WHERE ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy vật tư để xóa.");
            }

            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete TblChiTietHangHoa {Id}.", id);
            return (false, "Không thể xóa vật tư lúc này.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int SourceCount, int CreatedCount)> CopyAsync(
        IReadOnlyCollection<int> sourceIds,
        int copyQuantity,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        var normalizedIds = (sourceIds ?? [])
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (normalizedIds.Length == 0)
        {
            return (false, "Vui lòng chọn ít nhất một vật tư để copy.", 0, 0);
        }

        if (copyQuantity <= 0)
        {
            return (false, "Số lượng copy phải lớn hơn 0.", 0, 0);
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var hasTrangThaiSuDungColumn = await HasTrangThaiSuDungColumnAsync(connection, transaction, cancellationToken);
            var sources = await LoadCopySourcesAsync(connection, transaction, normalizedIds, hasTrangThaiSuDungColumn, cancellationToken);

            if (sources.Count == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy vật tư để copy.", 0, 0);
            }

            var createdCount = 0;
            foreach (var source in sources)
            {
                for (var copyIndex = 0; copyIndex < copyQuantity; copyIndex++)
                {
                    var newId = await InsertCopyAsync(
                        connection,
                        transaction,
                        source,
                        currentUser,
                        hasTrangThaiSuDungColumn,
                        cancellationToken);

                    if (newId > 0)
                    {
                        createdCount++;
                    }
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return (true, null, sources.Count, createdCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy TblChiTietHangHoa items.");
            return (false, "Không thể copy vật tư lúc này.", 0, 0);
        }
    }

    public async Task<bool> IsQrCodeInUseAsync(
        string qrCode,
        int? excludingId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(qrCode))
        {
            return false;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            return await IsQrCodeInUseAsync(connection, transaction: null, qrCode, excludingId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check QRCode usage for TblChiTietHangHoa.");
            return false;
        }
    }

    public async Task<int?> FindIdByQrCodeAsync(
        string qrCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(qrCode))
        {
            return null;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT TOP (1) ID
                FROM [{TableName}]
                WHERE UPPER(LTRIM(RTRIM(QRCode))) = UPPER(@QRCode)
                ORDER BY ID DESC
                """;
            command.Parameters.Add(new SqlParameter("@QRCode", SqlDbType.NVarChar, 50) { Value = qrCode.Trim() });

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is null or DBNull ? null : Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find TblChiTietHangHoa by QRCode.");
            return null;
        }
    }

    public async Task<(IReadOnlyList<VatTuLookupOption> KhoOptions, IReadOnlyList<VatTuLookupOption> HangHoaOptions, IReadOnlyList<VatTuLookupOption> DonViTinhOptions)> GetLookupDataAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var khoOptions = await LoadKhoOptionsAsync(connection, cancellationToken);
            var hangHoaOptions = await LoadHangHoaOptionsAsync(connection, cancellationToken);
            var donViTinhOptions = await LoadDonViTinhOptionsAsync(connection, cancellationToken);
            return (khoOptions, hangHoaOptions, donViTinhOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load VatTu lookup data.");
            return ([], [], []);
        }
    }

    public async Task<IReadOnlyList<QrCodeAssignmentTargetItem>> SearchForQrAssignmentAsync(
        QrCodeAssignmentSearchModel model,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            var filters = new List<string> { "ISNULL(ct.SoLuongTon, 0) > 0" };
            var tenChiTiet = NormalizeKeyword(model.TenChiTiet);
            var viTriLuuKho = NormalizeKeyword(model.ViTriLuuKho);
            var maSoLo = NormalizeKeyword(model.MaSoLo);
            var maPhieuNhap = NormalizeKeyword(model.MaPhieuNhap);
            var hasPhieuNhapChiTietColumn = await HasPhieuNhapChiTietColumnAsync(connection, transaction: null, cancellationToken);
            var phieuNhapSelect = hasPhieuNhapChiTietColumn
                ? "pn.MaPhieu AS MaPhieuNhap,"
                : "CAST(NULL AS nvarchar(50)) AS MaPhieuNhap,";
            var phieuNhapJoin = hasPhieuNhapChiTietColumn
                ? """
                LEFT JOIN [TblPhieuNhapKhoChiTiet] pnct ON pnct.ID = ct.IDPhieuNhapChiTiet
                LEFT JOIN [TblPhieuNhapKho] pn ON pn.ID = pnct.IDPhieuNhapKho
                """
                : string.Empty;

            if (model.HangHoaId.HasValue && model.HangHoaId.Value > 0)
            {
                filters.Add("ct.IDHangHoa = @HangHoaId");
                command.Parameters.Add(new SqlParameter("@HangHoaId", SqlDbType.Int)
                {
                    Value = model.HangHoaId.Value
                });
            }

            if (!string.IsNullOrWhiteSpace(tenChiTiet))
            {
                filters.Add($"ct.TenChiTiet COLLATE {SearchCollation} LIKE @TenChiTiet");
                command.Parameters.Add(new SqlParameter("@TenChiTiet", SqlDbType.NVarChar, 250)
                {
                    Value = $"%{tenChiTiet}%"
                });
            }

            if (model.KhoId.HasValue && model.KhoId.Value > 0)
            {
                filters.Add("ct.IDKho = @KhoId");
                command.Parameters.Add(new SqlParameter("@KhoId", SqlDbType.Int)
                {
                    Value = model.KhoId.Value
                });
            }

            if (!string.IsNullOrWhiteSpace(viTriLuuKho))
            {
                filters.Add($"ct.LuuTaiKho COLLATE {SearchCollation} LIKE @ViTriLuuKho");
                command.Parameters.Add(new SqlParameter("@ViTriLuuKho", SqlDbType.NVarChar, 250)
                {
                    Value = $"%{viTriLuuKho}%"
                });
            }

            if (!string.IsNullOrWhiteSpace(maSoLo))
            {
                filters.Add($"ct.MaSoLo COLLATE {SearchCollation} LIKE @MaSoLo");
                command.Parameters.Add(new SqlParameter("@MaSoLo", SqlDbType.NVarChar, 50)
                {
                    Value = $"%{maSoLo}%"
                });
            }

            if (!string.IsNullOrWhiteSpace(maPhieuNhap))
            {
                if (!hasPhieuNhapChiTietColumn)
                {
                    return [];
                }

                filters.Add($"pn.MaPhieu COLLATE {SearchCollation} LIKE @MaPhieuNhap");
                command.Parameters.Add(new SqlParameter("@MaPhieuNhap", SqlDbType.NVarChar, 50)
                {
                    Value = $"%{maPhieuNhap}%"
                });
            }

            filters.Add("NULLIF(LTRIM(RTRIM(ct.QRCode)), '') IS NULL");

            command.CommandText = $"""
                SELECT
                    ct.ID,
                    hh.TenHangHoa,
                    ct.TenChiTiet,
                    kho.TenKho,
                    ct.LuuTaiKho,
                    ct.MaSoLo,
                    {phieuNhapSelect}
                    ct.QRCode
                FROM [{TableName}] ct
                LEFT JOIN [TblKho] kho ON kho.ID = ct.IDKho
                LEFT JOIN [TblHangHoa] hh ON hh.ID = ct.IDHangHoa
                {phieuNhapJoin}
                WHERE {string.Join(" AND ", filters)}
                ORDER BY
                    ISNULL(hh.TenHangHoa, '') ASC,
                    ISNULL(ct.TenChiTiet, '') ASC,
                    ISNULL(kho.TenKho, '') ASC,
                    ct.ID ASC
                """;

            var items = new List<QrCodeAssignmentTargetItem>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new QrCodeAssignmentTargetItem
                {
                    Id = reader.GetInt32(reader.GetOrdinal("ID")),
                    TenHangHoa = GetNullableString(reader, "TenHangHoa"),
                    TenChiTiet = GetNullableString(reader, "TenChiTiet") ?? string.Empty,
                    TenKho = GetNullableString(reader, "TenKho"),
                    ViTriLuuKho = GetNullableString(reader, "LuuTaiKho"),
                    MaSoLo = GetNullableString(reader, "MaSoLo"),
                    MaPhieuNhap = GetNullableString(reader, "MaPhieuNhap"),
                    QRCode = GetNullableString(reader, "QRCode")
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search VatTu items for QR assignment.");
            return [];
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> AssignQrCodeAsync(
        int itemId,
        string qrCode,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (itemId <= 0)
        {
            return (false, "Không xác định được vật tư cần gán QR.");
        }

        if (string.IsNullOrWhiteSpace(qrCode))
        {
            return (false, "Vui lòng cung cấp mã QR hợp lệ.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            var updateState = await LoadVatTuUpdateStateAsync(connection, transaction, itemId, cancellationToken);
            if (updateState is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Khong tim thay vat tu de gan QR.");
            }

            if (updateState.IsZeroStock && !updateState.IsRelatedToDocument)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Vat tu da het ton nen khong duoc gan hoac thay doi ma QR.");
            }

            if (await IsQrCodeInUseAsync(connection, transaction, qrCode, itemId, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Mã QR đang được sử dụng trên hệ thống. Vui lòng dùng mã khác.");
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                UPDATE [{TableName}]
                SET
                    QRCode = @QRCode,
                    NgayCapNhatQRCode = GETDATE(),
                    NguoiCapNhatQRCode = @NguoiCapNhatQRCode,
                    Updated_Date = GETDATE(),
                    Updated_By = @UpdatedBy
                WHERE ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = itemId });
            command.Parameters.Add(new SqlParameter("@QRCode", SqlDbType.NVarChar, 50) { Value = qrCode.Trim() });
            command.Parameters.Add(new SqlParameter("@NguoiCapNhatQRCode", SqlDbType.NVarChar, 50)
            {
                Value = TrimToLength(currentUser, 50)
            });
            command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50)
            {
                Value = TrimToLength(currentUser, 50)
            });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy vật tư để gán QR.");
            }

            await _commonAuditService.WriteAsync(
                connection,
                transaction,
                new CommonAuditEntry(
                    "VAT_TU",
                    "ASSIGN_QR",
                    "VAT_TU",
                    itemId.ToString(),
                    qrCode,
                    "Gan hoac cap nhat ma QR vat tu.",
                    currentUser,
                    OldData: updateState,
                    NewData: new { QRCode = qrCode }),
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assign QRCode to TblChiTietHangHoa {Id}.", itemId);
            return (false, "Không thể cập nhật mã QR cho vật tư lúc này.");
        }
    }

    private static VatTuListItem MapItem(SqlDataReader reader)
    {
        return new VatTuListItem
        {
            Id = reader.GetInt32(reader.GetOrdinal("ID")),
            TrangThaiSuDung = reader.GetBoolean(reader.GetOrdinal("TrangThaiSuDung")),
            KhoId = GetNullableInt32(reader, "IDKho"),
            TenKho = GetNullableString(reader, "TenKho"),
            MaKho = GetNullableString(reader, "MaKho"),
            HangHoaId = GetNullableInt32(reader, "IDHangHoa"),
            TenHangHoa = GetNullableString(reader, "TenHangHoa"),
            MaHangHoa = GetNullableString(reader, "MaHangHoa"),
            DonViTinhId = GetNullableInt32(reader, "IDDonVinTinh"),
            TenDonViTinh = GetNullableString(reader, "TenDonVi"),
            TenVietTatDonViTinh = GetNullableString(reader, "TenVietTat"),
            DonViNhapId = GetNullableInt32(reader, "IDDonViNhap"),
            TenDonViNhap = GetNullableString(reader, "TenDonViNhap"),
            TenVietTatDonViNhap = GetNullableString(reader, "TenVietTatDonViNhap"),
            TenChiTiet = GetNullableString(reader, "TenChiTiet") ?? string.Empty,
            SoLuongTon = GetNullableDecimal(reader, "SoLuongTon") ?? 0,
            DonGiaBanLe = GetNullableDecimal(reader, "DonGiaBanLe") ?? 0,
            MaSoLo = GetNullableString(reader, "MaSoLo"),
            ViTriLuuKho = GetNullableString(reader, "LuuTaiKho"),
            GhiChu = GetNullableString(reader, "GhiChu"),
            QRCode = GetNullableString(reader, "QRCode"),
            ImageUrl = GetNullableString(reader, "Image"),
            PhieuNhapChiTietId = GetNullableInt32(reader, "IDPhieuNhapChiTiet"),
            PhieuNhapId = GetNullableInt32(reader, "IDPhieuNhapKho"),
            MaPhieuNhap = GetNullableString(reader, "MaPhieuNhap"),
            PhieuXuatId = GetNullableInt32(reader, "IDPhieuXuatKho"),
            MaPhieuXuat = GetNullableString(reader, "MaPhieuXuat"),
            CreatedDate = GetNullableDateTime(reader, "Created_Date"),
            CreatedBy = GetNullableString(reader, "Created_By"),
            UpdatedDate = GetNullableDateTime(reader, "Updated_Date"),
            UpdatedBy = GetNullableString(reader, "Updated_By")
        };
    }

    private static async Task<VatTuUpdateState?> LoadVatTuUpdateStateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int itemId,
        CancellationToken cancellationToken)
    {
        var hasPhieuNhapChiTietColumn = await HasPhieuNhapChiTietColumnAsync(connection, transaction, cancellationToken);
        var phieuNhapChiTietSelect = hasPhieuNhapChiTietColumn
            ? "ct.IDPhieuNhapChiTiet"
            : "CAST(NULL AS int) AS IDPhieuNhapChiTiet";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT TOP (1)
                ct.ID,
                ct.IDKho,
                ct.IDHangHoa,
                ct.IDDonVinTinh,
                ct.TenChiTiet,
                ct.QRCode,
                ct.SoLuongTon,
                ct.MaSoLo,
                ct.LuuTaiKho,
                ct.GhiChu,
                CAST(ISNULL(ct.TrangThaiSuDung, 1) AS bit) AS TrangThaiSuDung,
                {phieuNhapChiTietSelect},
                pn.ID AS IDPhieuNhapKho,
                pn.MaPhieu AS MaPhieuNhap,
                pxLatest.ID AS IDPhieuXuatKho,
                pxLatest.MaPhieu AS MaPhieuXuat
            FROM [{TableName}] ct
            LEFT JOIN [TblPhieuNhapKhoChiTiet] pnct ON pnct.ID = ct.IDPhieuNhapChiTiet
            LEFT JOIN [TblPhieuNhapKho] pn ON pn.ID = pnct.IDPhieuNhapKho
            OUTER APPLY (
                SELECT TOP (1)
                    px.ID,
                    px.MaPhieu
                FROM [TblPhieuXuatKhoChiTiet] pxct
                INNER JOIN [TblPhieuXuatKho] px ON px.ID = pxct.IDPhieuXuatKho
                WHERE pxct.IDChiTietHangHoa = ct.ID
                ORDER BY px.NgayXuatKho DESC, px.ID DESC
            ) pxLatest
            WHERE ct.ID = @Id
            """;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = itemId });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var stock = GetNullableDecimal(reader, "SoLuongTon") ?? 0;
        var phieuNhapChiTietId = GetNullableInt32(reader, "IDPhieuNhapChiTiet");
        var phieuXuatId = GetNullableInt32(reader, "IDPhieuXuatKho");
        return new VatTuUpdateState
        {
            Id = itemId,
            KhoId = GetNullableInt32(reader, "IDKho"),
            HangHoaId = GetNullableInt32(reader, "IDHangHoa"),
            DonViTinhId = GetNullableInt32(reader, "IDDonVinTinh"),
            TenChiTiet = GetNullableString(reader, "TenChiTiet"),
            QRCode = GetNullableString(reader, "QRCode"),
            SoLuongTon = stock,
            MaSoLo = GetNullableString(reader, "MaSoLo"),
            ViTriLuuKho = GetNullableString(reader, "LuuTaiKho"),
            GhiChu = GetNullableString(reader, "GhiChu"),
            TrangThaiSuDung = reader.GetBoolean(reader.GetOrdinal("TrangThaiSuDung")),
            PhieuNhapChiTietId = phieuNhapChiTietId,
            PhieuNhapId = GetNullableInt32(reader, "IDPhieuNhapKho"),
            MaPhieuNhap = GetNullableString(reader, "MaPhieuNhap"),
            PhieuXuatId = phieuXuatId,
            MaPhieuXuat = GetNullableString(reader, "MaPhieuXuat"),
            IsZeroStock = stock <= 0,
            IsRelatedToDocument = phieuNhapChiTietId.HasValue || phieuXuatId.HasValue
        };
    }

    private static async Task<bool> IsQrCodeInUseAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string? qrCode,
        int? excludingId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(qrCode))
        {
            return false;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT TOP (1) 1
            FROM [{TableName}]
            WHERE UPPER(LTRIM(RTRIM(QRCode))) = UPPER(@QRCode)
              AND (@ExcludingId IS NULL OR ID <> @ExcludingId)
            """;
        command.Parameters.Add(new SqlParameter("@QRCode", SqlDbType.NVarChar, 50) { Value = qrCode.Trim() });
        command.Parameters.Add(new SqlParameter("@ExcludingId", SqlDbType.Int)
        {
            Value = excludingId.HasValue ? excludingId.Value : DBNull.Value
        });

        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<bool> IsInventoryLockedAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int itemId,
        CancellationToken cancellationToken)
    {
        var hasPhieuNhapChiTietColumn = await HasPhieuNhapChiTietColumnAsync(connection, transaction, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var phieuNhapChiTietSelect = hasPhieuNhapChiTietColumn
            ? "IDPhieuNhapChiTiet"
            : "CAST(NULL AS int) AS IDPhieuNhapChiTiet";
        command.CommandText = $"""
            SELECT TOP (1) SoLuongTon, {phieuNhapChiTietSelect}
            FROM [{TableName}]
            WHERE ID = @Id
            """;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = itemId });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return false;
        }

        var stock = GetNullableDecimal(reader, "SoLuongTon") ?? 0;
        var phieuNhapChiTietId = GetNullableInt32(reader, "IDPhieuNhapChiTiet");
        return stock <= 0 || phieuNhapChiTietId.HasValue;
    }

    private static List<string> BuildFinalImageUrls(
        IReadOnlyList<string> storedImageUrls,
        string? legacyImageUrl,
        IReadOnlyList<string>? removedImageUrls,
        IReadOnlyList<string>? uploadedImageUrls)
    {
        var removedSet = NormalizeImageUrls(removedImageUrls).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var finalImageUrls = new List<string>();

        foreach (var imageUrl in NormalizeImageUrls(storedImageUrls))
        {
            if (!removedSet.Contains(imageUrl))
            {
                finalImageUrls.Add(imageUrl);
            }
        }

        if (!string.IsNullOrWhiteSpace(legacyImageUrl))
        {
            var normalizedLegacy = legacyImageUrl.Trim();
            if (!removedSet.Contains(normalizedLegacy) &&
                !finalImageUrls.Contains(normalizedLegacy, StringComparer.OrdinalIgnoreCase))
            {
                finalImageUrls.Add(normalizedLegacy);
            }
        }

        foreach (var imageUrl in NormalizeImageUrls(uploadedImageUrls))
        {
            if (!finalImageUrls.Contains(imageUrl, StringComparer.OrdinalIgnoreCase))
            {
                finalImageUrls.Add(imageUrl);
            }
        }

        return finalImageUrls;
    }

    private static string? ResolvePrimaryImageUrl(
        IReadOnlyList<string> finalImageUrls,
        string? preferredImageUrl)
    {
        if (!string.IsNullOrWhiteSpace(preferredImageUrl))
        {
            var normalizedPreferred = preferredImageUrl.Trim();
            var matchedImageUrl = finalImageUrls.FirstOrDefault(imageUrl =>
                string.Equals(imageUrl, normalizedPreferred, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(matchedImageUrl))
            {
                return matchedImageUrl;
            }
        }

        return finalImageUrls.FirstOrDefault();
    }

    private static async Task<IReadOnlyList<VatTuCopySource>> LoadCopySourcesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyList<int> sourceIds,
        bool hasTrangThaiSuDungColumn,
        CancellationToken cancellationToken)
    {
        if (sourceIds.Count == 0)
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        var idParameters = new List<string>();
        for (var index = 0; index < sourceIds.Count; index++)
        {
            var parameterName = $"@SourceId{index}";
            idParameters.Add(parameterName);
            command.Parameters.Add(new SqlParameter(parameterName, SqlDbType.Int) { Value = sourceIds[index] });
        }

        var trangThaiSuDungSelect = hasTrangThaiSuDungColumn
            ? "CAST(ISNULL(TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung,"
            : "CAST(1 AS bit) AS TrangThaiSuDung,";

        command.CommandText = $"""
            SELECT
                ID,
                {trangThaiSuDungSelect}
                IDKho,
                IDHangHoa,
                IDDonVinTinh,
                TenChiTiet,
                SoLuongTon,
                MaSoLo,
                LuuTaiKho,
                GhiChu,
                Image
            FROM [{TableName}]
            WHERE ID IN ({string.Join(", ", idParameters)})
            ORDER BY ID ASC
            """;

        var items = new List<VatTuCopySource>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new VatTuCopySource
            {
                Id = reader.GetInt32(reader.GetOrdinal("ID")),
                TrangThaiSuDung = reader.GetBoolean(reader.GetOrdinal("TrangThaiSuDung")),
                KhoId = GetNullableInt32(reader, "IDKho"),
                HangHoaId = GetNullableInt32(reader, "IDHangHoa"),
                DonViTinhId = GetNullableInt32(reader, "IDDonVinTinh"),
                TenChiTiet = GetNullableString(reader, "TenChiTiet") ?? string.Empty,
                SoLuongTon = GetNullableDecimal(reader, "SoLuongTon") ?? 0,
                MaSoLo = GetNullableString(reader, "MaSoLo"),
                ViTriLuuKho = GetNullableString(reader, "LuuTaiKho"),
                GhiChu = GetNullableString(reader, "GhiChu"),
                ImageUrl = GetNullableString(reader, "Image")
            });
        }

        return items;
    }

    private static async Task<int> InsertCopyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        VatTuCopySource source,
        string currentUser,
        bool includeTrangThaiSuDung,
        CancellationToken cancellationToken)
    {
        var createStatusColumns = includeTrangThaiSuDung ? "TrangThaiSuDung," : string.Empty;
        var createStatusValues = includeTrangThaiSuDung ? "@TrangThaiSuDung," : string.Empty;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO [{TableName}] (
                {createStatusColumns}
                IDKho,
                IDHangHoa,
                IDDonVinTinh,
                TenChiTiet,
                QRCode,
                SoLuongNhap,
                SoLuongTon,
                MaSoLo,
                LuuTaiKho,
                GhiChu,
                Image,
                NgayCapNhatQRCode,
                NguoiCapNhatQRCode,
                Created_Date,
                Created_By,
                Updated_Date,
                Updated_By
            )
            VALUES (
                {createStatusValues}
                @IDKho,
                @IDHangHoa,
                @IDDonVinTinh,
                @TenChiTiet,
                NULL,
                @SoLuongNhap,
                @SoLuongTon,
                @MaSoLo,
                @LuuTaiKho,
                @GhiChu,
                @Image,
                NULL,
                NULL,
                GETDATE(),
                @CreatedBy,
                GETDATE(),
                @UpdatedBy
            );

            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;

        if (includeTrangThaiSuDung)
        {
            command.Parameters.Add(new SqlParameter("@TrangThaiSuDung", SqlDbType.Bit)
            {
                Value = source.TrangThaiSuDung
            });
        }

        command.Parameters.Add(new SqlParameter("@IDKho", SqlDbType.Int)
        {
            Value = source.KhoId.HasValue ? source.KhoId.Value : DBNull.Value
        });
        command.Parameters.Add(new SqlParameter("@IDHangHoa", SqlDbType.Int)
        {
            Value = source.HangHoaId.HasValue ? source.HangHoaId.Value : DBNull.Value
        });
        command.Parameters.Add(new SqlParameter("@IDDonVinTinh", SqlDbType.Int)
        {
            Value = source.DonViTinhId.HasValue ? source.DonViTinhId.Value : DBNull.Value
        });
        command.Parameters.Add(new SqlParameter("@TenChiTiet", SqlDbType.NVarChar, 250)
        {
            Value = string.IsNullOrWhiteSpace(source.TenChiTiet) ? string.Empty : source.TenChiTiet.Trim()
        });
        command.Parameters.Add(new SqlParameter("@SoLuongNhap", SqlDbType.Decimal)
        {
            Precision = 18,
            Scale = 2,
            Value = source.SoLuongTon
        });
        command.Parameters.Add(new SqlParameter("@SoLuongTon", SqlDbType.Decimal)
        {
            Precision = 18,
            Scale = 2,
            Value = source.SoLuongTon
        });
        command.Parameters.Add(new SqlParameter("@MaSoLo", SqlDbType.NVarChar, 50)
        {
            Value = ToDbValue(source.MaSoLo)
        });
        command.Parameters.Add(new SqlParameter("@LuuTaiKho", SqlDbType.NVarChar, 250)
        {
            Value = ToDbValue(source.ViTriLuuKho)
        });
        command.Parameters.Add(new SqlParameter("@GhiChu", SqlDbType.NVarChar, 550)
        {
            Value = ToDbValue(source.GhiChu)
        });
        command.Parameters.Add(new SqlParameter("@Image", SqlDbType.NVarChar, 550)
        {
            Value = ToDbValue(source.ImageUrl)
        });
        command.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50)
        {
            Value = TrimToLength(currentUser, 50)
        });
        command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50)
        {
            Value = TrimToLength(currentUser, 50)
        });

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private static string BuildWhereClause(IReadOnlyList<string> keywordTerms)
    {
        var filters = new List<string> { "ISNULL(ct.SoLuongTon, 0) > 0" };

        for (var index = 0; index < keywordTerms.Count; index++)
        {
            var parameterName = $"@Keyword{index}";
            filters.Add("""
                (
                    ct.TenChiTiet COLLATE {SearchCollation} LIKE {parameterName} OR
                    ct.MaSoLo COLLATE {SearchCollation} LIKE {parameterName} OR
                    ct.LuuTaiKho COLLATE {SearchCollation} LIKE {parameterName} OR
                    ct.GhiChu COLLATE {SearchCollation} LIKE {parameterName} OR
                    ct.QRCode COLLATE {SearchCollation} LIKE {parameterName} OR
                    kho.TenKho COLLATE {SearchCollation} LIKE {parameterName} OR
                    kho.MaKho COLLATE {SearchCollation} LIKE {parameterName} OR
                    hh.TenHangHoa COLLATE {SearchCollation} LIKE {parameterName} OR
                    hh.MaHangHoa COLLATE {SearchCollation} LIKE {parameterName} OR
                    dvt.TenDonVi COLLATE {SearchCollation} LIKE {parameterName} OR
                    dvt.TenVietTat COLLATE {SearchCollation} LIKE {parameterName} OR
                    pn.MaPhieu COLLATE {SearchCollation} LIKE {parameterName} OR
                    pxLatest.MaPhieu COLLATE {SearchCollation} LIKE {parameterName}
                )
                """
                .Replace("{parameterName}", parameterName)
                .Replace("{SearchCollation}", SearchCollation));
        }

        return string.Join(" AND ", filters);
    }

    private static void AddFilterParameters(SqlCommand command, IReadOnlyList<string> keywordTerms)
    {
        for (var index = 0; index < keywordTerms.Count; index++)
        {
            command.Parameters.Add(new SqlParameter($"@Keyword{index}", SqlDbType.NVarChar, 250)
            {
                Value = $"%{keywordTerms[index]}%"
            });
        }
    }

    private static IReadOnlyList<string> SplitKeywordTerms(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return [];
        }

        return keyword
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizeImageUrls(IEnumerable<string>? imageUrls)
    {
        if (imageUrls is null)
        {
            return [];
        }

        var items = new List<string>();
        foreach (var imageUrl in imageUrls)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                continue;
            }

            var normalized = imageUrl.Trim();
            if (!items.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                items.Add(normalized);
            }
        }

        return items;
    }

    private static void FillSaveParameters(SqlCommand command, VatTuFormModel model, bool setSoLuongNhap, bool includeTrangThaiSuDung)
    {
        if (includeTrangThaiSuDung)
        {
            command.Parameters.Add(new SqlParameter("@TrangThaiSuDung", SqlDbType.Bit)
            {
                Value = model.TrangThaiSuDung
            });
        }

        command.Parameters.Add(new SqlParameter("@IDKho", SqlDbType.Int)
        {
            Value = model.KhoId!.Value
        });
        command.Parameters.Add(new SqlParameter("@IDHangHoa", SqlDbType.Int)
        {
            Value = model.HangHoaId!.Value
        });
        command.Parameters.Add(new SqlParameter("@IDDonVinTinh", SqlDbType.Int)
        {
            Value = model.DonViTinhId!.Value
        });
        command.Parameters.Add(new SqlParameter("@TenChiTiet", SqlDbType.NVarChar, 250)
        {
            Value = model.TenChiTiet.Trim()
        });
        command.Parameters.Add(new SqlParameter("@QRCode", SqlDbType.NVarChar, 50)
        {
            Value = ToDbValue(model.QRCode)
        });

        if (setSoLuongNhap)
        {
            command.Parameters.Add(new SqlParameter("@SoLuongNhap", SqlDbType.Decimal)
            {
                Precision = 18,
                Scale = 2,
                Value = model.SoLuongTon
            });
        }

        command.Parameters.Add(new SqlParameter("@SoLuongTon", SqlDbType.Decimal)
        {
            Precision = 18,
            Scale = 2,
            Value = model.SoLuongTon
        });
        command.Parameters.Add(new SqlParameter("@MaSoLo", SqlDbType.NVarChar, 50)
        {
            Value = ToDbValue(model.MaSoLo)
        });
        command.Parameters.Add(new SqlParameter("@LuuTaiKho", SqlDbType.NVarChar, 250)
        {
            Value = ToDbValue(model.ViTriLuuKho)
        });
        command.Parameters.Add(new SqlParameter("@GhiChu", SqlDbType.NVarChar, 550)
        {
            Value = ToDbValue(model.GhiChu)
        });
        command.Parameters.Add(new SqlParameter("@Image", SqlDbType.NVarChar, 550)
        {
            Value = ToDbValue(model.ImageUrl)
        });
    }

    private static async Task<bool> HasTrangThaiSuDungColumnAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CASE
                WHEN COL_LENGTH('dbo.TblChiTietHangHoa', 'TrangThaiSuDung') IS NULL THEN 0
                ELSE 1
            END
            """;

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0) == 1;
    }

    private static async Task<bool> HasPhieuNhapChiTietColumnAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CASE
                WHEN COL_LENGTH('dbo.TblChiTietHangHoa', 'IDPhieuNhapChiTiet') IS NULL THEN 0
                ELSE 1
            END
            """;

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0) == 1;
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

    private static async Task<IReadOnlyList<VatTuLookupOption>> LoadHangHoaOptionsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        var items = new List<VatTuLookupOption>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                ID,
                TenHangHoa,
                MaHangHoa
            FROM [TblHangHoa]
            ORDER BY TenHangHoa ASC, ID ASC
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var tenHangHoa = GetNullableString(reader, "TenHangHoa") ?? $"Hàng hóa #{reader.GetInt32(reader.GetOrdinal("ID"))}";
            var maHangHoa = GetNullableString(reader, "MaHangHoa");
            items.Add(new VatTuLookupOption
            {
                Id = reader.GetInt32(reader.GetOrdinal("ID")),
                Label = string.IsNullOrWhiteSpace(maHangHoa) ? tenHangHoa : $"{tenHangHoa} ({maHangHoa})"
            });
        }

        return items;
    }

    private static async Task<IReadOnlyList<VatTuLookupOption>> LoadKhoOptionsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        var items = new List<VatTuLookupOption>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                ID,
                TenKho,
                MaKho
            FROM [TblKho]
            ORDER BY TenKho ASC, ID ASC
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var tenKho = GetNullableString(reader, "TenKho") ?? $"Kho #{reader.GetInt32(reader.GetOrdinal("ID"))}";
            var maKho = GetNullableString(reader, "MaKho");
            items.Add(new VatTuLookupOption
            {
                Id = reader.GetInt32(reader.GetOrdinal("ID")),
                Label = string.IsNullOrWhiteSpace(maKho) ? tenKho : $"{tenKho} ({maKho})"
            });
        }

        return items;
    }

    private static async Task<IReadOnlyList<VatTuLookupOption>> LoadDonViTinhOptionsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        var items = new List<VatTuLookupOption>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                ID,
                TenDonVi,
                TenVietTat
            FROM [TblDonViTinh]
            ORDER BY TenDonVi ASC, ID ASC
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var tenDonVi = GetNullableString(reader, "TenDonVi") ?? $"Đơn vị #{reader.GetInt32(reader.GetOrdinal("ID"))}";
            var tenVietTat = GetNullableString(reader, "TenVietTat");
            items.Add(new VatTuLookupOption
            {
                Id = reader.GetInt32(reader.GetOrdinal("ID")),
                Label = string.IsNullOrWhiteSpace(tenVietTat) ? tenDonVi : $"{tenDonVi} ({tenVietTat})"
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

    private static async Task<IReadOnlyList<VatTuImageItem>> LoadImagesAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int vatTuId,
        string? primaryImageUrl,
        CancellationToken cancellationToken)
    {
        var imageUrls = await LoadStoredImageUrlsAsync(connection, transaction, vatTuId, cancellationToken);
        var images = imageUrls
            .Select(imageUrl => new VatTuImageItem
            {
                ImageUrl = imageUrl,
                IsPrimary = string.Equals(imageUrl, primaryImageUrl, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();

        if (images.Count == 0 && !string.IsNullOrWhiteSpace(primaryImageUrl))
        {
            images.Add(new VatTuImageItem
            {
                ImageUrl = primaryImageUrl.Trim(),
                IsPrimary = true
            });
        }
        else if (images.Count > 0 && images.All(image => !image.IsPrimary))
        {
            images[0].IsPrimary = true;
        }

        return images;
    }

    private static async Task<List<string>> LoadStoredImageUrlsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int vatTuId,
        CancellationToken cancellationToken)
    {
        var items = new List<string>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT Image
            FROM [{ImageTableName}]
            WHERE IDChiTietHangHoa = @VatTuId
            ORDER BY ID ASC
            """;
        command.Parameters.Add(new SqlParameter("@VatTuId", SqlDbType.Int) { Value = vatTuId });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var imageUrl = GetNullableString(reader, "Image");
            if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                items.Add(imageUrl.Trim());
            }
        }

        return items;
    }

    private static async Task<string?> LoadMainImageUrlAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int vatTuId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT TOP (1) Image
            FROM [{TableName}]
            WHERE ID = @VatTuId
            """;
        command.Parameters.Add(new SqlParameter("@VatTuId", SqlDbType.Int) { Value = vatTuId });

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result == null || result == DBNull.Value
            ? null
            : result.ToString()?.Trim();
    }

    private static async Task SyncImagesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int vatTuId,
        IReadOnlyList<string> storedImageUrls,
        string? legacyImageUrl,
        IReadOnlyList<string>? removedImageUrls,
        IReadOnlyList<string>? uploadedImageUrls,
        CancellationToken cancellationToken)
    {
        var removedSet = NormalizeImageUrls(removedImageUrls).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var storedSet = NormalizeImageUrls(storedImageUrls).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var urlsToInsert = new List<string>();

        if (removedSet.Count > 0)
        {
            await using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.Parameters.Add(new SqlParameter("@VatTuId", SqlDbType.Int) { Value = vatTuId });

            var parameterNames = new List<string>();
            var index = 0;
            foreach (var imageUrl in removedSet)
            {
                var parameterName = $"@DeleteImage{index++}";
                parameterNames.Add(parameterName);
                deleteCommand.Parameters.Add(new SqlParameter(parameterName, SqlDbType.NVarChar, 550) { Value = imageUrl });
            }

            deleteCommand.CommandText = $"""
                DELETE FROM [{ImageTableName}]
                WHERE IDChiTietHangHoa = @VatTuId
                  AND Image IN ({string.Join(", ", parameterNames)})
                """;
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(legacyImageUrl))
        {
            var normalizedLegacy = legacyImageUrl.Trim();
            if (!removedSet.Contains(normalizedLegacy) &&
                !storedSet.Contains(normalizedLegacy) &&
                !urlsToInsert.Contains(normalizedLegacy, StringComparer.OrdinalIgnoreCase))
            {
                urlsToInsert.Add(normalizedLegacy);
            }
        }

        foreach (var imageUrl in NormalizeImageUrls(uploadedImageUrls))
        {
            if (!storedSet.Contains(imageUrl) &&
                !urlsToInsert.Contains(imageUrl, StringComparer.OrdinalIgnoreCase))
            {
                urlsToInsert.Add(imageUrl);
            }
        }

        foreach (var imageUrl in urlsToInsert)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = $"""
                INSERT INTO [{ImageTableName}] (
                    IDChiTietHangHoa,
                    Image
                )
                VALUES (
                    @VatTuId,
                    @Image
                )
                """;
            insertCommand.Parameters.Add(new SqlParameter("@VatTuId", SqlDbType.Int) { Value = vatTuId });
            insertCommand.Parameters.Add(new SqlParameter("@Image", SqlDbType.NVarChar, 550) { Value = imageUrl });
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string? NormalizeKeyword(string? keyword)
    {
        return string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
    }

    private static string NormalizeQrStatusFilter(string? qrStatus)
    {
        var normalized = qrStatus?.Trim().ToLowerInvariant();
        return normalized switch
        {
            QrCodeAssignmentQrStatus.HasQr => QrCodeAssignmentQrStatus.HasQr,
            QrCodeAssignmentQrStatus.MissingQr => QrCodeAssignmentQrStatus.MissingQr,
            _ => QrCodeAssignmentQrStatus.All
        };
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

    private sealed class VatTuCopySource
    {
        public int Id { get; set; }
        public bool TrangThaiSuDung { get; set; }
        public int? KhoId { get; set; }
        public int? HangHoaId { get; set; }
        public int? DonViTinhId { get; set; }
        public string TenChiTiet { get; set; } = string.Empty;
        public decimal SoLuongTon { get; set; }
        public string? MaSoLo { get; set; }
        public string? ViTriLuuKho { get; set; }
        public string? GhiChu { get; set; }
        public string? ImageUrl { get; set; }
    }

    private sealed class VatTuUpdateState
    {
        public int Id { get; set; }
        public int? KhoId { get; set; }
        public int? HangHoaId { get; set; }
        public int? DonViTinhId { get; set; }
        public string? TenChiTiet { get; set; }
        public string? QRCode { get; set; }
        public decimal SoLuongTon { get; set; }
        public string? MaSoLo { get; set; }
        public string? ViTriLuuKho { get; set; }
        public string? GhiChu { get; set; }
        public bool TrangThaiSuDung { get; set; }
        public int? PhieuNhapChiTietId { get; set; }
        public int? PhieuNhapId { get; set; }
        public string? MaPhieuNhap { get; set; }
        public int? PhieuXuatId { get; set; }
        public string? MaPhieuXuat { get; set; }
        public bool IsZeroStock { get; set; }
        public bool IsRelatedToDocument { get; set; }
    }
}
