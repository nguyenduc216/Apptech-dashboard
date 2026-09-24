using Xunit;

namespace ApptechDashboard.Tests;

public sealed class ZaloAutomaticNotificationScopeTests
{
    [Fact]
    public void ServiceGuard_AppliesOnlyToTwoAutomaticRequestNotifications()
    {
        var source = File.ReadAllText(FindRepositoryFile("Services", "ZaloIntegrationService.cs"));

        Assert.Equal(2, Count(source, "if (!_zaloOptions.EnableAutomaticCustomerNotifications)"));
        Assert.Contains("SendRequestCreatedNotificationAsync", source);
        Assert.Contains("SendRequestProgressNotificationAsync", source);
    }

    [Fact]
    public void WebhookMappingAndPublicRequestController_DoNotUseAutomaticNotificationToggle()
    {
        var webhookSource = File.ReadAllText(FindRepositoryFile("Services", "ZaloIntegrationService.cs"));
        var requestControllerSource = File.ReadAllText(FindRepositoryFile("Controllers", "ZaloRequestController.cs"));

        var webhookStart = webhookSource.IndexOf("public async Task<ZaloWebhookProcessResult> ProcessAsync", StringComparison.Ordinal);
        Assert.True(webhookStart >= 0);
        Assert.DoesNotContain("EnableAutomaticCustomerNotifications", webhookSource[webhookStart..]);
        Assert.DoesNotContain("EnableAutomaticCustomerNotifications", requestControllerSource);
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

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
