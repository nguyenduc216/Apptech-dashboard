using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ApptechDashboard.Models;

public static class YeuCauTrangThaiCatalog
{
    public const string TaoMoi = "Tạo mới";
    public const string DangThucHien = "Đang thực hiện";
    public const string HoanThanh = "Hoàn thành";
    public const string Huy = "Hủy";
    private const string LegacyChoThucHien = "Chờ thực hiện";

    public static IReadOnlyList<YeuCauTrangThaiOption> Options { get; } =
    [
        new() { Value = TaoMoi, Label = TaoMoi },
        new() { Value = DangThucHien, Label = DangThucHien },
        new() { Value = HoanThanh, Label = HoanThanh },
        new() { Value = Huy, Label = Huy }
    ];

    public static string? NormalizeOrNull(string? value)
    {
        return NormalizeKey(value) switch
        {
            "" => null,
            "taomoi" => TaoMoi,
            "chothuchien" => TaoMoi,
            "dangthuchien" => DangThucHien,
            "hoanthanh" => HoanThanh,
            "huy" => Huy,
            _ => null
        };
    }

    public static string Normalize(string? value)
    {
        return NormalizeOrNull(value) ?? TaoMoi;
    }

    public static bool IsSupported(string? value)
    {
        return NormalizeOrNull(value) is not null;
    }

    public static string GetCssClass(string? value)
    {
        return Normalize(value) switch
        {
            TaoMoi => "pending",
            DangThucHien => "in-progress",
            HoanThanh => "completed",
            Huy => "cancelled",
            _ => "pending"
        };
    }

    public static IReadOnlyList<string> GetFilterValues(string? value)
    {
        return NormalizeOrNull(value) switch
        {
            TaoMoi => [TaoMoi, LegacyChoThucHien],
            DangThucHien => [DangThucHien],
            HoanThanh => [HoanThanh],
            Huy => [Huy, "Huỷ"],
            _ => []
        };
    }

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = RepairMojibake(value.Trim()).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (character is '\u0111' or '\u0110')
            {
                builder.Append('d');
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string RepairMojibake(string value)
    {
        if (!value.Contains('Ã') &&
            !value.Contains('Â') &&
            !value.Contains('Ä') &&
            !value.Contains('Æ'))
        {
            return value;
        }

        try
        {
            var bytes = Encoding.GetEncoding(1252).GetBytes(value);
            var decoded = Encoding.UTF8.GetString(bytes);
            return decoded.Contains('\uFFFD') ? value : decoded;
        }
        catch
        {
            return value;
        }
    }
}

public sealed class YeuCauTrangThaiOption
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public static class YeuCauCongViecTrangThaiCatalog
{
    public const string TaoMoi = "Tạo mới";
    public const string DangThucHien = "Đang thực hiện";
    public const string ChoDanhGia = "Chờ đánh giá";
    public const string HoanThanh = "Hoàn thành";
    public const string Huy = "Hủy";

    public static IReadOnlyList<YeuCauTrangThaiOption> Options { get; } =
    [
        new() { Value = TaoMoi, Label = TaoMoi },
        new() { Value = DangThucHien, Label = DangThucHien },
        new() { Value = ChoDanhGia, Label = ChoDanhGia },
        new() { Value = HoanThanh, Label = HoanThanh },
        new() { Value = Huy, Label = Huy }
    ];

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TaoMoi;
        }

        var normalized = RemoveMarks(RepairMojibake(value));
        return normalized switch
        {
            "taomoi" => TaoMoi,
            "dangthuchien" => DangThucHien,
            "chodanhgia" => ChoDanhGia,
            "hoanthanh" => HoanThanh,
            "huy" => Huy,
            _ => TaoMoi
        };
    }

    public static bool IsCompleted(string? value) => Normalize(value) == HoanThanh;

    public static string GetCssClass(string? value)
    {
        return Normalize(value) switch
        {
            TaoMoi => "pending",
            DangThucHien => "in-progress",
            ChoDanhGia => "review",
            HoanThanh => "completed",
            Huy => "cancelled",
            _ => "pending"
        };
    }

    private static string RemoveMarks(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark ||
                char.IsWhiteSpace(character) ||
                character is '-' or '_')
            {
                continue;
            }

            builder.Append(character is '\u0111' or '\u0110'
                ? 'd'
                : char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static string RepairMojibake(string value)
    {
        if (!value.Contains('Ã') &&
            !value.Contains('Â') &&
            !value.Contains('Ä') &&
            !value.Contains('Æ'))
        {
            return value;
        }

        try
        {
            var bytes = Encoding.GetEncoding(1252).GetBytes(value);
            var decoded = Encoding.UTF8.GetString(bytes);
            return decoded.Contains('\uFFFD') ? value : decoded;
        }
        catch
        {
            return value;
        }
    }
}

