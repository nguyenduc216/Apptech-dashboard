using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ApptechDashboard.Models;

public enum VatTuPopupMode
{
    None = 0,
    Create = 1,
    Edit = 2
}

public sealed class VatTuListQuery
{
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public bool ShowCreatePopup { get; set; }
    public int? EditId { get; set; }
}

public sealed class VatTuFilterState
{
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class VatTuLookupOption
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class VatTuListItem
{
    public int Id { get; set; }
    public bool TrangThaiSuDung { get; set; }
    public int? KhoId { get; set; }
    public string? TenKho { get; set; }
    public string? MaKho { get; set; }
    public int? HangHoaId { get; set; }
    public string? TenHangHoa { get; set; }
    public string? MaHangHoa { get; set; }
    public int? PhanLoaiHangHoaId { get; set; }
    public string? TenPhanLoaiHangHoa { get; set; }
    public int? DonViTinhId { get; set; }
    public string? TenDonViTinh { get; set; }
    public string? TenVietTatDonViTinh { get; set; }
    public int? DonViNhapId { get; set; }
    public string? TenDonViNhap { get; set; }
    public string? TenVietTatDonViNhap { get; set; }
    public string TenChiTiet { get; set; } = string.Empty;
    public decimal SoLuongTon { get; set; }
    public decimal DonGiaBanLe { get; set; }
    public string? MaSoLo { get; set; }
    public string? ViTriLuuKho { get; set; }
    public string? GhiChu { get; set; }
    public string? QRCode { get; set; }
    public string? ImageUrl { get; set; }
    public IReadOnlyList<VatTuImageItem> Images { get; set; } = [];
    public int? PhieuNhapChiTietId { get; set; }
    public int? PhieuNhapId { get; set; }
    public string? MaPhieuNhap { get; set; }
    public int? PhieuXuatId { get; set; }
    public string? MaPhieuXuat { get; set; }
    public DateTime? CreatedDate { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class VatTuImageItem
{
    public int? Id { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
}

public sealed class VatTuFormModel
{
    public int? Id { get; set; }
    public bool TrangThaiSuDung { get; set; } = true;

    [Required(ErrorMessage = "Vui lòng chọn kho.")]
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn kho.")]
    public int? KhoId { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn hàng hóa.")]
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn hàng hóa.")]
    public int? HangHoaId { get; set; }

    public int? PhanLoaiHangHoaId { get; set; }
    public string? TenPhanLoaiHangHoa { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn đơn vị tính.")]
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn đơn vị tính.")]
    public int? DonViTinhId { get; set; }
    public int? DonViNhapId { get; set; }
    public string? TenDonViNhap { get; set; }
    public string? TenVietTatDonViNhap { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên chi tiết.")]
    [StringLength(250, ErrorMessage = "Tên chi tiết tối đa 250 ký tự.")]
    public string TenChiTiet { get; set; } = string.Empty;

    [Range(typeof(decimal), "0.01", "9999999999999999.99", ErrorMessage = "Số lượng tồn phải lớn hơn 0.")]
    public decimal SoLuongTon { get; set; } = 1;
    public decimal DonGiaBanLe { get; set; }

    [StringLength(50, ErrorMessage = "Mã số lô tối đa 50 ký tự.")]
    public string? MaSoLo { get; set; }

    [StringLength(250, ErrorMessage = "Vị trí lưu kho tối đa 250 ký tự.")]
    public string? ViTriLuuKho { get; set; }

    [StringLength(550, ErrorMessage = "Ghi chú tối đa 550 ký tự.")]
    public string? GhiChu { get; set; }

    public string? QRCode { get; set; }
    public string? ImageUrl { get; set; }
    public int? PhieuNhapChiTietId { get; set; }
    public int? PhieuNhapId { get; set; }
    public string? MaPhieuNhap { get; set; }
    public int? PhieuXuatId { get; set; }
    public string? MaPhieuXuat { get; set; }

    [ValidateNever]
    public List<VatTuImageItem> ExistingImages { get; set; } = [];

    [ValidateNever]
    public List<string> RemovedImageUrls { get; set; } = [];

    [ValidateNever]
    public List<IFormFile> NewImageFiles { get; set; } = [];

    [ValidateNever]
    public List<string> UploadedImageUrls { get; set; } = [];

    public string? PrimaryImageSelection { get; set; }

    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
    public string ActiveTab { get; set; } = "thong-tin";
}

public sealed class VatTuDeleteModel
{
    public int Id { get; set; }
    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class VatTuCopyModel
{
    public string? SelectedIds { get; set; }

    [Range(1, 1000, ErrorMessage = "Số lượng copy phải lớn hơn 0.")]
    public int CopyQuantity { get; set; } = 1;

    public string? Keyword { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class VatTuManagementViewModel
{
    public VatTuFilterState Filter { get; set; } = new();
    public VatTuFormModel Form { get; set; } = new();
    public IReadOnlyList<VatTuListItem> Items { get; set; } = [];
    public IReadOnlyList<VatTuLookupOption> KhoOptions { get; set; } = [];
    public IReadOnlyList<VatTuLookupOption> HangHoaOptions { get; set; } = [];
    public IReadOnlyList<VatTuLookupOption> DonViTinhOptions { get; set; } = [];
    public VatTuPopupMode PopupMode { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; } = 1;
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";

    public bool IsPopupOpen => PopupMode != VatTuPopupMode.None;

    public string PopupTitle => PopupMode switch
    {
        VatTuPopupMode.Create => "Thêm vật tư",
        VatTuPopupMode.Edit => "Cập nhật vật tư",
        _ => "Vật tư"
    };

    public string SubmitAction => PopupMode == VatTuPopupMode.Edit ? "Update" : "Create";

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
