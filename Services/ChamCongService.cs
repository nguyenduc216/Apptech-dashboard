using System.Data;
using System.Globalization;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IChamCongService
{
    Task<IReadOnlyList<ChamCongLocationOption>> GetApptechLocationsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChamCongHistoryItem>> GetHistoryAsync(
        int employeeId,
        DateTime date,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChamCongHistoryItem>> GetHistoryAsync(
        IReadOnlyCollection<int> employeeIds,
        DateTime date,
        CancellationToken cancellationToken = default);

    Task<ChamCongHistoryItem?> GetOpenCheckinAsync(int employeeId, CancellationToken cancellationToken = default);

    Task<ChamCongHistoryItem?> GetOpenCheckinAsync(int employeeId, DateTime date, CancellationToken cancellationToken = default);

    Task<decimal?> GetCheckinDistanceLimitMetersAsync(CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage, int? Id)> CheckinAsync(
        int employeeId,
        ChamCongCheckinRequest model,
        CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string? ErrorMessage)> CheckoutAsync(
        int employeeId,
        ChamCongCheckoutRequest model,
        CancellationToken cancellationToken = default);
}

public sealed class ChamCongService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<ChamCongService> logger) : IChamCongService
{
    private const string CheckinHistoryTableName = "TblCheckinHistory";
    private const string CustomerTableName = "TblKhachHang";
    private const string LocationTableName = "TblKhachHangDiaDiem";
    private const string SystemConfigTableName = "TblCauHinhHeThong";
    private const string ChamCongType = "ChamCong";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<ChamCongService> _logger = logger;

    private sealed record AttendanceSchedule(TimeSpan Begin1, TimeSpan End1, TimeSpan Begin2, TimeSpan End2);
    private sealed record AttendanceShift(TimeSpan Begin, TimeSpan End);

