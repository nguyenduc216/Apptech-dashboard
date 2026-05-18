using System.ComponentModel.DataAnnotations;

namespace ApptechDashboard.Models;

public enum PhongBanPopupMode
{
    None = 0,
    Create = 1,
    Edit = 2
}

public sealed class PhongBanListQuery
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public bool ShowCreatePopup { get; set; }
    public int? EditId { get; set; }
}

public sealed class PhongBanFilterState
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class PhongBanListItem
{
    public int Id { get; set; }
    public string TenPhongBan { get; set; } = string.Empty;
    public string? TenVietTat { get; set; }
    public bool TrangThaiSuDung { get; set; }
    public string? NguoiTao { get; set; }
    public DateTime? NgayTao { get; set; }
    public string? NguoiCapNhat { get; set; }
    public DateTime? NgayCapNhat { get; set; }
}

public sealed class PhongBanImportRow
{
    public string TenPhongBan { get; set; } = string.Empty;
    public string? MaPhongBan { get; set; }
}

public sealed class PhongBanImportResult
{
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
}

public sealed class PhongBanFormModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên phòng ban.")]
    [StringLength(300, ErrorMessage = "Tên phòng ban tối đa 300 ký tự.")]
    public string TenPhongBan { get; set; } = string.Empty;

    [StringLength(40, ErrorMessage = "Tên viết tắt tối đa 40 ký tự.")]
    public string? TenVietTat { get; set; }

    public bool TrangThaiSuDung { get; set; } = true;

    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class PhongBanDeleteModel
{
    public int Id { get; set; }
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class PhongBanManagementViewModel
{
    public PhongBanFilterState Filter { get; set; } = new();
    public PhongBanFormModel Form { get; set; } = new();
    public IReadOnlyList<PhongBanListItem> Items { get; set; } = [];
    public PhongBanPopupMode PopupMode { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; } = 1;
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";

    public bool IsPopupOpen => PopupMode != PhongBanPopupMode.None;

    public string PopupTitle => PopupMode switch
    {
        PhongBanPopupMode.Create => "Thêm phòng ban",
        PhongBanPopupMode.Edit => "Cập nhật phòng ban",
        _ => "Phòng ban"
    };

    public string SubmitAction => PopupMode == PhongBanPopupMode.Edit ? "Update" : "Create";

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
