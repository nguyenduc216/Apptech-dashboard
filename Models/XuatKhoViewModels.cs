using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ApptechDashboard.Models;

public enum XuatKhoPopupMode
{
    None = 0,
    Create = 1,
    Edit = 2
}

public static class XuatKhoPhieuStatus
{
    public const string Draft = "phieu-nhap";
    public const string Exported = "xuat-kho";
    public const string Canceled = "huy-phieu";

    public static string Normalize(string? status)
    {
        var normalized = status?.Trim().ToLowerInvariant();
        return normalized switch
        {
            Draft or "phiếu nháp" or "phieu nhap" => Draft,
            Exported or "da-xuat" or "da xuat" or "xuat kho" or "xuất kho" => Exported,
            Canceled or "hủy phiếu" or "huy phieu" => Canceled,
            _ => Draft
        };
    }

    public static string GetDisplayName(string? status) => Normalize(status) switch
    {
        Exported => "Xuất kho",
        Canceled => "Hủy phiếu",
        _ => "Phiếu nháp"
    };

    public static string GetCssClass(string? status) => Normalize(status) switch
    {
        Exported => "active",
        Canceled => "locked",
        _ => "pending"
    };
}

public static class XuatKhoMucDich
{
    public const string XuatBanHang = "xuat-ban-hang";
    public const string XuatCongTrinh = "xuat-cong-trinh";
    public const string XuatTraNcc = "xuat-tra-ncc";
    public const string XuatHuy = "xuat-huy";

    public static IReadOnlyList<(string Value, string Text)> Options { get; } =
    [
        (XuatBanHang, "Xuất bán hàng"),
        (XuatCongTrinh, "Xuất công trình"),
        (XuatTraNcc, "Xuất trả NCC"),
        (XuatHuy, "Xuất hủy")
    ];

    public static string Normalize(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            XuatBanHang or "xuat ban hang" or "xuất bán hàng" => XuatBanHang,
            XuatCongTrinh or "xuat cong trinh" or "xuất công trình" => XuatCongTrinh,
            XuatTraNcc or "xuat tra ncc" or "xuất trả ncc" => XuatTraNcc,
            XuatHuy or "xuat huy" or "xuất hủy" => XuatHuy,
            _ => XuatBanHang
        };
    }

    public static string GetDisplayName(string? value)
    {
        var normalized = Normalize(value);
        return Options.FirstOrDefault(item => item.Value == normalized).Text ?? "Xuất bán hàng";
    }
}

public sealed class XuatKhoListQuery
{
    public string? Keyword { get; set; }
    public string? StatusFilter { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public bool ShowCreatePopup { get; set; }
    public int? EditId { get; set; }
}

public sealed class XuatKhoFilterState
{
    public string? Keyword { get; set; }
    public string? StatusFilter { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class XuatKhoListItem
{
    public int Id { get; set; }
    public DateTime? NgayXuatKho { get; set; }
    public string MaPhieu { get; set; } = string.Empty;
    public string? NoiDungXuatKho { get; set; }
    public string MucDichXuat { get; set; } = XuatKhoMucDich.XuatBanHang;
    public string? NguoiXuatKho { get; set; }
    public string? NguoiNhanHang { get; set; }
    public string? DiaChiNguoiNhanHang { get; set; }
    public string TrangThaiPhieu { get; set; } = XuatKhoPhieuStatus.Draft;
    public int DetailCount { get; set; }
    public decimal TotalQuantity { get; set; }

    public string TrangThaiDisplay => XuatKhoPhieuStatus.GetDisplayName(TrangThaiPhieu);
    public string TrangThaiCssClass => XuatKhoPhieuStatus.GetCssClass(TrangThaiPhieu);
}

public sealed class XuatKhoDetailItem
{
    public int? Id { get; set; }
    public int VatTuId { get; set; }
    public int? HangHoaId { get; set; }
    public string TenChiTiet { get; set; } = string.Empty;
    public string? TenHangHoa { get; set; }
    public string? MaHangHoa { get; set; }
    public string? TenKho { get; set; }
    public string? MaKho { get; set; }
    public string? DonViTinh { get; set; }
    public string? QRCode { get; set; }
    public string? MaSoLo { get; set; }
    public string? SoChungTu { get; set; }
    public DateTime? NgayNhapKho { get; set; }
    public string? ViTriLuuKho { get; set; }
    public string? ImageUrl { get; set; }
    public decimal SoLuongNhap { get; set; }
    public decimal SoLuongTon { get; set; }
    public decimal DonGiaNhap { get; set; }
    public decimal DonGiaBanLe { get; set; }
    public decimal DonGiaXuat { get; set; }
    public decimal TongTienXuat { get; set; }

    [Range(typeof(decimal), "1", "9999999999999999.99", ErrorMessage = "Số lượng xuất phải lớn hơn hoặc bằng 1.")]
    public decimal SoLuongXuat { get; set; } = 1;
}

public sealed class XuatKhoFormModel
{
    public int? Id { get; set; }
    public string? MaPhieu { get; set; }
    public DateTime? NgayXuatKho { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "Vui lòng nhập nội dung xuất kho.")]
    [StringLength(550, ErrorMessage = "Nội dung xuất kho tối đa 550 ký tự.")]
    public string? NoiDungXuatKho { get; set; }

    [Required(ErrorMessage = "Vui long chon muc dich xuat.")]
    public string MucDichXuat { get; set; } = XuatKhoMucDich.XuatBanHang;

    public string? NguoiXuatKho { get; set; }
    [StringLength(250, ErrorMessage = "Nguoi nhan hang toi da 250 ky tu.")]
    public string? NguoiNhanHang { get; set; }
    [StringLength(550, ErrorMessage = "Dia chi nguoi nhan hang toi da 550 ky tu.")]
    public string? DiaChiNguoiNhanHang { get; set; }
    public string TrangThaiPhieu { get; set; } = XuatKhoPhieuStatus.Draft;

    [ValidateNever]
    public List<XuatKhoDetailItem> Details { get; set; } = [];

    public string? Keyword { get; set; }
    public string? StatusFilter { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public string ActiveTab { get; set; } = "thong-tin";
}

public sealed class XuatKhoDeleteModel
{
    public int Id { get; set; }
    public string? Keyword { get; set; }
    public string? StatusFilter { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class XuatKhoExportViewModel
{
    public XuatKhoListItem Header { get; set; } = new();
    public IReadOnlyList<XuatKhoDetailItem> Details { get; set; } = [];
}

public sealed class XuatKhoManagementViewModel
{
    public XuatKhoFilterState Filter { get; set; } = new();
    public XuatKhoFormModel Form { get; set; } = new();
    public IReadOnlyList<XuatKhoListItem> Items { get; set; } = [];
    public XuatKhoPopupMode PopupMode { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; } = 1;
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";

    public bool IsPopupOpen => PopupMode != XuatKhoPopupMode.None;

    public string PopupTitle => PopupMode switch
    {
        XuatKhoPopupMode.Create => "Tạo phiếu xuất kho",
        XuatKhoPopupMode.Edit => "Chi tiết phiếu xuất kho",
        _ => "Xuất kho"
    };

    public string SubmitAction => PopupMode == XuatKhoPopupMode.Edit ? "Update" : "Create";

    public IReadOnlyList<int> VisiblePages
    {
        get
        {
            if (TotalPages <= 0)
            {
                return [];
            }

            var start = Math.Max(1, CurrentPage - 2);
            var end = Math.Min(TotalPages, start + 4);
            start = Math.Max(1, end - 4);
            return Enumerable.Range(start, end - start + 1).ToArray();
        }
    }
}