    public async Task<IReadOnlyList<ChamCongLocationOption>> GetApptechLocationsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT
                    dd.ID AS IDDiaDiem,
                    kh.ID AS IDKhachHang,
                    kh.MaKhachHang,
                    kh.TenKhachHang,
                    dd.DiaChi,
                    dd.LongAddress,
                    dd.LatAddress
                FROM [{LocationTableName}] AS dd
                INNER JOIN [{CustomerTableName}] AS kh ON kh.ID = dd.IDKhachHang
                WHERE kh.MaKhachHang LIKE @MaKhachHang
                  AND ISNULL(dd.TrangThaiSuDung, 1) = 1
                ORDER BY kh.TenKhachHang, dd.DiaChi, dd.ID
                """;
            command.Parameters.Add(new SqlParameter("@MaKhachHang", SqlDbType.NVarChar, 50) { Value = "Apptech%" });

            var items = new List<ChamCongLocationOption>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new ChamCongLocationOption
                {
                    IDDiaDiem = GetNullableInt32(reader, "IDDiaDiem") ?? 0,
                    IDKhachHang = GetNullableInt32(reader, "IDKhachHang") ?? 0,
                    MaKhachHang = GetNullableString(reader, "MaKhachHang") ?? string.Empty,
                    TenKhachHang = GetNullableString(reader, "TenKhachHang") ?? "AppTech",
                    DiaChi = GetNullableString(reader, "DiaChi") ?? string.Empty,
                    LongAddress = GetNullableDecimal(reader, "LongAddress"),
                    LatAddress = GetNullableDecimal(reader, "LatAddress")
                });
            }

            return items.Where(item => item.IDDiaDiem > 0 && item.IDKhachHang > 0).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load AppTech locations for attendance.");
            return [];
        }
    }

    public async Task<IReadOnlyList<ChamCongHistoryItem>> GetHistoryAsync(
        int employeeId,
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        return await GetHistoryAsync([employeeId], date, cancellationToken);
    }

    public async Task<IReadOnlyList<ChamCongHistoryItem>> GetHistoryAsync(
        IReadOnlyCollection<int> employeeIds,
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmployeeIds = employeeIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
        if (normalizedEmployeeIds.Length == 0)
        {
            return [];
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            var employeeFilters = new List<string>();
            for (var index = 0; index < normalizedEmployeeIds.Length; index++)
            {
                var parameterName = $"@IDNhanVien{index}";
                employeeFilters.Add(parameterName);
                command.Parameters.Add(new SqlParameter(parameterName, SqlDbType.Int) { Value = normalizedEmployeeIds[index] });
            }

            command.CommandText = BuildHistorySelect("""
                WHERE ch.IDNhanVien IN ({0})
                  AND ch.CheckInType = @CheckInType
                  AND ch.ThoiDiem >= @DateFrom
                  AND ch.ThoiDiem < DATEADD(day, 1, @DateFrom)
                ORDER BY ch.ThoiDiem ASC, ch.ID ASC
                """.Replace("{0}", string.Join(", ", employeeFilters)));
            command.Parameters.Add(new SqlParameter("@CheckInType", SqlDbType.NVarChar, 50) { Value = ChamCongType });
            command.Parameters.Add(new SqlParameter("@DateFrom", SqlDbType.DateTime) { Value = date.Date });

            var history = await ReadHistoryAsync(command, cancellationToken);
            await ApplyAttendanceViolationsAsync(connection, history, cancellationToken);
            return history;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load attendance history for employees {EmployeeIds}.", string.Join(",", normalizedEmployeeIds));
            return [];
        }
    }

    public async Task<ChamCongHistoryItem?> GetOpenCheckinAsync(int employeeId, CancellationToken cancellationToken = default)
    {
        if (employeeId <= 0)
        {
            return null;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            return await GetOpenCheckinAsync(connection, employeeId, date: null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load open attendance checkin for employee {EmployeeId}.", employeeId);
            return null;
        }
    }

    public async Task<ChamCongHistoryItem?> GetOpenCheckinAsync(int employeeId, DateTime date, CancellationToken cancellationToken = default)
    {
        if (employeeId <= 0)
        {
            return null;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            return await GetOpenCheckinAsync(connection, employeeId, date.Date, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load open attendance checkin for employee {EmployeeId} on {Date}.", employeeId, date.Date);
            return null;
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
            command.Parameters.Add(new SqlParameter("@MaCauHinh", SqlDbType.NVarChar, 100) { Value = "KM_limit" });
            return ParseNullableDecimal(await command.ExecuteScalarAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load KM_limit for attendance.");
            return null;
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage, int? Id)> CheckinAsync(
        int employeeId,
        ChamCongCheckinRequest model,
        CancellationToken cancellationToken = default)
    {
        if (employeeId <= 0)
        {
            return (false, "Tài khoản chưa liên kết nhân viên nên không thể chấm công.", null);
        }

        if (model.IDDiaDiem <= 0)
        {
            return (false, "Vui lòng chọn địa điểm chấm công.", null);
        }

        if (!model.LatAddress.HasValue || !model.LongAddress.HasValue)
        {
            return (false, "Vui lòng cấp quyền GPS để lấy vị trí chấm công.", null);
        }

        if (string.IsNullOrWhiteSpace(model.ImgPath))
        {
            return (false, "Vui lòng chụp ảnh checkin chấm công.", null);
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

            var location = await GetApptechLocationByIdAsync(connection, transaction, model.IDDiaDiem, cancellationToken);
            if (location is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Địa điểm chấm công không hợp lệ.", null);
            }

            var distanceError = await ValidateDistanceAsync(location, model.LatAddress.Value, model.LongAddress.Value, cancellationToken);
            if (distanceError is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, distanceError, null);
            }

            var checkinDate = (model.ThoiDiem ?? DateTime.Now).Date;
            if (await HasOpenCheckinAsync(connection, transaction, employeeId, checkinDate, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return (false, "Bạn đang có checkin chấm công chưa checkout trong ngày đang chọn.", null);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO [{CheckinHistoryTableName}] (
                    IDKhachHang,
                    IDNhanVien,
                    ThoiDiem,
                    IDDiaDiem,
                    IsCheckIn,
                    LongAddress,
                    LatAddress,
                    ImgPath,
                    GhiChuNhanVien,
                    CheckInType
                )
                VALUES (
                    @IDKhachHang,
                    @IDNhanVien,
                    @ThoiDiem,
                    @IDDiaDiem,
                    @IsCheckIn,
                    @LongAddress,
                    @LatAddress,
                    @ImgPath,
                    @GhiChuNhanVien,
                    @CheckInType
                );

                SELECT CAST(SCOPE_IDENTITY() AS int);
                """;
            command.Parameters.Add(new SqlParameter("@IDKhachHang", SqlDbType.Int) { Value = location.IDKhachHang });
            command.Parameters.Add(new SqlParameter("@IDNhanVien", SqlDbType.Int) { Value = employeeId });
            command.Parameters.Add(new SqlParameter("@ThoiDiem", SqlDbType.DateTime) { Value = model.ThoiDiem ?? DateTime.Now });
            command.Parameters.Add(new SqlParameter("@IDDiaDiem", SqlDbType.Int) { Value = location.IDDiaDiem });
            command.Parameters.Add(new SqlParameter("@IsCheckIn", SqlDbType.Bit) { Value = true });
            command.Parameters.Add(new SqlParameter("@LongAddress", SqlDbType.Decimal) { Precision = 18, Scale = 10, Value = model.LongAddress.Value });
            command.Parameters.Add(new SqlParameter("@LatAddress", SqlDbType.Decimal) { Precision = 18, Scale = 10, Value = model.LatAddress.Value });
            command.Parameters.Add(new SqlParameter("@ImgPath", SqlDbType.NVarChar, 500) { Value = model.ImgPath.Trim() });
            command.Parameters.Add(new SqlParameter("@GhiChuNhanVien", SqlDbType.NVarChar, 1000) { Value = ToDbValue(model.GhiChuNhanVien) });
            command.Parameters.Add(new SqlParameter("@CheckInType", SqlDbType.NVarChar, 50) { Value = ChamCongType });

