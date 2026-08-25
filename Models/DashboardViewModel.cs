namespace ApptechDashboard.Models;

public class DashboardViewModel
{
    public string CompanyName { get; set; } = string.Empty;
    public string DashboardTitle { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public ChamCongDashboardModel ChamCong { get; set; } = new();
    public List<MetricCard> MetricCards { get; set; } = [];
    public List<ZoneStatus> Zones { get; set; } = [];
    public List<AlertItem> Alerts { get; set; } = [];
    public List<QuickAction> QuickActions { get; set; } = [];
    public List<int> MonthlyEnergy { get; set; } = [];
    public Dictionary<string, int> DeviceDistribution { get; set; } = new();
    public List<WorkOrder> WorkOrders { get; set; } = [];

    public static DashboardViewModel BuildSample()
    {
        return new DashboardViewModel
        {
            CompanyName = "Apptech Smart Control",
            DashboardTitle = "Dashboard quản trị nhà thông minh",
            Subtitle = "Bố cục lấy cảm hứng từ Ace Admin, phối màu theo nhận diện Apptech Nha Trang.",
            MetricCards =
            [
                new MetricCard("Thiết bị online", "128", "+12 hôm nay", "online"),
                new MetricCard("Cảnh báo đang mở", "07", "2 mức ưu tiên cao", "alert"),
                new MetricCard("Điện năng hôm nay", "246 kWh", "-8% so với hôm qua", "energy"),
                new MetricCard("Đơn bảo trì", "14", "5 đơn cần xử lý sớm", "service")
            ],
            Zones =
            [
                new ZoneStatus("Biệt thự Mẫu", "Ổn định", 98, "Đèn, khóa cửa, rèm, cảm biến"),
                new ZoneStatus("Khách sạn Mini", "Cần kiểm tra", 72, "2 camera ngoại vi đang ngoại tuyến"),
                new ZoneStatus("Văn phòng", "Ổn định", 91, "Điều hòa trung tâm và chấm công hoạt động tốt"),
                new ZoneStatus("Showroom", "Bảo trì", 64, "Motor cổng cần hiệu chỉnh lịch tự động")
            ],
            Alerts =
            [
                new AlertItem("Cửa cổng chính", "Mở ngoài khung giờ", "18:42", "Cao"),
                new AlertItem("Camera sân trước", "Mất kết nối 12 phút", "18:11", "Trung bình"),
                new AlertItem("Đèn hành lang tầng 2", "Tiêu thụ điện bất thường", "17:56", "Thấp"),
                new AlertItem("Khóa cửa kho", "3 lần xác thực thất bại", "17:21", "Cao")
            ],
            QuickActions =
            [
                new QuickAction("Bật toàn bộ đèn ngoài trời", "19:00 hàng ngày"),
                new QuickAction("Khóa toàn bộ cửa", "23:00 hàng ngày"),
                new QuickAction("Kịch bản tiếp khách", "1 chạm"),
                new QuickAction("Báo động an ninh", "Kích hoạt tức thời")
            ],
            MonthlyEnergy = [180, 205, 198, 214, 226, 210, 234, 240, 228, 236, 244, 252],
            DeviceDistribution = new Dictionary<string, int>
            {
                ["Khóa cửa"] = 24,
                ["Camera"] = 18,
                ["Chiếu sáng"] = 52,
                ["Cảm biến"] = 21,
                ["Motor cổng"] = 13
            },
            WorkOrders =
            [
                new WorkOrder("WO-2401", "Hiệu chỉnh motor cổng biệt thự", "Anh Quân", "Đang xử lý"),
                new WorkOrder("WO-2402", "Thay adapter camera sân sau", "Chị Vy", "Chờ vật tư"),
                new WorkOrder("WO-2403", "Tối ưu lịch bật/tắt đèn showroom", "Anh Hùng", "Hoàn tất"),
                new WorkOrder("WO-2404", "Kiểm tra khóa cửa khách sạn tầng 3", "Anh Thịnh", "Mới tạo")
            ]
        };
    }
}

public record MetricCard(string Title, string Value, string Note, string Variant);
public record ZoneStatus(string Name, string Status, int HealthPercent, string Description);
public record AlertItem(string Device, string Message, string Time, string Priority);
public record QuickAction(string Title, string Description);
public record WorkOrder(string Code, string Name, string Assignee, string Status);

public sealed class ChamCongDashboardModel
{
    public DateTime SelectedDate { get; set; } = DateTime.Today;
    public int? CurrentEmployeeId { get; set; }
    public IReadOnlyList<ChamCongLocationOption> LocationOptions { get; set; } = [];
    public IReadOnlyList<ChamCongEmployeeOption> EmployeeOptions { get; set; } = [];
    public IReadOnlyList<int> SelectedEmployeeIds { get; set; } = [];
    public IReadOnlyList<ChamCongHistoryItem> History { get; set; } = [];
    public ChamCongHistoryItem? OpenCheckin { get; set; }
    public ChamCongHistoryItem? OpenPurchaseCheckin { get; set; }
    public decimal? CheckinDistanceLimitMeters { get; set; }
    public bool CanSelectEmployees { get; set; }
    public bool CanAdminManageAttendance { get; set; }
    public bool CanCheckin => LocationOptions.Count > 0 &&
        ((CurrentEmployeeId.HasValue && CurrentEmployeeId.Value > 0) ||
         (CanSelectEmployees && SelectedEmployeeIds.Any(id => id > 0)));
}

public sealed class ChamCongEmployeeOption
{
    public int Id { get; set; }
    public string HoTen { get; set; } = string.Empty;
}

public sealed class ChamCongLocationOption
{
    public int IDDiaDiem { get; set; }
    public int IDKhachHang { get; set; }
    public string MaKhachHang { get; set; } = string.Empty;
    public string TenKhachHang { get; set; } = string.Empty;
    public string DiaChi { get; set; } = string.Empty;
    public decimal? LongAddress { get; set; }
    public decimal? LatAddress { get; set; }

