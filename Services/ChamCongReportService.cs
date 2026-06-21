using System.Data;
using System.Globalization;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface IChamCongReportService
{
    Task<ChamCongReportViewModel> GetMonthlyReportAsync(
        int month,
        int year,
        string? activeTab = null,
        IReadOnlyCollection<int>? employeeIds = null,
        CancellationToken cancellationToken = default);
}

public sealed class ChamCongReportService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<ChamCongReportService> logger) : IChamCongReportService
{
    private const string EmployeeTableName = "TblNhanVien";
    private const string UserTableName = "TblTaiKhoanNguoiDung";
    private const string CheckinHistoryTableName = "TblCheckinHistory";
    private const string SystemConfigTableName = "TblCauHinhHeThong";
    private const string ChamCongType = "ChamCong";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<ChamCongReportService> _logger = logger;

    private sealed record AttendanceSchedule(TimeSpan Begin1, TimeSpan End1, TimeSpan Begin2, TimeSpan End2);
    private sealed record AttendanceShift(TimeSpan Begin, TimeSpan End);
    private sealed record AttendanceReportCheckin(int EmployeeId, DateTime CheckinTime, DateTime? CheckoutTime);
    private sealed record AttendanceReportMetrics(
        Dictionary<int, Dictionary<int, decimal>> HoursByEmployeeDay,
        Dictionary<int, Dictionary<int, int>> LateEarlyMinutesByEmployeeDay,
        Dictionary<int, Dictionary<int, ChamCongReportCount>> CountsByEmployeeDay,
        Dictionary<int, Dictionary<int, List<ChamCongReportCheckinDetail>>> DetailsByEmployeeDay);

    public async Task<ChamCongReportViewModel> GetMonthlyReportAsync(
        int month,
        int year,
        string? activeTab = null,
        IReadOnlyCollection<int>? employeeIds = null,
        CancellationToken cancellationToken = default)
    {
        month = Math.Clamp(month, 1, 12);
        year = Math.Clamp(year, 2000, 2100);

        var days = BuildDays(month, year);
        var model = new ChamCongReportViewModel
        {
            Month = month,
            Year = year,
            Days = days,
            MonthlyStandardHours = CalculateMonthlyStandardHours(days),
            ActiveTab = NormalizeTab(activeTab)
        };

        if (string.IsNullOrWhiteSpace(_connectionString) && !_sqlOptions.IsConfigured)
        {
            model.StatusMessage = "Chưa cấu hình kết nối dữ liệu.";
            model.StatusType = "error";
            return model;
        }

        try
        {
            await using var connection = await OpenConnectionAsync(cancellationToken);
            var allEmployees = await LoadEmployeesAsync(connection, cancellationToken);
            var selectedEmployeeIds = NormalizeSelectedEmployeeIds(employeeIds, allEmployees);
            var employees = selectedEmployeeIds.Count == 0
                ? allEmployees
                : allEmployees.Where(employee => selectedEmployeeIds.Contains(employee.EmployeeId)).ToList();
            var schedule = await GetAttendanceScheduleAsync(connection, cancellationToken);
            var metrics = await LoadAttendanceMetricsAsync(connection, month, year, schedule, cancellationToken);

            foreach (var employee in employees)
            {
                if (metrics.HoursByEmployeeDay.TryGetValue(employee.EmployeeId, out var hoursByDay))
                {
                    employee.HoursByDay = hoursByDay;
                }

                if (metrics.LateEarlyMinutesByEmployeeDay.TryGetValue(employee.EmployeeId, out var lateEarlyByDay))
                {
                    employee.LateEarlyMinutesByDay = lateEarlyByDay;
                }

                if (metrics.CountsByEmployeeDay.TryGetValue(employee.EmployeeId, out var countsByDay))
                {
                    employee.CountsByDay = countsByDay;
                }

                if (metrics.DetailsByEmployeeDay.TryGetValue(employee.EmployeeId, out var detailsByDay))
                {
                    employee.DetailsByDay = detailsByDay;
                }
            }

            model.EmployeeOptions = allEmployees
                .Select(employee => new ChamCongReportEmployeeOption
                {
                    EmployeeId = employee.EmployeeId,
                    HoTen = employee.HoTen,
                    ChucVu = employee.ChucVu
                })
                .ToList();
            model.SelectedEmployeeIds = selectedEmployeeIds;
            model.Employees = employees;
            return model;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load attendance report for {Month}/{Year}.", month, year);
            model.StatusMessage = "Không thể tải báo cáo chấm công.";
            model.StatusType = "error";
            return model;
        }
    }

