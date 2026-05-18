using System.ComponentModel.DataAnnotations;

namespace ApptechDashboard.Models;

public sealed class QrCodeBatchRequestModel
{
    [Range(1, 999, ErrorMessage = "Số lượng QR phải từ 1 đến 999.")]
    public int Quantity { get; set; } = 24;

    [Range(1, 20, ErrorMessage = "Số QR mỗi hàng phải từ 1 đến 20.")]
    public int QrPerRow { get; set; } = 4;

    [Range(10, 135, ErrorMessage = "Chiều ngang QR phải từ 10 đến 135 mm.")]
    public int QrWidth { get; set; } = 35;

    [Range(10, 135, ErrorMessage = "Chiều dọc QR phải từ 10 đến 135 mm.")]
    public int QrHeight { get; set; } = 35;

    [Range(0, 32, ErrorMessage = "Khoảng cách trái phải từ 0 đến 32 mm.")]
    public int MarginLeft { get; set; } = 2;

    [Range(0, 32, ErrorMessage = "Khoảng cách phải phải từ 0 đến 32 mm.")]
    public int MarginRight { get; set; } = 2;

    [Range(0, 32, ErrorMessage = "Khoảng cách trên phải từ 0 đến 32 mm.")]
    public int MarginTop { get; set; } = 2;

    [Range(0, 32, ErrorMessage = "Khoảng cách dưới phải từ 0 đến 32 mm.")]
    public int MarginBottom { get; set; } = 2;

    public bool ShowDashedLines { get; set; }

    [Range(1, 50, ErrorMessage = "Số trang In180 phải từ 1 đến 50.")]
    public int Print180Pages { get; set; } = 1;
}

public sealed class QrCodePrintItem
{
    public int Index { get; set; }
    public string Value { get; set; } = string.Empty;
    public string SvgMarkup { get; set; } = string.Empty;
}

public sealed class QrCodeBatchGenerationResult
{
    public IReadOnlyList<QrCodePrintItem> Items { get; init; } = [];
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public long FirstSequence { get; init; }
    public long LastSequence { get; init; }
}

public sealed class QrCodeBatchPageViewModel
{
    public QrCodeBatchRequestModel Request { get; set; } = new();
    public IReadOnlyList<QrCodePrintItem> Items { get; set; } = [];
    public DateTimeOffset? GeneratedAtUtc { get; set; }
    public long? FirstSequence { get; set; }
    public long? LastSequence { get; set; }
    public QrCodeAssignmentViewModel Assignment { get; set; } = new();

    public bool HasGeneratedItems => Items.Count > 0;
}

public sealed class QrCodePdfExportModel
{
    public QrCodeBatchRequestModel Request { get; set; } = new();
    public List<string> Values { get; set; } = [];
}

public static class QrCodeAssignmentQrStatus
{
    public const string All = "all";
    public const string HasQr = "has";
    public const string MissingQr = "missing";
}

public sealed class QrCodeAssignmentViewModel
{
    public IReadOnlyList<VatTuLookupOption> KhoOptions { get; set; } = [];
    public IReadOnlyList<VatTuLookupOption> HangHoaOptions { get; set; } = [];
}

public sealed class QrCodeAssignmentSearchModel
{
    public int? HangHoaId { get; set; }

    [StringLength(250, ErrorMessage = "Tên chi tiết tối đa 250 ký tự.")]
    public string? TenChiTiet { get; set; }

    public int? KhoId { get; set; }

    [StringLength(250, ErrorMessage = "Vị trí kho tối đa 250 ký tự.")]
    public string? ViTriLuuKho { get; set; }

    [StringLength(50, ErrorMessage = "Mã số lô tối đa 50 ký tự.")]
    public string? MaSoLo { get; set; }

    [StringLength(50, ErrorMessage = "Mã phiếu nhập tối đa 50 ký tự.")]
    public string? MaPhieuNhap { get; set; }

    public string QrStatus { get; set; } = QrCodeAssignmentQrStatus.MissingQr;
}

public sealed class QrCodeAssignmentTargetItem
{
    public int Id { get; set; }
    public string? TenHangHoa { get; set; }
    public string TenChiTiet { get; set; } = string.Empty;
    public string? TenKho { get; set; }
    public string? ViTriLuuKho { get; set; }
    public string? MaSoLo { get; set; }
    public string? MaPhieuNhap { get; set; }
    public string? QRCode { get; set; }
}

public sealed class QrCodeAssignmentApplyModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Không xác định được vật tư cần gán QR.")]
    public int ItemId { get; set; }

    public string? QRCode { get; set; }
}
