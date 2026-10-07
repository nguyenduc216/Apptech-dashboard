using System.ComponentModel.DataAnnotations;

namespace ApptechDashboard.Models;

public static class NhapXuatKhoReportLoai
{
    public const string All = "tat-ca";
    public const string Nhap = "nhap";
    public const string Xuat = "xuat";

    public static string Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            Nhap or "phiếu nhập" or "phieu nhap" => Nhap,
            Xuat or "phiếu xuất" or "phieu xuat" => Xuat,
            _ => All
        };
    }

    public static IReadOnlyList<(string Value, string Text)> Options { get; } =
    [
        (All, "Tất cả"),
        (Nhap, "Nhập kho"),
        (Xuat, "Xuất kho")
    ];
}

public sealed class NhapXuatKhoReportQuery
{
    public string? Loai { get; set; }

    [DataType(DataType.Date)]
    public DateTime? FromDate { get; set; }

    [DataType(DataType.Date)]
    public DateTime? ToDate { get; set; }

    public string? VatTu { get; set; }

    public string? HangHoa { get; set; }

    // FEATURE_ID: APPTECH-REPORT-NHAP-XUAT-TON
    // CHANGE_ID: APPTECH-20261008-NHAP-XUAT-TON-003 - Exact-name chỉ dùng cho drill-down row tổng hợp.
    public bool ExactHangHoa { get; set; }

    public int? KhoId { get; set; }

    public string? MaPhieu { get; set; }
}

public sealed class NhapXuatKhoReportItem
{
    public const string LoaiNhap = "nhap";
    public const string LoaiXuat = "xuat";

    public string Loai { get; set; } = LoaiNhap;
    public DateTime? Ngay { get; set; }
    public int PhieuId { get; set; }
    public string? MaPhieu { get; set; }
    public string? NoiDung { get; set; }
    public string? MucDich { get; set; }
    public string? NguoiThaoTac { get; set; }
    public string? TenChiTiet { get; set; }
    public string? TenHangHoa { get; set; }
    public string? MaHangHoa { get; set; }
    public string? MaSoLo { get; set; }
    public string? QrCode { get; set; }
    public string? TenKho { get; set; }
    public string? MaKho { get; set; }
    public string? DonViTinh { get; set; }
    public decimal SoLuong { get; set; }
    public decimal DonGia { get; set; }
    public decimal ThanhTien { get; set; }
    public string? TrangThaiPhieu { get; set; }

    public string LoaiDisplay => Loai == LoaiXuat ? "Xuất kho" : "Nhập kho";
    public string LoaiCssClass => Loai == LoaiXuat ? "locked" : "active";
    public bool IsNhap => Loai == LoaiNhap;
}

public sealed class NhapXuatKhoReportFilterState
{
    public string Loai { get; set; } = NhapXuatKhoReportLoai.All;
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? VatTu { get; set; }
    public string? HangHoa { get; set; }
    public bool ExactHangHoa { get; set; }
    public int? KhoId { get; set; }
    public string? MaPhieu { get; set; }
}

public sealed class NhapXuatKhoLookupOption
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class NhapXuatKhoReportViewModel
{
    public NhapXuatKhoReportFilterState Filter { get; set; } = new();
    public IReadOnlyList<NhapXuatKhoReportItem> Items { get; set; } = [];
    public IReadOnlyList<NhapXuatKhoLookupOption> KhoOptions { get; set; } = [];
    public int TotalNhap { get; set; }
    public int TotalXuat { get; set; }
    public decimal TongSoLuongNhap { get; set; }
    public decimal TongSoLuongXuat { get; set; }
    public decimal TongTienNhap { get; set; }
    public decimal TongTienXuat { get; set; }
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";

    public string FromDateValue => Filter.FromDate?.ToString("yyyy-MM-dd") ?? string.Empty;
    public string ToDateValue => Filter.ToDate?.ToString("yyyy-MM-dd") ?? string.Empty;
}
