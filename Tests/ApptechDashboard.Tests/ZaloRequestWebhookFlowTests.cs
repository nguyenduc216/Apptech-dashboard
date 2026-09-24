using System.Reflection;
using System.Text.Json;
using ApptechDashboard.Services;
using Xunit;

namespace ApptechDashboard.Tests;

public sealed class ZaloRequestWebhookFlowTests
{
    private const string VerificationToken = "-KLhCpZGsUlIyoMF1fT_NdBk3vrUDwYG45-2AYdUF2g";

    [Fact]
    public void MessageText_ExactRequestToken_IsPreservedForExactLookup()
    {
        Assert.Contains(VerificationToken, ExtractCandidates(VerificationToken));
        Assert.Contains("Token = @Candidate OR UserExternalId = @Candidate", RequestServiceSource());
        Assert.Contains("RequestId, CustomerId, Token, UserExternalId, Status, ExpiresAtUtc", RequestServiceSource());
    }

    [Fact]
    public void VerificationToken_LeadingDash_IsNotRemoved()
    {
        Assert.Contains(VerificationToken, ExtractCandidates($"Mã xác nhận: {VerificationToken}"));
    }

    [Fact]
    public void VerificationToken_UnderscoreAndDash_AreNotSplit()
    {
        const string token = "Abc_123-def_GHI-456";
        Assert.Contains(token, ExtractCandidates($"Gửi mã {token} vào OA"));
    }

    [Fact]
    public void RequestMapping_UpdatesCustomerZaloId()
    {
        var source = RequestServiceSource();
        Assert.Contains("UPDATE [TblKhachHang]", source);
        Assert.Contains("SET ZaloID = @ZaloUserId", source);
        Assert.Contains("WHERE ID = @CustomerId", source);
    }

    [Fact]
    public void RequestMapping_UpsertsCustomerProfile()
    {
        var source = RequestServiceSource();
        Assert.Contains("MERGE [{ProfileTable}] AS target", source);
        Assert.Contains("CustomerId = @CustomerId, RequestId = @RequestId", source);
        Assert.Contains("ConnectedAtUtc, LastInteractionAtUtc, CreatedAtUtc, UpdatedAtUtc", source);
    }

    [Fact]
    public void CreatedOrOpenedRequest_BecomesZaloConnected()
    {
        Assert.Contains(
            "Status = CASE WHEN Status = N'Rated' THEN Status ELSE N'ZaloConnected' END",
            RequestServiceSource());
    }

    [Fact]
    public void RatedRequest_IsNotDowngraded()
    {
        var source = RequestServiceSource();
        Assert.Contains("WHEN Status = N'Rated' THEN Status", source);
    }

    [Fact]
    public void RepeatedToken_UsesUniqueProfileMergeInsteadOfInsertOnly()
    {
        var source = RequestServiceSource();
        Assert.Contains("ON target.ZaloUserId = source.ZaloUserId AND target.OaId = source.OaId", source);
        Assert.Contains("WHEN MATCHED THEN UPDATE", source);
        Assert.Contains("WHEN NOT MATCHED THEN INSERT", source);
    }

    [Fact]
    public void RequestLookup_PrecedesLegacyCustomerInteractionFallback()
    {
        var source = IntegrationSource();
        var requestLookup = source.IndexOf("ResolveWebhookContextAsync", StringComparison.Ordinal);
        var legacyLookup = source.IndexOf("ResolveLegacyExternalIdFromMessageAsync", StringComparison.Ordinal);
        Assert.True(requestLookup >= 0 && legacyLookup > requestLookup);
        Assert.Contains("TblCustomerInteractionLinks", source);
    }

    [Fact]
    public void MultipleRecentLinks_AreNotGuessedAndFollowNeverUsesRecentFallback()
    {
        var source = IntegrationSource();
        var recentStart = source.IndexOf("private async Task<string?> ResolveRecentExternalIdAsync", StringComparison.Ordinal);
        var profileStart = source.IndexOf("private async Task<ZaloWebhookProfile>", recentStart, StringComparison.Ordinal);
        var recentMethod = source[recentStart..profileStart];
        Assert.Contains("SELECT TOP (2)", recentMethod);
        Assert.Contains("uniqueCandidates.Length > 1", recentMethod);
        Assert.Contains("skipping automatic customer mapping", recentMethod);
        Assert.DoesNotContain("TblZaloRequestLinks", recentMethod);
        Assert.Contains("!isFollowEvent", source);
    }

    [Fact]
    public void ProfileApiFailure_DoesNotPreventZaloIdMapping()
    {
        var source = IntegrationSource();
        Assert.Contains("Could not load Zalo user profile", source);
        Assert.Contains("return ZaloWebhookProfile.Empty", source);
        Assert.True(
            source.IndexOf("var profile = await TryLoadZaloUserProfileAsync", StringComparison.Ordinal) <
            source.IndexOf("var mapResult = await zaloRequestService.MapWebhookUserAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void AcknowledgementFailure_DoesNotRollbackCustomerMapping()
    {
        var source = IntegrationSource();
        var mapCall = source.IndexOf("var mapResult = await zaloRequestService.MapWebhookUserAsync", StringComparison.Ordinal);
        var acknowledgement = source.IndexOf("await SendDirectZaloMessageAsync", mapCall, StringComparison.Ordinal);
        Assert.True(mapCall >= 0 && acknowledgement > mapCall);
        Assert.Contains("Failed to send Zalo direct message", source);
    }

    [Fact]
    public void StatusApi_ReturnsConnectedAndProfileAfterMapping()
    {
        var serviceSource = RequestServiceSource();
        var controllerSource = File.ReadAllText(FindRepositoryFile("Controllers", "ZaloRequestController.cs"));
        Assert.Contains("status is \"ZaloConnected\" or \"Rated\"", serviceSource);
        Assert.Contains("zaloConnected = status.ZaloConnected", controllerSource);
        Assert.Contains("zaloUserId = status.ZaloUserId", controllerSource);
    }

    [Fact]
    public void WebhookParser_ReadsSenderIdAndSupportedMessageFields()
    {
        using var document = JsonDocument.Parse("""
            {"event_name":"user_send_text","sender":{"id":"zalo-123"},"message":{"content":"code_123-ABC"}}
            """);
        Assert.Equal("zalo-123", InvokeJsonParser("FindZaloUserIdFromWebhook", document.RootElement));
        Assert.Equal("code_123-ABC", InvokeJsonParser("FindIncomingMessageText", document.RootElement));
    }

    private static IReadOnlyList<string> ExtractCandidates(string message)
    {
        var method = typeof(ZaloIntegrationService).GetMethod(
            "ExtractMessageCandidates",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ExtractMessageCandidates was not found.");
        return Assert.IsAssignableFrom<IReadOnlyList<string>>(method.Invoke(null, [message]));
    }

    private static string? InvokeJsonParser(string methodName, JsonElement element)
    {
        var method = typeof(ZaloIntegrationService).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{methodName} was not found.");
        return method.Invoke(null, [element]) as string;
    }

    private static string RequestServiceSource() =>
        File.ReadAllText(FindRepositoryFile("Services", "ZaloRequestService.cs"));

    private static string IntegrationSource() =>
        File.ReadAllText(FindRepositoryFile("Services", "ZaloIntegrationService.cs"));

    private static string FindRepositoryFile(params string[] relativePathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativePathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find repository file.", Path.Combine(relativePathParts));
    }
}
