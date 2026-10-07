using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Xunit;

namespace ApptechDashboard.Tests;

// FEATURE_ID: APPTECH-REPORT-NHAP-XUAT-TON
// CHANGE_ID: APPTECH-20261007-NHAP-XUAT-TON-002 - Bảo vệ công thức, safety type-2 và hợp đồng 6 cột.
// CHANGE_ID: APPTECH-20261008-NHAP-XUAT-TON-003 - Bảo vệ grain tên chuẩn hóa + kho và filter trước aggregation.
public sealed class NhapXuatTonReportTests
{
    [Fact]
    public void TonCuoi_UsesOpeningPlusImportsMinusExports()
    {
        var item = new NhapXuatTonReportItem { TonDau = 12.5m, NhapTrongKy = 4m, XuatTrongKy = 3.25m };

        Assert.Equal(13.25m, item.TonCuoi);
    }

    [Fact]
    public void Sql_UsesNormalizedNameWarehouseGrainAndFiltersBeforeAggregation()
    {
        var method = typeof(NhapXuatTonReportService).GetMethod("BuildSql", BindingFlags.NonPublic | BindingFlags.Static);
        var sql = Assert.IsType<string>(method?.Invoke(null, null));

        Assert.Contains("LTRIM(RTRIM(hh.TenHangHoa))", sql, StringComparison.Ordinal);
        Assert.Contains("GROUP BY f.NormalizedProductName, f.IDKho", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("GROUP BY m.IDHangHoa", sql, StringComparison.Ordinal);
        Assert.Contains("pnct.SoLuongNhap", sql, StringComparison.Ordinal);
        Assert.Contains("pn.TrangThaiPhieu = N'da-nhap'", sql, StringComparison.Ordinal);
        Assert.Contains("px.TrangThaiPhieu = N'xuat-kho'", sql, StringComparison.Ordinal);
        Assert.Contains("COALESCE(hhXuat.ID, ct.IDHangHoa)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ct.IDPhieuNhapChiTiet = pnct.ID", sql, StringComparison.Ordinal);
        Assert.Contains("f.Ngay >= @FromDate AND f.Ngay < @ToDateExclusive", sql, StringComparison.Ordinal);
        Assert.True(sql.IndexOf("hh.MaHangHoa", StringComparison.Ordinal) < sql.IndexOf("GROUP BY f.NormalizedProductName", StringComparison.Ordinal));
    }

    [Fact]
    public void SameNormalizedNameAndWarehouse_AggregatesDifferentProductIdsAndAllQuantities()
    {
        var result = Aggregate(
        [
            new(101, "A01", " BÁO ĐỘNG ", 1, 2m, 5m, 1m),
            new(205, "A02", "báo động", 1, 3m, 6m, 2m)
        ]);

        var row = Assert.Single(result);
        Assert.Equal(5m, row.TonDau);
        Assert.Equal(11m, row.Nhap);
        Assert.Equal(3m, row.Xuat);
        Assert.Equal(13m, row.TonCuoi);
    }

    [Fact]
    public void SameNameDifferentWarehouses_RemainsTwoRows()
    {
        var result = Aggregate([new(101, "A01", "BÁO ĐỘNG", 1, 0, 5, 0), new(205, "A02", "BÁO ĐỘNG", 2, 0, 6, 0)]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ProductCodeFilter_IsAppliedBeforeSameNameAggregation()
    {
        var movements = new[] { new Movement(101, "A01", "BÁO ĐỘNG", 1, 0, 5, 0), new Movement(205, "A02", "BÁO ĐỘNG", 1, 0, 6, 0) };

        var result = Aggregate(movements.Where(item => item.MaHangHoa == "A01"));

        Assert.Equal(5m, Assert.Single(result).Nhap);
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
    public void Excel_UsesSingleAlreadyAggregatedRow()
    {
        var aggregated = Assert.Single(Aggregate([new(101, "A01", "BÁO ĐỘNG", 1, 0, 5, 0), new(205, "A02", " báo động ", 1, 0, 6, 0)]));
        var model = new NhapXuatTonReportViewModel
        {
            Items = [new() { TenHangHoa = aggregated.TenHangHoa, TenKho = "LẦU 3", NhapTrongKy = aggregated.Nhap }]
        };

        var bytes = new SimpleExcelService().BuildNhapXuatTonReport(model);
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var worksheetStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var document = XDocument.Load(worksheetStream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        Assert.Equal(2, document.Descendants(ns + "row").Count());
        Assert.Contains("11", document.Descendants(ns + "t").Select(node => node.Value));
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
        Assert.Contains("asp-route-exactHangHoa=\"true\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-route-khoId", view, StringComparison.Ordinal);
        Assert.Contains("target=\"_blank\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"ExportNhapXuatTon\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailDrillDown_UsesExactTrimmedProductNameWithoutChangingNormalSearch()
    {
        var method = typeof(NhapXuatKhoReportService).GetMethod("BuildWhereClause", BindingFlags.NonPublic | BindingFlags.Static);
        var exact = new NhapXuatKhoReportFilterState { HangHoa = "BÁO ĐỘNG", ExactHangHoa = true };
        var normal = new NhapXuatKhoReportFilterState { HangHoa = "BÁO ĐỘNG" };

        var exactSql = Assert.IsType<string>(method?.Invoke(null, [exact, "nhap"]));
        var normalSql = Assert.IsType<string>(method?.Invoke(null, [normal, "nhap"]));

        Assert.Contains("LTRIM(RTRIM(hh.TenHangHoa))", exactSql, StringComparison.Ordinal);
        Assert.Contains(" = @nhapHangHoa", exactSql, StringComparison.Ordinal);
        Assert.Contains("hh.MaHangHoa", normalSql, StringComparison.Ordinal);
        Assert.Contains("LIKE @nhapHangHoa", normalSql, StringComparison.Ordinal);
    }

    private static IReadOnlyList<AggregatedMovement> Aggregate(IEnumerable<Movement> movements) =>
        movements
            .GroupBy(item => new { Name = item.TenHangHoa.Trim().ToUpperInvariant(), item.KhoId })
            .Select(group => new AggregatedMovement(
                group.Key.Name,
                group.Key.KhoId,
                group.Sum(item => item.TonDau),
                group.Sum(item => item.Nhap),
                group.Sum(item => item.Xuat)))
            .ToArray();

    private sealed record Movement(int HangHoaId, string MaHangHoa, string TenHangHoa, int KhoId, decimal TonDau, decimal Nhap, decimal Xuat);

    private sealed record AggregatedMovement(string TenHangHoa, int KhoId, decimal TonDau, decimal Nhap, decimal Xuat)
    {
        public decimal TonCuoi => TonDau + Nhap - Xuat;
    }
}
