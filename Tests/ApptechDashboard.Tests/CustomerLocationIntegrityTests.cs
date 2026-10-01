using Xunit;

namespace ApptechDashboard.Tests;

public sealed class CustomerLocationIntegrityTests
{
    [Fact]
    public void CustomerSave_UpdatesExistingLocationsInsteadOfDeletingAllRows()
    {
        var source = ReadRepositoryFile("Services", "KhachHangService.cs");
        var syncStart = source.IndexOf("private static async Task<string?> SyncDiaDiemLamViecAsync", StringComparison.Ordinal);
        var syncEnd = source.IndexOf("private static IReadOnlyList<KhachHangDiaDiemFormItem> NormalizeDiaDiemLamViec", syncStart, StringComparison.Ordinal);
        var syncSource = source[syncStart..syncEnd];

        Assert.Contains("UPDATE [{LocationTableName}]", syncSource);
        Assert.Contains("WHERE ID = @Id AND IDKhachHang = @IDKhachHang", syncSource);
        Assert.DoesNotContain("DELETE FROM [{LocationTableName}]\n                WHERE IDKhachHang", syncSource);
    }

    [Fact]
    public void LocationDeletion_IsBlockedWhenUsedByRequestsOrCheckins()
    {
        var source = ReadRepositoryFile("Services", "KhachHangService.cs");

        Assert.Contains("LocationIsInUseAsync(connection, transaction, locationId", source);
        Assert.Contains("EXISTS (SELECT 1 FROM [TblYeuCau] WHERE IDDiaDiem = @Id)", source);
        Assert.Contains("EXISTS (SELECT 1 FROM [TblCheckinHistory] WHERE IDDiaDiem = @Id)", source);
    }

    [Fact]
    public void RequestDetail_FallsBackToCustomerNameWhenLocationIsMissing()
    {
        var modelSource = ReadRepositoryFile("Models", "YeuCauViewModels.cs");
        var controllerSource = ReadRepositoryFile("Controllers", "YeuCauController.cs");
        var viewSource = ReadRepositoryFile("Views", "YeuCau", "Detail.cshtml");

        Assert.Contains("public string? CustomerName { get; set; }", modelSource);
        Assert.Contains("CustomerName = customer?.TenKhachHang", controllerSource);
        Assert.Contains("!string.IsNullOrWhiteSpace(Model.CustomerName) ? Model.CustomerName", viewSource);
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {Path.Combine(parts)}");
    }
}
