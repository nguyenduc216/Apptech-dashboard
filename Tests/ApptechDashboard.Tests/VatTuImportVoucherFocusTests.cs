using ApptechDashboard.Models;
using Xunit;

namespace ApptechDashboard.Tests;

// FEATURE_ID: APPTECH-WAREHOUSE-MATERIAL
// CHANGE_ID: APPTECH-20261008-VAT-TU-PHIEU-NHAP-003
public sealed class VatTuImportVoucherFocusTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void MaterialImportLink_UsesExistingRouteDetailIdTabAndNewWindowContract()
    {
        var view = Read("Views", "VatTu", "Index.cshtml");

        Assert.Contains("asp-controller=\"NhapKho\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Index\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-route-editId=\"@item.PhieuNhapId.Value\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-route-highlightDetailId=\"@item.PhieuNhapChiTietId\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-route-activeTab=\"hang-hoa-nhap\"", view, StringComparison.Ordinal);
        Assert.Contains("target=\"_blank\"", view, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportQuery_BindsOptionalHighlightDetailIdAndActiveTab()
    {
        var query = new NhapKhoListQuery
        {
            HighlightDetailId = 123,
            ActiveTab = "hang-hoa-nhap"
        };

        Assert.Equal(123, query.HighlightDetailId);
        Assert.Equal("hang-hoa-nhap", query.ActiveTab);
    }

    [Fact]
    public void Controller_PreservesPositiveDetailIdAndActivatesExistingImportItemsTab()
    {
        var controller = Read("Controllers", "NhapKhoController.cs");

        Assert.Contains("model.HighlightDetailId = query.HighlightDetailId is > 0 ? query.HighlightDetailId : null;", controller, StringComparison.Ordinal);
        Assert.Contains("string.Equals(query.ActiveTab, \"hang-hoa-nhap\", StringComparison.OrdinalIgnoreCase)", controller, StringComparison.Ordinal);
        Assert.Contains("? \"hang-hoa-nhap\"", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailRow_UsesPersistentPnctIdMarkerAndHighlightsOnlyMatchingId()
    {
        var view = Read("Views", "NhapKho", "Index.cshtml");

        Assert.Contains("data-nhap-kho-detail-id=\"@detail.Id\"", view, StringComparison.Ordinal);
        Assert.Contains("detail.Id == Model.HighlightDetailId", view, StringComparison.Ordinal);
        Assert.Contains("is-highlighted-material", view, StringComparison.Ordinal);
        Assert.DoesNotContain("detail.TenHangHoa ==", view, StringComparison.Ordinal);
        Assert.DoesNotContain("detail.MaHangHoa ==", view, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoScroll_RunsOnlyForPositiveExistingDetailRow()
    {
        var view = Read("Views", "NhapKho", "Index.cshtml");

        Assert.Contains("if (!Number.isInteger(highlightDetailId) || highlightDetailId <= 0)", view, StringComparison.Ordinal);
        Assert.Contains("getRows().find((row) =>", view, StringComparison.Ordinal);
        Assert.Contains("if (!(highlightedRow instanceof HTMLTableRowElement))", view, StringComparison.Ordinal);
        Assert.Contains("highlightedRow.scrollIntoView({ behavior: \"smooth\", block: \"center\" });", view, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalImportOpen_KeepsExistingDefaultTabAndNoHighlight()
    {
        var query = new NhapKhoListQuery { EditId = 10 };
        var form = new NhapKhoFormModel();
        var model = new NhapKhoManagementViewModel();

        Assert.Null(query.HighlightDetailId);
        Assert.Null(query.ActiveTab);
        Assert.Equal("thong-tin", form.ActiveTab);
        Assert.Null(model.HighlightDetailId);
    }

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([RepositoryRoot, .. path]));
}
