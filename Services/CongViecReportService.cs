using System.Data;
using ApptechDashboard.Configuration;
using ApptechDashboard.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ApptechDashboard.Services;

public interface ICongViecReportService
{
    Task<CongViecReportViewModel> GetReportAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        IReadOnlyCollection<int>? employeeIds = null,
        CancellationToken cancellationToken = default);
}

public sealed class CongViecReportService(
    IOptions<SqlServerOptions> sqlOptions,
    IConfiguration configuration,
    ILogger<CongViecReportService> logger) : ICongViecReportService
{
    private const string EmployeeTableName = "TblNhanVien";
    private const string RequestTableName = "TblYeuCau";
    private const string CustomerTableName = "TblKhachHang";
    private const string LocationTableName = "TblKhachHangDiaDiem";
    private const string WorkCatalogTableName = "TblCongViec";
    private const string WorkTableName = "TblYeuCauCongViec";
    private const string AssignmentTableName = "TblYeuCauCongViecNhanVien";
    private const string CompletedStatus = "Hoàn thành";
    private const string NewStatus = "Tạo mới";

    private readonly SqlServerOptions _sqlOptions = sqlOptions.Value;
    private readonly string? _connectionString = configuration.GetConnectionString("DefaultConnection");
    private readonly ILogger<CongViecReportService> _logger = logger;

    public async Task<CongViecReportViewModel> GetReportAsync(
        DateTime? dateFrom,
        DateTime? dateTo,
        IReadOnlyCollection<int>? employeeIds = null,
        CancellationToken cancellationToken = default)
    {
        var today = DateTime.Today;
        var from = (dateFrom ?? today.AddDays(-30)).Date;
        var to = (dateTo ?? today).Date;
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var model = new CongViecReportViewModel
        {
            DateFrom = from,
            DateTo = to
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
            await EnsureWorkMetadataColumnsAsync(connection, cancellationToken);

            var allEmployees = await LoadEmployeesAsync(connection, cancellationToken);
            var selectedEmployeeIds = NormalizeSelectedEmployeeIds(employeeIds, allEmployees);
            var details = await LoadDetailsAsync(connection, from, to, selectedEmployeeIds, cancellationToken);
            var rows = BuildRows(allEmployees, selectedEmployeeIds, details);

            model.EmployeeOptions = allEmployees;
            model.SelectedEmployeeIds = selectedEmployeeIds;
            model.Employees = rows;
            model.Details = details;
            return model;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load work report from {DateFrom} to {DateTo}.", from, to);
            model.StatusMessage = "Không thể tải báo cáo công việc.";
            model.StatusType = "error";
            return model;
        }
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

    private static async Task<IReadOnlyList<CongViecReportEmployeeOption>> LoadEmployeesAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                ID,
                LTRIM(RTRIM(CONCAT(ISNULL(Ho, N''), N' ', ISNULL(Ten, N'')))) AS HoTen,
                ChucVu
            FROM [{EmployeeTableName}]
            WHERE ISNULL(TrangThaiSuDung, 1) = 1
            ORDER BY HoTen, ID
            """;

        var employees = new List<CongViecReportEmployeeOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var employeeId = GetInt32(reader, "ID");
            employees.Add(new CongViecReportEmployeeOption
            {
                EmployeeId = employeeId,
                HoTen = GetString(reader, "HoTen") ?? $"Nhân viên #{employeeId}",
                ChucVu = GetString(reader, "ChucVu")
            });
        }

        return employees;
    }

    private static IReadOnlyList<int> NormalizeSelectedEmployeeIds(
        IReadOnlyCollection<int>? selectedIds,
        IReadOnlyList<CongViecReportEmployeeOption> employees)
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

    private static IReadOnlyList<CongViecReportEmployeeRow> BuildRows(
        IReadOnlyList<CongViecReportEmployeeOption> allEmployees,
        IReadOnlyList<int> selectedEmployeeIds,
        IReadOnlyList<CongViecReportDetailItem> details)
    {
        var detailLookup = details
            .GroupBy(detail => detail.EmployeeId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var selected = selectedEmployeeIds.Count == 0
            ? allEmployees
            : allEmployees.Where(employee => selectedEmployeeIds.Contains(employee.EmployeeId)).ToList();

        return selected
            .Select(employee =>
            {
                detailLookup.TryGetValue(employee.EmployeeId, out var employeeDetails);
                employeeDetails ??= [];
                return new CongViecReportEmployeeRow
                {
                    EmployeeId = employee.EmployeeId,
                    HoTen = employee.HoTen,
                    ChucVu = employee.ChucVu,
                    CompletedCount = employeeDetails.Count(detail => detail.IsCompleted),
                    IncompleteCount = employeeDetails.Count(detail => !detail.IsCompleted)
                };
            })
            .Where(row => row.CompletedCount > 0 || row.IncompleteCount > 0)
            .OrderBy(row => row.HoTen)
            .ToList();
    }

    private static async Task<IReadOnlyList<CongViecReportDetailItem>> LoadDetailsAsync(
        SqlConnection connection,
        DateTime dateFrom,
        DateTime dateTo,
        IReadOnlyList<int> selectedEmployeeIds,
        CancellationToken cancellationToken)
    {
        var employeeFilter = "";
        if (selectedEmployeeIds.Count > 0)
        {
            employeeFilter = $" AND lknv.IDNhanVien IN ({string.Join(", ", selectedEmployeeIds.Select((_, index) => $"@EmployeeId{index}"))})";
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                yc.ID AS YeuCauId,
                yc.MaYeuCau,
                yc.NgayYeuCau,
                ycvc.ID AS WorkId,
                cv.TenCongViec,
                ISNULL(NULLIF(LTRIM(RTRIM(ycvc.TrangThaiCongViec)), N''), @NewStatus) AS TrangThaiCongViec,
                lknv.IDNhanVien AS EmployeeId,
                LTRIM(RTRIM(CONCAT(ISNULL(nv.Ho, N''), N' ', ISNULL(nv.Ten, N'')))) AS EmployeeName,
                kh.TenKhachHang,
                dd.DiaChi
            FROM [{WorkTableName}] AS ycvc
            INNER JOIN [{RequestTableName}] AS yc ON yc.ID = ycvc.IDYeuCau
            INNER JOIN [{AssignmentTableName}] AS lknv ON lknv.IDYeuCauCongViec = ycvc.ID
            LEFT JOIN [{WorkCatalogTableName}] AS cv ON cv.ID = ycvc.IDCongViec
            LEFT JOIN [{EmployeeTableName}] AS nv ON nv.ID = lknv.IDNhanVien
            LEFT JOIN [{CustomerTableName}] AS kh ON kh.ID = yc.IDKhachHang
            LEFT JOIN [{LocationTableName}] AS dd ON dd.ID = yc.IDDiaDiem
            WHERE yc.NgayYeuCau >= @DateFrom
              AND yc.NgayYeuCau < DATEADD(day, 1, @DateTo)
              {employeeFilter}
            ORDER BY yc.NgayYeuCau DESC, yc.ID DESC, ycvc.ID DESC
            """;
        command.Parameters.Add(new SqlParameter("@DateFrom", SqlDbType.DateTime) { Value = dateFrom });
        command.Parameters.Add(new SqlParameter("@DateTo", SqlDbType.DateTime) { Value = dateTo });
        command.Parameters.Add(new SqlParameter("@NewStatus", SqlDbType.NVarChar, 50) { Value = NewStatus });
        for (var index = 0; index < selectedEmployeeIds.Count; index++)
        {
            command.Parameters.Add(new SqlParameter($"@EmployeeId{index}", SqlDbType.Int) { Value = selectedEmployeeIds[index] });
        }

        var details = new List<CongViecReportDetailItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var status = GetString(reader, "TrangThaiCongViec") ?? NewStatus;
            details.Add(new CongViecReportDetailItem
            {
                YeuCauId = GetInt32(reader, "YeuCauId"),
                WorkId = GetInt32(reader, "WorkId"),
                EmployeeId = GetInt32(reader, "EmployeeId"),
                EmployeeName = GetString(reader, "EmployeeName") ?? "",
                MaYeuCau = GetString(reader, "MaYeuCau") ?? "",
                TenKhachHang = GetString(reader, "TenKhachHang") ?? "",
                DiaDiem = GetString(reader, "DiaChi") ?? "",
                NgayYeuCau = GetNullableDateTime(reader, "NgayYeuCau"),
                TenCongViec = GetString(reader, "TenCongViec") ?? "",
                TrangThaiCongViec = status,
                IsCompleted = string.Equals(status, CompletedStatus, StringComparison.OrdinalIgnoreCase)
            });
        }

        return details;
    }

    private static async Task EnsureWorkMetadataColumnsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var schemaCommand = connection.CreateCommand();
        schemaCommand.CommandText = $"""
            IF COL_LENGTH('dbo.{WorkTableName}', 'TrangThaiCongViec') IS NULL
            BEGIN
                ALTER TABLE [dbo].[{WorkTableName}] ADD [TrangThaiCongViec] NVARCHAR(50) NULL;
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
        dataCommand.Parameters.Add(new SqlParameter("@NewStatus", SqlDbType.NVarChar, 50) { Value = NewStatus });
        dataCommand.Parameters.Add(new SqlParameter("@InProgressStatus", SqlDbType.NVarChar, 50) { Value = "Đang thực hiện" });
        dataCommand.Parameters.Add(new SqlParameter("@CompletedStatus", SqlDbType.NVarChar, 50) { Value = CompletedStatus });
        await dataCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static int GetInt32(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static string? GetString(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal)?.ToString()?.Trim();
    }

    private static DateTime? GetNullableDateTime(SqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : Convert.ToDateTime(reader.GetValue(ordinal));
    }
}
