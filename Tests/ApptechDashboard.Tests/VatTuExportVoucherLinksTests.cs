using System.Reflection;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Xunit;

namespace ApptechDashboard.Tests;

// FEATURE_ID: APPTECH-WAREHOUSE-MATERIAL
// CHANGE_ID: APPTECH-20261008-VAT-TU-PHIEU-XUAT-001
public sealed class VatTuExportVoucherLinksTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void BatchSql_IsParameterizedDistinctAndCompletedOnly()
    {
        var method = typeof(VatTuService).GetMethod("BuildExportLinksSql", BindingFlags.NonPublic | BindingFlags.Static);
        var sql = Assert.IsType<string>(method?.Invoke(null, [new[] { "@VatTuId0", "@VatTuId1" }]));

        Assert.Contains("SELECT DISTINCT", sql, StringComparison.Ordinal);
        Assert.Contains("pxct.IDChiTietHangHoa IN (@VatTuId0, @VatTuId1)", sql, StringComparison.Ordinal);
        Assert.Contains("px.TrangThaiPhieu = N'xuat-kho'", sql, StringComparison.Ordinal);
        Assert.Contains("px.ID AS PhieuXuatId", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY pxct.IDChiTietHangHoa, px.NgayXuatKho DESC, px.ID DESC", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("nhap", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ListModel_DefaultsToEmptyStructuredVoucherCollection()
    {
        var item = new VatTuListItem();

        Assert.Empty(item.PhieuXuatList);
        Assert.IsAssignableFrom<IReadOnlyList<VatTuExportLinkItem>>(item.PhieuXuatList);
    }

    [Fact]
    public void View_RendersEachVoucherAsIndependentExistingRouteLinkAndEmptyFallback()
    {
        var view = Read("Views", "VatTu", "Index.cshtml");

        Assert.Contains("for (var exportIndex = 0; exportIndex < item.PhieuXuatList.Count; exportIndex++)", view, StringComparison.Ordinal);
        Assert.Contains("var phieu = item.PhieuXuatList[exportIndex]", view, StringComparison.Ordinal);
        Assert.Contains("asp-controller=\"XuatKho\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Index\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-route-editId=\"@phieu.PhieuXuatId\"", view, StringComparison.Ordinal);
        Assert.Contains("target=\"_blank\"", view, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener\"", view, StringComparison.Ordinal);
        Assert.Contains("<text>, </text>", view, StringComparison.Ordinal);
        Assert.Contains("<span>-</span>", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Controller_UsesOneBatchMappingForGetAndPostbackWithoutPerItemDatabaseCalls()
    {
        var controller = Read("Controllers", "VatTuController.cs");

        Assert.Equal(2, Count(controller, "await PopulateExportLinksAsync(items, cancellationToken);"));
        Assert.Equal(1, Count(controller, "await _vatTuService.GetExportLinksByVatTuIdsAsync("));
        Assert.Contains("foreach (var item in items)", controller, StringComparison.Ordinal);
        var helperStart = controller.IndexOf("private async Task PopulateExportLinksAsync", StringComparison.Ordinal);
        var helperEnd = controller.IndexOf("private object BuildRouteValues", helperStart, StringComparison.Ordinal);
        var helper = controller[helperStart..helperEnd];
        Assert.DoesNotContain("GetExportHistoryAsync", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingPagingAndPopupExportHistoryContractsRemainPresent()
    {
        var service = Read("Services", "VatTuService.cs");
        var controller = Read("Controllers", "VatTuController.cs");

        Assert.Contains("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", service, StringComparison.Ordinal);
        Assert.Contains("SELECT COUNT(1)", service, StringComparison.Ordinal);
        Assert.Contains("GetExportHistoryAsync(", service, StringComparison.Ordinal);
        Assert.Contains("int vatTuId", service, StringComparison.Ordinal);
        Assert.Contains("_vatTuService.GetExportHistoryAsync(form.Id.Value", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexMigration_IsIdempotentIndexOnlyAndNeverMutatesData()
    {
        var migration = Read("App_Data", "Migrations", "20261008_add_vat_tu_export_lookup_index.sql");

        Assert.Contains("IF NOT EXISTS", migration, StringComparison.Ordinal);
        Assert.Contains("([IDChiTietHangHoa], [IDPhieuXuatKho])", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP INDEX", migration, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([RepositoryRoot, .. path]));

    private static int Count(string value, string fragment) =>
        (value.Length - value.Replace(fragment, string.Empty, StringComparison.Ordinal).Length) / fragment.Length;
}