    public string DisplayText => string.IsNullOrWhiteSpace(DiaChi)
        ? $"{TenKhachHang} ({MaKhachHang})"
        : $"{TenKhachHang} - {DiaChi}";
}

public sealed class ChamCongHistoryItem
{
    public int Id { get; set; }
    public int? IDYeuCau { get; set; }
    public string? MaYeuCau { get; set; }
    public int? IDKhachHang { get; set; }
    public int? IDDiaDiem { get; set; }
    public int? IDNhanVien { get; set; }
    public string? HoTenNhanVien { get; set; }
    public string? TenKhachHang { get; set; }
    public string? DiaChi { get; set; }
    public string? NguoiLienHe { get; set; }
    public string? DienThoai { get; set; }
    public string? DanhSachCongViec { get; set; }
    public string? CheckInType { get; set; }
    public DateTime? ThoiDiem { get; set; }
    public DateTime? ThoiDiemCheckOut { get; set; }
    public decimal? LongAddress { get; set; }
    public decimal? LatAddress { get; set; }
    public decimal? LongAddressCheckOut { get; set; }
    public decimal? LatAddressCheckOut { get; set; }
    public string? ImgPath { get; set; }
    public string? ImgPathCheckOut { get; set; }
    public string? GhiChuNhanVien { get; set; }
    public string? GhiChuCheckOut { get; set; }
    public bool? DuyetCheckIn { get; set; }
    public bool IsCheckinViolation { get; set; }
    public bool IsCheckoutViolation { get; set; }
    public IReadOnlyList<string> PurchaseWorkContent => ParsePurchaseNote(GhiChuNhanVien).WorkContent;
    public string? PurchaseNote => ParsePurchaseNote(GhiChuNhanVien).Note;
    public bool HasLegacyPurchaseNote => IsPurchase && ParsePurchaseNote(GhiChuNhanVien).IsLegacy;
    public bool IsOpen => ThoiDiem.HasValue && !ThoiDiemCheckOut.HasValue;
    public string AttendanceType
    {
        get
        {
            if (string.Equals(CheckInType, "MuaHang", StringComparison.OrdinalIgnoreCase))
            {
                return "MuaHang";
            }

            return IDYeuCau.HasValue ? "KhachHang" : "ChamCong";
        }
    }
    public bool IsPurchase => string.Equals(AttendanceType, "MuaHang", StringComparison.OrdinalIgnoreCase);
    public bool IsQuickPurchase => IsPurchase &&
        ThoiDiem.HasValue &&
        ThoiDiemCheckOut.HasValue &&
        ThoiDiem.Value == ThoiDiemCheckOut.Value &&
        string.Equals(ImgPath, ImgPathCheckOut, StringComparison.OrdinalIgnoreCase);
    public string CustomerDisplayName => string.IsNullOrWhiteSpace(TenKhachHang)
        ? (IsPurchase ? "Mua hàng" : (IDYeuCau.HasValue ? "Khách hàng chưa xác định" : "AppTech"))
        : TenKhachHang.Trim();
    public string LocationDisplayText => string.IsNullOrWhiteSpace(DiaChi)
        ? (IsPurchase ? "Vị trí GPS phát sinh" : "Chưa có địa chỉ")
        : DiaChi.Trim();
    public string Title => AttendanceType switch
    {
        "KhachHang" => $"Chấm công tại khách hàng: {CustomerDisplayName}",
        "MuaHang" => IsQuickPurchase ? "Đi mua hàng · Hoàn tất nhanh" : "Đi mua hàng / Đi ra ngoài",
        _ => $"Chấm công tại {CustomerDisplayName}"
    };

