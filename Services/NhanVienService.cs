using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface INhanVienService
{
    Task<(IReadOnlyList<NhanVienListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
        string? keyword,
        bool? statusFilter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<NhanVienListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChamCongEmployeeOption>> GetChamCongEmployeeOptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PhongBanOption>> GetPhongBanOptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NhanVienRoleOption>> GetRoleOptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> GetAssignedRoleIdsAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(NhanVienFormModel model, string currentUser, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(NhanVienFormModel model, string currentUser, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> SaveRoleAssignmentsAsync(Guid accountId, IReadOnlyCollection<int> roleIds, CancellationToken cancellationToken = default);
    Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class NhanVienService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<NhanVienService> logger) : INhanVienService
{
    private const string TableName = "TblNhanVien";
    private const string DepartmentTableName = "TblPhongBan";
    private const string UserTableName = "TblTaiKhoanNguoiDung";
    private const string RoleTableName = "TblVaiTro";
    private const string UserRoleTableName = "TblVaiTroVaNguoiDung";
    private const string SearchCollation = "Latin1_General_100_CI_AI";

    private static readonly string[] UserAvatarColumns = ["AnhDaiDien", "Avatar", "AvatarUrl", "DuongDanAvatar", "HinhDaiDien", "HinhAnhDaiDien", "DuongDanAnh"];

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<NhanVienService> _logger = logger;

    public async Task<(IReadOnlyList<NhanVienListItem> Items, int TotalCount, int CurrentPage, int TotalPages, int PageSize)> GetPagedAsync(
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
                FROM [{TableName}] AS nv
                LEFT JOIN [{DepartmentTableName}] AS pb ON pb.ID = nv.IDPhongBan
                WHERE {whereClause}
                """;
            AddFilterParameters(countCommand, normalizedKeyword, statusFilter);

            var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0);
            var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            var currentPage = totalPages == 0 ? 1 : Math.Min(page, totalPages);
            var offset = (currentPage - 1) * pageSize;

            await using var listCommand = connection.CreateCommand();
            listCommand.CommandText = $"""
                {BuildListSelect()}
                WHERE {whereClause}
                ORDER BY nv.ID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
                """;
            AddFilterParameters(listCommand, normalizedKeyword, statusFilter);
            listCommand.Parameters.Add(new SqlParameter("@Offset", SqlDbType.Int) { Value = offset });
            listCommand.Parameters.Add(new SqlParameter("@PageSize", SqlDbType.Int) { Value = pageSize });

            var items = new List<NhanVienListItem>();
            await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(MapItem(reader));
            }

            return (items, totalCount, currentPage, totalPages, pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblNhanVien list.");
            return ([], 0, 1, 0, pageSize);
        }
    }

    public async Task<NhanVienListItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
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
                {BuildListSelect()}
                WHERE nv.ID = @Id
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? MapItem(reader) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TblNhanVien item {Id}.", id);
            return null;
        }
    }

    public async Task<IReadOnlyList<ChamCongEmployeeOption>> GetChamCongEmployeeOptionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT
                    nv.ID,
                    LTRIM(RTRIM(CONCAT(ISNULL(nv.Ho, N''), N' ', ISNULL(nv.Ten, N'')))) AS HoTen
                FROM [{TableName}] AS nv
                LEFT JOIN [{UserTableName}] AS tk ON tk.IDNhanVien = nv.ID
                WHERE ISNULL(nv.TrangThaiSuDung, 1) = 1
                    AND ISNULL(tk.QuanTriVien, 0) = 0
                    AND UPPER(LTRIM(RTRIM(ISNULL(tk.TenDangNhap, N'')))) COLLATE {SearchCollation} NOT IN (N'ADMIN', N'ADMINISTRATOR', N'QUANTRI', N'QUANTRIHETHONG')
                    AND UPPER(LTRIM(RTRIM(ISNULL(tk.NhomNguoiDung, N'')))) COLLATE {SearchCollation} NOT LIKE N'%ADMIN%'
                    AND UPPER(LTRIM(RTRIM(ISNULL(tk.NhomNguoiDung, N'')))) COLLATE {SearchCollation} NOT LIKE N'%QUANTRI%'
                ORDER BY nv.Ho, nv.Ten, nv.ID
                """;

            var items = new List<ChamCongEmployeeOption>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = GetNullableInt32(reader, "ID") ?? 0;
                if (id <= 0)
                {
                    continue;
                }

                items.Add(new ChamCongEmployeeOption
                {
                    Id = id,
                    HoTen = GetNullableString(reader, "HoTen") ?? $"Nhân viên #{id}"
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load employee options for attendance dashboard.");
            return [];
        }
    }

    public async Task<IReadOnlyList<PhongBanOption>> GetPhongBanOptionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT ID, TenPhongBan
                FROM [{DepartmentTableName}]
                WHERE ISNULL(TrangThaiSuDung, 1) = 1
                ORDER BY TenPhongBan
                """;

            var options = new List<PhongBanOption>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                options.Add(new PhongBanOption
                {
                    Id = reader.GetInt32(reader.GetOrdinal("ID")),
                    TenPhongBan = GetNullableString(reader, "TenPhongBan") ?? ""
                });
            }

            return options;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load department options for employees.");
            return [];
        }
    }

    public async Task<IReadOnlyList<NhanVienRoleOption>> GetRoleOptionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT ID, TenVaiTro, MieuTa
                FROM [{RoleTableName}]
                WHERE ISNULL(TrangThaiSuDung, 1) = 1
                ORDER BY TenVaiTro
                """;

            var items = new List<NhanVienRoleOption>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new NhanVienRoleOption
                {
                    Id = reader.GetInt32(reader.GetOrdinal("ID")),
                    TenVaiTro = GetNullableString(reader, "TenVaiTro") ?? string.Empty,
                    MieuTa = GetNullableString(reader, "MieuTa")
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load role options for employees.");
            return [];
        }
    }

    public async Task<IReadOnlyList<int>> GetAssignedRoleIdsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
        {
            return [];
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT IDVaiTro
                FROM [{UserRoleTableName}]
                WHERE IDTaiKhoanNguoiDung = @IDTaiKhoanNguoiDung
                """;
            command.Parameters.Add(new SqlParameter("@IDTaiKhoanNguoiDung", SqlDbType.UniqueIdentifier) { Value = accountId });

            var ids = new List<int>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(Convert.ToInt32(reader.GetValue(0)));
            }

            return ids;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load role assignments for account {AccountId}.", accountId);
            return [];
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? Id)> CreateAsync(
        NhanVienFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateAccountRequest(model, isCreate: true);
        if (validationError is not null)
        {
            return (false, validationError, null);
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            if (model.TaoTaiKhoan)
            {
                var duplicateError = await ValidateDuplicateUsernameAsync(connection, transaction, model.TenDangNhap, null, cancellationToken);
                if (duplicateError is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return (false, duplicateError, null);
                }
            }

            var newId = await InsertEmployeeAsync(connection, transaction, model, currentUser, cancellationToken);
            if (newId <= 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không thể thêm mới nhân viên.", null);
            }

            if (model.TaoTaiKhoan)
            {
                await UpsertAccountAsync(connection, transaction, newId, model, currentUser, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return (true, null, newId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create TblNhanVien.");
            return (false, "Không thể thêm mới nhân viên lúc này.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> UpdateAsync(
        NhanVienFormModel model,
        string currentUser,
        CancellationToken cancellationToken = default)
    {
        if (model.Id is null or <= 0)
        {
            return (false, "Không xác định được nhân viên cần cập nhật.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var isAdministratorAccount = model.IDTaiKhoan.HasValue &&
                await IsAdministratorAccountAsync(connection, transaction, model.IDTaiKhoan.Value, cancellationToken);

            if (!isAdministratorAccount)
            {
                var validationError = ValidateAccountRequest(model, isCreate: false);
                if (validationError is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return (false, validationError);
                }
            }

            if (!isAdministratorAccount && model.TaoTaiKhoan)
            {
                var duplicateError = await ValidateDuplicateUsernameAsync(connection, transaction, model.TenDangNhap, model.IDTaiKhoan, cancellationToken);
                if (duplicateError is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return (false, duplicateError);
                }
            }

            var affectedRows = await UpdateEmployeeAsync(connection, transaction, model, currentUser, cancellationToken);
            if (affectedRows == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không tìm thấy nhân viên để cập nhật.");
            }

            if (isAdministratorAccount)
            {
                // Tài khoản quản trị chỉ được quản lý hồ sơ nhân viên tại đây.
            }
            else if (model.TaoTaiKhoan)
            {
                await UpsertAccountAsync(connection, transaction, model.Id.Value, model, currentUser, cancellationToken);
            }
            else if (model.IDTaiKhoan.HasValue)
            {
                await UnlinkAccountAsync(connection, transaction, model.IDTaiKhoan.Value, currentUser, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update TblNhanVien {Id}.", model.Id);
            return (false, "Không thể cập nhật nhân viên lúc này.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> SaveRoleAssignmentsAsync(
        Guid accountId,
        IReadOnlyCollection<int> roleIds,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
        {
            return (false, "Không xác định được tài khoản người dùng để phân vai trò.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = (SqlTransaction)transaction;
                deleteCommand.CommandText = $"DELETE FROM [{UserRoleTableName}] WHERE IDTaiKhoanNguoiDung = @IDTaiKhoanNguoiDung";
                deleteCommand.Parameters.Add(new SqlParameter("@IDTaiKhoanNguoiDung", SqlDbType.UniqueIdentifier) { Value = accountId });
                await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            var selectedIds = roleIds
                .Where(id => id > 0)
                .Distinct()
                .ToArray();

            foreach (var roleId in selectedIds)
            {
                await using var insertCommand = connection.CreateCommand();
                insertCommand.Transaction = (SqlTransaction)transaction;
                insertCommand.CommandText = $"""
                    INSERT INTO [{UserRoleTableName}] (IDVaiTro, IDTaiKhoanNguoiDung)
                    VALUES (@IDVaiTro, @IDTaiKhoanNguoiDung)
                    """;
                insertCommand.Parameters.Add(new SqlParameter("@IDVaiTro", SqlDbType.Int) { Value = roleId });
                insertCommand.Parameters.Add(new SqlParameter("@IDTaiKhoanNguoiDung", SqlDbType.UniqueIdentifier) { Value = accountId });
                await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save role assignments for account {AccountId}.", accountId);
            return (false, "Không thể lưu phân vai trò cho nhân viên lúc này.");
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return (false, "Không xác định được nhân viên cần xóa.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            if (await EmployeeHasAdministratorAccountAsync(connection, transaction, id, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Không được xóa nhân viên đang liên kết tài khoản quản trị.");
            }

            await using (var unlinkCommand = connection.CreateCommand())
            {
                unlinkCommand.Transaction = (SqlTransaction)transaction;
                unlinkCommand.CommandText = $"""
                    UPDATE [{UserTableName}]
                    SET IDNhanVien = NULL
                    WHERE IDNhanVien = @IDNhanVien
                    """;
                unlinkCommand.Parameters.Add(new SqlParameter("@IDNhanVien", SqlDbType.Int) { Value = id });
                await unlinkCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = (SqlTransaction)transaction;
            command.CommandText = $"DELETE FROM [{TableName}] WHERE ID = @Id";
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
            var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return affectedRows > 0
                ? (true, null)
                : (false, "Không tìm thấy nhân viên để xóa.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete TblNhanVien {Id}.", id);
            return (false, "Không thể xóa nhân viên lúc này.");
        }
    }

    private static string BuildListSelect()
    {
        return $"""
            SELECT
                nv.ID,
                nv.Ho,
                nv.Ten,
                nv.GioiTinh,
                nv.NgaySinh,
                nv.IDPhongBan,
                pb.TenPhongBan,
                CAST(ISNULL(nv.TrangThaiSuDung, 0) AS bit) AS TrangThaiSuDung,
                nv.ChucVu,
                nv.Email,
                nv.Avatar,
                tk.IDTaiKhoan,
                tk.TenDangNhap,
                tk.NhomNguoiDung,
                CAST(ISNULL(tk.QuanTriVien, 0) AS bit) AS QuanTriVien
            FROM [{TableName}] AS nv
            LEFT JOIN [{DepartmentTableName}] AS pb ON pb.ID = nv.IDPhongBan
            LEFT JOIN [{UserTableName}] AS tk ON tk.IDNhanVien = nv.ID
            """;
    }

    private static string BuildWhereClause(string? keyword, bool? statusFilter)
    {
        var filters = new List<string> { "1 = 1" };

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filters.Add($"""
                (
                    nv.Ho COLLATE {SearchCollation} LIKE @Keyword
                    OR nv.Ten COLLATE {SearchCollation} LIKE @Keyword
                    OR CONCAT(nv.Ho, N' ', nv.Ten) COLLATE {SearchCollation} LIKE @Keyword
                    OR nv.GioiTinh COLLATE {SearchCollation} LIKE @Keyword
                    OR nv.ChucVu COLLATE {SearchCollation} LIKE @Keyword
                    OR nv.Email COLLATE {SearchCollation} LIKE @Keyword
                    OR pb.TenPhongBan COLLATE {SearchCollation} LIKE @Keyword
                    OR tk.TenDangNhap COLLATE {SearchCollation} LIKE @Keyword
                )
                """);
        }

        if (statusFilter.HasValue)
        {
            filters.Add("ISNULL(nv.TrangThaiSuDung, 0) = @StatusFilter");
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
            command.Parameters.Add(new SqlParameter("@StatusFilter", SqlDbType.Bit) { Value = statusFilter.Value });
        }
    }

    private static NhanVienListItem MapItem(SqlDataReader reader)
    {
        return new NhanVienListItem
        {
            Id = reader.GetInt32(reader.GetOrdinal("ID")),
            Ho = GetNullableString(reader, "Ho") ?? "",
            Ten = GetNullableString(reader, "Ten") ?? "",
            GioiTinh = NormalizeGenderDisplay(GetNullableString(reader, "GioiTinh")),
            NgaySinh = GetNullableDateTime(reader, "NgaySinh"),
            IDPhongBan = GetNullableInt32(reader, "IDPhongBan"),
            TenPhongBan = GetNullableString(reader, "TenPhongBan"),
            TrangThaiSuDung = GetNullableBoolean(reader, "TrangThaiSuDung") ?? true,
            ChucVu = GetNullableString(reader, "ChucVu"),
            Email = GetNullableString(reader, "Email"),
            Avatar = GetNullableString(reader, "Avatar"),
            IDTaiKhoan = GetNullableGuid(reader, "IDTaiKhoan"),
            TenDangNhap = GetNullableString(reader, "TenDangNhap"),
            IsAdministrator = IsAdministratorAccount(
                GetNullableBoolean(reader, "QuanTriVien") ?? false,
                GetNullableString(reader, "TenDangNhap"),
                GetNullableString(reader, "NhomNguoiDung"))
        };
    }

    private static bool IsAdministratorAccount(bool quanTriVien, string? username, string? groupName)
    {
        if (quanTriVien)
        {
            return true;
        }

        var normalizedUsername = NormalizeAdminText(username);
        var normalizedGroupName = NormalizeAdminText(groupName);
        return normalizedUsername is "admin" or "administrator" or "quantri" or "quantrihethong"
            || normalizedGroupName.Contains("admin", StringComparison.Ordinal)
            || normalizedGroupName.Contains("quantri", StringComparison.Ordinal);
    }

    private static string NormalizeAdminText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var normalized = value.Trim().ToLowerInvariant()
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal);

        return normalized
            .Replace("ả", "a", StringComparison.Ordinal)
            .Replace("à", "a", StringComparison.Ordinal)
            .Replace("á", "a", StringComparison.Ordinal)
            .Replace("ạ", "a", StringComparison.Ordinal)
            .Replace("ã", "a", StringComparison.Ordinal)
            .Replace("ă", "a", StringComparison.Ordinal)
            .Replace("ằ", "a", StringComparison.Ordinal)
            .Replace("ắ", "a", StringComparison.Ordinal)
            .Replace("ặ", "a", StringComparison.Ordinal)
            .Replace("ẵ", "a", StringComparison.Ordinal)
            .Replace("ẳ", "a", StringComparison.Ordinal)
            .Replace("â", "a", StringComparison.Ordinal)
            .Replace("ầ", "a", StringComparison.Ordinal)
            .Replace("ấ", "a", StringComparison.Ordinal)
            .Replace("ậ", "a", StringComparison.Ordinal)
            .Replace("ẫ", "a", StringComparison.Ordinal)
            .Replace("ẩ", "a", StringComparison.Ordinal)
            .Replace("đ", "d", StringComparison.Ordinal)
            .Replace("ị", "i", StringComparison.Ordinal)
            .Replace("ì", "i", StringComparison.Ordinal)
            .Replace("í", "i", StringComparison.Ordinal)
            .Replace("ỉ", "i", StringComparison.Ordinal)
            .Replace("ĩ", "i", StringComparison.Ordinal)
            .Replace("ộ", "o", StringComparison.Ordinal)
            .Replace("ồ", "o", StringComparison.Ordinal)
            .Replace("ố", "o", StringComparison.Ordinal)
            .Replace("ổ", "o", StringComparison.Ordinal)
            .Replace("ỗ", "o", StringComparison.Ordinal)
            .Replace("ơ", "o", StringComparison.Ordinal)
            .Replace("ờ", "o", StringComparison.Ordinal)
            .Replace("ớ", "o", StringComparison.Ordinal)
            .Replace("ợ", "o", StringComparison.Ordinal)
            .Replace("ở", "o", StringComparison.Ordinal)
            .Replace("ỡ", "o", StringComparison.Ordinal)
            .Replace("ò", "o", StringComparison.Ordinal)
            .Replace("ó", "o", StringComparison.Ordinal)
            .Replace("ọ", "o", StringComparison.Ordinal)
            .Replace("ỏ", "o", StringComparison.Ordinal)
            .Replace("õ", "o", StringComparison.Ordinal)
            .Replace("ư", "u", StringComparison.Ordinal)
            .Replace("ừ", "u", StringComparison.Ordinal)
            .Replace("ứ", "u", StringComparison.Ordinal)
            .Replace("ự", "u", StringComparison.Ordinal)
            .Replace("ử", "u", StringComparison.Ordinal)
            .Replace("ữ", "u", StringComparison.Ordinal)
            .Replace("ù", "u", StringComparison.Ordinal)
            .Replace("ú", "u", StringComparison.Ordinal)
            .Replace("ụ", "u", StringComparison.Ordinal)
            .Replace("ủ", "u", StringComparison.Ordinal)
            .Replace("ũ", "u", StringComparison.Ordinal)
            .Replace("ỳ", "y", StringComparison.Ordinal)
            .Replace("ý", "y", StringComparison.Ordinal)
            .Replace("ỵ", "y", StringComparison.Ordinal)
            .Replace("ỷ", "y", StringComparison.Ordinal)
            .Replace("ỹ", "y", StringComparison.Ordinal);
    }

    private static async Task<bool> IsAdministratorAccountAsync(
        SqlConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction)transaction;
        command.CommandText = $"""
            SELECT
                CAST(ISNULL(QuanTriVien, 0) AS bit) AS QuanTriVien,
                TenDangNhap,
                NhomNguoiDung
            FROM [{UserTableName}]
            WHERE IDTaiKhoan = @IDTaiKhoan
            """;
        command.Parameters.Add(new SqlParameter("@IDTaiKhoan", SqlDbType.UniqueIdentifier) { Value = accountId });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return false;
        }

        return IsAdministratorAccount(
            GetNullableBoolean(reader, "QuanTriVien") ?? false,
            GetNullableString(reader, "TenDangNhap"),
            GetNullableString(reader, "NhomNguoiDung"));
    }

    private static async Task<bool> EmployeeHasAdministratorAccountAsync(
        SqlConnection connection,
        System.Data.Common.DbTransaction transaction,
        int employeeId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction)transaction;
        command.CommandText = $"""
            SELECT COUNT(1)
            FROM [{UserTableName}]
            WHERE IDNhanVien = @IDNhanVien
                AND (
                    ISNULL(QuanTriVien, 0) = 1
                    OR UPPER(LTRIM(RTRIM(TenDangNhap))) IN (N'ADMIN', N'ADMINISTRATOR')
                    OR NhomNguoiDung COLLATE {SearchCollation} LIKE N'%admin%'
                    OR NhomNguoiDung COLLATE {SearchCollation} LIKE N'%quan tri%'
                    OR NhomNguoiDung LIKE N'%quản trị%'
                )
            """;
        command.Parameters.Add(new SqlParameter("@IDNhanVien", SqlDbType.Int) { Value = employeeId });

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0) > 0;
    }

    private async Task UpsertAccountAsync(
        SqlConnection connection,
        System.Data.Common.DbTransaction transaction,
        int employeeId,
        NhanVienFormModel model,
        string currentUser,
        CancellationToken cancellationToken)
    {
        if (model.IDTaiKhoan.HasValue)
        {
            await UpdateAccountAsync(connection, transaction, employeeId, model, currentUser, cancellationToken);
            return;
        }

        await InsertAccountAsync(connection, transaction, employeeId, model, currentUser, cancellationToken);
    }

    private async Task<int> InsertEmployeeAsync(
        SqlConnection connection,
        System.Data.Common.DbTransaction transaction,
        NhanVienFormModel model,
        string currentUser,
        CancellationToken cancellationToken)
    {
        var columns = await GetAvailableColumnsAsync(connection, TableName, transaction, cancellationToken);
        var genderDbValue = await BuildGenderDbValueAsync(connection, transaction, TableName, model.GioiTinh, cancellationToken);
        var fieldNames = new List<string>();
        var parameterNames = new List<string>();
        await using var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction)transaction;

        AddInsertValue(command, columns, fieldNames, parameterNames, "Ho", "@Ho", SqlDbType.NVarChar, model.Ho);
        AddInsertValue(command, columns, fieldNames, parameterNames, "Ten", "@Ten", SqlDbType.NVarChar, model.Ten);
        AddInsertValue(command, columns, fieldNames, parameterNames, "GioiTinh", "@GioiTinh", genderDbValue.DbType, genderDbValue.Value);
        AddInsertValue(command, columns, fieldNames, parameterNames, "NgaySinh", "@NgaySinh", SqlDbType.DateTime, model.NgaySinh);
        AddInsertValue(command, columns, fieldNames, parameterNames, "IDPhongBan", "@IDPhongBan", SqlDbType.Int, model.IDPhongBan);
        AddInsertValue(command, columns, fieldNames, parameterNames, "TrangThaiSuDung", "@TrangThaiSuDung", SqlDbType.Bit, model.TrangThaiSuDung);
        AddInsertValue(command, columns, fieldNames, parameterNames, "ChucVu", "@ChucVu", SqlDbType.NVarChar, model.ChucVu);
        AddInsertValue(command, columns, fieldNames, parameterNames, "Email", "@Email", SqlDbType.NVarChar, model.Email);
        AddInsertValue(command, columns, fieldNames, parameterNames, "Avatar", "@Avatar", SqlDbType.NVarChar, model.Avatar);
        AddInsertValue(command, columns, fieldNames, parameterNames, "NguoiTao", "@NguoiTao", SqlDbType.NVarChar, TrimToLength(currentUser, 100));
        AddInsertValue(command, columns, fieldNames, parameterNames, "NgayTao", "@NgayTao", SqlDbType.DateTime, DateTime.Now);
        AddInsertValue(command, columns, fieldNames, parameterNames, "NguoiCapNhat", "@NguoiCapNhat", SqlDbType.NVarChar, TrimToLength(currentUser, 100));
        AddInsertValue(command, columns, fieldNames, parameterNames, "NgayCapNhat", "@NgayCapNhat", SqlDbType.DateTime, DateTime.Now);

        command.CommandText = $"""
            INSERT INTO [{TableName}] ({string.Join(", ", fieldNames.Select(QuoteIdentifier))})
            VALUES ({string.Join(", ", parameterNames)});

            SELECT CAST(SCOPE_IDENTITY() AS int);
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private async Task<int> UpdateEmployeeAsync(
        SqlConnection connection,
        System.Data.Common.DbTransaction transaction,
        NhanVienFormModel model,
        string currentUser,
        CancellationToken cancellationToken)
    {
        var columns = await GetAvailableColumnsAsync(connection, TableName, transaction, cancellationToken);
        var genderDbValue = await BuildGenderDbValueAsync(connection, transaction, TableName, model.GioiTinh, cancellationToken);
        var updates = new List<string>();
        await using var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction)transaction;
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id!.Value });

        AddUpdateValue(command, columns, updates, "Ho", "@Ho", SqlDbType.NVarChar, model.Ho);
        AddUpdateValue(command, columns, updates, "Ten", "@Ten", SqlDbType.NVarChar, model.Ten);
        AddUpdateValue(command, columns, updates, "GioiTinh", "@GioiTinh", genderDbValue.DbType, genderDbValue.Value);
        AddUpdateValue(command, columns, updates, "NgaySinh", "@NgaySinh", SqlDbType.DateTime, model.NgaySinh);
        AddUpdateValue(command, columns, updates, "IDPhongBan", "@IDPhongBan", SqlDbType.Int, model.IDPhongBan);
        AddUpdateValue(command, columns, updates, "TrangThaiSuDung", "@TrangThaiSuDung", SqlDbType.Bit, model.TrangThaiSuDung);
        AddUpdateValue(command, columns, updates, "ChucVu", "@ChucVu", SqlDbType.NVarChar, model.ChucVu);
        AddUpdateValue(command, columns, updates, "Email", "@Email", SqlDbType.NVarChar, model.Email);
        AddUpdateValue(command, columns, updates, "Avatar", "@Avatar", SqlDbType.NVarChar, model.Avatar);
        AddUpdateValue(command, columns, updates, "NguoiCapNhat", "@NguoiCapNhat", SqlDbType.NVarChar, TrimToLength(currentUser, 100));
        AddUpdateValue(command, columns, updates, "NgayCapNhat", "@NgayCapNhat", SqlDbType.DateTime, DateTime.Now);

        command.CommandText = $"""
            UPDATE [{TableName}]
            SET {string.Join(", ", updates)}
            WHERE ID = @Id
            """;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertAccountAsync(
        SqlConnection connection,
        System.Data.Common.DbTransaction transaction,
        int employeeId,
        NhanVienFormModel model,
        string currentUser,
        CancellationToken cancellationToken)
    {
        var columns = await GetAvailableColumnsAsync(connection, UserTableName, transaction, cancellationToken);
        var genderDbValue = await BuildGenderDbValueAsync(connection, transaction, UserTableName, model.GioiTinh, cancellationToken);
        var id = Guid.NewGuid();
        var fieldNames = new List<string> { "IDTaiKhoan", "TenDangNhap", "MatKhau", "Ho", "Ten", "Email" };
        var parameterNames = new List<string> { "@IDTaiKhoan", "@TenDangNhap", "@MatKhau", "@Ho", "@Ten", "@Email" };

        await using var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction)transaction;
        command.Parameters.Add(new SqlParameter("@IDTaiKhoan", SqlDbType.UniqueIdentifier) { Value = id });
        command.Parameters.Add(new SqlParameter("@TenDangNhap", SqlDbType.NVarChar, 100) { Value = model.TenDangNhap!.Trim() });
        command.Parameters.Add(new SqlParameter("@MatKhau", SqlDbType.NVarChar, -1) { Value = PasswordSecurity.HashPassword(model.MatKhau!) });
        command.Parameters.Add(new SqlParameter("@Ho", SqlDbType.NVarChar, 120) { Value = ToDbValue(model.Ho) });
        command.Parameters.Add(new SqlParameter("@Ten", SqlDbType.NVarChar, 80) { Value = ToDbValue(model.Ten) });
        command.Parameters.Add(new SqlParameter("@Email", SqlDbType.NVarChar, 150) { Value = ToDbValue(model.Email) });

        AddInsertValue(command, columns, fieldNames, parameterNames, "IDNhanVien", "@IDNhanVien", SqlDbType.Int, employeeId);
        AddInsertValue(command, columns, fieldNames, parameterNames, "NgaySinh", "@NgaySinh", SqlDbType.DateTime, model.NgaySinh);
        AddInsertValue(command, columns, fieldNames, parameterNames, "GioiTinh", "@GioiTinh", genderDbValue.DbType, genderDbValue.Value);
        AddInsertValue(command, columns, fieldNames, parameterNames, "TrangThaiKichHoat", "@TrangThaiKichHoat", SqlDbType.Bit, model.TrangThaiSuDung);
        AddInsertValue(command, columns, fieldNames, parameterNames, "QuanTriVien", "@QuanTriVien", SqlDbType.Bit, false);
        AddInsertValue(command, columns, fieldNames, parameterNames, "NhomNguoiDung", "@NhomNguoiDung", SqlDbType.NVarChar, "Nhân viên");
        AddInsertValue(command, columns, fieldNames, parameterNames, "NguoiTao", "@NguoiTao", SqlDbType.NVarChar, TrimToLength(currentUser, 100));
        AddInsertValue(command, columns, fieldNames, parameterNames, "NgayTao", "@NgayTao", SqlDbType.DateTime, DateTime.Now);
        AddInsertValue(command, columns, fieldNames, parameterNames, "NguoiCapNhatCuoi", "@NguoiCapNhatCuoi", SqlDbType.NVarChar, TrimToLength(currentUser, 100));
        AddInsertValue(command, columns, fieldNames, parameterNames, "NgayCapNhatCuoi", "@NgayCapNhatCuoi", SqlDbType.DateTime, DateTime.Now);

        var avatarColumn = UserAvatarColumns.FirstOrDefault(columns.Contains);
        if (avatarColumn is not null)
        {
            AddInsertValue(command, columns, fieldNames, parameterNames, avatarColumn, "@Avatar", SqlDbType.NVarChar, model.Avatar);
        }

        command.CommandText = $"""
            INSERT INTO [{UserTableName}] ({string.Join(", ", fieldNames.Select(QuoteIdentifier))})
            VALUES ({string.Join(", ", parameterNames)})
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        model.IDTaiKhoan = id;
    }

    private async Task UpdateAccountAsync(
        SqlConnection connection,
        System.Data.Common.DbTransaction transaction,
        int employeeId,
        NhanVienFormModel model,
        string currentUser,
        CancellationToken cancellationToken)
    {
        var columns = await GetAvailableColumnsAsync(connection, UserTableName, transaction, cancellationToken);
        var genderDbValue = await BuildGenderDbValueAsync(connection, transaction, UserTableName, model.GioiTinh, cancellationToken);
        var updates = new List<string>
        {
            "TenDangNhap = @TenDangNhap",
            "Ho = @Ho",
            "Ten = @Ten",
            "Email = @Email"
        };

        await using var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction)transaction;
        command.Parameters.Add(new SqlParameter("@IDTaiKhoan", SqlDbType.UniqueIdentifier) { Value = model.IDTaiKhoan!.Value });
        command.Parameters.Add(new SqlParameter("@TenDangNhap", SqlDbType.NVarChar, 100) { Value = model.TenDangNhap!.Trim() });
        command.Parameters.Add(new SqlParameter("@Ho", SqlDbType.NVarChar, 120) { Value = ToDbValue(model.Ho) });
        command.Parameters.Add(new SqlParameter("@Ten", SqlDbType.NVarChar, 80) { Value = ToDbValue(model.Ten) });
        command.Parameters.Add(new SqlParameter("@Email", SqlDbType.NVarChar, 150) { Value = ToDbValue(model.Email) });

        AddUpdateValue(command, columns, updates, "IDNhanVien", "@IDNhanVien", SqlDbType.Int, employeeId);
        AddUpdateValue(command, columns, updates, "NgaySinh", "@NgaySinh", SqlDbType.DateTime, model.NgaySinh);
        AddUpdateValue(command, columns, updates, "GioiTinh", "@GioiTinh", genderDbValue.DbType, genderDbValue.Value);
        AddUpdateValue(command, columns, updates, "TrangThaiKichHoat", "@TrangThaiKichHoat", SqlDbType.Bit, model.TrangThaiSuDung);
        AddUpdateValue(command, columns, updates, "NguoiCapNhatCuoi", "@NguoiCapNhatCuoi", SqlDbType.NVarChar, TrimToLength(currentUser, 100));

        if (columns.Contains("NgayCapNhatCuoi"))
        {
            updates.Add("NgayCapNhatCuoi = GETDATE()");
        }

        var avatarColumn = UserAvatarColumns.FirstOrDefault(columns.Contains);
        if (avatarColumn is not null)
        {
            AddUpdateValue(command, columns, updates, avatarColumn, "@Avatar", SqlDbType.NVarChar, model.Avatar);
        }

        if (!string.IsNullOrWhiteSpace(model.MatKhau))
        {
            updates.Add("MatKhau = @MatKhau");
            command.Parameters.Add(new SqlParameter("@MatKhau", SqlDbType.NVarChar, -1) { Value = PasswordSecurity.HashPassword(model.MatKhau) });
        }

        command.CommandText = $"""
            UPDATE [{UserTableName}]
            SET {string.Join(", ", updates)}
            WHERE IDTaiKhoan = @IDTaiKhoan
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UnlinkAccountAsync(
        SqlConnection connection,
        System.Data.Common.DbTransaction transaction,
        Guid accountId,
        string currentUser,
        CancellationToken cancellationToken)
    {
        var columns = await GetAvailableColumnsAsync(connection, UserTableName, transaction, cancellationToken);
        if (!columns.Contains("IDNhanVien"))
        {
            return;
        }

        var updates = new List<string> { "IDNhanVien = NULL" };
        await using var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction)transaction;
        command.Parameters.Add(new SqlParameter("@IDTaiKhoan", SqlDbType.UniqueIdentifier) { Value = accountId });
        AddUpdateValue(command, columns, updates, "NguoiCapNhatCuoi", "@NguoiCapNhatCuoi", SqlDbType.NVarChar, TrimToLength(currentUser, 100));
        if (columns.Contains("NgayCapNhatCuoi"))
        {
            updates.Add("NgayCapNhatCuoi = GETDATE()");
        }

        command.CommandText = $"""
            UPDATE [{UserTableName}]
            SET {string.Join(", ", updates)}
            WHERE IDTaiKhoan = @IDTaiKhoan
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<string?> ValidateDuplicateUsernameAsync(
        SqlConnection connection,
        System.Data.Common.DbTransaction transaction,
        string? username,
        Guid? excludedAccountId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return "Vui lòng nhập tên đăng nhập.";
        }

        await using var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction)transaction;
        command.CommandText = $"""
            SELECT COUNT(1)
            FROM [{UserTableName}]
            WHERE UPPER(LTRIM(RTRIM(TenDangNhap))) = UPPER(@TenDangNhap)
                AND (@ExcludedAccountId IS NULL OR IDTaiKhoan <> @ExcludedAccountId)
            """;
        command.Parameters.Add(new SqlParameter("@TenDangNhap", SqlDbType.NVarChar, 100) { Value = username.Trim() });
        command.Parameters.Add(new SqlParameter("@ExcludedAccountId", SqlDbType.UniqueIdentifier) { Value = excludedAccountId.HasValue ? excludedAccountId.Value : DBNull.Value });

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
        return count > 0 ? "Tên đăng nhập đã tồn tại." : null;
    }

    private static string? ValidateAccountRequest(NhanVienFormModel model, bool isCreate)
    {
        if (!model.TaoTaiKhoan)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(model.TenDangNhap))
        {
            return "Vui lòng nhập tên đăng nhập khi tạo tài khoản.";
        }

        if (model.TenDangNhap.Any(char.IsWhiteSpace))
        {
            return "Tên đăng nhập không được chứa khoảng trắng.";
        }

        if ((isCreate || !model.IDTaiKhoan.HasValue) && string.IsNullOrWhiteSpace(model.MatKhau))
        {
            return "Vui lòng nhập mật khẩu khi tạo tài khoản.";
        }

        if (!string.IsNullOrEmpty(model.MatKhau) && model.MatKhau.Any(char.IsWhiteSpace))
        {
            return "Mật khẩu không được chứa khoảng trắng.";
        }

        return null;
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

    private static Task<HashSet<string>> GetAvailableColumnsAsync(SqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        return GetAvailableColumnsAsync(connection, tableName, null, cancellationToken);
    }

    private static async Task<HashSet<string>> GetAvailableColumnsAsync(
        SqlConnection connection,
        string tableName,
        System.Data.Common.DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@TableName)
            """;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction as SqlTransaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("@TableName", tableName);

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static void AddInsertValue(SqlCommand command, HashSet<string> columns, ICollection<string> fieldNames, ICollection<string> parameterNames, string columnName, string parameterName, SqlDbType dbType, object? value)
    {
        if (!columns.Contains(columnName))
        {
            return;
        }

        fieldNames.Add(columnName);
        parameterNames.Add(parameterName);
        command.Parameters.Add(new SqlParameter(parameterName, dbType) { Value = ToDbValue(value) });
    }

    private static void AddUpdateValue(SqlCommand command, HashSet<string> columns, ICollection<string> updates, string columnName, string parameterName, SqlDbType dbType, object? value)
    {
        if (!columns.Contains(columnName))
        {
            return;
        }

        updates.Add($"{QuoteIdentifier(columnName)} = {parameterName}");
        command.Parameters.Add(new SqlParameter(parameterName, dbType) { Value = ToDbValue(value) });
    }

    private static async Task<(SqlDbType DbType, object? Value)> BuildGenderDbValueAsync(
        SqlConnection connection,
        System.Data.Common.DbTransaction transaction,
        string tableName,
        string? gender,
        CancellationToken cancellationToken)
    {
        var normalizedGender = NormalizeGenderDisplay(gender);
        var isBitColumn = await IsBitColumnAsync(connection, transaction, tableName, "GioiTinh", cancellationToken);
        if (!isBitColumn)
        {
            return (SqlDbType.NVarChar, normalizedGender);
        }

        return normalizedGender switch
        {
            "Nam" => (SqlDbType.Bit, true),
            "Nữ" => (SqlDbType.Bit, false),
            _ => (SqlDbType.Bit, null)
        };
    }

    private static async Task<bool> IsBitColumnAsync(
        SqlConnection connection,
        System.Data.Common.DbTransaction transaction,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction)transaction;
        command.CommandText = """
            SELECT TYPE_NAME(system_type_id)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@TableName)
              AND name = @ColumnName
            """;
        command.Parameters.AddWithValue("@TableName", tableName);
        command.Parameters.AddWithValue("@ColumnName", columnName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return string.Equals(result?.ToString(), "bit", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeGenderDisplay(string? gender)
    {
        if (string.IsNullOrWhiteSpace(gender))
        {
            return null;
        }

        var value = gender.Trim();
        if (string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
        {
            return "Nam";
        }

        if (string.Equals(value, "False", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
        {
            return "Nữ";
        }

        return value switch
        {
            "Ná»¯" => "Nữ",
            "KhÃ¡c" => "Khác",
            _ => value
        };
    }

    private static object ToDbValue(object? value)
    {
        return value switch
        {
            null => DBNull.Value,
            string text when string.IsNullOrWhiteSpace(text) => DBNull.Value,
            string text => text.Trim(),
            DateTime date => date,
            bool flag => flag,
            int number => number,
            Guid guid => guid,
            _ => value
        };
    }

    private static string? NormalizeKeyword(string? keyword)
    {
        return string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
    }

    private static string TrimToLength(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "system" : value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static string? GetNullableString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal)?.ToString()?.Trim();
    }

    private static DateTime? GetNullableDateTime(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }

    private static int? GetNullableInt32(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static Guid? GetNullableGuid(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        return value is Guid guid ? guid : Guid.TryParse(value.ToString(), out var parsed) ? parsed : null;
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
            long typedLong => typedLong != 0,
            string typedString => bool.TryParse(typedString, out var parsedBool)
                ? parsedBool
                : int.TryParse(typedString, out var parsedInt) && parsedInt != 0,
            _ => null
        };
    }
}
