using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ApptechDashboard.Models;

public sealed class UpdateAccountViewModel
{
    public UpdateProfileInputModel Profile { get; set; } = new();

    public ChangePasswordInputModel Password { get; set; } = new();
}

public sealed class UpdateProfileInputModel
{
    [Display(Name = "Tên đăng nhập")]
    public string Username { get; set; } = "";

    [Display(Name = "Nhóm người dùng")]
    public string? GroupName { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập họ và tên.")]
    [StringLength(120, ErrorMessage = "Họ và tên không được vượt quá 120 ký tự.")]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = "";

    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(150, ErrorMessage = "Email không được vượt quá 150 ký tự.")]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Ngày sinh")]
    public DateTime? DateOfBirth { get; set; }

    [StringLength(250, ErrorMessage = "Địa chỉ không được vượt quá 250 ký tự.")]
    [Display(Name = "Địa chỉ")]
    public string? Address { get; set; }

    [StringLength(30, ErrorMessage = "Số điện thoại không được vượt quá 30 ký tự.")]
    [Phone(ErrorMessage = "Số điện thoại không đúng định dạng.")]
    [Display(Name = "Điện thoại")]
    public string? PhoneNumber { get; set; }

    [StringLength(20, ErrorMessage = "Giới tính không hợp lệ.")]
    [Display(Name = "Giới tính")]
    public string? Gender { get; set; }

    [StringLength(100, ErrorMessage = "Zalo ID không được vượt quá 100 ký tự.")]
    [Display(Name = "Zalo ID")]
    public string? ZaloId { get; set; }

    public string? AvatarUrl { get; set; }

    [Display(Name = "Avatar đại diện")]
    public IFormFile? AvatarFile { get; set; }
}

public sealed class ChangePasswordInputModel
{
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu hiện tại.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu hiện tại")]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu mới phải từ 6 ký tự trở lên.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mật khẩu mới")]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu mới.")]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "Xác nhận mật khẩu mới không khớp.")]
    [Display(Name = "Xác nhận mật khẩu mới")]
    public string ConfirmNewPassword { get; set; } = "";
}
