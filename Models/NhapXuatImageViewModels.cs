using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ApptechDashboard.Models;

public static class NhapXuatImageLoaiPhieu
{
    public const string Nhap = "nhap";
    public const string Xuat = "xuat";
}

public sealed class NhapXuatImageItem
{
    public int Id { get; set; }
    public int PhieuId { get; set; }
    public string LoaiPhieu { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
}

public sealed class NhapXuatImageFormFields
{
    [ValidateNever]
    public List<NhapXuatImageItem> ExistingImages { get; set; } = [];

    [ValidateNever]
    public List<string> RemovedImagePaths { get; set; } = [];

    [ValidateNever]
    public List<IFormFile> NewImageFiles { get; set; } = [];

    [ValidateNever]
    public List<string> UploadedImagePaths { get; set; } = [];
}
