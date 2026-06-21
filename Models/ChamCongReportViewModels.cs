namespace ApptechDashboard.Models;

public sealed class ChamCongReportQuery
{
    public int? Month { get; set; }
    public int? Year { get; set; }
    public string? Tab { get; set; }
    public List<int> EmployeeIds { get; set; } = [];
}

public sealed class ChamCongReportViewModel
{
    public int Month { get; set; } = DateTime.Today.Month;
    public int Year { get; set; } = DateTime.Today.Year;
    public IReadOnlyList<ChamCongReportDay> Days { get; set; } = [];
    public IReadOnlyList<ChamCongReportEmployeeRow> Employees { get; set; } = [];
    public IReadOnlyList<ChamCongReportEmployeeOption> EmployeeOptions { get; set; } = [];
    public IReadOnlyList<int> SelectedEmployeeIds { get; set; } = [];
    public decimal MonthlyStandardHours { get; set; }
    public string ActiveTab { get; set; } = "gio-cong";
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";

    public decimal GrandTotalHours => Employees.Sum(employee => employee.TotalHours);
    public decimal GrandStandardHours => MonthlyStandardHours * Employees.Count;
    public int GrandLateEarlyMinutes => Employees.Sum(employee => employee.TotalLateEarlyMinutes);
    public int GrandCheckinCount => Employees.Sum(employee => employee.TotalCheckinCount);
    public int GrandCheckoutCount => Employees.Sum(employee => employee.TotalCheckoutCount);
    public bool AllEmployeesSelected => SelectedEmployeeIds.Count == 0 || SelectedEmployeeIds.Count >= EmployeeOptions.Count;
}

public sealed class ChamCongReportDay
{
    public int Day { get; set; }
    public DateTime Date { get; set; }
    public string WeekdayLabel { get; set; } = string.Empty;
    public bool IsSaturday => Date.DayOfWeek == DayOfWeek.Saturday;
    public bool IsSunday => Date.DayOfWeek == DayOfWeek.Sunday;
}

public sealed class ChamCongReportEmployeeRow
{
    public int EmployeeId { get; set; }
    public string HoTen { get; set; } = string.Empty;
    public string? ChucVu { get; set; }
    public Dictionary<int, decimal> HoursByDay { get; set; } = [];
    public Dictionary<int, int> LateEarlyMinutesByDay { get; set; } = [];
    public Dictionary<int, ChamCongReportCount> CountsByDay { get; set; } = [];
    public Dictionary<int, List<ChamCongReportCheckinDetail>> DetailsByDay { get; set; } = [];
    public decimal TotalHours => HoursByDay.Values.Sum();
    public int TotalLateEarlyMinutes => LateEarlyMinutesByDay.Values.Sum();
    public int TotalCheckinCount => CountsByDay.Values.Sum(item => item.CheckinCount);
    public int TotalCheckoutCount => CountsByDay.Values.Sum(item => item.CheckoutCount);
}

public sealed class ChamCongReportEmployeeOption
{
    public int EmployeeId { get; set; }
    public string HoTen { get; set; } = string.Empty;
    public string? ChucVu { get; set; }
    public string DisplayText => string.IsNullOrWhiteSpace(ChucVu) ? HoTen : $"{HoTen} - {ChucVu}";
}

public sealed class ChamCongReportCount
{
    public int CheckinCount { get; set; }
    public int CheckoutCount { get; set; }
}

public sealed class ChamCongReportCheckinDetail
{
    public DateTime? CheckinTime { get; set; }
    public DateTime? CheckoutTime { get; set; }
    public string? CheckinImage { get; set; }
    public string? CheckoutImage { get; set; }
    public string? CheckinNote { get; set; }
    public string? CheckoutNote { get; set; }
}
