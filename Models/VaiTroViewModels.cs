using System.ComponentModel.DataAnnotations;

namespace ApptechDashboard.Models;

public enum VaiTroPopupMode
{
    None = 0,
    Create = 1,
    Edit = 2,
    Permissions = 3
}

public sealed class VaiTroListQuery
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public bool ShowCreatePopup { get; set; }
    public int? EditId { get; set; }
    public int? PermissionRoleId { get; set; }
}

public sealed class VaiTroFilterState
{
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public sealed class VaiTroListItem
{
    public int Id { get; set; }
    public string TenVaiTro { get; set; } = string.Empty;
    public string? MieuTa { get; set; }
    public bool TrangThaiSuDung { get; set; } = true;
}

public sealed class VaiTroFormModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên vai trò.")]
    [StringLength(200, ErrorMessage = "Tên vai trò tối đa 200 ký tự.")]
    public string TenVaiTro { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Miêu tả tối đa 500 ký tự.")]
    public string? MieuTa { get; set; }

    public bool TrangThaiSuDung { get; set; } = true;
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class VaiTroDeleteModel
{
    public int Id { get; set; }
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class VaiTroPermissionSaveModel
{
    public int RoleId { get; set; }
    public List<int> SelectedPermissionIds { get; set; } = [];
    public string? Keyword { get; set; }
    public bool? StatusFilter { get; set; }
    public int Page { get; set; } = 1;
}

public sealed class VaiTroPermissionNode
{
    public int ChucNangId { get; set; }
    public string MaChucNang { get; set; } = string.Empty;
    public string? MaChucNangCha { get; set; }
    public string TenChucNang { get; set; } = string.Empty;
    public string? MieuTa { get; set; }
    public string? ThuTuHienThi { get; set; }
    public List<VaiTroPermissionItem> Permissions { get; set; } = [];
    public List<VaiTroPermissionNode> Children { get; set; } = [];
}

public sealed class VaiTroPermissionItem
{
    public int QuyenId { get; set; }
    public int ChucNangId { get; set; }
    public string TenQuyen { get; set; } = string.Empty;
    public string? MieuTa { get; set; }
    public bool IsSelected { get; set; }
}

public sealed class VaiTroPermissionMatrix
{
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public IReadOnlyList<VaiTroPermissionNode> Nodes { get; set; } = [];
}

public sealed class VaiTroManagementViewModel
{
    public VaiTroFilterState Filter { get; set; } = new();
    public VaiTroFormModel Form { get; set; } = new();
    public VaiTroPermissionMatrix? PermissionMatrix { get; set; }
    public IReadOnlyList<VaiTroListItem> Items { get; set; } = [];
    public VaiTroPopupMode PopupMode { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public int CurrentPage { get; set; } = 1;
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";
    public bool IsPopupOpen => PopupMode != VaiTroPopupMode.None;
    public string PopupTitle => PopupMode switch
    {
        VaiTroPopupMode.Create => "Thêm vai trò",
        VaiTroPopupMode.Edit => "Cập nhật vai trò",
        VaiTroPopupMode.Permissions => "Phân quyền vai trò",
        _ => "Vai trò"
    };
    public string SubmitAction => PopupMode == VaiTroPopupMode.Edit ? "Update" : "Create";

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