public static class YeuCauCongViecTrangThaiFilter
{
    public const string TatCa = "all";
    public const string HoanThanh = "Hoàn thành";
    public const string ChuaHoanThanh = "incomplete";

    public static IReadOnlyList<YeuCauTrangThaiOption> Options { get; } =
    [
        new() { Value = TatCa, Label = "Tất cả" },
        new() { Value = HoanThanh, Label = "Hoàn thành" },
        new() { Value = ChuaHoanThanh, Label = "Chưa hoàn thành" }
    ];

    public static string Normalize(string? value, string defaultValue = TatCa)
    {
        if (string.Equals(value, HoanThanh, StringComparison.OrdinalIgnoreCase))
        {
            return HoanThanh;
        }

        if (string.Equals(value, ChuaHoanThanh, StringComparison.OrdinalIgnoreCase))
        {
            return ChuaHoanThanh;
        }

        return defaultValue;
    }
}

public sealed class YeuCauListQuery
{
    public string? Keyword { get; set; }
    public string? StatusFilter { get; set; }
    public DateTime? RequestDateFrom { get; set; }
    public DateTime? RequestDateTo { get; set; }
    public DateTime? ExecutionDateFrom { get; set; }
    public DateTime? ExecutionDateTo { get; set; }
    public string? AssigneeKeyword { get; set; }
    public string? WorkStatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class YeuCauFilterState
{
    public string? Keyword { get; set; }
    public string? StatusFilter { get; set; }
    public DateTime? RequestDateFrom { get; set; }
    public DateTime? RequestDateTo { get; set; }
    public DateTime? ExecutionDateFrom { get; set; }
    public DateTime? ExecutionDateTo { get; set; }
    public string? AssigneeKeyword { get; set; }
    public string? WorkStatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class YeuCauListItem
{
    public int Id { get; set; }
    public string MaYeuCau { get; set; } = string.Empty;
    public int? IDKhachHang { get; set; }
    public string? TenKhachHang { get; set; }
    public DateTime? NgayYeuCau { get; set; }
    public int? IDDiaDiem { get; set; }
    public string? DiaChi { get; set; }
    public string? NguoiLienHe { get; set; }
    public string? DienThoai { get; set; }
    public decimal? LongAddress { get; set; }
    public decimal? LatAddress { get; set; }
    public bool CheckinTheoKhoangCach { get; set; }
    public string? GhiChu { get; set; }
    public string? NhanVienThucHien { get; set; }
    public string? TrangThaiYeuCau { get; set; }
    public DateTime? NgayThucHien { get; set; }
    public DateTime? NgayHetHan { get; set; }
    public DateTime? NgayHoanThanh { get; set; }
    public DateTime? NgayHenTiepTheo { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedDate { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public IReadOnlyList<YeuCauNhanVienLienKetItem> NhanVienLienKet { get; set; } = [];
    public int SoCongViec { get; set; }
    public int SoCongViecHoanThanh { get; set; }
    public int TiLeHoanThanh => SoCongViec <= 0
        ? 0
        : (int)Math.Round(SoCongViecHoanThanh * 100m / SoCongViec);

    public string TenKhachHangDisplay => string.IsNullOrWhiteSpace(TenKhachHang)
        ? (IDKhachHang.HasValue ? $"Khách hàng #{IDKhachHang.Value}" : "Chưa chọn khách hàng")
        : TenKhachHang;

    public string DiaChiDisplay => string.IsNullOrWhiteSpace(DiaChi) ? "Chưa chọn địa điểm" : DiaChi;

    public string TrangThaiHienThi => YeuCauTrangThaiCatalog.Normalize(TrangThaiYeuCau);

    public string TrangThaiCssClass => YeuCauTrangThaiCatalog.GetCssClass(TrangThaiYeuCau);
}

public sealed class YeuCauLocationOption
{
    public int IDDiaDiem { get; set; }
    public int? IDKhachHang { get; set; }
    public string? TenKhachHang { get; set; }
    public string? DiaChi { get; set; }
    public string? NguoiLienHe { get; set; }
    public string? DienThoai { get; set; }
    public decimal? LongAddress { get; set; }
    public decimal? LatAddress { get; set; }
    public bool TrangThaiSuDung { get; set; } = true;

    public string TenKhachHangDisplay => string.IsNullOrWhiteSpace(TenKhachHang)
        ? (IDKhachHang.HasValue ? $"Khách hàng #{IDKhachHang.Value}" : "Khách hàng chưa xác định")
        : TenKhachHang;

    public string DiaChiDisplay => string.IsNullOrWhiteSpace(DiaChi) ? "Chưa có địa điểm" : DiaChi;

    public string DisplayLabel => $"{TenKhachHangDisplay} - {DiaChiDisplay}";

    public string CoordinateDisplay => LatAddress.HasValue && LongAddress.HasValue
        ? $"{LatAddress.Value:0.00000}, {LongAddress.Value:0.00000}"
        : "Chưa có tọa độ";
}

public sealed class YeuCauNhanVienOption
{
    public int Id { get; set; }
    public string HoTen { get; set; } = string.Empty;
    public string? ChucVu { get; set; }
    public string? Avatar { get; set; }
    public bool TrangThaiSuDung { get; set; } = true;

    public string DisplayText => string.IsNullOrWhiteSpace(ChucVu)
        ? HoTen
        : $"{HoTen} · {ChucVu}";
}

public sealed class YeuCauNhanVienLienKetItem
{
    public int? NhanVienId { get; set; }
    public string HoTen { get; set; } = string.Empty;
    public string? ChucVu { get; set; }
    public string? Avatar { get; set; }
}

public sealed class YeuCauCongViecChecklistFormItem
{
    public int ChecklistId { get; set; }
    public string TenChecklist { get; set; } = string.Empty;
    public int ViTri { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CreatedDate { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? FinishDate { get; set; }
    public string? FinishBy { get; set; }
}

public sealed class YeuCauCongViecNhanVienFormItem
{
    public int? NhanVienId { get; set; }
    public string HoTen { get; set; } = string.Empty;
    public string? ChucVu { get; set; }
    public string? Avatar { get; set; }
}

public sealed class YeuCauCongViecFormItem
{
    public int? YeuCauCongViecId { get; set; }
    public int? CongViecId { get; set; }
    public string TenCongViec { get; set; } = string.Empty;
    public string TrangThaiCongViec { get; set; } = YeuCauCongViecTrangThaiCatalog.TaoMoi;
    public string? GhiChu { get; set; }
    public DateTime? CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }
    public int SoLuongAnhCheckIn { get; set; }
    public int SoLuongAnhCheckOut { get; set; }

    public string TrangThaiCongViecDisplay => YeuCauCongViecTrangThaiCatalog.Normalize(TrangThaiCongViec);

    public string TrangThaiCongViecCssClass => YeuCauCongViecTrangThaiCatalog.GetCssClass(TrangThaiCongViec);

    [ValidateNever]
    public List<YeuCauCongViecImageItem> Images { get; set; } = [];

    [ValidateNever]
    public List<YeuCauCongViecChecklistFormItem> Checklists { get; set; } = [];

    [ValidateNever]
    public List<YeuCauCongViecNhanVienFormItem> NhanViens { get; set; } = [];
}

public sealed class YeuCauCongViecOption
{
    public int Id { get; set; }
    public string TenCongViec { get; set; } = string.Empty;
    public int SoLuongAnhCheckIn { get; set; }
    public int SoLuongAnhCheckOut { get; set; }
    public List<YeuCauCongViecChecklistFormItem> Checklists { get; set; } = [];
}

public sealed class YeuCauCongViecImageItem
{
    public int Id { get; set; }
    public int IDCongViec { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public string ImageType { get; set; } = YeuCauCongViecImageTypes.CheckIn;
    public DateTime? CreatedDate { get; set; }
    public string? CreatedBy { get; set; }
    public Guid? CreatedByAccountId { get; set; }
    public bool CanDelete { get; set; }
}

public sealed class YeuCauWorkChecklistCreateModel
{
    public int CongViecId { get; set; }
    public string? TenChecklist { get; set; }
}

public sealed class YeuCauWorkImageDeleteModel
{
    public int ImageId { get; set; }
}

public static class YeuCauCongViecImageTypes
{
    public const string CheckIn = "CheckIn";
    public const string CheckOut = "CheckOut";

    public static string Normalize(string? value)
    {
        return string.Equals(value, CheckOut, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "checkout", StringComparison.OrdinalIgnoreCase)
            ? CheckOut
            : CheckIn;
    }
}

public sealed class YeuCauCongViecImageUploadModel
{
    public int IDCongViec { get; set; }
    public string ImageType { get; set; } = YeuCauCongViecImageTypes.CheckIn;
}

public sealed class YeuCauLocationCoordinateUpdateModel
{
    public int IDYeuCau { get; set; }
    public int IDDiaDiem { get; set; }
    public decimal? LongAddress { get; set; }
    public decimal? LatAddress { get; set; }
}

public sealed class YeuCauLocationCreateModel
{
    public int? IDKhachHang { get; set; }
    public string? TenKhachHang { get; set; }
    public string? SoDienThoai { get; set; }
    public string? DiaChiKhachHang { get; set; }
    public string? DiaChi { get; set; }
    public string? NguoiLienHe { get; set; }
    public string? DienThoai { get; set; }
    public decimal? LongAddress { get; set; }
    public decimal? LatAddress { get; set; }
}

public sealed class YeuCauFormModel : IValidatableObject
{
    public int? Id { get; set; }

    [StringLength(10, ErrorMessage = "Mã yêu cầu tối đa 10 ký tự.")]
    public string? MaYeuCau { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ngày yêu cầu.")]
    [DataType(DataType.Date)]
    public DateTime? NgayYeuCau { get; set; }

    [DataType(DataType.Date)]
    public DateTime? NgayThucHien { get; set; }

    [DataType(DataType.Date)]
    public DateTime? NgayHetHan { get; set; }

    [DataType(DataType.Date)]
    public DateTime? NgayHoanThanh { get; set; }

    [DataType(DataType.Date)]
    public DateTime? NgayHenTiepTheo { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn địa điểm khách hàng.")]
    public int? IDDiaDiem { get; set; }

    [Required(ErrorMessage = "Không xác định được khách hàng từ địa điểm đã chọn.")]
    public int? IDKhachHang { get; set; }

    public int? NhanVienThucHienId { get; set; }

    public bool CheckinTheoKhoangCach { get; set; }

    [ValidateNever]
    public string? NhanVienThucHienText { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn trạng thái yêu cầu.")]
    [StringLength(250, ErrorMessage = "Trạng thái yêu cầu tối đa 250 ký tự.")]
    public string TrangThaiYeuCau { get; set; } = YeuCauTrangThaiCatalog.TaoMoi;

    [StringLength(50, ErrorMessage = "Ghi chú tối đa 50 ký tự.")]
    public string? GhiChu { get; set; }

    [ValidateNever]
    public List<YeuCauNhanVienLienKetItem> NhanVienLienKet { get; set; } = [];

    [ValidateNever]
    public List<YeuCauCongViecFormItem> CongViecs { get; set; } = [];

    public string? Keyword { get; set; }
    public string? StatusFilter { get; set; }
    public string? WorkStatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public string ActiveTab { get; set; } = "khach-hang";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!YeuCauTrangThaiCatalog.IsSupported(TrangThaiYeuCau))
        {
            yield return new ValidationResult(
                "Trạng thái yêu cầu không hợp lệ.",
                [nameof(TrangThaiYeuCau)]);
        }

        if (NgayThucHien.HasValue && NgayYeuCau.HasValue && NgayThucHien.Value.Date < NgayYeuCau.Value.Date)
        {
            yield return new ValidationResult(
                "Ngày thực hiện không được nhỏ hơn ngày yêu cầu.",
                [nameof(NgayThucHien)]);
        }

        var normalizedWorks = CongViecs
            .Where(work => work.CongViecId.HasValue && work.CongViecId.Value > 0)
            .ToList();

        foreach (var work in normalizedWorks)
        {
            work.TrangThaiCongViec = YeuCauCongViecTrangThaiCatalog.Normalize(work.TrangThaiCongViec);
            work.GhiChu = string.IsNullOrWhiteSpace(work.GhiChu) ? null : work.GhiChu.Trim();

            if (work.CheckInTime.HasValue &&
                work.CheckOutTime.HasValue &&
                work.CheckOutTime.Value <= work.CheckInTime.Value)
            {
                yield return new ValidationResult(
                    "Thời gian kết thúc công việc phải lớn hơn thời gian bắt đầu.",
                    [nameof(CongViecs)]);
            }
        }

        for (var i = 0; i < normalizedWorks.Count; i++)
        {
            for (var j = i + 1; j < normalizedWorks.Count; j++)
            {
                if (normalizedWorks[i].CongViecId != normalizedWorks[j].CongViecId)
                {
                    continue;
                }

                if (!normalizedWorks[i].CheckInTime.HasValue || !normalizedWorks[i].CheckOutTime.HasValue ||
                    !normalizedWorks[j].CheckInTime.HasValue || !normalizedWorks[j].CheckOutTime.HasValue)
                {
                    continue;
                }

                var startA = normalizedWorks[i].CheckInTime ?? DateTime.MinValue;
                var endA = normalizedWorks[i].CheckOutTime ?? DateTime.MinValue;
                var startB = normalizedWorks[j].CheckInTime ?? DateTime.MinValue;
                var endB = normalizedWorks[j].CheckOutTime ?? DateTime.MinValue;

                var hasOverlap = startA < endB && startB < endA;
                if (hasOverlap)
                {
                    yield return new ValidationResult(
                        "Cùng một công việc không được trùng khoảng thời gian thực hiện. Công việc trước phải kết thúc trước khi thêm lại.",
                        [nameof(CongViecs)]);
                }
            }
        }
    }
}

public sealed class YeuCauDeleteModel
{
    public int Id { get; set; }
    public string? Keyword { get; set; }
    public string? StatusFilter { get; set; }
    public DateTime? RequestDateFrom { get; set; }
    public DateTime? RequestDateTo { get; set; }
    public DateTime? ExecutionDateFrom { get; set; }
    public DateTime? ExecutionDateTo { get; set; }
    public string? AssigneeKeyword { get; set; }
    public string? WorkStatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class YeuCauCheckinItem
{
    public int Id { get; set; }
    public int? IDYeuCau { get; set; }
    public int? IDKhachHang { get; set; }
    public string? TenKhachHang { get; set; }
    public int? IDNhanVien { get; set; }
    public string? TenNhanVien { get; set; }
    public int? IDDiaDiem { get; set; }
    public DateTime? ThoiDiem { get; set; }
    public DateTime? ThoiDiemCheckOut { get; set; }
    public bool IsCheckIn { get; set; }
    public decimal? LongAddress { get; set; }
    public decimal? LatAddress { get; set; }
    public decimal? LongAddressCheckOut { get; set; }
    public decimal? LatAddressCheckOut { get; set; }
    public string? ImgPath { get; set; }
    public string? ImgPathCheckOut { get; set; }
    public string? GhiChuNhanVien { get; set; }
    public string? GhiChuCheckOut { get; set; }
    public bool IsOpen => ThoiDiem.HasValue && !ThoiDiemCheckOut.HasValue;

    public string CustomerDisplay => string.IsNullOrWhiteSpace(TenKhachHang)
        ? (IDKhachHang.HasValue ? $"Khách hàng #{IDKhachHang.Value}" : "Chưa xác định")
        : TenKhachHang;

    public string EmployeeDisplay => string.IsNullOrWhiteSpace(TenNhanVien)
        ? (IDNhanVien.HasValue ? $"Nhân viên #{IDNhanVien.Value}" : "Chưa xác định")
        : TenNhanVien;

    public string CoordinateDisplay => LatAddress.HasValue && LongAddress.HasValue
        ? $"{LatAddress.Value:0.00000}, {LongAddress.Value:0.00000}"
        : "Chưa có tọa độ";
}

public sealed class YeuCauCheckinCreateModel
{
    public int IDYeuCau { get; set; }
    public int? IDKhachHang { get; set; }
    public int? IDNhanVien { get; set; }
    public int? IDDiaDiem { get; set; }
    public DateTime? ThoiDiem { get; set; }
    public decimal? LongAddress { get; set; }
    public decimal? LatAddress { get; set; }
    public string? ImgPath { get; set; }
    public string? GhiChuNhanVien { get; set; }
}

public sealed class YeuCauCheckoutCreateModel
{
    public int Id { get; set; }
    public int IDYeuCau { get; set; }
    public int? IDNhanVien { get; set; }
    public DateTime? ThoiDiemCheckOut { get; set; }
    public decimal? LongAddressCheckOut { get; set; }
    public decimal? LatAddressCheckOut { get; set; }
    public string? ImgPathCheckOut { get; set; }
    public string? GhiChuCheckOut { get; set; }
}

public sealed class YeuCauManagementViewModel
{
    public YeuCauFilterState Filter { get; set; } = new();
    public IReadOnlyList<YeuCauListItem> Items { get; set; } = [];
    public IReadOnlyList<YeuCauNhanVienOption> NhanVienOptions { get; set; } = [];
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; } = 1;
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";
    public bool CurrentUserIsAdmin { get; set; }
    public int? CurrentEmployeeId { get; set; }
    public IReadOnlyList<YeuCauTrangThaiOption> StatusOptions { get; set; } = YeuCauTrangThaiCatalog.Options;
    public IReadOnlyList<YeuCauTrangThaiOption> WorkStatusOptions { get; set; } = YeuCauCongViecTrangThaiFilter.Options;

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

public sealed class YeuCauDetailViewModel
{
    public YeuCauFilterState Filter { get; set; } = new();
    public YeuCauFormModel Form { get; set; } = new();
    public IReadOnlyList<YeuCauNhanVienOption> NhanVienOptions { get; set; } = [];
    public IReadOnlyList<YeuCauCongViecOption> WorkOptions { get; set; } = [];
    public IReadOnlyList<YeuCauTrangThaiOption> StatusOptions { get; set; } = YeuCauTrangThaiCatalog.Options;
    public IReadOnlyList<YeuCauTrangThaiOption> WorkStatusOptions { get; set; } = YeuCauCongViecTrangThaiCatalog.Options;
    public IReadOnlyList<YeuCauCheckinItem> Checkins { get; set; } = [];
    public YeuCauLocationOption? SelectedLocation { get; set; }
    public string GeneratedCode { get; set; } = string.Empty;
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";
    public int? CurrentEmployeeId { get; set; }
    public bool CurrentUserIsAdmin { get; set; }
    public bool CanToggleCheckinDistanceConstraint { get; set; }
    public decimal? CheckinDistanceLimitMeters { get; set; }

    public bool IsEditMode => Form.Id.HasValue && Form.Id.Value > 0;

    public string PageTitle => IsEditMode ? "Cập nhật yêu cầu" : "Thêm yêu cầu";

    public string PageDescription => IsEditMode
        ? "Theo dõi yêu cầu khách hàng, cập nhật trạng thái xử lý và thông tin địa điểm thực hiện trên một trang riêng."
        : "Tạo mới yêu cầu khách hàng, chọn địa điểm bằng autocomplete và phân công nhân viên chịu trách nhiệm.";

    public string SubmitAction => IsEditMode ? "Update" : "Create";

    public string MaYeuCauDisplay => IsEditMode
        ? (string.IsNullOrWhiteSpace(Form.MaYeuCau) ? GeneratedCode : Form.MaYeuCau!)
        : GeneratedCode;
}
