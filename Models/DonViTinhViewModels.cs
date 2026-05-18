using System.ComponentModel.DataAnnotations;

namespace ApptechDashboard.Models;

public enum DonViTinhPopupMode
{
    None = 0,
    Create = 1,
    Edit = 2
}

public sealed class DonViTinhListQuery
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public bool ShowCreatePopup { get; set; }
    public int? EditId { get; set; }
}

public sealed class DonViTinhFilterState
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class DonViTinhListItem
{
    public int Id { get; set; }
    public string TenDonVi { get; set; } = string.Empty;
    public string? TenVietTat { get; set; }
    public bool TrangThaiSuDung { get; set; }
    public string? NguoiTao { get; set; }
    public DateTime? NgayTao { get; set; }
    public string? NguoiCapNhap { get; set; }
    public DateTime? NgayCapNhat { get; set; }
}

public sealed class DonViTinhImportRow
{
    public string TenDonVi { get; set; } = string.Empty;
    public string? MaDonVi { get; set; }
}

public sealed class DonViTinhImportResult
{
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
}

public sealed class DonViTinhFormModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên đơn vị.")]
    [StringLength(300, ErrorMessage = "Tên đơn vị tối đa 300 ký tự.")]
    public string TenDonVi { get; set; } = string.Empty;

    [StringLength(40, ErrorMessage = "Tên viết tắt tối đa 40 ký tự.")]
    public string? TenVietTat { get; set; }

    public bool TrangThaiSuDung { get; set; } = true;

    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class DonViTinhDeleteModel
{
    public int Id { get; set; }
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class DonViTinhManagementViewModel
{
    public DonViTinhFilterState Filter { get; set; } = new();
    public DonViTinhFormModel Form { get; set; } = new();
    public IReadOnlyList<DonViTinhListItem> Items { get; set; } = [];
    public DonViTinhPopupMode PopupMode { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; } = 1;
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";

    public bool IsPopupOpen => PopupMode != DonViTinhPopupMode.None;

    public string PopupTitle => PopupMode switch
    {
        DonViTinhPopupMode.Create => "Thêm đơn vị tính",
        DonViTinhPopupMode.Edit => "Cập nhật đơn vị tính",
        _ => "Đơn vị tính"
    };

    public string SubmitAction => PopupMode == DonViTinhPopupMode.Edit ? "Update" : "Create";

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
