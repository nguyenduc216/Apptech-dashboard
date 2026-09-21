using System.ComponentModel.DataAnnotations;

namespace ApptechDashboard.Models;

public sealed class DanhMucChamCongNgoaiOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class DanhMucChamCongNgoaiItem
{
    public int Id { get; set; }
    public string TenNoiDung { get; set; } = string.Empty;
    public int ThuTuHienThi { get; set; }
    public bool IsActive { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class DanhMucChamCongNgoaiForm
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập nội dung chấm công ngoài.")]
    [StringLength(250, ErrorMessage = "Nội dung tối đa 250 ký tự.")]
    public string TenNoiDung { get; set; } = string.Empty;

    [Range(0, 9999, ErrorMessage = "Thứ tự hiển thị phải từ 0 đến 9999.")]
    public int ThuTuHienThi { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class DanhMucChamCongNgoaiPageModel
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public IReadOnlyList<DanhMucChamCongNgoaiItem> Items { get; set; } = [];
    public DanhMucChamCongNgoaiForm Form { get; set; } = new();
    public bool IsEditing => Form.Id is > 0;
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";
}
