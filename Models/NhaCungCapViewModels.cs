using System.ComponentModel.DataAnnotations;

namespace ApptechDashboard.Models;

public enum NhaCungCapPopupMode
{
    None = 0,
    Create = 1,
    Edit = 2
}

public sealed class NhaCungCapListQuery
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public bool ShowCreatePopup { get; set; }
    public int? EditId { get; set; }
}

public sealed class NhaCungCapFilterState
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class NhaCungCapListItem
{
    public int Id { get; set; }
    public string TenNhaCungCap { get; set; } = string.Empty;
    public string? SoDienThoai { get; set; }
    public string? Email { get; set; }
    public string? DiaChi { get; set; }
    public bool TrangThaiSuDung { get; set; }
}

public sealed class NhaCungCapFormModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên nhà cung cấp.")]
    [StringLength(250, ErrorMessage = "Tên nhà cung cấp tối đa 250 ký tự.")]
    public string TenNhaCungCap { get; set; } = string.Empty;

    [StringLength(50, ErrorMessage = "Số điện thoại tối đa 50 ký tự.")]
    public string? SoDienThoai { get; set; }

    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(250, ErrorMessage = "Email tối đa 250 ký tự.")]
    public string? Email { get; set; }

    [StringLength(550, ErrorMessage = "Địa chỉ tối đa 550 ký tự.")]
    public string? DiaChi { get; set; }

    public bool TrangThaiSuDung { get; set; } = true;
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class NhaCungCapDeleteModel
{
    public int Id { get; set; }
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class NhaCungCapManagementViewModel
{
    public NhaCungCapFilterState Filter { get; set; } = new();
    public NhaCungCapFormModel Form { get; set; } = new();
    public IReadOnlyList<NhaCungCapListItem> Items { get; set; } = [];
    public NhaCungCapPopupMode PopupMode { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; } = 1;
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";

    public bool IsPopupOpen => PopupMode != NhaCungCapPopupMode.None;

    public string PopupTitle => PopupMode switch
    {
        NhaCungCapPopupMode.Create => "Thêm nhà cung cấp",
        NhaCungCapPopupMode.Edit => "Cập nhật nhà cung cấp",
        _ => "Nhà cung cấp"
    };

    public string SubmitAction => PopupMode == NhaCungCapPopupMode.Edit ? "Update" : "Create";

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