    private (IReadOnlyList<string> WorkContent, string? Note, bool IsLegacy) ParsePurchaseNote(string? rawValue)
    {
        if (!IsPurchase)
        {
            return ([], string.IsNullOrWhiteSpace(rawValue) ? null : rawValue.Trim(), false);
        }

        var note = string.IsNullOrWhiteSpace(rawValue) ? null : rawValue.Trim();
        if (string.IsNullOrWhiteSpace(note))
        {
            return ([], null, false);
        }

        if (!note.StartsWith('['))
        {
            return ([], note, true);
        }

        var endIndex = note.IndexOf(']');
        if (endIndex <= 1)
        {
            return ([], note, true);
        }

        var workContent = note[1..endIndex]
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var detailNote = note[(endIndex + 1)..].Trim();
        return workContent.Count == 0
            ? ([], note, true)
            : (workContent, string.IsNullOrWhiteSpace(detailNote) ? null : detailNote, false);
    }
}

public sealed class ChamCongCheckinRequest
{
    public int? IDNhanVien { get; set; }
    public int IDDiaDiem { get; set; }
    public DateTime? ThoiDiem { get; set; }
    public decimal? LongAddress { get; set; }
    public decimal? LatAddress { get; set; }
    public string? ImgPath { get; set; }
    public string? GhiChuNhanVien { get; set; }
}

public sealed class ChamCongCheckoutRequest
{
    public int? IDNhanVien { get; set; }
    public int Id { get; set; }
    public DateTime? ThoiDiemCheckOut { get; set; }
    public decimal? LongAddressCheckOut { get; set; }
    public decimal? LatAddressCheckOut { get; set; }
    public string? ImgPathCheckOut { get; set; }
    public string? GhiChuCheckOut { get; set; }
}

public sealed class MuaHangCheckinRequest
{
    public int? IDNhanVien { get; set; }
    public DateTime? ThoiDiem { get; set; }
    public decimal? LongAddress { get; set; }
    public decimal? LatAddress { get; set; }
    public string? ImgPath { get; set; }
    public string? GhiChuNhanVien { get; set; }
    public string? NoiDungCongViec { get; set; }
    public bool QuickCheckin { get; set; } = true;
}

public sealed class MuaHangCheckoutRequest
{
    public int? IDNhanVien { get; set; }
    public int Id { get; set; }
    public DateTime? ThoiDiemCheckOut { get; set; }
    public decimal? LongAddressCheckOut { get; set; }
    public decimal? LatAddressCheckOut { get; set; }
    public string? ImgPathCheckOut { get; set; }
    public string? GhiChuCheckOut { get; set; }
}

public sealed class MuaHangDeleteRequest
{
    public int Id { get; set; }
}
