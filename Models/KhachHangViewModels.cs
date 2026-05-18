using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ApptechDashboard.Models;

public sealed class KhachHangListQuery
{
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class KhachHangFilterState
{
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class KhachHangListItem
{
    public int Id { get; set; }
    public string TenKhachHang { get; set; } = string.Empty;
    public string? MaKhachHang { get; set; }
    public string? DiaChi { get; set; }
    public string? SoDienThoai { get; set; }
    public string? NguoiDaiDien { get; set; }
    public string? NganhNghe { get; set; }
    public string? ZaloID { get; set; }
    public string? GhiChu { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedDate { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public IReadOnlyList<KhachHangDiaDiemFormItem> DiaDiemLamViec { get; set; } = [];
    public bool IsApptechProtected => !string.IsNullOrWhiteSpace(MaKhachHang) &&
        MaKhachHang.Trim().StartsWith("Apptech", StringComparison.OrdinalIgnoreCase);
}

public sealed class KhachHangDiaDiemFormItem
{
    public int? Id { get; set; }
    public string? DiaChi { get; set; }
    public string? NguoiLienHe { get; set; }
    public string? DienThoai { get; set; }
    public decimal? LongAddress { get; set; }
    public decimal? LatAddress { get; set; }
    public bool TrangThaiSuDung { get; set; } = true;
}

public sealed class KhachHangDiaDiemSaveModel
{
    public int? Id { get; set; }
    public int? IDKhachHang { get; set; }
    public string? DiaChi { get; set; }
    public string? NguoiLienHe { get; set; }
    public string? DienThoai { get; set; }
    public decimal? LongAddress { get; set; }
    public decimal? LatAddress { get; set; }
    public bool TrangThaiSuDung { get; set; } = true;
}

public sealed class KhachHangFormModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên khách hàng.")]
    [StringLength(250, ErrorMessage = "Tên khách hàng tối đa 250 ký tự.")]
    public string TenKhachHang { get; set; } = string.Empty;

    [StringLength(50, ErrorMessage = "Mã khách hàng tối đa 50 ký tự.")]
    public string? MaKhachHang { get; set; }

    [StringLength(250, ErrorMessage = "Địa chỉ tối đa 250 ký tự.")]
    public string? DiaChi { get; set; }

    [StringLength(50, ErrorMessage = "Số điện thoại tối đa 50 ký tự.")]
    public string? SoDienThoai { get; set; }

    [StringLength(150, ErrorMessage = "Người đại diện tối đa 150 ký tự.")]
    public string? NguoiDaiDien { get; set; }

    [StringLength(250, ErrorMessage = "Ngành nghề tối đa 250 ký tự.")]
    public string? NganhNghe { get; set; }

    [StringLength(150, ErrorMessage = "Thông tin Zalo tối đa 150 ký tự.")]
    public string? ZaloID { get; set; }

    [StringLength(250, ErrorMessage = "Ghi chú tối đa 250 ký tự.")]
    public string? GhiChu { get; set; }

    [ValidateNever]
    public List<KhachHangDiaDiemFormItem> DiaDiemLamViec { get; set; } = [];

    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public string ActiveTab { get; set; } = "thong-tin";
}

public sealed class KhachHangDeleteModel
{
    public int Id { get; set; }
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class KhachHangManagementViewModel
{
    public KhachHangFilterState Filter { get; set; } = new();
    public IReadOnlyList<KhachHangListItem> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; } = 1;
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";
    public bool CurrentUserIsAdmin { get; set; }

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

public sealed class KhachHangDetailViewModel
{
    public KhachHangFilterState Filter { get; set; } = new();
    public KhachHangFormModel Form { get; set; } = new();
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";
    public bool CurrentUserIsAdmin { get; set; }

    public bool IsEditMode => Form.Id.HasValue && Form.Id.Value > 0;

    public bool IsProtectedApptechCustomer => IsEditMode &&
        !string.IsNullOrWhiteSpace(Form.MaKhachHang) &&
        Form.MaKhachHang.Trim().StartsWith("Apptech", StringComparison.OrdinalIgnoreCase);

    public string PageTitle => IsEditMode ? "Cập nhật khách hàng" : "Thêm khách hàng";

    public string PageDescription => IsEditMode
        ? "Chỉnh sửa thông tin khách hàng và danh sách địa điểm làm việc trên một trang riêng."
        : "Tạo mới khách hàng và khai báo các địa điểm làm việc kèm vị trí bản đồ.";

    public string SubmitAction => IsEditMode ? "Update" : "Create";
}
