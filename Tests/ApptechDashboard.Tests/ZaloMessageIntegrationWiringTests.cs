using Xunit;

namespace ApptechDashboard.Tests;

public sealed class ZaloMessageIntegrationWiringTests
{
    [Fact]
    public void BothBusinessMessagePathsUseSharedApiClient()
    {
        var source = ReadIntegrationService();

        Assert.Equal(2, Count(source, "zaloTextApiClient.SendAsync("));
    }

    [Fact]
    public void BothBusinessMessagePathsPersistParsedApiOutcome()
    {
        var source = ReadIntegrationService();

        Assert.True(Count(source, "result.ApiSucceeded,") >= 2);
        Assert.True(Count(source, "result.ErrorMessage,") >= 2);
    }

    [Fact]
    public void MessageTransportOwnsAccessTokenHeaderWhileProfileLookupRemainsSeparate()
    {
        var integrationSource = ReadIntegrationService();
        var transportSource = File.ReadAllText(FindRepositoryFile("Services", "ZaloTextApiClient.cs"));

        Assert.Equal(1, Count(integrationSource, "new AuthenticationHeaderValue(\"Bearer\", accessToken)"));
        Assert.Contains("TryAddWithoutValidation(\"access_token\", accessToken)", transportSource);
        Assert.Contains("TryAddWithoutValidation(\"appsecret_proof\", appSecretProof)", transportSource);
        Assert.DoesNotContain("AuthenticationHeaderValue(\"Bearer\"", transportSource);
    }

    private static string ReadIntegrationService() =>
        File.ReadAllText(FindRepositoryFile("Services", "ZaloIntegrationService.cs"));

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

        throw new FileNotFoundException($"Could not find repository file: {Path.Combine(segments)}");
    }
}
