using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ApptechDashboard.Models;

public enum CongViecPopupMode
{
    None = 0,
    Create = 1,
    Edit = 2
}

public sealed class CongViecListQuery
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public bool ShowCreatePopup { get; set; }
    public int? EditId { get; set; }
}

public sealed class CongViecFilterState
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class CongViecListItem
{
    public int Id { get; set; }
    public string TenCongViec { get; set; } = string.Empty;
    public string? MieuTa { get; set; }
    public bool TrangThaiSuDung { get; set; }
    public decimal? DonGia { get; set; }
    public int? SoLuongAnhCheckIn { get; set; }
    public int? SoLuongAnhCheckOut { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedDate { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public IReadOnlyList<CongViecChecklistFormItem> DanhSachChecklist { get; set; } = [];
}

public sealed class CongViecImportRow
{
    public string TenCongViec { get; set; } = string.Empty;
    public string? MieuTa { get; set; }
    public decimal? DonGia { get; set; }
    public int? SoLuongAnhCheckIn { get; set; }
    public int? SoLuongAnhCheckOut { get; set; }
}

public sealed class CongViecImportResult
{
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
}

public sealed class CongViecChecklistFormItem
{
    public string TenChecklist { get; set; } = string.Empty;
    public int ViTri { get; set; }
    public bool TrangThaiSuDung { get; set; } = true;
}

public sealed class CongViecFormModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên công việc.")]
    [StringLength(250, ErrorMessage = "Tên công việc tối đa 250 ký tự.")]
    public string TenCongViec { get; set; } = string.Empty;

    [StringLength(250, ErrorMessage = "Mô tả tối đa 250 ký tự.")]
    public string? MieuTa { get; set; }

    [Range(typeof(decimal), "0", "999999999999999999", ErrorMessage = "Đơn giá phải lớn hơn hoặc bằng 0.")]
    public decimal? DonGia { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Số lượng ảnh check-in phải lớn hơn hoặc bằng 0.")]
    public int? SoLuongAnhCheckIn { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Số lượng ảnh check-out phải lớn hơn hoặc bằng 0.")]
    public int? SoLuongAnhCheckOut { get; set; }

    [ValidateNever]
    public List<CongViecChecklistFormItem> DanhSachChecklist { get; set; } = [];

    public bool TrangThaiSuDung { get; set; } = true;
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public string ActiveTab { get; set; } = "cong-viec";
}

public sealed class CongViecDeleteModel
{
    public int Id { get; set; }
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class CongViecManagementViewModel
{
    public CongViecFilterState Filter { get; set; } = new();
    public CongViecFormModel Form { get; set; } = new();
    public IReadOnlyList<CongViecListItem> Items { get; set; } = [];
    public CongViecPopupMode PopupMode { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; } = 1;
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";
    public bool ShouldClearChecklistDraftCache { get; set; }

    public bool IsPopupOpen => PopupMode != CongViecPopupMode.None;

    public string PopupTitle => PopupMode switch
    {
        CongViecPopupMode.Create => "Thêm công việc",
        CongViecPopupMode.Edit => "Cập nhật công việc",
        _ => "Công việc"
    };

    public string SubmitAction => PopupMode == CongViecPopupMode.Edit ? "Update" : "Create";

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
