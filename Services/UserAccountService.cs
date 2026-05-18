using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IUserAccountService
{
    Task<UserAuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    Task<UserAccount?> GetAccountByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<UserProfileUpdateResult> UpdateProfileAsync(
        UserProfileUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<PasswordChangeResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        string updatedBy,
        CancellationToken cancellationToken = default);
}

public sealed record UserAuthenticationResult(
    bool Succeeded,
    string? ErrorMessage = null,
    UserAccount? Account = null);

public sealed record UserProfileUpdateRequest(
    Guid UserId,
    string FullName,
    string? Email,
    DateTime? DateOfBirth,
    string? Address,
    string? PhoneNumber,
    string? Gender,
    string? GroupName,
    string? ZaloId,
    string? AvatarUrl,
    string UpdatedBy);

public sealed record UserProfileUpdateResult(
    bool Succeeded,
    string? ErrorMessage = null,
    UserAccount? Account = null);

public sealed record PasswordChangeResult(
    bool Succeeded,
    string? ErrorMessage = null);

public sealed class UserAccountService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<UserAccountService> logger) : IUserAccountService
{
    private const string UserTableName = "TblTaiKhoanNguoiDung";

    private static readonly string[] DateOfBirthColumns = ["NgaySinh", "DateOfBirth", "BirthDate", "NgayThangNamSinh", "NamSinh"];
    private static readonly string[] AddressColumns = ["DiaChi", "Address", "DiaChiLienHe", "ThuongTru"];
    private static readonly string[] PhoneColumns = ["SoDienThoai", "DienThoai", "Phone", "PhoneNumber", "SoDT", "SDT"];
    private static readonly string[] GenderColumns = ["GioiTinh", "Gender"];
    private static readonly string[] ZaloIdColumns = ["ZaloID", "ZaloId", "Zalo", "IDZalo"];
    private static readonly string[] AvatarColumns = ["AnhDaiDien", "Avatar", "AvatarUrl", "DuongDanAvatar", "HinhDaiDien", "HinhAnhDaiDien", "DuongDanAnh"];
    private static readonly string[] FullNameColumns = ["HoTen", "TenDayDu", "FullName"];
    private static readonly string[] EmployeeIdColumns = ["IDNhanVien", "NhanVienId", "EmployeeId"];

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<UserAccountService> _logger = logger;

