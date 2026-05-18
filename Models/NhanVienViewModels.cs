using System.ComponentModel.DataAnnotations;

namespace ApptechDashboard.Models;

public sealed class NhanVienListQuery
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public int? RoleEmployeeId { get; set; }
}

public sealed class NhanVienFilterState
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class NhanVienListItem
{
    public int Id { get; set; }
    public string Ho { get; set; } = string.Empty;
    public string Ten { get; set; } = string.Empty;
    public string? GioiTinh { get; set; }
    public DateTime? NgaySinh { get; set; }
    public int? IDPhongBan { get; set; }
    public string? TenPhongBan { get; set; }
    public bool TrangThaiSuDung { get; set; } = true;
    public string? ChucVu { get; set; }
    public string? Email { get; set; }
    public string? Avatar { get; set; }
    public Guid? IDTaiKhoan { get; set; }
    public string? TenDangNhap { get; set; }
    public bool IsAdministrator { get; set; }

    public string HoTen => string.Join(" ", new[] { Ho, Ten }.Where(static part => !string.IsNullOrWhiteSpace(part)));
}

public sealed class PhongBanOption
{
    public int Id { get; set; }
    public string TenPhongBan { get; set; } = string.Empty;
}

public sealed class NhanVienFormModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập họ nhân viên.")]
    [StringLength(120, ErrorMessage = "Họ tối đa 120 ký tự.")]
    public string Ho { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên nhân viên.")]
    [StringLength(80, ErrorMessage = "Tên tối đa 80 ký tự.")]
    public string Ten { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng chọn giới tính.")]
    [RegularExpression("^(Nam|Nữ|Khác)$", ErrorMessage = "Giới tính không hợp lệ.")]
    public string GioiTinh { get; set; } = "Nam";

    [DataType(DataType.Date)]
    public DateTime? NgaySinh { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn phòng ban.")]
    public int? IDPhongBan { get; set; }

    public bool TrangThaiSuDung { get; set; } = true;

    [StringLength(150, ErrorMessage = "Chức vụ tối đa 150 ký tự.")]
    public string? ChucVu { get; set; }

    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(150, ErrorMessage = "Email tối đa 150 ký tự.")]
    public string? Email { get; set; }

    public string? Avatar { get; set; }

    [Display(Name = "Avatar")]
    public IFormFile? AvatarFile { get; set; }

    public bool TaoTaiKhoan { get; set; }
    public Guid? IDTaiKhoan { get; set; }
    public bool IsAdministrator { get; set; }

    [StringLength(100, ErrorMessage = "Tên đăng nhập tối đa 100 ký tự.")]
    public string? TenDangNhap { get; set; }

    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải từ 6 đến 100 ký tự.")]
    public string? MatKhau { get; set; }

    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class NhanVienDeleteModel
{
    public int Id { get; set; }
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class NhanVienRoleOption
{
    public int Id { get; set; }
    public string TenVaiTro { get; set; } = string.Empty;
    public string? MieuTa { get; set; }
}

public sealed class NhanVienRoleAssignmentModel
{
    public int EmployeeId { get; set; }
    public Guid AccountId { get; set; }
    public List<int> SelectedRoleIds { get; set; } = [];
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class NhanVienRoleAssignmentViewModel
{
    public int EmployeeId { get; set; }
    public Guid AccountId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string? Username { get; set; }
    public IReadOnlyList<int> SelectedRoleIds { get; set; } = [];
}

public sealed class NhanVienManagementViewModel
{
    public NhanVienFilterState Filter { get; set; } = new();
    public IReadOnlyList<NhanVienListItem> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; } = 1;
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";
    public IReadOnlyList<NhanVienRoleOption> RoleOptions { get; set; } = [];
    public NhanVienRoleAssignmentViewModel? RoleAssignment { get; set; }
    public bool IsRolePopupOpen => RoleAssignment is not null;

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

public sealed class NhanVienDetailViewModel
{
    public NhanVienFilterState Filter { get; set; } = new();
    public NhanVienFormModel Form { get; set; } = new();
    public IReadOnlyList<PhongBanOption> PhongBanOptions { get; set; } = [];
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";
    public bool IsEditMode => Form.Id.HasValue;
    public string PageTitle => IsEditMode ? "Cập nhật nhân viên" : "Thêm nhân viên";
    public string SubmitAction => IsEditMode ? "Update" : "Create";
}
