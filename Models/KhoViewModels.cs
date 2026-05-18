using System.ComponentModel.DataAnnotations;

namespace ApptechDashboard.Models;

public enum KhoPopupMode
{
    None = 0,
    Create = 1,
    Edit = 2
}

public sealed class KhoListQuery
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public bool ShowCreatePopup { get; set; }
    public int? EditId { get; set; }
}

public sealed class KhoFilterState
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class KhoListItem
{
    public int Id { get; set; }
    public string TenKho { get; set; } = string.Empty;
    public string? MaKho { get; set; }
    public bool TrangThaiSuDung { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedDate { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
}

public sealed class KhoFormModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên kho.")]
    [StringLength(300, ErrorMessage = "Tên kho tối đa 300 ký tự.")]
    public string TenKho { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Mã kho tối đa 100 ký tự.")]
    public string? MaKho { get; set; }

    public bool TrangThaiSuDung { get; set; } = true;
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class KhoDeleteModel
{
    public int Id { get; set; }
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class KhoImportRow
{
    public string TenKho { get; set; } = string.Empty;
    public string? MaKho { get; set; }
}

public sealed class KhoImportResult
{
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
}

public sealed class KhoManagementViewModel
{
    public KhoFilterState Filter { get; set; } = new();
    public KhoFormModel Form { get; set; } = new();
    public IReadOnlyList<KhoListItem> Items { get; set; } = [];
    public KhoPopupMode PopupMode { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; } = 1;
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";

    public bool IsPopupOpen => PopupMode != KhoPopupMode.None;

    public string PopupTitle => PopupMode switch
    {
        KhoPopupMode.Create => "Thêm kho",
        KhoPopupMode.Edit => "Cập nhật kho",
        _ => "Kho"
    };

    public string SubmitAction => PopupMode == KhoPopupMode.Edit ? "Update" : "Create";

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
