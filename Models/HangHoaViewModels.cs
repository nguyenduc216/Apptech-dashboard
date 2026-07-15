using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ApptechDashboard.Models;

public enum HangHoaPopupMode
{
    None = 0,
    Create = 1,
    Edit = 2
}

public sealed class HangHoaListQuery
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public bool ShowCreatePopup { get; set; }
    public int? EditId { get; set; }
}

public sealed class HangHoaFilterState
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class HangHoaListItem
{
    public int Id { get; set; }
    public string TenHangHoa { get; set; } = string.Empty;
    public string? MaHangHoa { get; set; }
    public string LoaiHinhNhap { get; set; } = NhapKhoLoaiHinh.NhapTheoLo;
    public int? DonViTinhId { get; set; }
    public string? TenDonViTinh { get; set; }
    public string? TenVietTatDonViTinh { get; set; }
    public string? ImageUrl { get; set; }
    public bool TrangThaiSuDung { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? CreatedDate { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
}

public sealed class HangHoaFormModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên hàng hóa.")]
    [StringLength(250, ErrorMessage = "Tên hàng hóa tối đa 250 ký tự.")]
    public string TenHangHoa { get; set; } = string.Empty;

    [StringLength(50, ErrorMessage = "Mã hàng hóa tối đa 50 ký tự.")]
    public string? MaHangHoa { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn loại hình nhập.")]
    public string LoaiHinhNhap { get; set; } = NhapKhoLoaiHinh.NhapTheoLo;

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn đơn vị tính.")]
    public int? DonViTinhId { get; set; }

    public string? ImageUrl { get; set; }
    public IFormFile? ImageFile { get; set; }

    public bool TrangThaiSuDung { get; set; } = true;
    public string ActiveTab { get; set; } = "thong-tin";
    public List<HangHoaPhanLoaiModel> PhanLoai { get; set; } = [];
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class HangHoaPhanLoaiModel
{
    public int? Id { get; set; }

    [StringLength(250, ErrorMessage = "Tên phân loại tối đa 250 ký tự.")]
    public string? TenPhanLoai { get; set; }

    public bool TrangThaiSuDung { get; set; } = true;
}

public sealed class HangHoaDeleteModel
{
    public int Id { get; set; }
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class HangHoaImportRow
{
    public string TenHangHoa { get; set; } = string.Empty;
    public string? MaHangHoa { get; set; }
    public string? DonViTinh { get; set; }
    public string? TenKho { get; set; }
    public string? TenPhanLoai { get; set; }
    public string? TenChiTiet { get; set; }
    public decimal? TonKhoDauKy { get; set; }
    public string? SheetName { get; set; }
    public int RowNumber { get; set; }
    public IReadOnlyList<string> SourceHeaders { get; set; } = [];
    public IReadOnlyList<string?> SourceValues { get; set; } = [];
}

public sealed class HangHoaImportResult
{
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<string> FailedCodes { get; set; } = [];
    public List<HangHoaImportRowResult> Rows { get; set; } = [];
    public byte[]? ResultFileContent { get; set; }
    public string? ResultFileName { get; set; }
}

public sealed class HangHoaImportRowResult
{
    public HangHoaImportRow Row { get; set; } = new();
    public bool Succeeded { get; set; }
    public bool Skipped { get; set; }
    public string Result { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

public sealed class HangHoaLookupOption
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class HangHoaManagementViewModel
{
    public HangHoaFilterState Filter { get; set; } = new();
    public HangHoaFormModel Form { get; set; } = new();
    public IReadOnlyList<HangHoaListItem> Items { get; set; } = [];
    public IReadOnlyList<HangHoaLookupOption> DonViTinhOptions { get; set; } = [];
    public HangHoaPopupMode PopupMode { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; } = 1;
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";

    public bool IsPopupOpen => PopupMode != HangHoaPopupMode.None;

    public string PopupTitle => PopupMode switch
    {
        HangHoaPopupMode.Create => "Thêm hàng hóa",
        HangHoaPopupMode.Edit => "Cập nhật hàng hóa",
        _ => "Hàng hóa"
    };

    public string SubmitAction => PopupMode == HangHoaPopupMode.Edit ? "Update" : "Create";

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
