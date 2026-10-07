using System.Reflection;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Xunit;

namespace ApptechDashboard.Tests;

// FEATURE_ID: APPTECH-REPORT-NHAP-XUAT-TON
// CHANGE_ID: APPTECH-20261008-NHAP-XUAT-TON-005
// Bảo vệ ID vật tư trong query/mapper và navigation tới route VatTu.Index hiện hữu.
public sealed class NhapXuatKhoMaterialLinkTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Theory]
    [InlineData("BuildNhapSql")]
    [InlineData("BuildXuatSql")]
    public void ReportQueries_ReturnMaterialIdFromJoinedMaterial(string methodName)
    {
        var method = typeof(NhapXuatKhoReportService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        var sql = Assert.IsType<string>(method?.Invoke(null, ["1 = 1"]));

        Assert.Contains("ct.ID AS ChiTietHangHoaId", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultModel_HasNullableMaterialId_AndMapperReadsIt()
    {
        var property = typeof(NhapXuatKhoReportItem).GetProperty("ChiTietHangHoaId");
        var service = Read("Services", "NhapXuatKhoReportService.cs");

        Assert.NotNull(property);
        Assert.Equal(typeof(int?), property.PropertyType);
        Assert.Contains("ChiTietHangHoaId = GetNullableInt32(reader, \"ChiTietHangHoaId\")", service, StringComparison.Ordinal);
    }

    [Fact]
    public void View_LinksValidMaterialToExistingVatTuIndexInNewTab_AndFallsBackToText()
    {
        var view = Read("Views", "Report", "NhapXuatKho.cshtml");

        Assert.Contains("item.ChiTietHangHoaId is > 0", view, StringComparison.Ordinal);
        Assert.Contains("asp-controller=\"VatTu\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"Index\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-route-editId=\"@item.ChiTietHangHoaId.Value\"", view, StringComparison.Ordinal);
        Assert.Contains("target=\"_blank\"", view, StringComparison.Ordinal);
        Assert.Contains("rel=\"noopener\"", view, StringComparison.Ordinal);
        Assert.Contains("title=\"Xem chi tiết vật tư\"", view, StringComparison.Ordinal);
        Assert.Contains("else", view, StringComparison.Ordinal);
        Assert.Contains("<span>@(string.IsNullOrWhiteSpace(item.TenChiTiet)", view, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingMaterialPage_UsesVatTuIndexEditIdContractAndAuthorization()
    {
        var controller = Read("Controllers", "VatTuController.cs");
        var routes = Read("Program.cs");

        Assert.Contains("[Authorize]", controller, StringComparison.Ordinal);
        Assert.Contains("public async Task<IActionResult> Index([FromQuery] VatTuListQuery query)", controller, StringComparison.Ordinal);
        Assert.Contains("query.EditId", controller, StringComparison.Ordinal);
        Assert.Contains("pattern: \"vat-tu\"", routes, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([RepositoryRoot, .. parts]));
}
