using System.Data;
using System.Text;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IHangHoaService
{
    Task<(IReadOnlyList<HangHoaListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        bool? statusFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<HangHoaListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HangHoaPhanLoaiModel>> GetPhanLoaiAsync(int hangHoaId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HangHoaLookupOption>> GetDonViTinhOptionsAsync(CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        HangHoaFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        HangHoaFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default);

    Task<HangHoaImportResult> ImportAsync(
        IReadOnlyList<HangHoaImportRow> rows,
        string currentUser,
        CancellationToken cancellationToken = default);
}

public sealed class HangHoaService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<HangHoaService> logger) : IHangHoaService
{
    private const string TableName = "TblHangHoa";
    private const string PhanLoaiTableName = "TblHangHoaPhanLoai";
    private const string DonViTinhTableName = "TblDonViTinh";
    private const string KhoTableName = "TblKho";
    private const string ChiTietHangHoaTableName = "TblChiTietHangHoa";
    private const string NhapKhoHeaderTableName = "TblPhieuNhapKho";
    private const string NhapKhoDetailTableName = "TblPhieuNhapKhoChiTiet";
    private const string DefaultDonViTinhType = "DV";
    private const string SearchCollation = "Latin1_General_100_CI_AI";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<HangHoaService> _logger = logger;
    private readonly int _taoNhapKhoMode = Math.Clamp(configuration.GetValue<int?>("TaoNhapKho") ?? 0, 0, 2);

    public async Task<(IReadOnlyList<HangHoaListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
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
            await EnsureHangHoaSchemaAsync(connection, null, cancellationToken);
            var normalizedKeyword = NormalizeKeyword(keyword);
            var whereClause = BuildWhereClause(normalizedKeyword, statusFilter, "hh");
            var donViTinhColumnName = await ResolveDonViTinhColumnNameAsync(connection, null, cancellationToken);
            var donViTinhSelect = donViTinhColumnName is null ? "CAST(NULL AS int) AS IDDonViTinh," : $"hh.[{donViTinhColumnName}] AS IDDonViTinh,";
            var tenDonViSelect = donViTinhColumnName is null ? "CAST(NULL AS nvarchar(300)) AS TenDonVi," : "dvt.TenDonVi,";
            var tenVietTatSelect = donViTinhColumnName is null ? "CAST(NULL AS nvarchar(40)) AS TenVietTat," : "dvt.TenVietTat,";
            var donViTinhJoin = donViTinhColumnName is null
                ? string.Empty
                : $"LEFT JOIN [TblDonViTinh] dvt ON dvt.ID = hh.[{donViTinhColumnName}]";

            await using var countCommand = connection.CreateCommand();
            countCommand.CommandText = $"""
                SELECT COUNT(1)
                FROM [{TableName}] hh
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
                    hh.ID,
                    hh.TenHangHoa,
                    hh.MaHangHoa,
                    hh.LoaiHinhNhap,
                    {donViTinhSelect}
                    {tenDonViSelect}
                    {tenVietTatSelect}
                    hh.Image,
                    CAST(ISNULL(hh.TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung,
                    hh.Created_Date,
                    hh.Created_By,
                    hh.Updated_Date,
                    hh.Updated_By
                FROM [{TableName}] hh
                {donViTinhJoin}
                WHERE {whereClause}
                ORDER BY hh.ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                """;
            AddFilterParameters(listCommand, normalizedKeyword, statusFilter);
            listCommand.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = offset });
            listCommand.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = pageSize });

            var items = new List<HangHoaListItem>();
            await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapItem(reader));
            }

            return (items, totalCount, currentPage, totalPages, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblHangHoa list.");
            return ([], 0, 1, 0, pageSize);
        }
    }

    public async Task<HangHoaListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return null;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureHangHoaSchemaAsync(connection, null, cancellationToken);
            var donViTinhColumnName = await ResolveDonViTinhColumnNameAsync(connection, null, cancellationToken);
            var donViTinhSelect = donViTinhColumnName is null ? "CAST(NULL AS int) AS IDDonViTinh," : $"hh.[{donViTinhColumnName}] AS IDDonViTinh,";
            var tenDonViSelect = donViTinhColumnName is null ? "CAST(NULL AS nvarchar(300)) AS TenDonVi," : "dvt.TenDonVi,";
            var tenVietTatSelect = donViTinhColumnName is null ? "CAST(NULL AS nvarchar(40)) AS TenVietTat," : "dvt.TenVietTat,";
            var donViTinhJoin = donViTinhColumnName is null
                ? string.Empty
                : $"LEFT JOIN [TblDonViTinh] dvt ON dvt.ID = hh.[{donViTinhColumnName}]";
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT TOP (1)
                    hh.ID,
                    hh.TenHangHoa,
                    hh.MaHangHoa,
                    hh.LoaiHinhNhap,
                    {donViTinhSelect}
                    {tenDonViSelect}
                    {tenVietTatSelect}
                    hh.Image,
                    CAST(ISNULL(hh.TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung,
                    hh.Created_Date,
                    hh.Created_By,
                    hh.Updated_Date,
                    hh.Updated_By
                FROM [{TableName}] hh
                {donViTinhJoin}
                WHERE hh.ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? MapItem(reader) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblHangHoa item {Id}.", id);
            return null;
        }
    }

    public async Task<IReadOnlyList<HangHoaLookupOption>> GetDonViTinhOptionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    ID,
                    TenDonVi,
                    TenVietTat
                FROM [TblDonViTinh]
                WHERE ISNULL(TrangThaiSuDung, 1) = 1
                ORDER BY TenDonVi ASC, ID ASC
                """;

            var items = new List<HangHoaLookupOption>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tenDonVi = GetNullableString(reader, "TenDonVi") ?? $"#{reader.GetInt32(reader.GetOrdinal("ID"))}";
                var tenVietTat = GetNullableString(reader, "TenVietTat");
                items.Add(new HangHoaLookupOption
                {
                    Id = reader.GetInt32(reader.GetOrdinal("ID")),
                    Label = string.IsNullOrWhiteSpace(tenVietTat) ? tenDonVi : $"{tenDonVi} ({tenVietTat})"
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load HangHoa don vi tinh lookup.");
            return [];
        }
    }

    public async Task<IReadOnlyList<HangHoaPhanLoaiModel>> GetPhanLoaiAsync(int hangHoaId, CancellationToken cancellationToken = default)
    {
        if (hangHoaId <= 0)
        {
            return [];
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsurePhanLoaiTableAsync(connection, null, cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT
                    ID,
                    TenPhanLoai,
                    CAST(ISNULL(TrangThaiSuDung, 1) AS bit) AS TrangThaiSuDung
                FROM [{PhanLoaiTableName}]
                WHERE IDHangHoa = @IDHangHoa
                ORDER BY ID ASC
                """;
            command.Parameters.Add(new SqlParameter("@IDHangHoa", SqlDbType.Int) { Value = hangHoaId });

            var items = new List<HangHoaPhanLoaiModel>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new HangHoaPhanLoaiModel
                {
                    Id = reader.GetInt32(reader.GetOrdinal("ID")),
                    TenPhanLoai = GetNullableString(reader, "TenPhanLoai"),
                    TrangThaiSuDung = reader.GetBoolean(reader.GetOrdinal("TrangThaiSuDung"))
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblHangHoaPhanLoai for TblHangHoa {Id}.", hangHoaId);
            return [];
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        HangHoaFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureHangHoaSchemaAsync(connection, null, cancellationToken);
            var duplicateError = await ValidateDuplicateNameAsync(connection, model.TenHangHoa, null, cancellationToken);
            if (duplicateError is not null)
            {
                return (false, duplicateError, null);
            }

            var donViTinhColumnName = await ResolveDonViTinhColumnNameAsync(connection, null, cancellationToken);
            var donViTinhColumn = donViTinhColumnName is null ? string.Empty : $"[{donViTinhColumnName}],";
            var donViTinhValue = donViTinhColumnName is null ? string.Empty : "@IDDonViTinh,";

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                INSERT INTO [{TableName}] (
                    TenHangHoa,
                    MaHangHoa,
                    LoaiHinhNhap,
                    {donViTinhColumn}
                    Image,
                    TrangThaiSuDung,
                    Created_Date,
                    Created_By,
                    Updated_Date,
                    Updated_By
                )
                VALUES (
                    @TenHangHoa,
                    @MaHangHoa,
                    @LoaiHinhNhap,
                    {donViTinhValue}
                    @Image,
                    @TrangThaiSuDung,
                    GETDATE(),
                    @CreatedBy,
                    GETDATE(),
                    @UpdatedBy
                );

                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;

            FillSaveParameters(command, model, donViTinhColumnName is not null);
            command.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });
            command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });

            var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
            if (newId > 0)
            {
                await SyncPhanLoaiAsync(connection, null, newId, model.PhanLoai, cancellationToken);
            }

            return newId > 0
                ? (true, null, newId)
                : (false, "Không thể thêm mới hàng hóa.", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TblHangHoa.");
            return (false, "Không thể thêm mới hàng hóa lúc này.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        HangHoaFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (model.Id is null or <= 0)
        {
            return (false, "Không xác định được hàng hóa cần cập nhật.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await EnsureHangHoaSchemaAsync(connection, null, cancellationToken);
            var duplicateError = await ValidateDuplicateNameAsync(connection, model.TenHangHoa, model.Id, cancellationToken);
            if (duplicateError is not null)
            {
                return (false, duplicateError);
            }

            var donViTinhColumnName = await ResolveDonViTinhColumnNameAsync(connection, null, cancellationToken);
            var donViTinhSet = donViTinhColumnName is null ? string.Empty : $"[{donViTinhColumnName}] = @IDDonViTinh,";

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                UPDATE [{TableName}]
                SET
                    TenHangHoa = @TenHangHoa,
                    MaHangHoa = @MaHangHoa,
                    LoaiHinhNhap = @LoaiHinhNhap,
                    {donViTinhSet}
                    Image = @Image,
                    TrangThaiSuDung = @TrangThaiSuDung,
                    Updated_Date = GETDATE(),
                    Updated_By = @UpdatedBy
                WHERE ID = @Id
                """;

            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id.Value });
            FillSaveParameters(command, model, donViTinhColumnName is not null);
            command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentUser, 50) });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            if (affectedRows > 0)
            {
                await SyncPhanLoaiAsync(connection, null, model.Id.Value, model.PhanLoai, cancellationToken);
            }

            return affectedRows > 0
                ? (true, null)
                : (false, "Không tìm thấy hàng hóa để cập nhật.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update TblHangHoa {Id}.", model.Id);
            return (false, "Không thể cập nhật hàng hóa lúc này.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return (false, "Không xác định được hàng hóa cần xóa.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF OBJECT_ID(N'dbo.{PhanLoaiTableName}', N'U') IS NOT NULL
                BEGIN
                    DELETE FROM [{PhanLoaiTableName}]
                    WHERE IDHangHoa = @Id;
                END;

                DELETE FROM [{TableName}]
                WHERE ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
            return affectedRows > 0
                ? (true, null)
                : (false, "Không tìm thấy hàng hóa để xóa.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete TblHangHoa {Id}.", id);
            return (false, "Không thể xóa hàng hóa lúc này.");
        }
    }

    public async Task<HangHoaImportResult> ImportAsync(
        IReadOnlyList<HangHoaImportRow> rows,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        var result = new HangHoaImportResult();
        if (rows.Count == 0)
        {
            return result;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await EnsureHangHoaSchemaAsync(connection, transaction, cancellationToken);
            await EnsurePhanLoaiTableAsync(connection, transaction, cancellationToken);
            await EnsureChiTietHangHoaImportSchemaAsync(connection, transaction, cancellationToken);
            if (_taoNhapKhoMode is 1 or 2)
            {
                await EnsureNhapKhoImportSchemaAsync(connection, transaction, cancellationToken);
            }

            var donViTinhColumnName = await ResolveDonViTinhColumnNameAsync(connection, transaction, cancellationToken);
            var currentAuditUser = TrimToLength(currentUser, 50);
            var donViTinhLookup = await LoadDonViTinhLookupAsync(connection, transaction, cancellationToken);
            var khoLookup = await LoadKhoLookupAsync(connection, transaction, cancellationToken);
            var hangHoaLookup = await LoadHangHoaByCodeLookupAsync(connection, transaction, cancellationToken);
            var phanLoaiLookup = await LoadPhanLoaiLookupAsync(connection, transaction, cancellationToken);
            var chiTietKeys = await LoadChiTietHangHoaKeysAsync(connection, transaction, cancellationToken);
            var failedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nhapKhoDetails = new List<HangHoaImportNhapKhoDetail>();

            foreach (var row in rows)
            {
                try
                {
                    var tenHangHoa = NormalizeComparisonKey(row.TenHangHoa);
                    var maHangHoa = NormalizeComparisonKey(row.MaHangHoa);
                    var tenKho = NormalizeComparisonKey(row.TenKho);
                    var tenDonViTinh = NormalizeComparisonKey(row.DonViTinh);
                    var tenPhanLoai = NormalizeComparisonKey(row.TenPhanLoai);
                    var tenChiTiet = NormalizeComparisonKey(row.TenChiTiet);

                    if (maHangHoa is null)
                    {
                        AddImportRowResult(result, row, false, true, "Loi", "Thieu MSP.");
                        failedCodes.Add($"Dong {row.RowNumber}");
                        continue;
                    }

                    if (tenHangHoa is null)
                    {
                        AddImportRowResult(result, row, false, true, "Loi", "Thieu Ten SP.");
                        failedCodes.Add(maHangHoa);
                        continue;
                    }

                    if (tenDonViTinh is null)
                    {
                        AddImportRowResult(result, row, false, true, "Loi", "Thieu DVT.");
                        failedCodes.Add(maHangHoa);
                        continue;
                    }

                    if (tenKho is null)
                    {
                        AddImportRowResult(result, row, false, true, "Loi", "Thieu Ten Kho.");
                        failedCodes.Add(maHangHoa);
                        continue;
                    }

                    if (tenChiTiet is null)
                    {
                        AddImportRowResult(result, row, false, true, "Loi", "Thieu Chi tiet SP.");
                        failedCodes.Add(maHangHoa);
                        continue;
                    }

                    if (!row.TonKhoDauKy.HasValue)
                    {
                        AddImportRowResult(result, row, false, true, "Loi", "Ton Kho dau ky khong hop le.");
                        failedCodes.Add(maHangHoa);
                        continue;
                    }

                    var resolvedDonViTinhId = await ResolveOrCreateDonViTinhIdAsync(connection, transaction, donViTinhLookup, tenDonViTinh, currentUser, cancellationToken);
                    if (!resolvedDonViTinhId.HasValue || resolvedDonViTinhId.Value <= 0)
                    {
                        AddImportRowResult(result, row, false, true, "Loi", "Khong lay duoc DVT.");
                        failedCodes.Add(maHangHoa);
                        continue;
                    }

                    var donViTinhId = resolvedDonViTinhId.Value;
                    var khoId = await ResolveOrCreateKhoIdAsync(connection, transaction, khoLookup, tenKho, currentAuditUser, cancellationToken);
                    var hangHoaId = await ResolveOrCreateHangHoaIdAsync(connection, transaction, donViTinhColumnName, hangHoaLookup, maHangHoa, tenHangHoa, donViTinhId, currentAuditUser, cancellationToken);
                    await UpdateHangHoaImportNameAsync(connection, transaction, hangHoaId, tenHangHoa, currentAuditUser, cancellationToken);
                    var phanLoaiId = tenPhanLoai is null
                        ? (int?)null
                        : await ResolveOrCreatePhanLoaiIdAsync(connection, transaction, phanLoaiLookup, hangHoaId, tenPhanLoai, cancellationToken);
                    if (phanLoaiId.HasValue)
                    {
                        await UpdatePhanLoaiImportNameAsync(connection, transaction, phanLoaiId.Value, tenPhanLoai!, cancellationToken);
                    }

                    var chiTietKey = BuildChiTietHangHoaKey(hangHoaId, phanLoaiId, donViTinhId, khoId);
                    if (chiTietKeys.TryGetValue(chiTietKey, out var existingChiTietId))
                    {
                        await UpdateChiTietHangHoaImportNameAsync(connection, transaction, existingChiTietId, tenChiTiet, currentAuditUser, cancellationToken);
                        if (_taoNhapKhoMode == 2)
                        {
                            AddNhapKhoImportDetail(nhapKhoDetails, hangHoaId, phanLoaiId, donViTinhId, khoId, row.TonKhoDauKy.Value);
                        }

                        AddImportRowResult(result, row, true, false, "Cap nhat", "Sua thong tin ten hang hoa, ten phan loai va ten chi tiet hang hoa.");
                        continue;
                    }

                    var chiTietHangHoaId = await InsertChiTietHangHoaFromImportAsync(
                        connection,
                        transaction,
                        hangHoaId,
                        phanLoaiId,
                        donViTinhId,
                        khoId,
                        tenChiTiet,
                        row.TonKhoDauKy.Value,
                        currentAuditUser,
                        cancellationToken);
                    chiTietKeys[chiTietKey] = chiTietHangHoaId;
                    if (_taoNhapKhoMode is 1 or 2)
                    {
                        AddNhapKhoImportDetail(nhapKhoDetails, hangHoaId, phanLoaiId, donViTinhId, khoId, row.TonKhoDauKy.Value);
                    }

                    AddImportRowResult(result, row, true, false, "Thanh cong", "Da them/lien ket du lieu import.");
                }
                catch (Exception rowEx)
                {
                    _logger.LogWarning(rowEx, "Failed to import HangHoa row {RowNumber} ({Code}).", row.RowNumber, row.MaHangHoa);
                    AddImportRowResult(result, row, false, true, "Loi", rowEx.Message);
                    if (!string.IsNullOrWhiteSpace(row.MaHangHoa))
                    {
                        failedCodes.Add(row.MaHangHoa.Trim());
                    }
                }
            }

            if (nhapKhoDetails.Count > 0)
            {
                await CreateNhapKhoPhieuFromImportAsync(connection, transaction, nhapKhoDetails, currentAuditUser, cancellationToken);
            }

            result.FailedCodes = failedCodes.OrderBy(code => code, StringComparer.OrdinalIgnoreCase).ToList();
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import TblHangHoa from Excel.");
            foreach (var row in rows)
            {
                AddImportRowResult(result, row, false, true, "Loi", "Khong the import file vao database.");
            }

            return result;
        }
    }

    private static void AddImportRowResult(
        HangHoaImportResult result,
        HangHoaImportRow row,
        bool succeeded,
        bool skipped,
        string status,
        string note)
    {
        result.Rows.Add(new HangHoaImportRowResult
        {
            Row = row,
            Succeeded = succeeded,
            Skipped = skipped,
            Result = status,
            Note = note
        });

        if (succeeded)
        {
            result.ImportedCount++;
        }
        else
        {
            result.SkippedCount++;
        }
    }

    private static async Task<Dictionary<string, int>> LoadKhoLookupAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT ID, TenKho
            FROM [{KhoTableName}]
            WHERE TenKho IS NOT NULL AND LTRIM(RTRIM(TenKho)) <> ''
            ORDER BY ID ASC
            """;

        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            AddLookupKey(lookup, GetNullableString(reader, "TenKho"), reader.GetInt32(reader.GetOrdinal("ID")));
        }

        return lookup;
    }

    private static async Task<Dictionary<string, int>> LoadHangHoaByCodeLookupAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT ID, MaHangHoa
            FROM [{TableName}]
            WHERE MaHangHoa IS NOT NULL AND LTRIM(RTRIM(MaHangHoa)) <> ''
            ORDER BY ID ASC
            """;

        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            AddLookupKey(lookup, GetNullableString(reader, "MaHangHoa"), reader.GetInt32(reader.GetOrdinal("ID")));
        }

        return lookup;
    }

    private static async Task<Dictionary<string, int>> LoadPhanLoaiLookupAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT ID, IDHangHoa, TenPhanLoai
            FROM [{PhanLoaiTableName}]
            WHERE TenPhanLoai IS NOT NULL AND LTRIM(RTRIM(TenPhanLoai)) <> ''
            ORDER BY ID ASC
            """;

        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = BuildPhanLoaiKey(reader.GetInt32(reader.GetOrdinal("IDHangHoa")), GetNullableString(reader, "TenPhanLoai"));
            if (key is not null && !lookup.ContainsKey(key))
            {
                lookup[key] = reader.GetInt32(reader.GetOrdinal("ID"));
            }
        }

        return lookup;
    }

    private static async Task<Dictionary<string, int>> LoadChiTietHangHoaKeysAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                ID,
                IDHangHoa,
                IDPhanLoaiHangHoa,
                IDDonVinTinh,
                IDKho
            FROM [{ChiTietHangHoaTableName}]
            """;

        var keys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt32(reader.GetOrdinal("ID"));
            var hangHoaId = GetNullableInt32(reader, "IDHangHoa") ?? 0;
            var phanLoaiId = GetNullableInt32(reader, "IDPhanLoaiHangHoa");
            var donViTinhId = GetNullableInt32(reader, "IDDonVinTinh") ?? 0;
            var khoId = GetNullableInt32(reader, "IDKho") ?? 0;
            if (hangHoaId > 0 && donViTinhId > 0 && khoId > 0)
            {
                var key = BuildChiTietHangHoaKey(hangHoaId, phanLoaiId, donViTinhId, khoId);
                if (!keys.ContainsKey(key))
                {
                    keys[key] = id;
                }
            }
        }

        return keys;
    }

    private static async Task<int> ResolveOrCreateKhoIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IDictionary<string, int> khoLookup,
        string tenKho,
        string currentAuditUser,
        CancellationToken cancellationToken)
    {
        var key = NormalizeComparisonKey(tenKho)
            ?? throw new InvalidOperationException("Ten Kho khong hop le.");
        if (khoLookup.TryGetValue(key, out var existingId))
        {
            return existingId;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO [{KhoTableName}] (
                TenKho,
                MaKho,
                TrangThaiSuDung,
                Created_Date,
                Created_By,
                Updated_Date,
                Updated_By
            )
            VALUES (
                @TenKho,
                NULL,
                1,
                GETDATE(),
                @CreatedBy,
                GETDATE(),
                @UpdatedBy
            );

            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;
        command.Parameters.Add(new SqlParameter("@TenKho", SqlDbType.NVarChar, 300) { Value = TrimToLength(key, 300) });
        command.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentAuditUser, 100) });
        command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentAuditUser, 100) });

        var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
        if (newId <= 0)
        {
            throw new InvalidOperationException("Khong the tao moi kho.");
        }

        khoLookup[key] = newId;
        return newId;
    }

    private static async Task<int> ResolveOrCreateHangHoaIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string? donViTinhColumnName,
        IDictionary<string, int> hangHoaLookup,
        string maHangHoa,
        string tenHangHoa,
        int donViTinhId,
        string currentAuditUser,
        CancellationToken cancellationToken)
    {
        var codeKey = NormalizeComparisonKey(maHangHoa)
            ?? throw new InvalidOperationException("MSP khong hop le.");
        if (hangHoaLookup.TryGetValue(codeKey, out var existingId))
        {
            return existingId;
        }

        var newId = await InsertImportedHangHoaAsync(
            connection,
            transaction,
            donViTinhColumnName,
            tenHangHoa,
            codeKey,
            donViTinhId,
            currentAuditUser,
            cancellationToken);
        hangHoaLookup[codeKey] = newId;
        return newId;
    }

    private static async Task<int> ResolveOrCreatePhanLoaiIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IDictionary<string, int> phanLoaiLookup,
        int hangHoaId,
        string tenPhanLoai,
        CancellationToken cancellationToken)
    {
        var key = BuildPhanLoaiKey(hangHoaId, tenPhanLoai)
            ?? throw new InvalidOperationException("Phan loai khong hop le.");
        if (phanLoaiLookup.TryGetValue(key, out var existingId))
        {
            return existingId;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO [{PhanLoaiTableName}] (
                IDHangHoa,
                TenPhanLoai,
                TrangThaiSuDung
            )
            VALUES (
                @IDHangHoa,
                @TenPhanLoai,
                1
            );

            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;
        command.Parameters.Add(new SqlParameter("@IDHangHoa", SqlDbType.Int) { Value = hangHoaId });
        command.Parameters.Add(new SqlParameter("@TenPhanLoai", SqlDbType.NVarChar, 250) { Value = TrimToLength(tenPhanLoai, 250) });

        var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
        if (newId <= 0)
        {
            throw new InvalidOperationException("Khong the tao phan loai hang hoa.");
        }

        phanLoaiLookup[key] = newId;
        return newId;
    }

    private static async Task UpdateHangHoaImportNameAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int hangHoaId,
        string tenHangHoa,
        string currentAuditUser,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE [{TableName}]
            SET
                TenHangHoa = @TenHangHoa,
                Updated_Date = GETDATE(),
                Updated_By = @UpdatedBy
            WHERE ID = @ID
            """;
        command.Parameters.Add(new SqlParameter("@ID", SqlDbType.Int) { Value = hangHoaId });
        command.Parameters.Add(new SqlParameter("@TenHangHoa", SqlDbType.NVarChar, 250) { Value = TrimToLength(tenHangHoa, 250) });
        command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentAuditUser, 50) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdatePhanLoaiImportNameAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int phanLoaiId,
        string tenPhanLoai,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE [{PhanLoaiTableName}]
            SET TenPhanLoai = @TenPhanLoai
            WHERE ID = @ID
            """;
        command.Parameters.Add(new SqlParameter("@ID", SqlDbType.Int) { Value = phanLoaiId });
        command.Parameters.Add(new SqlParameter("@TenPhanLoai", SqlDbType.NVarChar, 250) { Value = TrimToLength(tenPhanLoai, 250) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateChiTietHangHoaImportNameAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int chiTietHangHoaId,
        string tenChiTiet,
        string currentAuditUser,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE [{ChiTietHangHoaTableName}]
            SET
                TenChiTiet = @TenChiTiet,
                Updated_Date = GETDATE(),
                Updated_By = @UpdatedBy
            WHERE ID = @ID
            """;
        command.Parameters.Add(new SqlParameter("@ID", SqlDbType.Int) { Value = chiTietHangHoaId });
        command.Parameters.Add(new SqlParameter("@TenChiTiet", SqlDbType.NVarChar, 250) { Value = TrimToLength(tenChiTiet, 250) });
        command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentAuditUser, 50) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> InsertChiTietHangHoaFromImportAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int hangHoaId,
        int? phanLoaiId,
        int donViTinhId,
        int khoId,
        string tenChiTiet,
        decimal tonKhoDauKy,
        string currentAuditUser,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO [{ChiTietHangHoaTableName}] (
                IDKho,
                IDHangHoa,
                IDPhanLoaiHangHoa,
                IDDonVinTinh,
                IDDonViNhap,
                TenChiTiet,
                SoLuongNhap,
                SoLuongTon,
                TrangThaiSuDung,
                Created_Date,
                Created_By,
                Updated_Date,
                Updated_By
            )
            VALUES (
                @IDKho,
                @IDHangHoa,
                @IDPhanLoaiHangHoa,
                @IDDonVinTinh,
                @IDDonViNhap,
                @TenChiTiet,
                @SoLuongNhap,
                @SoLuongTon,
                1,
                GETDATE(),
                @CreatedBy,
                GETDATE(),
                @UpdatedBy
            );

            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;
        command.Parameters.Add(new SqlParameter("@IDKho", SqlDbType.Int) { Value = khoId });
        command.Parameters.Add(new SqlParameter("@IDHangHoa", SqlDbType.Int) { Value = hangHoaId });
        command.Parameters.Add(new SqlParameter("@IDPhanLoaiHangHoa", SqlDbType.Int) { Value = phanLoaiId.HasValue && phanLoaiId.Value > 0 ? phanLoaiId.Value : DBNull.Value });
        command.Parameters.Add(new SqlParameter("@IDDonVinTinh", SqlDbType.Int) { Value = donViTinhId });
        command.Parameters.Add(new SqlParameter("@IDDonViNhap", SqlDbType.Int) { Value = donViTinhId });
        command.Parameters.Add(new SqlParameter("@TenChiTiet", SqlDbType.NVarChar, 250) { Value = TrimToLength(tenChiTiet, 250) });
        command.Parameters.Add(new SqlParameter("@SoLuongNhap", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = tonKhoDauKy });
        command.Parameters.Add(new SqlParameter("@SoLuongTon", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = tonKhoDauKy });
        command.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentAuditUser, 50) });
        command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = TrimToLength(currentAuditUser, 50) });
        var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
        if (newId <= 0)
        {
            throw new InvalidOperationException("Khong the tao chi tiet hang hoa.");
        }

        return newId;
    }

    private static void AddNhapKhoImportDetail(
        ICollection<HangHoaImportNhapKhoDetail> details,
        int hangHoaId,
        int? phanLoaiId,
        int donViTinhId,
        int khoId,
        decimal soLuongNhap)
    {
        if (soLuongNhap <= 0)
        {
            return;
        }

        details.Add(new HangHoaImportNhapKhoDetail
        {
            KhoId = khoId,
            HangHoaId = hangHoaId,
            PhanLoaiId = phanLoaiId,
            DonViTinhId = donViTinhId,
            SoLuongNhap = soLuongNhap
        });
    }

    private static async Task CreateNhapKhoPhieuFromImportAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IReadOnlyList<HangHoaImportNhapKhoDetail> details,
        string currentAuditUser,
        CancellationToken cancellationToken)
    {
        var ngayNhapKho = DateTime.Today;
        foreach (var khoGroup in details.GroupBy(detail => detail.KhoId).OrderBy(group => group.Key))
        {
            var phieuDetails = khoGroup
                .GroupBy(detail => new { detail.HangHoaId, detail.PhanLoaiId, detail.DonViTinhId })
                .Select(group => new HangHoaImportNhapKhoDetail
                {
                    KhoId = khoGroup.Key,
                    HangHoaId = group.Key.HangHoaId,
                    PhanLoaiId = group.Key.PhanLoaiId,
                    DonViTinhId = group.Key.DonViTinhId,
                    SoLuongNhap = group.Sum(item => item.SoLuongNhap)
                })
                .Where(detail => detail.SoLuongNhap > 0)
                .ToArray();

            if (phieuDetails.Length == 0)
            {
                continue;
            }

            var maPhieu = await GenerateNextNhapKhoMaPhieuAsync(connection, transaction, ngayNhapKho, cancellationToken);
            await using var headerCommand = connection.CreateCommand();
            headerCommand.Transaction = transaction;
            headerCommand.CommandText = $"""
                INSERT INTO [{NhapKhoHeaderTableName}] (
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
                    NULL,
                    @TrangThaiPhieu,
                    @IDKho
                );

                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;
            headerCommand.Parameters.Add(new SqlParameter("@MaPhieu", SqlDbType.NVarChar, 50) { Value = maPhieu });
            headerCommand.Parameters.Add(new SqlParameter("@NgayNhapKho", SqlDbType.DateTime) { Value = ngayNhapKho });
            headerCommand.Parameters.Add(new SqlParameter("@NguoiNhapKho", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentAuditUser, 100) });
            headerCommand.Parameters.Add(new SqlParameter("@NoiDungNhapKho", SqlDbType.NVarChar, 550) { Value = "Nhap kho tu import hang hoa" });
            headerCommand.Parameters.Add(new SqlParameter("@TrangThaiPhieu", SqlDbType.NVarChar, 50) { Value = NhapKhoPhieuStatus.Imported });
            headerCommand.Parameters.Add(new SqlParameter("@IDKho", SqlDbType.Int) { Value = khoGroup.Key });

            var phieuId = Convert.ToInt32(await headerCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
            if (phieuId <= 0)
            {
                throw new InvalidOperationException("Khong the tao phieu nhap kho tu import hang hoa.");
            }

            foreach (var detail in phieuDetails)
            {
                await InsertNhapKhoImportDetailAsync(connection, transaction, phieuId, detail, cancellationToken);
            }
        }
    }

    private static async Task InsertNhapKhoImportDetailAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int phieuId,
        HangHoaImportNhapKhoDetail detail,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO [{NhapKhoDetailTableName}] (
                IDPhieuNhapKho,
                IDHangHoa,
                IDPhanLoaiHangHoa,
                IDDonViTinh,
                MaSoLo,
                SoChungTu,
                SoLuongNhap,
                SoLuongQuyDoi,
                IDDonViNhap,
                DonGiaNhap,
                DonGiaBanLe,
                LoaiHinhNhap
            )
            VALUES (
                @IDPhieuNhapKho,
                @IDHangHoa,
                @IDPhanLoaiHangHoa,
                @IDDonViTinh,
                NULL,
                NULL,
                @SoLuongNhap,
                1,
                @IDDonViNhap,
                0,
                0,
                @LoaiHinhNhap
            )
            """;
        command.Parameters.Add(new SqlParameter("@IDPhieuNhapKho", SqlDbType.Int) { Value = phieuId });
        command.Parameters.Add(new SqlParameter("@IDHangHoa", SqlDbType.Int) { Value = detail.HangHoaId });
        command.Parameters.Add(new SqlParameter("@IDPhanLoaiHangHoa", SqlDbType.Int) { Value = detail.PhanLoaiId.HasValue && detail.PhanLoaiId.Value > 0 ? detail.PhanLoaiId.Value : DBNull.Value });
        command.Parameters.Add(new SqlParameter("@IDDonViTinh", SqlDbType.Int) { Value = detail.DonViTinhId });
        command.Parameters.Add(new SqlParameter("@SoLuongNhap", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = detail.SoLuongNhap });
        command.Parameters.Add(new SqlParameter("@IDDonViNhap", SqlDbType.Int) { Value = detail.DonViTinhId });
        command.Parameters.Add(new SqlParameter("@LoaiHinhNhap", SqlDbType.NVarChar, 100) { Value = NhapKhoLoaiHinh.NhapTheoLo });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> GenerateNextNhapKhoMaPhieuAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DateTime ngayNhapKho,
        CancellationToken cancellationToken)
    {
        var prefix = $"N{ngayNhapKho:yyMM}";
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT ISNULL(MAX(TRY_CONVERT(int, RIGHT(MaPhieu, 3))), 0)
            FROM [{NhapKhoHeaderTableName}]
            WHERE MaPhieu LIKE @Prefix + '[0-9][0-9][0-9]'
            """;
        command.Parameters.Add(new SqlParameter("@Prefix", SqlDbType.NVarChar, 5) { Value = prefix });
        var maxSequence = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
        return $"{prefix}{maxSequence + 1:000}";
    }

    private static string? BuildPhanLoaiKey(int hangHoaId, string? tenPhanLoai)
    {
        var normalizedName = NormalizeComparisonKey(tenPhanLoai);
        return normalizedName is null ? null : $"{hangHoaId}|{normalizedName}";
    }

    private static string BuildChiTietHangHoaKey(int hangHoaId, int? phanLoaiId, int donViTinhId, int khoId)
    {
        return $"{hangHoaId}|{phanLoaiId?.ToString() ?? "NULL"}|{donViTinhId}|{khoId}";
    }

    private async Task<string?> ValidateDuplicateNameAsync(
        SqlConnection connection,
        string tenHangHoa,
        int? excludeId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1) ID
            FROM [{TableName}]
            WHERE UPPER(LTRIM(RTRIM(TenHangHoa))) = UPPER(@TenHangHoa)
            {(excludeId.HasValue ? "AND ID <> @ExcludeId" : string.Empty)}
            """;
        command.Parameters.Add(new SqlParameter("@TenHangHoa", SqlDbType.NVarChar, 250) { Value = tenHangHoa.Trim() });

        if (excludeId.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@ExcludeId", SqlDbType.Int) { Value = excludeId.Value });
        }

        var existingId = await command.ExecuteScalarAsync(cancellationToken);
        return existingId is null ? null : "Tên hàng hóa đã tồn tại.";
    }

    private static async Task<(Dictionary<string, List<HangHoaImportExistingItem>> ByCode, Dictionary<string, List<HangHoaImportExistingItem>> ByName)> LoadHangHoaImportLookupAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                ID,
                TenHangHoa,
                MaHangHoa
            FROM [{TableName}]
            WHERE
                (TenHangHoa IS NOT NULL AND LTRIM(RTRIM(TenHangHoa)) <> '') OR
                (MaHangHoa IS NOT NULL AND LTRIM(RTRIM(MaHangHoa)) <> '')
            ORDER BY ID ASC
            """;

        var byCode = new Dictionary<string, List<HangHoaImportExistingItem>>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, List<HangHoaImportExistingItem>>(StringComparer.OrdinalIgnoreCase);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var item = new HangHoaImportExistingItem
            {
                Id = reader.GetInt32(reader.GetOrdinal("ID")),
                NameKey = NormalizeComparisonKey(GetNullableString(reader, "TenHangHoa")),
                CodeKey = NormalizeComparisonKey(GetNullableString(reader, "MaHangHoa"))
            };

            AddImportLookupItem(byCode, item.CodeKey, item);
            AddImportLookupItem(byName, item.NameKey, item);
        }

        return (byCode, byName);
    }

    private static (HangHoaImportExistingItem? Item, bool HasCodeConflict, bool ShouldSkip) FindExistingImportItem(
        IReadOnlyDictionary<string, List<HangHoaImportExistingItem>> existingByCode,
        IReadOnlyDictionary<string, List<HangHoaImportExistingItem>> existingByName,
        string? maHangHoa,
        string tenHangHoa)
    {
        if (maHangHoa is not null &&
            existingByCode.TryGetValue(maHangHoa, out var codeMatches) &&
            codeMatches.Count > 0)
        {
            var codeNameMatches = codeMatches
                .Where(item => string.Equals(item.NameKey, tenHangHoa, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return codeNameMatches.Count == 1
                ? (codeNameMatches[0], false, false)
                : (null, true, true);
        }

        if (existingByName.TryGetValue(tenHangHoa, out var nameMatches) &&
            nameMatches.Count > 0)
        {
            if (nameMatches.Count == 1)
            {
                return (nameMatches[0], false, false);
            }

            return (null, false, true);
        }

        return (null, false, false);
    }

    private static async Task<int> InsertImportedHangHoaAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string? donViTinhColumnName,
        string tenHangHoa,
        string? maHangHoa,
        int? donViTinhId,
        string currentAuditUser,
        CancellationToken cancellationToken)
    {
        var donViTinhColumn = donViTinhColumnName is null ? string.Empty : $"[{donViTinhColumnName}],";
        var donViTinhValue = donViTinhColumnName is null ? string.Empty : "@IDDonViTinh,";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO [{TableName}] (
                TenHangHoa,
                MaHangHoa,
                {donViTinhColumn}
                Image,
                TrangThaiSuDung,
                Created_Date,
                Created_By,
                Updated_Date,
                Updated_By
            )
            VALUES (
                @TenHangHoa,
                @MaHangHoa,
                {donViTinhValue}
                NULL,
                1,
                GETDATE(),
                @CreatedBy,
                GETDATE(),
                @UpdatedBy
            );

            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;

        command.Parameters.Add(new SqlParameter("@TenHangHoa", SqlDbType.NVarChar, 250) { Value = tenHangHoa });
        command.Parameters.Add(new SqlParameter("@MaHangHoa", SqlDbType.NVarChar, 50) { Value = ToDbValue(maHangHoa) });
        if (donViTinhColumnName is not null)
        {
            command.Parameters.Add(new SqlParameter("@IDDonViTinh", SqlDbType.Int)
            {
                Value = donViTinhId.HasValue && donViTinhId.Value > 0 ? donViTinhId.Value : DBNull.Value
            });
        }
        command.Parameters.Add(new SqlParameter("@CreatedBy", SqlDbType.NVarChar, 50) { Value = currentAuditUser });
        command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = currentAuditUser });

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private static async Task UpdateImportedHangHoaAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string? donViTinhColumnName,
        int id,
        string tenHangHoa,
        string? maHangHoa,
        bool updateCode,
        int? donViTinhId,
        string currentAuditUser,
        CancellationToken cancellationToken)
    {
        var maHangHoaSet = updateCode ? "MaHangHoa = @MaHangHoa," : string.Empty;
        var donViTinhSet = donViTinhColumnName is null ? string.Empty : $"[{donViTinhColumnName}] = @IDDonViTinh,";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE [{TableName}]
            SET
                TenHangHoa = @TenHangHoa,
                {maHangHoaSet}
                {donViTinhSet}
                Updated_Date = GETDATE(),
                Updated_By = @UpdatedBy
            WHERE ID = @Id
            """;

        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        command.Parameters.Add(new SqlParameter("@TenHangHoa", SqlDbType.NVarChar, 250) { Value = tenHangHoa });
        if (updateCode)
        {
            command.Parameters.Add(new SqlParameter("@MaHangHoa", SqlDbType.NVarChar, 50) { Value = ToDbValue(maHangHoa) });
        }
        if (donViTinhColumnName is not null)
        {
            command.Parameters.Add(new SqlParameter("@IDDonViTinh", SqlDbType.Int)
            {
                Value = donViTinhId.HasValue && donViTinhId.Value > 0 ? donViTinhId.Value : DBNull.Value
            });
        }
        command.Parameters.Add(new SqlParameter("@UpdatedBy", SqlDbType.NVarChar, 50) { Value = currentAuditUser });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, int>> LoadDonViTinhLookupAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT
                ID,
                TenDonVi,
                TenVietTat
            FROM [{DonViTinhTableName}]
            WHERE
                (TenDonVi IS NOT NULL AND LTRIM(RTRIM(TenDonVi)) <> '') OR
                (TenVietTat IS NOT NULL AND LTRIM(RTRIM(TenVietTat)) <> '')
            ORDER BY ID ASC
            """;

        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt32(reader.GetOrdinal("ID"));
            AddLookupKey(lookup, GetNullableString(reader, "TenDonVi"), id);
            AddLookupKey(lookup, GetNullableString(reader, "TenVietTat"), id);
        }

        return lookup;
    }

    private static async Task<int?> ResolveOrCreateDonViTinhIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        IDictionary<string, int> donViTinhLookup,
        string? donViTinh,
        string currentUser,
        CancellationToken cancellationToken)
    {
        var normalizedDonViTinh = NormalizeComparisonKey(donViTinh);
        if (normalizedDonViTinh is null)
        {
            return null;
        }

        if (donViTinhLookup.TryGetValue(normalizedDonViTinh, out var existingId))
        {
            return existingId;
        }

        var newId = await InsertDonViTinhAsync(connection, transaction, normalizedDonViTinh, currentUser, cancellationToken);
        donViTinhLookup[normalizedDonViTinh] = newId;
        return newId;
    }

    private static async Task<int> InsertDonViTinhAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string tenDonVi,
        string currentUser,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO [{DonViTinhTableName}] (
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
                NULL,
                1,
                @NguoiTao,
                GETDATE(),
                @NguoiCapNhap,
                GETDATE(),
                @Type
            );

            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;

        command.Parameters.Add(new SqlParameter("@TenDonVi", SqlDbType.NVarChar, 300) { Value = tenDonVi });
        command.Parameters.Add(new SqlParameter("@NguoiTao", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentUser, 100) });
        command.Parameters.Add(new SqlParameter("@NguoiCapNhap", SqlDbType.NVarChar, 100) { Value = TrimToLength(currentUser, 100) });
        command.Parameters.Add(new SqlParameter("@Type", SqlDbType.NVarChar, 100) { Value = DefaultDonViTinhType });

        var newId = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
        if (newId <= 0)
        {
            throw new InvalidOperationException("Không thể tạo mới đơn vị tính khi import hàng hóa.");
        }

        return newId;
    }

    private static HangHoaListItem MapItem(SqlDataReader reader)
    {
        return new HangHoaListItem
        {
            Id = reader.GetInt32(reader.GetOrdinal("ID")),
            TenHangHoa = GetNullableString(reader, "TenHangHoa") ?? string.Empty,
            MaHangHoa = GetNullableString(reader, "MaHangHoa"),
            LoaiHinhNhap = NhapKhoLoaiHinh.Normalize(GetNullableString(reader, "LoaiHinhNhap")) is { Length: > 0 } loaiHinhNhap
                ? loaiHinhNhap
                : NhapKhoLoaiHinh.NhapTheoLo,
            DonViTinhId = GetNullableInt32(reader, "IDDonViTinh"),
            TenDonViTinh = GetNullableString(reader, "TenDonVi"),
            TenVietTatDonViTinh = GetNullableString(reader, "TenVietTat"),
            ImageUrl = GetNullableString(reader, "Image"),
            TrangThaiSuDung = reader.GetBoolean(reader.GetOrdinal("TrangThaiSuDung")),
            CreatedDate = GetNullableDateTime(reader, "Created_Date"),
            CreatedBy = GetNullableString(reader, "Created_By"),
            UpdatedDate = GetNullableDateTime(reader, "Updated_Date"),
            UpdatedBy = GetNullableString(reader, "Updated_By")
        };
    }

    private static string BuildWhereClause(string? keyword, bool? statusFilter, string? tableAlias = null)
    {
        var prefix = string.IsNullOrWhiteSpace(tableAlias) ? string.Empty : $"{tableAlias}.";
        var filters = new List<string> { "1 = 1" };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filters.Add($"""
                (
                    {prefix}TenHangHoa COLLATE {SearchCollation} LIKE @Keyword OR
                    {prefix}MaHangHoa COLLATE {SearchCollation} LIKE @Keyword
                )
                """);
        }

        if (statusFilter.HasValue)
        {
            filters.Add($"{prefix}TrangThaiSuDung = @TrangThaiSuDung");
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

    private static void FillSaveParameters(SqlCommand command, HangHoaFormModel model, bool includeDonViTinh)
    {
        command.Parameters.Add(new SqlParameter("@TenHangHoa", SqlDbType.NVarChar, 250)
        {
            Value = model.TenHangHoa.Trim()
        });
        command.Parameters.Add(new SqlParameter("@MaHangHoa", SqlDbType.NVarChar, 50)
        {
            Value = ToDbValue(model.MaHangHoa)
        });
        command.Parameters.Add(new SqlParameter("@LoaiHinhNhap", SqlDbType.NVarChar, 100)
        {
            Value = NhapKhoLoaiHinh.Normalize(model.LoaiHinhNhap) is { Length: > 0 } loaiHinhNhap
                ? loaiHinhNhap
                : NhapKhoLoaiHinh.NhapTheoLo
        });
        if (includeDonViTinh)
        {
            command.Parameters.Add(new SqlParameter("@IDDonViTinh", SqlDbType.Int)
            {
                Value = model.DonViTinhId.HasValue && model.DonViTinhId.Value > 0 ? model.DonViTinhId.Value : DBNull.Value
            });
        }
        command.Parameters.Add(new SqlParameter("@Image", SqlDbType.NVarChar, 550)
        {
            Value = ToDbValue(model.ImageUrl)
        });
        command.Parameters.Add(new SqlParameter("@TrangThaiSuDung", SqlDbType.Bit)
        {
            Value = model.TrangThaiSuDung
        });
    }

    private static async Task SyncPhanLoaiAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int hangHoaId,
        IEnumerable<HangHoaPhanLoaiModel>? phanLoai,
        CancellationToken cancellationToken)
    {
        await EnsurePhanLoaiTableAsync(connection, transaction, cancellationToken);

        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        deleteCommand.CommandText = $"""
            DELETE FROM [{PhanLoaiTableName}]
            WHERE IDHangHoa = @IDHangHoa
            """;
        deleteCommand.Parameters.Add(new SqlParameter("@IDHangHoa", SqlDbType.Int) { Value = hangHoaId });
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

        var items = (phanLoai ?? [])
            .Select(item => new HangHoaPhanLoaiModel
            {
                TenPhanLoai = item.TenPhanLoai?.Trim(),
                TrangThaiSuDung = item.TrangThaiSuDung
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.TenPhanLoai))
            .GroupBy(item => item.TenPhanLoai!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        foreach (var item in items)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = $"""
                INSERT INTO [{PhanLoaiTableName}] (
                    IDHangHoa,
                    TenPhanLoai,
                    TrangThaiSuDung
                )
                VALUES (
                    @IDHangHoa,
                    @TenPhanLoai,
                    @TrangThaiSuDung
                )
                """;
            insertCommand.Parameters.Add(new SqlParameter("@IDHangHoa", SqlDbType.Int) { Value = hangHoaId });
            insertCommand.Parameters.Add(new SqlParameter("@TenPhanLoai", SqlDbType.NVarChar, 250) { Value = TrimToLength(item.TenPhanLoai!, 250) });
            insertCommand.Parameters.Add(new SqlParameter("@TrangThaiSuDung", SqlDbType.Bit) { Value = item.TrangThaiSuDung });
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task EnsurePhanLoaiTableAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            IF OBJECT_ID(N'dbo.{PhanLoaiTableName}', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[{PhanLoaiTableName}] (
                    [ID] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_{PhanLoaiTableName}] PRIMARY KEY,
                    [IDHangHoa] int NOT NULL,
                    [TenPhanLoai] nvarchar(250) NOT NULL,
                    [TrangThaiSuDung] bit NOT NULL CONSTRAINT [DF_{PhanLoaiTableName}_TrangThaiSuDung] DEFAULT(1)
                );
            END;

            IF COL_LENGTH(N'dbo.{PhanLoaiTableName}', N'TrangThaiSuDung') IS NULL
            BEGIN
                ALTER TABLE [dbo].[{PhanLoaiTableName}]
                ADD [TrangThaiSuDung] bit NOT NULL CONSTRAINT [DF_{PhanLoaiTableName}_TrangThaiSuDung] DEFAULT(1);
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'IX_{PhanLoaiTableName}_IDHangHoa'
                  AND object_id = OBJECT_ID(N'dbo.{PhanLoaiTableName}')
            )
            BEGIN
                CREATE INDEX [IX_{PhanLoaiTableName}_IDHangHoa]
                ON [dbo].[{PhanLoaiTableName}] ([IDHangHoa]);
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static int? GetNullableInt32(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static async Task<string?> ResolveDonViTinhColumnNameAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (await HasColumnAsync(connection, transaction, TableName, "IDDonViTinh", cancellationToken))
        {
            return "IDDonViTinh";
        }

        if (await HasColumnAsync(connection, transaction, TableName, "IDDonVinTinh", cancellationToken))
        {
            return "IDDonVinTinh";
        }

        return null;
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

    private static async Task EnsureHangHoaSchemaAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (await HasColumnAsync(connection, transaction, TableName, "LoaiHinhNhap", cancellationToken))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"ALTER TABLE [dbo].[{TableName}] ADD [LoaiHinhNhap] nvarchar(100) NOT NULL CONSTRAINT [DF_{TableName}_LoaiHinhNhap] DEFAULT('{NhapKhoLoaiHinh.NhapTheoLo}');";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureChiTietHangHoaImportSchemaAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(connection, transaction, ChiTietHangHoaTableName, "IDPhanLoaiHangHoa", "int NULL", cancellationToken);
        await EnsureColumnAsync(connection, transaction, ChiTietHangHoaTableName, "IDDonViNhap", "int NULL", cancellationToken);
        await EnsureColumnAsync(connection, transaction, ChiTietHangHoaTableName, "TrangThaiSuDung", "bit NOT NULL CONSTRAINT DF_TblChiTietHangHoa_TrangThaiSuDung DEFAULT(1)", cancellationToken);
        await EnsureColumnAsync(connection, transaction, ChiTietHangHoaTableName, "SoLuongNhap", "decimal(18,2) NOT NULL CONSTRAINT DF_TblChiTietHangHoa_SoLuongNhap DEFAULT(0)", cancellationToken);
        await EnsureColumnAsync(connection, transaction, ChiTietHangHoaTableName, "SoLuongTon", "decimal(18,2) NOT NULL CONSTRAINT DF_TblChiTietHangHoa_SoLuongTon DEFAULT(0)", cancellationToken);
    }

    private static async Task EnsureNhapKhoImportSchemaAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(connection, transaction, NhapKhoDetailTableName, "IDPhanLoaiHangHoa", "int NULL", cancellationToken);
        await EnsureColumnAsync(connection, transaction, NhapKhoDetailTableName, "IDDonViTinh", "int NULL", cancellationToken);
        await EnsureColumnAsync(connection, transaction, NhapKhoDetailTableName, "MaSoLo", "nvarchar(50) NULL", cancellationToken);
        await EnsureColumnAsync(connection, transaction, NhapKhoDetailTableName, "SoChungTu", "nvarchar(50) NULL", cancellationToken);
        await EnsureColumnAsync(connection, transaction, NhapKhoDetailTableName, "IDDonViNhap", "int NULL", cancellationToken);
        await EnsureColumnAsync(connection, transaction, NhapKhoDetailTableName, "SoLuongQuyDoi", "decimal(18,4) NOT NULL CONSTRAINT DF_TblPhieuNhapKhoChiTiet_SoLuongQuyDoi DEFAULT(1)", cancellationToken);
        await EnsureColumnAsync(connection, transaction, NhapKhoDetailTableName, "DonGiaNhap", "decimal(18,2) NOT NULL CONSTRAINT DF_TblPhieuNhapKhoChiTiet_DonGiaNhap DEFAULT(0)", cancellationToken);
        await EnsureColumnAsync(connection, transaction, NhapKhoDetailTableName, "DonGiaBanLe", "decimal(18,2) NOT NULL CONSTRAINT DF_TblPhieuNhapKhoChiTiet_DonGiaBanLe DEFAULT(0)", cancellationToken);
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

    private static string? NormalizeComparisonKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC);
        var parts = normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? null : string.Join(' ', parts);
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

    private static void AddLookupKey(ISet<string> values, string? value)
    {
        var normalizedValue = NormalizeComparisonKey(value);
        if (normalizedValue is not null)
        {
            values.Add(normalizedValue);
        }
    }

    private static void AddLookupKey(IDictionary<string, int> values, string? value, int id)
    {
        var normalizedValue = NormalizeComparisonKey(value);
        if (normalizedValue is not null && !values.ContainsKey(normalizedValue))
        {
            values[normalizedValue] = id;
        }
    }

    private static void AddImportLookupItem(
        IDictionary<string, List<HangHoaImportExistingItem>> lookup,
        string? key,
        HangHoaImportExistingItem item)
    {
        if (key is null)
        {
            return;
        }

        if (!lookup.TryGetValue(key, out var items))
        {
            items = [];
            lookup[key] = items;
        }

        items.Add(item);
    }

    private static void RefreshHangHoaImportLookup(
        IDictionary<string, List<HangHoaImportExistingItem>> lookup,
        string? previousKey,
        string? nextKey,
        HangHoaImportExistingItem item)
    {
        if (previousKey is not null &&
            lookup.TryGetValue(previousKey, out var previousItems))
        {
            previousItems.Remove(item);
            if (previousItems.Count == 0)
            {
                lookup.Remove(previousKey);
            }
        }

        AddImportLookupItem(lookup, nextKey, item);
    }

    private sealed class HangHoaImportExistingItem
    {
        public int Id { get; set; }
        public string? NameKey { get; set; }
        public string? CodeKey { get; set; }
    }

    private sealed class HangHoaImportNhapKhoDetail
    {
        public int KhoId { get; set; }
        public int HangHoaId { get; set; }
        public int? PhanLoaiId { get; set; }
        public int DonViTinhId { get; set; }
        public decimal SoLuongNhap { get; set; }
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
