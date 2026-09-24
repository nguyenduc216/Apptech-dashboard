using ApptechDashboard.Services;
using Xunit;

namespace ApptechDashboard.Tests;

public sealed class ZaloSettingsServiceTests
{
    [Fact]
    public void ProductionPlaceholder_DoesNotOverrideEarlierConfiguredAppSecret()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        ZaloSettingsService.ApplyConfiguredProviderValue(values, "AppSecret", "configured-secret");
        ZaloSettingsService.ApplyConfiguredProviderValue(values, "AppSecret", "YOUR_APP_SECRET");

        Assert.Equal("configured-secret", values["AppSecret"]);
    }
}