    public async Task<UserAuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return new UserAuthenticationResult(false, "Tên đăng nhập hoặc mật khẩu không đúng.");
        }

        if (string.IsNullOrWhiteSpace(_connectionString) && !_sqlOptions.IsConfigured)
        {
            _logger.LogError("Database connection is not configured for user authentication.");
            return new UserAuthenticationResult(false, "Hệ thống đang tạm thời không kết nối được cơ sở dữ liệu.");
        }

        try
        {
            var account = await LoadAccountByUsernameAsync(username.Trim(), cancellationToken);

            if (account is null)
            {
                return new UserAuthenticationResult(false, "Tên đăng nhập hoặc mật khẩu không đúng.");
            }

            if (!account.IsActive)
            {
                return new UserAuthenticationResult(false, "Tài khoản hiện đang bị khóa.");
            }

            var verificationStatus = PasswordSecurity.VerifyPassword(password, account.PasswordHash);
            if (verificationStatus == PasswordVerificationStatus.Failed)
            {
                return new UserAuthenticationResult(false, "Tên đăng nhập hoặc mật khẩu không đúng.");
            }

            if (verificationStatus == PasswordVerificationStatus.SuccessRehashNeeded)
            {
                account.PasswordHash = PasswordSecurity.HashPassword(password);
                await UpdatePasswordHashAsync(account.Id, account.PasswordHash, account.Username, cancellationToken);
            }

            return new UserAuthenticationResult(true, Account: account);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to authenticate user {Username}.", username);
            return new UserAuthenticationResult(false, "Hệ thống không thể xác thực tài khoản lúc này.");
        }
    }

    public async Task<UserAccount?> GetAccountByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return null;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var availableColumns = await GetAvailableColumnsAsync(connection, cancellationToken);

            return await LoadAccountAsync(
                connection,
                availableColumns,
                "IDTaiKhoan = @UserId",
                command =>
                {
                    command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.UniqueIdentifier) { Value = userId });
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load user account {UserId}.", userId);
            return null;
        }
    }

    public async Task<UserProfileUpdateResult> UpdateProfileAsync(
        UserProfileUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.UserId == Guid.Empty)
        {
            return new UserProfileUpdateResult(false, "Không xác định được tài khoản cần cập nhật.");
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return new UserProfileUpdateResult(false, "Thông tin hồ sơ chưa đầy đủ.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var availableColumns = await GetAvailableColumnsAsync(connection, cancellationToken);
            var currentAccount = await LoadAccountAsync(
                connection,
                availableColumns,
                "IDTaiKhoan = @UserId",
                command =>
                {
                    command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.UniqueIdentifier) { Value = request.UserId });
                },
                cancellationToken);

            if (currentAccount is null)
            {
                return new UserProfileUpdateResult(false, "Không tìm thấy tài khoản để cập nhật.");
            }

            var updates = new List<string>();
            await using var command = connection.CreateCommand();
            command.Parameters.Add(new SqlParameter("@UserId", SqlDbType.UniqueIdentifier) { Value = request.UserId });

            var (lastName, firstName) = SplitFullName(request.FullName);

            if (availableColumns.Contains("Ho"))
            {
                updates.Add("Ho = @Ho");
                command.Parameters.AddWithValue("@Ho", ToDbValue(lastName));
            }

            if (availableColumns.Contains("Ten"))
            {
                updates.Add("Ten = @Ten");
                command.Parameters.AddWithValue("@Ten", ToDbValue(firstName));
            }

            var fullNameColumn = ResolveColumn(availableColumns, FullNameColumns);
            if (fullNameColumn is not null)
            {
                updates.Add($"{QuoteIdentifier(fullNameColumn)} = @FullName");
                command.Parameters.AddWithValue("@FullName", request.FullName.Trim());
            }

            if (availableColumns.Contains("Email"))
            {
                updates.Add("Email = @Email");
                command.Parameters.AddWithValue("@Email", ToDbValue(request.Email));
            }

            AddOptionalStringUpdate(command, updates, ResolveColumn(availableColumns, AddressColumns), "@Address", request.Address);
            AddOptionalStringUpdate(command, updates, ResolveColumn(availableColumns, PhoneColumns), "@PhoneNumber", request.PhoneNumber);
            AddOptionalStringUpdate(command, updates, ResolveColumn(availableColumns, GenderColumns), "@Gender", request.Gender);
            AddOptionalStringUpdate(command, updates, "NhomNguoiDung", "@GroupName", request.GroupName);
            AddOptionalStringUpdate(command, updates, ResolveColumn(availableColumns, ZaloIdColumns), "@ZaloId", request.ZaloId);
            AddOptionalStringUpdate(command, updates, ResolveColumn(availableColumns, AvatarColumns), "@AvatarUrl", request.AvatarUrl);

            var dateOfBirthColumn = ResolveColumn(availableColumns, DateOfBirthColumns);
            if (dateOfBirthColumn is not null)
            {
                updates.Add($"{QuoteIdentifier(dateOfBirthColumn)} = @DateOfBirth");
                command.Parameters.Add(new SqlParameter("@DateOfBirth", SqlDbType.DateTime)
                {
                    Value = request.DateOfBirth.HasValue ? request.DateOfBirth.Value : DBNull.Value
                });
            }

            if (availableColumns.Contains("NguoiCapNhatCuoi"))
            {
                updates.Add("NguoiCapNhatCuoi = @NguoiCapNhatCuoi");
                command.Parameters.AddWithValue("@NguoiCapNhatCuoi", request.UpdatedBy.Trim());
            }

            if (availableColumns.Contains("NgayCapNhatCuoi"))
            {
                updates.Add("NgayCapNhatCuoi = GETDATE()");
            }

            if (updates.Count == 0)
            {
                return new UserProfileUpdateResult(false, "Không có trường dữ liệu phù hợp để cập nhật hồ sơ.");
            }

            command.CommandText = $"""
                UPDATE {QuoteIdentifier(UserTableName)}
                SET {string.Join(", ", updates)}
                WHERE IDTaiKhoan = @UserId
                """;

            await command.ExecuteNonQueryAsync(cancellationToken);

            var refreshedAccount = await LoadAccountAsync(
                connection,
                availableColumns,
                "IDTaiKhoan = @UserId",
                reloadCommand =>
                {
                    reloadCommand.Parameters.Add(new SqlParameter("@UserId", SqlDbType.UniqueIdentifier) { Value = request.UserId });
                },
                cancellationToken);

            return refreshedAccount is null
                ? new UserProfileUpdateResult(false, "Cập nhật thành công nhưng không thể nạp lại thông tin tài khoản.")
                : new UserProfileUpdateResult(true, Account: refreshedAccount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update profile for {UserId}.", request.UserId);
            return new UserProfileUpdateResult(false, "Không thể cập nhật thông tin cá nhân lúc này.");
        }
    }

    public async Task<PasswordChangeResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        string updatedBy,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return new PasswordChangeResult(false, "Không xác định được tài khoản đổi mật khẩu.");
        }

        if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
        {
            return new PasswordChangeResult(false, "Thông tin đổi mật khẩu chưa đầy đủ.");
        }

        try
        {
            var account = await GetAccountByIdAsync(userId, cancellationToken);
            if (account is null)
            {
                return new PasswordChangeResult(false, "Không tìm thấy tài khoản để đổi mật khẩu.");
            }

            if (PasswordSecurity.VerifyPassword(currentPassword, account.PasswordHash) == PasswordVerificationStatus.Failed)
            {
                return new PasswordChangeResult(false, "Mật khẩu hiện tại không đúng.");
            }

            await UpdatePasswordHashAsync(
                userId,
                PasswordSecurity.HashPassword(newPassword),
                updatedBy,
                cancellationToken);

            return new PasswordChangeResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change password for {UserId}.", userId);
            return new PasswordChangeResult(false, "Không thể đổi mật khẩu lúc này.");
        }
    }

    private async Task<UserAccount?> LoadAccountByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var availableColumns = await GetAvailableColumnsAsync(connection, cancellationToken);

        return await LoadAccountAsync(
            connection,
            availableColumns,
            "UPPER(LTRIM(RTRIM(TenDangNhap))) = UPPER(@Username)",
            command =>
            {
                command.Parameters.Add(new SqlParameter("@Username", SqlDbType.NVarChar, 100) { Value = username });
            },
            cancellationToken);
    }

    private async Task<UserAccount?> LoadAccountAsync(
        SqlConnection connection,
        HashSet<string> availableColumns,
        string filterClause,
        Action<SqlCommand> configureParameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = BuildSelectSql(filterClause, availableColumns);
        configureParameters(command);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new UserAccount
        {
            Id = GetGuid(reader, "IDTaiKhoan"),
            EmployeeId = GetNullableInt32(reader, "IDNhanVien"),
            Username = GetNullableString(reader, "TenDangNhap") ?? "",
            LastName = GetNullableString(reader, "Ho") ?? "",
            FirstName = GetNullableString(reader, "Ten") ?? "",
            Email = GetNullableString(reader, "Email") ?? "",
            DateOfBirth = GetNullableDateTime(reader, "DateOfBirth"),
            Address = GetNullableString(reader, "Address") ?? "",
            PhoneNumber = GetNullableString(reader, "PhoneNumber") ?? "",
            Gender = GetNullableString(reader, "Gender") ?? "",
            ZaloId = GetNullableString(reader, "ZaloId") ?? "",
            AvatarUrl = GetNullableString(reader, "AvatarUrl") ?? "",
            PasswordHash = GetNullableString(reader, "MatKhau") ?? "",
            IsActive = GetNullableBoolean(reader, "TrangThaiKichHoat") ?? true,
            IsAdministrator = GetNullableBoolean(reader, "QuanTriVien") ?? false,
            GroupName = GetNullableString(reader, "NhomNguoiDung") ?? ""
        };
    }

    private string BuildSelectSql(string filterClause, HashSet<string> availableColumns)
    {
        var dateOfBirthColumn = BuildNullableProjection(ResolveColumn(availableColumns, DateOfBirthColumns), "DateOfBirth", "datetime");
        var addressColumn = BuildNullableProjection(ResolveColumn(availableColumns, AddressColumns), "Address", "nvarchar(250)");
        var phoneColumn = BuildNullableProjection(ResolveColumn(availableColumns, PhoneColumns), "PhoneNumber", "nvarchar(50)");
        var genderColumn = BuildNullableProjection(ResolveColumn(availableColumns, GenderColumns), "Gender", "nvarchar(50)");
        var zaloIdColumn = BuildNullableProjection(ResolveColumn(availableColumns, ZaloIdColumns), "ZaloId", "nvarchar(100)");
        var avatarColumn = BuildNullableProjection(ResolveColumn(availableColumns, AvatarColumns), "AvatarUrl", "nvarchar(512)");
        var employeeIdColumn = BuildNullableProjection(ResolveColumn(availableColumns, EmployeeIdColumns), "IDNhanVien", "int");

        return $"""
            SELECT TOP (1)
                IDTaiKhoan,
                {employeeIdColumn},
                TenDangNhap,
                Ho,
                Ten,
                Email,
                MatKhau,
                TrangThaiKichHoat,
                QuanTriVien,
                NhomNguoiDung,
                {dateOfBirthColumn},
                {addressColumn},
                {phoneColumn},
                {genderColumn},
                {zaloIdColumn},
                {avatarColumn}
            FROM {QuoteIdentifier(UserTableName)}
            WHERE {filterClause}
            """;
    }

    private async Task UpdatePasswordHashAsync(
        Guid userId,
        string passwordHash,
        string updatedBy,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var availableColumns = await GetAvailableColumnsAsync(connection, cancellationToken);

        var updates = new List<string> { "MatKhau = @MatKhau" };

        await using var command = connection.CreateCommand();
        command.Parameters.Add(new SqlParameter("@IDTaiKhoan", SqlDbType.UniqueIdentifier) { Value = userId });
        command.Parameters.AddWithValue("@MatKhau", passwordHash);

        if (availableColumns.Contains("NguoiCapNhatCuoi"))
        {
            updates.Add("NguoiCapNhatCuoi = @NguoiCapNhatCuoi");
            command.Parameters.AddWithValue("@NguoiCapNhatCuoi", updatedBy);
        }

        if (availableColumns.Contains("NgayCapNhatCuoi"))
        {
            updates.Add("NgayCapNhatCuoi = GETDATE()");
        }

        command.CommandText = $"""
            UPDATE {QuoteIdentifier(UserTableName)}
            SET {string.Join(", ", updates)}
            WHERE IDTaiKhoan = @IDTaiKhoan
            """;

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

    private static async Task<HashSet<string>> GetAvailableColumnsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@TableName)
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@TableName", UserTableName);

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static void AddOptionalStringUpdate(
        SqlCommand command,
        ICollection<string> updates,
        string? columnName,
        string parameterName,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return;
        }

        updates.Add($"{QuoteIdentifier(columnName)} = {parameterName}");
        command.Parameters.AddWithValue(parameterName, ToDbValue(value));
    }

    private static object ToDbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static string BuildNullableProjection(string? columnName, string alias, string sqlType)
    {
        return columnName is null
            ? $"CAST(NULL AS {sqlType}) AS {QuoteIdentifier(alias)}"
            : $"{QuoteIdentifier(columnName)} AS {QuoteIdentifier(alias)}";
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static string? ResolveColumn(HashSet<string> availableColumns, params string[] candidates)
    {
        return candidates.FirstOrDefault(availableColumns.Contains);
    }

    private static (string LastName, string FirstName) SplitFullName(string fullName)
    {
        var normalized = fullName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return ("", "");
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 1)
        {
            return ("", tokens[0]);
        }

        return (string.Join(' ', tokens[..^1]), tokens[^1]);
    }

    private static Guid GetGuid(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.GetGuid(ordinal);
    }

    private static string? GetNullableString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal)?.ToString()?.Trim();
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
            DateOnly typedDateOnly => typedDateOnly.ToDateTime(TimeOnly.MinValue),
            string typedString when DateTime.TryParse(typedString, out var parsedDate) => parsedDate,
            _ => null
        };
    }
}
