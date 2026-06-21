namespace ApptechDashboard.Models;

public sealed class CongViecReportQuery
{
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public List<int> EmployeeIds { get; set; } = [];
}

public sealed class CongViecReportViewModel
{
    public DateTime DateFrom { get; set; } = DateTime.Today.AddDays(-30);
    public DateTime DateTo { get; set; } = DateTime.Today;
    public IReadOnlyList<int> SelectedEmployeeIds { get; set; } = [];
    public IReadOnlyList<CongViecReportEmployeeOption> EmployeeOptions { get; set; } = [];
    public IReadOnlyList<CongViecReportEmployeeRow> Employees { get; set; } = [];
    public IReadOnlyList<CongViecReportDetailItem> Details { get; set; } = [];
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";

    public bool AllEmployeesSelected => SelectedEmployeeIds.Count == 0 || SelectedEmployeeIds.Count >= EmployeeOptions.Count;
    public int TotalCompleted => Employees.Sum(employee => employee.CompletedCount);
    public int TotalIncomplete => Employees.Sum(employee => employee.IncompleteCount);
}

public sealed class CongViecReportEmployeeOption
{
    public int EmployeeId { get; set; }
    public string HoTen { get; set; } = string.Empty;
    public string? ChucVu { get; set; }
    public string DisplayText => string.IsNullOrWhiteSpace(ChucVu) ? HoTen : $"{HoTen} - {ChucVu}";
}

public sealed class CongViecReportEmployeeRow
{
    public int EmployeeId { get; set; }
    public string HoTen { get; set; } = string.Empty;
    public string? ChucVu { get; set; }
    public int CompletedCount { get; set; }
    public int IncompleteCount { get; set; }
}

public sealed class CongViecReportDetailItem
{
    public int YeuCauId { get; set; }
    public int WorkId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string MaYeuCau { get; set; } = string.Empty;
    public string TenKhachHang { get; set; } = string.Empty;
    public string DiaDiem { get; set; } = string.Empty;
    public DateTime? NgayYeuCau { get; set; }
    public string TenCongViec { get; set; } = string.Empty;
    public string TrangThaiCongViec { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
}
