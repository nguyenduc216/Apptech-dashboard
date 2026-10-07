using System.Reflection;
using ApptechDashboard.Services;
using Xunit;

namespace ApptechDashboard.Tests;

// FEATURE_ID: APPTECH-REPORT-NHAP-XUAT-TON
// CHANGE_ID: APPTECH-20261008-NHAP-XUAT-TON-004
// Bảo vệ dependency, permission, menu/navigation và migration tách biệt của hai report.
public sealed class NhapXuatTonReportIndependenceTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void ReportServices_DependOnNeutralWarehouseOwner_NotEachOther()
    {
        var inventoryParameters = GetPrimaryConstructorParameterTypes(typeof(NhapXuatTonReportService));
        var detailParameters = GetPrimaryConstructorParameterTypes(typeof(NhapXuatKhoReportService));

        Assert.Contains(typeof(IKhoService), inventoryParameters);
        Assert.Contains(typeof(IKhoService), detailParameters);
        Assert.DoesNotContain(typeof(INhapXuatKhoReportService), inventoryParameters);
        Assert.DoesNotContain(typeof(INhapXuatTonReportService), detailParameters);
    }

    [Fact]
    public void Controller_WebAndExcelActions_UseIndependentPermissions()
    {
        var source = Read("Controllers", "ReportController.cs");
        var detailWeb = MethodBlock(source, "public async Task<IActionResult> NhapXuatKho");
        var detailExcel = MethodBlock(source, "public async Task<IActionResult> ExportNhapXuatKho");
        var inventoryWeb = MethodBlock(source, "public async Task<IActionResult> NhapXuatTon");
        var inventoryExcel = MethodBlock(source, "public async Task<IActionResult> ExportNhapXuatTon");

        Assert.Contains("CanViewNhapXuatKhoReportAsync", detailWeb);
        Assert.Contains("CanViewNhapXuatKhoReportAsync", detailExcel);
        Assert.Contains("CanViewNhapXuatTonReportAsync", inventoryWeb);
        Assert.Contains("CanViewNhapXuatTonReportAsync", inventoryExcel);
        Assert.Equal("Report_NhapXuatKho_View", PermissionCatalogService.WarehouseInOutReportViewPermissionCode);
        Assert.Equal("Report_NhapXuatTon_View", PermissionCatalogService.InventoryBalanceReportViewPermissionCode);
    }

    [Fact]
    public void InventoryView_HidesDetailNavigationWithoutDetailPermission()
    {
        var view = Read("Views", "Report", "NhapXuatTon.cshtml");

        Assert.Contains("CanViewNhapXuatKhoReport", view, StringComparison.Ordinal);
        Assert.Contains("@if (canViewDetailReport)", view, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"NhapXuatKho\"", view, StringComparison.Ordinal);
        Assert.Contains("else", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_IsIdempotent_CreatesSeparateMenuPermission_AndDoesNotGrantRoles()
    {
        var migration = Read("App_Data", "Migrations", "20261008_add_nhap_xuat_ton_report_permission.sql");

        Assert.Contains("IF NOT EXISTS", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Report_NhapXuatTon", migration, StringComparison.Ordinal);
        Assert.Contains("/bao-cao/nhap-xuat-ton", migration, StringComparison.Ordinal);
        Assert.Contains("Report_NhapXuatTon_View", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO dbo.TblVaiTroVaQuyen", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerificationSql_IsReadOnlyAndChecksBothPermissions()
    {
        var verification = Read("sql", "20261008_verify_nhap_xuat_ton_report_permission.sql");

        Assert.Contains("Report_NhapXuatTon_View", verification, StringComparison.Ordinal);
        Assert.Contains("Report_NhapXuatKho_View", verification, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT ", verification, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE ", verification, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE ", verification, StringComparison.OrdinalIgnoreCase);
    }

    private static Type[] GetPrimaryConstructorParameterTypes(Type type) =>
        type.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray();

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([RepositoryRoot, .. parts]));

    private static string MethodBlock(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method: {signature}");
        var nextMethod = source.IndexOf("public async Task<IActionResult>", start + signature.Length, StringComparison.Ordinal);
        return nextMethod < 0 ? source[start..] : source[start..nextMethod];
    }
}