    private static IReadOnlyList<ChamCongReportDay> BuildDays(int month, int year)
    {
        var dayCount = DateTime.DaysInMonth(year, month);
        return Enumerable.Range(1, dayCount)
            .Select(day =>
            {
                var date = new DateTime(year, month, day);
                return new ChamCongReportDay
                {
                    Day = day,
                    Date = date,
                    WeekdayLabel = date.DayOfWeek switch
                    {
                        DayOfWeek.Monday => "T2",
                        DayOfWeek.Tuesday => "T3",
                        DayOfWeek.Wednesday => "T4",
                        DayOfWeek.Thursday => "T5",
                        DayOfWeek.Friday => "T6",
                        DayOfWeek.Saturday => "T7",
                        DayOfWeek.Sunday => "CN",
                        _ => ""
                    }
                };
            })
            .ToList();
    }

    private static decimal CalculateMonthlyStandardHours(IReadOnlyList<ChamCongReportDay> days)
    {
        var nonSundayDays = days.Count(day => !day.IsSunday);
        var saturdayCount = days.Count(day => day.IsSaturday);
        return (8m * nonSundayDays) - (4m * saturdayCount);
    }

    private static string NormalizeTab(string? tab)
    {
        return tab is "di-tre-ve-som" or "checkin-out"
            ? tab
            : "gio-cong";
    }

    private static IReadOnlyList<int> NormalizeSelectedEmployeeIds(
        IReadOnlyCollection<int>? selectedIds,
        IReadOnlyList<ChamCongReportEmployeeRow> employees)
    {
        if (selectedIds is null || selectedIds.Count == 0)
        {
            return [];
        }

        var availableIds = employees.Select(employee => employee.EmployeeId).ToHashSet();
        return selectedIds
            .Where(id => id > 0 && availableIds.Contains(id))
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }

