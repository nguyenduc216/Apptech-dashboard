using System.Text.RegularExpressions;
using Xunit;

namespace ApptechDashboard.Tests;

// FEATURE_ID: APPTECH-WAREHOUSE-MATERIAL
// CHANGE_ID: APPTECH-20261008-VAT-TU-PHIEU-XUAT-002
public sealed class VatTuExportVoucherColumnVisibilityTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void View_ShowsExportVoucherColumnAndKeepsAllElevenColumnsAligned()
    {
        var view = File.ReadAllText(Path.Combine(RepositoryRoot, "Views", "VatTu", "Index.cshtml"));

        Assert.Contains("var tableColumnCount = 11;", view, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(@"\.vat-tu-table\s+(?:th|td):nth-child\(10\)[^{]*\{[^}]*display\s*:\s*none", RegexOptions.IgnoreCase),
            view);

        var headers = new[]
        {
            "selection-col",
            "Ảnh",
            "QR",
            "Tên chi tiết",
            "Hàng hóa",
            "Tồn kho",
            "Kho",
            "Vị trí lưu kho",
            "Phiếu nhập",
            "Phiếu xuất",
            "actions-col"
        };

        var previousIndex = -1;
        foreach (var header in headers)
        {
            var currentIndex = view.IndexOf(header, previousIndex + 1, StringComparison.Ordinal);
            Assert.True(currentIndex > previousIndex, $"Expected table header marker '{header}' in order.");
            previousIndex = currentIndex;
        }

        Assert.Contains("colspan=\"@tableColumnCount\"", view, StringComparison.Ordinal);
        Assert.Contains("target=\"_blank\"", view, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener\"", view, StringComparison.Ordinal);
    }
}