            var id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
            await transaction.CommitAsync(cancellationToken);
            return (true, null, id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create attendance checkin for employee {EmployeeId}.", employeeId);
            return (false, "Không thể lưu thông tin chấm công.", null);
        }
    }

    public async Task<(bool Succeeded, string? ErrorMessage)> CheckoutAsync(
        int employeeId,
        ChamCongCheckoutRequest model,
        CancellationToken cancellationToken = default)
    {
        if (employeeId <= 0)
        {
            return (false, "Tài khoản chưa liên kết nhân viên nên không thể chấm công.");
        }

        if (model.Id <= 0)
        {
            return (false, "Không xác định được lượt checkin cần checkout.");
        }

        if (!model.LatAddressCheckOut.HasValue || !model.LongAddressCheckOut.HasValue)
        {
            return (false, "Vui lòng cấp quyền GPS để lấy vị trí checkout.");
        }

        if (string.IsNullOrWhiteSpace(model.ImgPathCheckOut))
        {
            return (false, "Vui lòng chụp ảnh checkout chấm công.");
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var openCheckin = await GetOpenCheckinByIdAsync(connection, model.Id, employeeId, cancellationToken);
            if (openCheckin is null)
            {
                return (false, "Không tìm thấy lượt checkin chấm công đang mở.");
            }

            if (openCheckin.IDDiaDiem.HasValue)
            {
                var location = await GetApptechLocationByIdAsync(connection, transaction: null, openCheckin.IDDiaDiem.Value, cancellationToken);
                if (location is not null)
                {
                    var distanceError = await ValidateDistanceAsync(location, model.LatAddressCheckOut.Value, model.LongAddressCheckOut.Value, cancellationToken);
                    if (distanceError is not null)
                    {
                        return (false, distanceError);
                    }
                }
            }

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
                  AND IDNhanVien = @IDNhanVien
                  AND CheckInType = @CheckInType
                  AND ThoiDiem IS NOT NULL
                  AND ThoiDiemCheckOut IS NULL
                  AND @ThoiDiemCheckOut >= ThoiDiem
                """;
            command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = model.Id });
            command.Parameters.Add(new SqlParameter("@IDNhanVien", SqlDbType.Int) { Value = employeeId });
            command.Parameters.Add(new SqlParameter("@CheckInType", SqlDbType.NVarChar, 50) { Value = ChamCongType });
            command.Parameters.Add(new SqlParameter("@ThoiDiemCheckOut", SqlDbType.DateTime) { Value = model.ThoiDiemCheckOut ?? DateTime.Now });
            command.Parameters.Add(new SqlParameter("@LongAddressCheckOut", SqlDbType.Decimal) { Precision = 18, Scale = 10, Value = model.LongAddressCheckOut.Value });
            command.Parameters.Add(new SqlParameter("@LatAddressCheckOut", SqlDbType.Decimal) { Precision = 18, Scale = 10, Value = model.LatAddressCheckOut.Value });
            command.Parameters.Add(new SqlParameter("@ImgPathCheckOut", SqlDbType.NVarChar, 500) { Value = model.ImgPathCheckOut.Trim() });
            command.Parameters.Add(new SqlParameter("@GhiChuCheckOut", SqlDbType.NVarChar, 1000) { Value = ToDbValue(model.GhiChuCheckOut) });

            return await command.ExecuteNonQueryAsync(cancellationToken) > 0
                ? (true, null)
                : (false, "Không tìm thấy lượt checkin đang mở hoặc thời gian checkout nhỏ hơn thời gian checkin.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to checkout attendance checkin {CheckinId} for employee {EmployeeId}.", model.Id, employeeId);
            return (false, "Không thể lưu thông tin checkout.");
        }
    }

    private async Task<ChamCongHistoryItem?> GetOpenCheckinAsync(
        SqlConnection connection,
        int employeeId,
        DateTime? date,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var dateFilter = date.HasValue
            ? """
              AND ch.ThoiDiem >= @DateFrom
              AND ch.ThoiDiem < DATEADD(day, 1, @DateFrom)
            """
            : "";
        command.CommandText = BuildHistorySelect("""
            WHERE ch.IDNhanVien = @IDNhanVien
              AND ch.CheckInType = @CheckInType
              AND ch.ThoiDiemCheckOut IS NULL
              {0}
            ORDER BY ch.ThoiDiem DESC, ch.ID DESC
            """.Replace("{0}", dateFilter), top: "TOP (1)");
        command.Parameters.Add(new SqlParameter("@IDNhanVien", SqlDbType.Int) { Value = employeeId });
        command.Parameters.Add(new SqlParameter("@CheckInType", SqlDbType.NVarChar, 50) { Value = ChamCongType });
        if (date.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@DateFrom", SqlDbType.DateTime) { Value = date.Value.Date });
        }
            var history = await ReadHistoryAsync(command, cancellationToken);
            await ApplyAttendanceViolationsAsync(connection, history, cancellationToken);
            return history.FirstOrDefault();
    }

    private static async Task<ChamCongHistoryItem?> GetOpenCheckinByIdAsync(
        SqlConnection connection,
        int id,
        int employeeId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = BuildHistorySelect("""
            WHERE ch.ID = @Id
              AND ch.IDNhanVien = @IDNhanVien
              AND ch.CheckInType = @CheckInType
              AND ch.ThoiDiemCheckOut IS NULL
            """, top: "TOP (1)");
        command.Parameters.Add(new SqlParameter("@Id", SqlDbType.Int) { Value = id });
        command.Parameters.Add(new SqlParameter("@IDNhanVien", SqlDbType.Int) { Value = employeeId });
        command.Parameters.Add(new SqlParameter("@CheckInType", SqlDbType.NVarChar, 50) { Value = ChamCongType });
        return (await ReadHistoryAsync(command, cancellationToken)).FirstOrDefault();
    }

    private static async Task<IReadOnlyList<ChamCongHistoryItem>> ReadHistoryAsync(
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        var items = new List<ChamCongHistoryItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ChamCongHistoryItem
            {
                Id = GetNullableInt32(reader, "ID") ?? 0,
                IDKhachHang = GetNullableInt32(reader, "IDKhachHang"),
                IDDiaDiem = GetNullableInt32(reader, "IDDiaDiem"),
                IDNhanVien = GetNullableInt32(reader, "IDNhanVien"),
                HoTenNhanVien = GetNullableString(reader, "HoTenNhanVien"),
                TenKhachHang = GetNullableString(reader, "TenKhachHang"),
                DiaChi = GetNullableString(reader, "DiaChi"),
                ThoiDiem = GetNullableDateTime(reader, "ThoiDiem"),
                ThoiDiemCheckOut = GetNullableDateTime(reader, "ThoiDiemCheckOut"),
                LongAddress = GetNullableDecimal(reader, "LongAddress"),
                LatAddress = GetNullableDecimal(reader, "LatAddress"),
                LongAddressCheckOut = GetNullableDecimal(reader, "LongAddressCheckOut"),
                LatAddressCheckOut = GetNullableDecimal(reader, "LatAddressCheckOut"),
                ImgPath = GetNullableString(reader, "ImgPath"),
                ImgPathCheckOut = GetNullableString(reader, "ImgPathCheckOut"),
                GhiChuNhanVien = GetNullableString(reader, "GhiChuNhanVien"),
                GhiChuCheckOut = GetNullableString(reader, "GhiChuCheckOut"),
                DuyetCheckIn = GetNullableBoolean(reader, "DuyetCheckIn")
            });
        }

        return items;
    }

    private async Task ApplyAttendanceViolationsAsync(
        SqlConnection connection,
        IReadOnlyList<ChamCongHistoryItem> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        var schedule = await GetAttendanceScheduleAsync(connection, cancellationToken);
        if (schedule is null)
        {
            return;
        }

        foreach (var item in items)
        {
            if (item.DuyetCheckIn == true)
            {
                item.IsCheckinViolation = false;
                item.IsCheckoutViolation = false;
                continue;
            }

            var checkinShift = ResolveCheckinShift(item.ThoiDiem?.TimeOfDay, schedule);
            item.IsCheckinViolation = checkinShift is not null &&
                item.ThoiDiem.HasValue &&
                item.ThoiDiem.Value.TimeOfDay > checkinShift.Begin;

            var checkoutEnd = ResolveCheckoutEnd(item.ThoiDiemCheckOut?.TimeOfDay, schedule);
            item.IsCheckoutViolation = checkoutEnd.HasValue &&
                item.ThoiDiemCheckOut.HasValue &&
                item.ThoiDiemCheckOut.Value.TimeOfDay < checkoutEnd.Value;
        }
    }

    private async Task<AttendanceSchedule?> GetAttendanceScheduleAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT MaCauHinh, GiaTri
            FROM [{SystemConfigTableName}]
            WHERE MaCauHinh IN (N'Begin_1', N'End_1', N'Begin_2', N'End_2')
            """;

        TimeSpan? begin1 = null;
        TimeSpan? end1 = null;
        TimeSpan? begin2 = null;
        TimeSpan? end2 = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = GetNullableString(reader, "MaCauHinh");
            var value = ParseNullableTime(GetNullableString(reader, "GiaTri"));
            if (!value.HasValue)
            {
                continue;
            }

            switch (key)
            {
                case "Begin_1":
                    begin1 = value.Value;
                    break;
                case "End_1":
                    end1 = value.Value;
                    break;
                case "Begin_2":
                    begin2 = value.Value;
                    break;
                case "End_2":
                    end2 = value.Value;
                    break;
            }
        }

        return begin1.HasValue && end1.HasValue && begin2.HasValue && end2.HasValue
            ? new AttendanceSchedule(begin1.Value, end1.Value, begin2.Value, end2.Value)
            : null;
    }

    private static AttendanceShift? ResolveCheckinShift(TimeSpan? checkinTime, AttendanceSchedule schedule)
    {
        if (!checkinTime.HasValue)
        {
            return null;
        }

        var morningDistance = Math.Abs((checkinTime.Value - schedule.Begin1).TotalMinutes);
        var afternoonDistance = Math.Abs((checkinTime.Value - schedule.Begin2).TotalMinutes);
        return morningDistance <= afternoonDistance
            ? new AttendanceShift(schedule.Begin1, schedule.End1)
            : new AttendanceShift(schedule.Begin2, schedule.End2);
    }

    private static TimeSpan? ResolveCheckoutEnd(TimeSpan? checkoutTime, AttendanceSchedule schedule)
    {
        if (!checkoutTime.HasValue)
        {
            return null;
        }

        return checkoutTime.Value < schedule.Begin2
            ? schedule.End1
            : schedule.End2;
    }

    private static string BuildHistorySelect(string whereClause, string top = "")
    {
        return $"""
            SELECT {top}
                ch.ID,
                ch.IDKhachHang,
                ch.IDDiaDiem,
                ch.IDNhanVien,
                LTRIM(RTRIM(CONCAT(ISNULL(nv.Ho, N''), N' ', ISNULL(nv.Ten, N'')))) AS HoTenNhanVien,
                kh.TenKhachHang,
                dd.DiaChi,
                ch.ThoiDiem,
                ch.ThoiDiemCheckOut,
                ch.LongAddress,
                ch.LatAddress,
                ch.LongAddressCheckOut,
                ch.LatAddressCheckOut,
                ch.ImgPath,
                ch.ImgPathCheckOut,
                ch.GhiChuNhanVien,
                ch.GhiChuCheckOut,
                ch.DuyetCheckIn
            FROM [{CheckinHistoryTableName}] AS ch
            LEFT JOIN [{CustomerTableName}] AS kh ON kh.ID = ch.IDKhachHang
            LEFT JOIN [{LocationTableName}] AS dd ON dd.ID = ch.IDDiaDiem
            LEFT JOIN [TblNhanVien] AS nv ON nv.ID = ch.IDNhanVien
            {whereClause}
            """;
    }

    private static async Task<ChamCongLocationOption?> GetApptechLocationByIdAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        int idDiaDiem,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT TOP (1)
                dd.ID AS IDDiaDiem,
                kh.ID AS IDKhachHang,
                kh.MaKhachHang,
                kh.TenKhachHang,
                dd.DiaChi,
                dd.LongAddress,
                dd.LatAddress
            FROM [{LocationTableName}] AS dd
            INNER JOIN [{CustomerTableName}] AS kh ON kh.ID = dd.IDKhachHang
            WHERE dd.ID = @IDDiaDiem
              AND kh.MaKhachHang LIKE @MaKhachHang
              AND ISNULL(dd.TrangThaiSuDung, 1) = 1
            """;
        command.Parameters.Add(new SqlParameter("@IDDiaDiem", SqlDbType.Int) { Value = idDiaDiem });
        command.Parameters.Add(new SqlParameter("@MaKhachHang", SqlDbType.NVarChar, 50) { Value = "Apptech%" });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ChamCongLocationOption
        {
            IDDiaDiem = GetNullableInt32(reader, "IDDiaDiem") ?? 0,
            IDKhachHang = GetNullableInt32(reader, "IDKhachHang") ?? 0,
            MaKhachHang = GetNullableString(reader, "MaKhachHang") ?? string.Empty,
            TenKhachHang = GetNullableString(reader, "TenKhachHang") ?? "AppTech",
            DiaChi = GetNullableString(reader, "DiaChi") ?? string.Empty,
            LongAddress = GetNullableDecimal(reader, "LongAddress"),
            LatAddress = GetNullableDecimal(reader, "LatAddress")
        };
    }

    private static async Task<bool> HasOpenCheckinAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int employeeId,
        DateTime date,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT TOP (1) 1
            FROM [{CheckinHistoryTableName}] WITH (UPDLOCK, HOLDLOCK)
            WHERE IDNhanVien = @IDNhanVien
              AND CheckInType = @CheckInType
              AND ThoiDiem >= @DateFrom
              AND ThoiDiem < DATEADD(day, 1, @DateFrom)
              AND ThoiDiemCheckOut IS NULL
            """;
        command.Parameters.Add(new SqlParameter("@IDNhanVien", SqlDbType.Int) { Value = employeeId });
        command.Parameters.Add(new SqlParameter("@CheckInType", SqlDbType.NVarChar, 50) { Value = ChamCongType });
        command.Parameters.Add(new SqlParameter("@DateFrom", SqlDbType.DateTime) { Value = date.Date });
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private async Task<string?> ValidateDistanceAsync(
        ChamCongLocationOption location,
        decimal latAddress,
        decimal longAddress,
        CancellationToken cancellationToken)
    {
        if (!location.LatAddress.HasValue || !location.LongAddress.HasValue)
        {
            return "Địa điểm chấm công chưa có tọa độ nên không thể kiểm tra khoảng cách.";
        }

        var limitMeters = await GetCheckinDistanceLimitMetersAsync(cancellationToken);
        if (!limitMeters.HasValue || limitMeters.Value <= 0)
        {
            return null;
        }

        var distanceMeters = CalculateDistanceMeters(
            Convert.ToDouble(location.LatAddress.Value),
            Convert.ToDouble(location.LongAddress.Value),
            Convert.ToDouble(latAddress),
            Convert.ToDouble(longAddress));
        return distanceMeters <= Convert.ToDouble(limitMeters.Value)
            ? null
            : $"Khoảng cách chấm công không được quá {limitMeters.Value:0.##} mét.";
    }

    private static double CalculateDistanceMeters(double startLat, double startLng, double endLat, double endLng)
    {
        const double earthRadiusMeters = 6371000;
        static double ToRadians(double value) => value * Math.PI / 180;

        var deltaLat = ToRadians(endLat - startLat);
        var deltaLng = ToRadians(endLng - startLng);
        var startLatRad = ToRadians(startLat);
        var endLatRad = ToRadians(endLat);
        var haversine =
            Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
            Math.Cos(startLatRad) * Math.Cos(endLatRad) * Math.Sin(deltaLng / 2) * Math.Sin(deltaLng / 2);
        return 2 * earthRadiusMeters * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(1 - haversine));
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
            decimal typedDecimal => typedDecimal != 0,
            string typedString when bool.TryParse(typedString, out var parsedBool) => parsedBool,
            string typedString when int.TryParse(typedString, out var parsedInt) => parsedInt != 0,
            _ => null
        };
    }

    private static TimeSpan? ParseNullableTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (TimeSpan.TryParse(normalized, CultureInfo.InvariantCulture, out var parsedTime))
        {
            return parsedTime;
        }

        return DateTime.TryParse(normalized, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out var parsedDate)
            ? parsedDate.TimeOfDay
            : null;
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

    private static object ToDbValue(object? value)
    {
        return value switch
        {
            null => DBNull.Value,
            string text when string.IsNullOrWhiteSpace(text) => DBNull.Value,
            string text => text.Trim(),
            _ => value
        };
    }
}
