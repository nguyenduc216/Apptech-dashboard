using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ApptechDashboard.Configuration;

namespace ApptechDashboard.Services;

public interface IZaloTextApiClient
{
    Task<ZaloApiSendResponse> SendAsync(
        IReadOnlyDictionary<string, string?> recipient,
        string message,
        Func<CancellationToken, Task<string>> getAccessToken,
        Func<CancellationToken, Task> forceRefreshToken,
        ZaloSendContext context,
        CancellationToken cancellationToken = default);
}

public sealed class ZaloTextApiClient(
    IZaloSettingsService zaloSettings,
    IHttpClientFactory httpClientFactory,
    ILogger<ZaloTextApiClient> logger) : IZaloTextApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ZaloApiSendResponse> SendAsync(
        IReadOnlyDictionary<string, string?> recipient,
        string message,
        Func<CancellationToken, Task<string>> getAccessToken,
        Func<CancellationToken, Task> forceRefreshToken,
        ZaloSendContext context,
        CancellationToken cancellationToken = default)
    {
        var requestJson = JsonSerializer.Serialize(new
        {
            recipient,
            message = new { text = message }
        }, JsonOptions);

        var accessToken = await getAccessToken(cancellationToken);
        var result = await SendAttemptAsync(accessToken, requestJson, cancellationToken);
        if (!result.IsInvalidAccessToken)
        {
            return result;
        }

        logger.LogWarning(
            "Zalo access token was rejected; refreshing once before retry. RequestId: {RequestId}; CustomerId: {CustomerId}; MessageType: {MessageType}; ErrorCode: {ErrorCode}",
            context.RequestId,
            context.CustomerId,
            context.MessageType,
            result.ErrorCode);

        try
        {
            await forceRefreshToken(cancellationToken);
            accessToken = await getAccessToken(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Zalo access token refresh failed. RequestId: {RequestId}; CustomerId: {CustomerId}; MessageType: {MessageType}",
                context.RequestId,
                context.CustomerId,
                context.MessageType);
            return result with
            {
                ApiSucceeded = false,
                ErrorMessage = "Access token không hợp lệ và không thể refresh. Vui lòng kết nối lại Zalo OA."
            };
        }

        return await SendAttemptAsync(accessToken, requestJson, cancellationToken);
    }

    private async Task<ZaloApiSendResponse> SendAttemptAsync(
        string accessToken,
        string requestJson,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("ZaloOA");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildApiUri(zaloSettings.Current, zaloSettings.Current.TextMessageEndpoint));
        request.Headers.TryAddWithoutValidation("access_token", accessToken);
        request.Content = new StringContent(requestJson, Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseResponse(response.IsSuccessStatusCode, (int)response.StatusCode, responseJson);
    }

    internal static ZaloApiSendResponse ParseResponse(bool httpSucceeded, int statusCode, string responseJson)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var root = document.RootElement;
            var errorCode = ReadErrorCode(root);
            var apiMessage = ReadString(root, "message");
            var messageId = root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                ? ReadString(data, "message_id")
                : null;
            var apiSucceeded = httpSucceeded && errorCode == 0;

            return new ZaloApiSendResponse(
                httpSucceeded,
                apiSucceeded,
                errorCode,
                apiSucceeded ? null : BuildErrorMessage(statusCode, errorCode, apiMessage, responseJson),
                responseJson,
                messageId);
        }
        catch (JsonException)
        {
            return new ZaloApiSendResponse(
                httpSucceeded,
                false,
                null,
                $"Zalo API trả về JSON không hợp lệ (HTTP {statusCode}).",
                responseJson,
                null);
        }
    }

    private static int? ReadErrorCode(JsonElement root)
    {
        if (!root.TryGetProperty("error", out var error))
        {
            return null;
        }

        return error.ValueKind switch
        {
            JsonValueKind.Number when error.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(error.GetString(), out var value) => value,
            _ => null
        };
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string BuildErrorMessage(int statusCode, int? errorCode, string? message, string responseJson)
    {
        if (statusCode is < 200 or >= 300)
        {
            return $"Zalo API lỗi HTTP {statusCode}: {message ?? TrimTo(responseJson, 500) ?? "Không có nội dung phản hồi."}";
        }

        if (errorCode.HasValue)
        {
            return $"Zalo API lỗi {errorCode.Value}: {message ?? "Không có thông báo lỗi."}";
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            return $"Zalo API lỗi HTTP {statusCode}: {message}";
        }

        return $"Zalo API lỗi HTTP {statusCode}: {TrimTo(responseJson, 500) ?? "Không có nội dung phản hồi."}";
    }

    private static Uri BuildApiUri(ZaloOptions options, string path)
    {
        var baseUrl = options.ApiBaseUrl.TrimEnd('/');
        var normalizedPath = path.StartsWith('/') ? path : $"/{path}";
        return new Uri(baseUrl + normalizedPath, UriKind.Absolute);
    }

    private static string? TrimTo(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maxLength ? value : value[..maxLength];
}

public sealed record ZaloSendContext(int? RequestId, int? CustomerId, string MessageType);

public sealed record ZaloApiSendResponse(
    bool HttpSucceeded,
    bool ApiSucceeded,
    int? ErrorCode,
    string? ErrorMessage,
    string ResponseJson,
    string? MessageId)
{
    public bool IsInvalidAccessToken =>
        ErrorCode == -216 ||
        (!string.IsNullOrWhiteSpace(ErrorMessage) &&
         ErrorMessage.Contains("access token", StringComparison.OrdinalIgnoreCase) &&
         (ErrorMessage.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
          ErrorMessage.Contains("expired", StringComparison.OrdinalIgnoreCase)));
}
