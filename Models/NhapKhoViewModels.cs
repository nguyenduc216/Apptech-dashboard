using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ApptechDashboard.Models;

public enum NhapKhoPopupMode
{
    None = 0,
    Create = 1,
    Edit = 2
}

public static class NhapKhoPhieuStatus
{
    public const string Draft = "phieu-nhap";
    public const string Imported = "da-nhap";
    public const string Canceled = "huy-phieu";

    public static string Normalize(string? status)
    {
        var normalized = status?.Trim().ToLowerInvariant();
        return normalized switch
        {
            Draft or "phieu nhap" or "phiếu nháp" => Draft,
            Imported or "da nhap" or "đã nhập" or "nhap kho" or "nhập kho" => Imported,
            Canceled or "huy phieu" or "hủy phiếu" => Canceled,
            _ => Draft
        };
    }

    public static string GetDisplayName(string? status) => Normalize(status) switch
    {
        Imported => "Đã nhập",
        Canceled => "Hủy phiếu",
        _ => "Phiếu nháp"
    };

    public static string GetCssClass(string? status) => Normalize(status) switch
    {
        Imported => "active",
        Canceled => "locked",
        _ => "pending"
    };
}

public static class NhapKhoLoaiHinh
{
    public const string NhapTungVatTu = "nhap-tung-vat-tu";
    public const string NhapTheoLo = "nhap-theo-lo";

    public static string Normalize(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            NhapTungVatTu or "nhap tung vat tu" or "nhập từng vật tư" => NhapTungVatTu,
            NhapTheoLo or "nhap theo lo" or "nhập theo lô" => NhapTheoLo,
            _ => string.Empty
        };
    }

    public static string GetDisplayName(string? value) => Normalize(value) switch
    {
        NhapTungVatTu => "Nhập từng vật tư",
        NhapTheoLo => "Nhập theo lô",
        _ => string.Empty
    };
}

public sealed class NhapKhoListQuery
{
    public string? Keyword { get; set; }
    public string? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public bool ShowCreatePopup { get; set; }
    public int? EditId { get; set; }
}

public sealed class NhapKhoFilterState
{
    public string? Keyword { get; set; }
    public string? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class NhapKhoListItem
{
    public int Id { get; set; }
    public DateTime? NgayNhapKho { get; set; }
    public string MaPhieu { get; set; } = string.Empty;
    public string? NoiDungNhapKho { get; set; }
    public string? NguoiNhapKho { get; set; }
    public int? KhoId { get; set; }
    public string? TenKho { get; set; }
    public int? NhaCungCapId { get; set; }
    public string? TenNhaCungCap { get; set; }
    public string TrangThaiPhieu { get; set; } = NhapKhoPhieuStatus.Draft;
    public int DetailCount { get; set; }
    public decimal TotalQuantity { get; set; }

    public string TrangThaiDisplay => NhapKhoPhieuStatus.GetDisplayName(TrangThaiPhieu);
    public string TrangThaiCssClass => NhapKhoPhieuStatus.GetCssClass(TrangThaiPhieu);
}

public sealed class NhapKhoDetailItem
{
    public int? Id { get; set; }
    public int HangHoaId { get; set; }
    public string? TenHangHoa { get; set; }
    public string? MaHangHoa { get; set; }
    public int? DonViTinhId { get; set; }
    public string? TenDonViTinh { get; set; }
    public string? TenVietTatDonViTinh { get; set; }
    [StringLength(50, ErrorMessage = "Mã số lô tối đa 50 ký tự.")]
    public string? MaSoLo { get; set; }
    [StringLength(50, ErrorMessage = "So chung tu toi da 50 ky tu.")]
    public string? SoChungTu { get; set; }
    [Required(ErrorMessage = "Vui lòng chọn loại hình nhập.")]
    public string? LoaiHinhNhap { get; set; }

    [Range(typeof(decimal), "0.01", "9999999999999999.99", ErrorMessage = "Số lượng nhập phải lớn hơn 0.")]
    public decimal SoLuongNhap { get; set; } = 1;

    [Range(typeof(decimal), "0.01", "9999999999999999.9999", ErrorMessage = "So luong quy doi phai lon hon 0.")]
    public decimal SoLuongQuyDoi { get; set; } = 1;

    [Range(1, int.MaxValue, ErrorMessage = "Vui long chon don vi nhap.")]
    public int? DonViNhapId { get; set; }
    public string? TenDonViNhap { get; set; }
    public string? TenVietTatDonViNhap { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "Don gia nhap khong hop le.")]
    public decimal DonGiaNhap { get; set; }

    [Range(typeof(decimal), "0", "9999999999999999.99", ErrorMessage = "Don gia ban le khong hop le.")]
    public decimal DonGiaBanLe { get; set; }
}

public sealed class NhapKhoFormModel
{
    public int? Id { get; set; }
    public string? MaPhieu { get; set; }
    public DateTime? NgayNhapKho { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "Vui lòng nhập nội dung nhập kho.")]
    [StringLength(550, ErrorMessage = "Nội dung nhập kho tối đa 550 ký tự.")]
    public string? NoiDungNhapKho { get; set; }

    public string? NguoiNhapKho { get; set; }
    public string TrangThaiPhieu { get; set; } = NhapKhoPhieuStatus.Draft;

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn kho nhập.")]
    public int? KhoId { get; set; }

    public int? NhaCungCapId { get; set; }

    [ValidateNever]
    public List<NhapKhoDetailItem> Details { get; set; } = [];

    public string? Keyword { get; set; }
    public string? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public string ActiveTab { get; set; } = "thong-tin";
}

public sealed class NhapKhoExportViewModel
{
    public NhapKhoListItem Header { get; set; } = new();
    public IReadOnlyList<NhapKhoDetailItem> Details { get; set; } = [];
}

public sealed class NhapKhoDeleteModel
{
    public int Id { get; set; }
    public string? Keyword { get; set; }
    public string? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class NhapKhoLookupOption
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public int? DonViTinhId { get; set; }
}

public sealed class NhapKhoManagementViewModel
{
    public NhapKhoFilterState Filter { get; set; } = new();
    public NhapKhoFormModel Form { get; set; } = new();
    public IReadOnlyList<NhapKhoListItem> Items { get; set; } = [];
    public IReadOnlyList<NhapKhoLookupOption> KhoOptions { get; set; } = [];
    public IReadOnlyList<NhapKhoLookupOption> HangHoaOptions { get; set; } = [];
    public IReadOnlyList<NhapKhoLookupOption> DonViTinhOptions { get; set; } = [];
    public IReadOnlyList<NhapKhoLookupOption> NhaCungCapOptions { get; set; } = [];
    public NhapKhoPopupMode PopupMode { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; } = 1;
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";

    public bool IsPopupOpen => PopupMode != NhapKhoPopupMode.None;

    public string PopupTitle => PopupMode switch
    {
        NhapKhoPopupMode.Create => "Tạo phiếu nhập kho",
        NhapKhoPopupMode.Edit => "Chi tiết phiếu nhập kho",
        _ => "Nhập kho"
    };

    public string SubmitAction => PopupMode == NhapKhoPopupMode.Edit ? "Update" : "Create";

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
