using System.ComponentModel.DataAnnotations;
using System.Reflection;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Xunit;

namespace ApptechDashboard.Tests;

public sealed class VatTuStockValidationTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void Form_AllowsZeroStockButRejectsNegativeStock()
    {
        Assert.DoesNotContain(ValidateStock(0), result => result.MemberNames.Contains(nameof(VatTuFormModel.SoLuongTon)));
        Assert.Contains(ValidateStock(-0.01m), result => result.MemberNames.Contains(nameof(VatTuFormModel.SoLuongTon)));
    }

    [Fact]
    public void View_AllowsZeroStockInBrowserValidation()
    {
        var view = File.ReadAllText(Path.Combine(RepositoryRoot, "Views", "VatTu", "Index.cshtml"));

        Assert.Contains("asp-for=\"Form.SoLuongTon\" type=\"number\" step=\"0.01\" min=\"0\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=\"Form.SoLuongTon\" type=\"number\" step=\"0.01\" min=\"0.01\"", view, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(VatTuStockFilters.InStock, "ISNULL(ct.SoLuongTon, 0) > 0")]
    [InlineData(VatTuStockFilters.OutOfStock, "ISNULL(ct.SoLuongTon, 0) <= 0")]
    [InlineData(VatTuStockFilters.All, "1 = 1")]
    public void StockFilter_BuildsExpectedSql(string filter, string expectedSql)
    {
        var sql = BuildWhere(VatTuUsageStatusFilters.All, filter);

        Assert.Contains(expectedSql, sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(VatTuUsageStatusFilters.Active, "ISNULL(ct.TrangThaiSuDung, 0) = 1")]
    [InlineData(VatTuUsageStatusFilters.Inactive, "ISNULL(ct.TrangThaiSuDung, 0) = 0")]
    public void UsageStatusFilter_BuildsExpectedSql(string filter, string expectedSql)
    {
        var sql = BuildWhere(filter, VatTuStockFilters.All);

        Assert.Contains(expectedSql, sql, StringComparison.Ordinal);
    }

    [Fact]
    public void View_ProvidesBothFiltersAndPreservesThemAcrossNavigationAndExport()
    {
        var view = File.ReadAllText(Path.Combine(RepositoryRoot, "Views", "VatTu", "Index.cshtml"));

        Assert.Contains("name=\"statusFilter\"", view, StringComparison.Ordinal);
        Assert.Contains(">Đang sử dụng</option>", view, StringComparison.Ordinal);
        Assert.Contains(">Ngưng sử dụng</option>", view, StringComparison.Ordinal);
        Assert.Contains("name=\"stockFilter\"", view, StringComparison.Ordinal);
        Assert.Contains(">Còn vật tư</option>", view, StringComparison.Ordinal);
        Assert.Contains(">Hết vật tư</option>", view, StringComparison.Ordinal);
        Assert.Contains("asp-route-statusFilter=\"@Model.Filter.StatusFilter\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-route-stockFilter=\"@Model.Filter.StockFilter\"", view, StringComparison.Ordinal);
        Assert.Contains("<input asp-for=\"Form.StatusFilter\" type=\"hidden\" />", view, StringComparison.Ordinal);
        Assert.Contains("<input asp-for=\"Form.StockFilter\" type=\"hidden\" />", view, StringComparison.Ordinal);
    }

    private static IReadOnlyList<ValidationResult> ValidateStock(decimal stock)
    {
        var property = typeof(VatTuFormModel).GetProperty(nameof(VatTuFormModel.SoLuongTon))!;
        var context = new ValidationContext(new VatTuFormModel())
        {
            MemberName = nameof(VatTuFormModel.SoLuongTon)
        };
        var results = new List<ValidationResult>();

        Validator.TryValidateProperty(stock, context, results);
        return results;
    }

    private static string BuildWhere(string statusFilter, string stockFilter)
    {
        var method = typeof(VatTuService).GetMethod("BuildWhereClause", BindingFlags.NonPublic | BindingFlags.Static);
        return Assert.IsType<string>(method?.Invoke(null, [Array.Empty<string>(), statusFilter, stockFilter, true]));
    }
}
