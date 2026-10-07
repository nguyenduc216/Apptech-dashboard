using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Xunit;

namespace ApptechDashboard.Tests;

// FEATURE_ID: APPTECH-REPORT-NHAP-XUAT-TON
// CHANGE_ID: APPTECH-20261007-NHAP-XUAT-TON-002
// Bảo vệ công thức, grain SQL, safety type-2 và hợp đồng Excel sáu cột.
public sealed class NhapXuatTonReportTests
{
    [Fact]
    public void TonCuoi_UsesOpeningPlusImportsMinusExports()
    {
        var item = new NhapXuatTonReportItem { TonDau = 12.5m, NhapTrongKy = 4m, XuatTrongKy = 3.25m };

        Assert.Equal(13.25m, item.TonCuoi);
    }

    [Fact]
    public void Sql_UsesFixedProductWarehouseGrainAndDoesNotJoinImportDetailToMaterials()
    {
        var method = typeof(NhapXuatTonReportService).GetMethod("BuildSql", BindingFlags.NonPublic | BindingFlags.Static);
        var sql = Assert.IsType<string>(method?.Invoke(null, null));

        Assert.Contains("GROUP BY m.IDHangHoa, m.IDKho", sql, StringComparison.Ordinal);
        Assert.Contains("pnct.SoLuongNhap", sql, StringComparison.Ordinal);
        Assert.Contains("pn.TrangThaiPhieu = N'da-nhap'", sql, StringComparison.Ordinal);
        Assert.Contains("px.TrangThaiPhieu = N'xuat-kho'", sql, StringComparison.Ordinal);
        Assert.Contains("COALESCE(hhXuat.ID, ct.IDHangHoa)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ct.IDPhieuNhapChiTiet = pnct.ID", sql, StringComparison.Ordinal);
        Assert.Contains("m.Ngay >= @FromDate AND m.Ngay < @ToDateExclusive", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Excel_HasExactlySixExpectedColumnsAndValues()
    {
        var model = new NhapXuatTonReportViewModel
        {
            Items =
            [
                new NhapXuatTonReportItem
                {
                    TenHangHoa = "Thiết bị A", TenKho = "Kho 1", TonDau = 10m,
                    NhapTrongKy = 5m, XuatTrongKy = 2m
                }
            ]
        };

        var bytes = new SimpleExcelService().BuildNhapXuatTonReport(model);
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var worksheetStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var document = XDocument.Load(worksheetStream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = document.Descendants(ns + "row").ToArray();
        var headers = rows[0].Descendants(ns + "t").Select(node => node.Value).ToArray();
        var values = rows[1].Descendants(ns + "t").Select(node => node.Value).ToArray();

        Assert.Equal(new[] { "Tên hàng hóa", "Kho", "Tồn đầu", "Nhập", "Xuất", "Tồn cuối" }, headers);
        Assert.Equal(new[] { "Thiết bị A", "Kho 1", "10", "5", "2", "13" }, values);
    }

    [Fact]
    public void View_HasSixColumnsAndDrillDownCarriesAllFiltersInNewTab()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var view = File.ReadAllText(Path.Combine(repositoryRoot, "Views", "Report", "NhapXuatTon.cshtml"));

        Assert.Equal(6, Regex.Matches(view, "<th(?:\\s|>)", RegexOptions.IgnoreCase).Count);
        Assert.DoesNotContain("groupBy", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("asp-route-fromDate", view, StringComparison.Ordinal);
        Assert.Contains("asp-route-toDate", view, StringComparison.Ordinal);
        Assert.Contains("asp-route-hangHoa", view, StringComparison.Ordinal);
        Assert.Contains("asp-route-khoId", view, StringComparison.Ordinal);
        Assert.Contains("target=\"_blank\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"ExportNhapXuatTon\"", view, StringComparison.Ordinal);
    }
}
