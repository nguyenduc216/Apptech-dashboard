using System.ComponentModel.DataAnnotations;

namespace ApptechDashboard.Models;

// FEATURE_ID: APPTECH-REPORT-NHAP-XUAT-TON
// CHANGE_ID: APPTECH-20261007-NHAP-XUAT-TON-002 - Contract filter cố định cho báo cáo Hàng hóa × Kho.
public sealed class NhapXuatTonReportQuery
{
    [DataType(DataType.Date)]
    public DateTime? FromDate { get; set; }

    [DataType(DataType.Date)]
    public DateTime? ToDate { get; set; }

    public string? HangHoa { get; set; }

    public int? KhoId { get; set; }

}

public sealed class NhapXuatTonReportFilterState
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string? HangHoa { get; set; }
    public int? KhoId { get; set; }
}

public sealed class NhapXuatTonReportItem
{
    public int? KhoId { get; set; }
    public string? TenHangHoa { get; set; }
    public string? TenKho { get; set; }
    public decimal TonDau { get; set; }
    public decimal NhapTrongKy { get; set; }
    public decimal XuatTrongKy { get; set; }
    public decimal TonCuoi => TonDau + NhapTrongKy - XuatTrongKy;
}

public sealed class NhapXuatTonReportViewModel
{
    public NhapXuatTonReportFilterState Filter { get; set; } = new();
    public IReadOnlyList<NhapXuatTonReportItem> Items { get; set; } = [];
    public IReadOnlyList<KhoLookupOption> KhoOptions { get; set; } = [];
    public string? StatusMessage { get; set; }
    public string StatusType { get; set; } = "info";

    public string FromDateValue => Filter.FromDate.ToString("yyyy-MM-dd");
    public string ToDateValue => Filter.ToDate.ToString("yyyy-MM-dd");
}
