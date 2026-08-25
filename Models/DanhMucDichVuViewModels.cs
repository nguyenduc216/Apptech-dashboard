using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ApptechDashboard.Models;

public enum DanhMucDichVuPopupMode
{
    None = 0,
    Create = 1,
    Edit = 2
}

public sealed class DanhMucDichVuListQuery
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public bool ShowCreatePopup { get; set; }
    public int? EditId { get; set; }
}

public sealed class DanhMucDichVuFilterState
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class DanhMucDichVuListItem
{
    public int Id { get; set; }
    public string TenDichVu { get; set; } = string.Empty;
    public string? MieuTa { get; set; }
    public bool TrangThaiSuDung { get; set; } = true;
    public int SoCongViec { get; set; }
    public DateTime? CreatedDate { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public string? UpdatedBy { get; set; }
    public IReadOnlyList<DanhMucDichVuWorkItem> CongViecs { get; set; } = [];
}

public sealed class DanhMucDichVuWorkItem
{
    public int IDCongViec { get; set; }
    public string TenCongViec { get; set; } = string.Empty;
    public string? MieuTa { get; set; }
    public decimal? DonGia { get; set; }
    public int SoLuongAnhCheckIn { get; set; }
    public int SoLuongAnhCheckOut { get; set; }
    public int ThuTu { get; set; }
    public bool TrangThaiSuDung { get; set; } = true;
    public IReadOnlyList<YeuCauCongViecChecklistFormItem> Checklists { get; set; } = [];
}

public sealed class DanhMucDichVuOption
{
    public int Id { get; set; }
    public string TenDichVu { get; set; } = string.Empty;
    public int SoCongViec { get; set; }
}

public sealed class DanhMucDichVuFormWorkItem
{
    public int IDCongViec { get; set; }
    public int ThuTu { get; set; }
}

public sealed class DanhMucDichVuFormModel : IValidatableObject
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên dịch vụ.")]
    [StringLength(250, ErrorMessage = "Tên dịch vụ tối đa 250 ký tự.")]
    public string TenDichVu { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Mô tả tối đa 1000 ký tự.")]
    public string? MieuTa { get; set; }

    public bool TrangThaiSuDung { get; set; } = true;

    [ValidateNever]
    public List<DanhMucDichVuFormWorkItem> CongViecs { get; set; } = [];

    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var normalizedIds = CongViecs
            .Where(item => item.IDCongViec > 0)
            .Select(item => item.IDCongViec)
            .ToList();

        if (normalizedIds.Count == 0)
        {
            yield return new ValidationResult(
                "Dịch vụ phải có tối thiểu 1 công việc.",
                [nameof(CongViecs)]);
        }

        if (normalizedIds.Count != normalizedIds.Distinct().Count())
        {
            yield return new ValidationResult(
                "Không được chọn trùng công việc trong cùng một dịch vụ.",
                [nameof(CongViecs)]);
        }
    }
}

public sealed class DanhMucDichVuDeleteModel
{
    public int Id { get; set; }
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class DanhMucDichVuManagementViewModel
{
    public DanhMucDichVuFilterState Filter { get; set; } = new();
    public DanhMucDichVuFormModel Form { get; set; } = new();
    public IReadOnlyList<DanhMucDichVuListItem> Items { get; set; } = [];
    public IReadOnlyList<DanhMucDichVuWorkItem> WorkOptions { get; set; } = [];
    public DanhMucDichVuPopupMode PopupMode { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; } = 1;
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";

    public bool IsPopupOpen => PopupMode != DanhMucDichVuPopupMode.None;
    public string PopupTitle => PopupMode == DanhMucDichVuPopupMode.Edit ? "Cập nhật dịch vụ" : "Thêm dịch vụ";
    public string SubmitAction => PopupMode == DanhMucDichVuPopupMode.Edit ? "Update" : "Create";

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