    private static async Task<List<ChamCongReportEmployeeRow>> LoadEmployeesAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                nv.ID,
                LTRIM(RTRIM(CONCAT(ISNULL(nv.Ho, N''), N' ', ISNULL(nv.Ten, N'')))) AS HoTen,
                nv.ChucVu
            FROM [{EmployeeTableName}] AS nv
            WHERE ISNULL(nv.TrangThaiSuDung, 1) = 1
              AND NOT EXISTS (
                  SELECT 1
                  FROM [{UserTableName}] AS tk
                  WHERE tk.IDNhanVien = nv.ID
                    AND (
                        ISNULL(tk.QuanTriVien, 0) = 1
                        OR UPPER(LTRIM(RTRIM(ISNULL(tk.TenDangNhap, N'')))) IN (N'ADMIN', N'ADMINISTRATOR')
                        OR LTRIM(RTRIM(ISNULL(tk.NhomNguoiDung, N''))) LIKE N'%Quản trị%'
                        OR UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(ISNULL(tk.NhomNguoiDung, N''))), N' ', N''), N'Ả', N'A'), N'Ị', N'I'), N'Ệ', N'E'), N'Ố', N'O')) LIKE N'%QUANTRI%'
                        OR UPPER(LTRIM(RTRIM(ISNULL(tk.NhomNguoiDung, N'')))) LIKE N'%ADMIN%'
                    )
              )
            ORDER BY nv.Ho, nv.Ten, nv.ID
            """;

        var employees = new List<ChamCongReportEmployeeRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var employeeId = GetNullableInt32(reader, "ID") ?? 0;
            if (employeeId <= 0)
            {
                continue;
            }

            employees.Add(new ChamCongReportEmployeeRow
            {
                EmployeeId = employeeId,
                HoTen = GetNullableString(reader, "HoTen") ?? $"Nhân viên #{employeeId}",
                ChucVu = GetNullableString(reader, "ChucVu")
            });
        }

        return employees;
    }

    private static async Task<AttendanceReportMetrics> LoadAttendanceMetricsAsync(
        SqlConnection connection,
        int month,
        int year,
        AttendanceSchedule? schedule,
        CancellationToken cancellationToken)
    {
        var dateFrom = new DateTime(year, month, 1);
        var dateTo = dateFrom.AddMonths(1);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                IDNhanVien,
                ThoiDiem,
                ThoiDiemCheckOut,
                ImgPath,
                ImgPathCheckOut,
                GhiChuNhanVien,
                GhiChuCheckOut
            FROM [{CheckinHistoryTableName}]
            WHERE CheckInType = @CheckInType
              AND ThoiDiem >= @DateFrom
              AND ThoiDiem < @DateTo
              AND IDNhanVien IS NOT NULL
            ORDER BY IDNhanVien, ThoiDiem, ID
            """;
        command.Parameters.Add(new SqlParameter("@CheckInType", SqlDbType.NVarChar, 50) { Value = ChamCongType });
        command.Parameters.Add(new SqlParameter("@DateFrom", SqlDbType.DateTime) { Value = dateFrom });
        command.Parameters.Add(new SqlParameter("@DateTo", SqlDbType.DateTime) { Value = dateTo });

        var hoursResult = new Dictionary<int, Dictionary<int, decimal>>();
        var lateEarlyResult = new Dictionary<int, Dictionary<int, int>>();
        var countsResult = new Dictionary<int, Dictionary<int, ChamCongReportCount>>();
        var detailsResult = new Dictionary<int, Dictionary<int, List<ChamCongReportCheckinDetail>>>();
        var totals = new Dictionary<(int EmployeeId, int Day), decimal>();
        var lateEarlyTotals = new Dictionary<(int EmployeeId, int Day), decimal>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var employeeId = GetNullableInt32(reader, "IDNhanVien") ?? 0;
            var checkinTime = GetNullableDateTime(reader, "ThoiDiem");
            if (employeeId <= 0 || !checkinTime.HasValue)
            {
                continue;
            }

            var day = checkinTime.Value.Day;
            var checkoutTime = GetNullableDateTime(reader, "ThoiDiemCheckOut");
            if (!detailsResult.TryGetValue(employeeId, out var detailsByDay))
            {
                detailsByDay = [];
                detailsResult[employeeId] = detailsByDay;
            }

            if (!detailsByDay.TryGetValue(day, out var details))
            {
                details = [];
                detailsByDay[day] = details;
            }

            details.Add(new ChamCongReportCheckinDetail
            {
                CheckinTime = checkinTime,
                CheckoutTime = checkoutTime,
                CheckinImage = GetNullableString(reader, "ImgPath"),
                CheckoutImage = GetNullableString(reader, "ImgPathCheckOut"),
                CheckinNote = GetNullableString(reader, "GhiChuNhanVien"),
                CheckoutNote = GetNullableString(reader, "GhiChuCheckOut")
            });

            if (!hoursResult.TryGetValue(employeeId, out var hoursByDay))
            {
                hoursByDay = [];
                hoursResult[employeeId] = hoursByDay;
            }

            hoursByDay.TryAdd(day, 0);
            if (!countsResult.TryGetValue(employeeId, out var countsByDay))
            {
                countsByDay = [];
                countsResult[employeeId] = countsByDay;
            }

            if (!countsByDay.TryGetValue(day, out var count))
            {
                count = new ChamCongReportCount();
                countsByDay[day] = count;
            }

            count.CheckinCount++;

            if (!checkoutTime.HasValue)
            {
                continue;
            }

            count.CheckoutCount++;

            var minutes = schedule is null
                ? CalculateRawMinutes(checkinTime.Value, checkoutTime.Value)
                : CalculateScheduledMinutes(new AttendanceReportCheckin(employeeId, checkinTime.Value, checkoutTime), schedule);
            var key = (employeeId, day);
            totals[key] = totals.GetValueOrDefault(key) + minutes;
            hoursByDay[day] = RoundMinutesToHalfHour(totals[key]);

            if (schedule is not null)
            {
                if (!lateEarlyResult.TryGetValue(employeeId, out var lateEarlyByDay))
                {
                    lateEarlyByDay = [];
                    lateEarlyResult[employeeId] = lateEarlyByDay;
                }

                var lateEarlyMinutes = CalculateLateEarlyMinutes(new AttendanceReportCheckin(employeeId, checkinTime.Value, checkoutTime), schedule);
                lateEarlyTotals[key] = lateEarlyTotals.GetValueOrDefault(key) + lateEarlyMinutes;
                lateEarlyByDay[day] = Convert.ToInt32(Math.Round(lateEarlyTotals[key], MidpointRounding.AwayFromZero));
            }
        }

        return new AttendanceReportMetrics(hoursResult, lateEarlyResult, countsResult, detailsResult);
    }

    private static decimal CalculateRawMinutes(DateTime checkinTime, DateTime checkoutTime)
    {
        return checkoutTime > checkinTime
            ? Convert.ToDecimal((checkoutTime - checkinTime).TotalMinutes)
            : 0;
    }

    private static decimal CalculateScheduledMinutes(AttendanceReportCheckin item, AttendanceSchedule schedule)
    {
        if (!item.CheckoutTime.HasValue)
        {
            return 0;
        }

        var shift = ResolveAttendanceShift(item.CheckinTime.TimeOfDay, schedule);
        var begin = item.CheckinTime.Date.Add(shift.Begin);
        var end = item.CheckinTime.Date.Add(shift.End);
        var effectiveStart = item.CheckinTime <= begin ? begin : item.CheckinTime;
        var effectiveEnd = item.CheckoutTime.Value <= end ? item.CheckoutTime.Value : end;

        return effectiveEnd > effectiveStart
            ? Convert.ToDecimal((effectiveEnd - effectiveStart).TotalMinutes)
            : 0;
    }

    private static decimal CalculateLateEarlyMinutes(AttendanceReportCheckin item, AttendanceSchedule schedule)
    {
        if (!item.CheckoutTime.HasValue)
        {
            return 0;
        }

        var shift = ResolveAttendanceShift(item.CheckinTime.TimeOfDay, schedule);
        var begin = item.CheckinTime.Date.Add(shift.Begin);
        var end = item.CheckinTime.Date.Add(shift.End);
        var lateMinutes = item.CheckinTime > begin
            ? Convert.ToDecimal((item.CheckinTime - begin).TotalMinutes)
            : 0;
        var earlyMinutes = item.CheckoutTime.Value < end
            ? Convert.ToDecimal((end - item.CheckoutTime.Value).TotalMinutes)
            : 0;

        return lateMinutes + earlyMinutes;
    }

    private static AttendanceShift ResolveAttendanceShift(TimeSpan checkinTime, AttendanceSchedule schedule)
    {
        return checkinTime < schedule.Begin2
            ? new AttendanceShift(schedule.Begin1, schedule.End1)
            : new AttendanceShift(schedule.Begin2, schedule.End2);
    }

    private static async Task<AttendanceSchedule?> GetAttendanceScheduleAsync(
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

    private static decimal RoundMinutesToHalfHour(decimal totalMinutes)
    {
        if (totalMinutes <= 0)
        {
            return 0;
        }

        var halfHourBlocks = Math.Floor(totalMinutes / 30m);
        return halfHourBlocks * 0.5m;
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
}
