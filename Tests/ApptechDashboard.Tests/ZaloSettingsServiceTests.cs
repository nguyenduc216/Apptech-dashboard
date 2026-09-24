using ApptechDashboard.Services;
using Xunit;

namespace ApptechDashboard.Tests;

public sealed class ZaloSettingsServiceTests
{
    [Fact]
    public void AutomaticCustomerNotifications_DefaultsToFalse()
    {
        Assert.False(new ApptechDashboard.Configuration.ZaloOptions().EnableAutomaticCustomerNotifications);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Merge_PreservesStoredAutomaticNotificationSetting(bool enabled)
    {
        var database = new ApptechDashboard.Configuration.ZaloOptions
        {
            EnableAutomaticCustomerNotifications = enabled
        };

        var result = ZaloSettingsService.Merge(
            database,
            new ApptechDashboard.Configuration.ZaloOptions(),
            new ApptechDashboard.Configuration.ZaloOptions());

        Assert.Equal(enabled, result.EnableAutomaticCustomerNotifications);
    }

    [Fact]
    public void SettingsSql_LoadsAndSavesAutomaticNotificationSetting()
    {
        var source = File.ReadAllText(FindRepositoryFile("Services", "ZaloSettingsService.cs"));

        Assert.Contains("EnableAutomaticCustomerNotifications = @EnableAutomaticCustomerNotifications", source);
        Assert.Contains("reader[\"EnableAutomaticCustomerNotifications\"]", source);
        Assert.Contains("new SqlParameter(\"@EnableAutomaticCustomerNotifications\"", source);
    }

    [Fact]
    public void ProductionPlaceholder_DoesNotOverrideEarlierConfiguredAppSecret()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        ZaloSettingsService.ApplyConfiguredProviderValue(values, "AppSecret", "configured-secret");
        ZaloSettingsService.ApplyConfiguredProviderValue(values, "AppSecret", "YOUR_APP_SECRET");

        Assert.Equal("configured-secret", values["AppSecret"]);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException();
    }
}
