using System.Net;
using System.Text;
using ApptechDashboard.Configuration;
using ApptechDashboard.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ApptechDashboard.Tests;

public sealed class ZaloTextApiClientTests
{
    [Fact]
    public void GenerateAppSecretProof_UsesOfficialHmacSha256LowercaseHexAlgorithm()
    {
        var proof = ZaloTextApiClient.GenerateAppSecretProof("access-token", "app-secret");

        Assert.Equal("dbf9c72b4c8f56924f8e07138f6d465c69cb5c9dbca908ce1403e993e1a5f799", proof);
        Assert.Equal(proof, ZaloTextApiClient.GenerateAppSecretProof("access-token", "app-secret"));
        Assert.NotEqual(proof, ZaloTextApiClient.GenerateAppSecretProof("new-token", "app-secret"));
        Assert.NotEqual(proof, ZaloTextApiClient.GenerateAppSecretProof("access-token", "new-secret"));
    }

    [Fact]
    public async Task SendAsync_Http200AndErrorZero_IsSuccessfulAndReadsMessageId()
    {
        var fixture = new ClientFixture(Json(HttpStatusCode.OK, """{"error":0,"message":"Success","data":{"message_id":"mid-1"}}"""));

        var result = await fixture.SendAsync();

        Assert.True(result.HttpSucceeded);
        Assert.True(result.ApiSucceeded);
        Assert.Null(result.ErrorMessage);
        Assert.Equal("mid-1", result.MessageId);
    }

    [Fact]
    public async Task SendAsync_Http200AndBusinessError_IsFailure()
    {
        var fixture = new ClientFixture(Json(HttpStatusCode.OK, """{"error":-201,"message":"Permission denied"}"""));

        var result = await fixture.SendAsync();

        Assert.True(result.HttpSucceeded);
        Assert.False(result.ApiSucceeded);
        Assert.Equal(-201, result.ErrorCode);
        Assert.Contains("Permission denied", result.ErrorMessage);
        Assert.Equal(0, fixture.RefreshCount);
    }

    [Fact]
    public async Task SendAsync_InvalidToken_RefreshesAndRetriesWithNewToken()
    {
        var fixture = new ClientFixture(
            Json(HttpStatusCode.OK, """{"error":-216,"message":"Access token is invalid"}"""),
            Json(HttpStatusCode.OK, """{"error":0,"message":"Success"}"""));

        var result = await fixture.SendAsync();

        Assert.True(result.ApiSucceeded);
        Assert.Equal(1, fixture.RefreshCount);
        Assert.Equal(2, fixture.Handler.Requests.Count);
        Assert.Equal("old-token", fixture.Handler.Requests[0].AccessToken);
        Assert.Equal("new-token", fixture.Handler.Requests[1].AccessToken);
        Assert.Equal(ZaloTextApiClient.GenerateAppSecretProof("old-token", fixture.AppSecret), fixture.Handler.Requests[0].AppSecretProof);
        Assert.Equal(ZaloTextApiClient.GenerateAppSecretProof("new-token", fixture.AppSecret), fixture.Handler.Requests[1].AppSecretProof);
        Assert.NotEqual(fixture.Handler.Requests[0].AppSecretProof, fixture.Handler.Requests[1].AppSecretProof);
    }

    [Fact]
    public async Task SendAsync_InvalidTokenTwice_RetriesOnlyOnceAndReturnsFailure()
    {
        var fixture = new ClientFixture(
            Json(HttpStatusCode.OK, """{"error":-216,"message":"Access token is invalid"}"""),
            Json(HttpStatusCode.OK, """{"error":-216,"message":"Access token is invalid again"}"""),
            Json(HttpStatusCode.OK, """{"error":0,"message":"must not be used"}"""));

        var result = await fixture.SendAsync();

        Assert.False(result.ApiSucceeded);
        Assert.Equal(-216, result.ErrorCode);
        Assert.Equal(1, fixture.RefreshCount);
        Assert.Equal(2, fixture.Handler.Requests.Count);
    }

    [Fact]
    public async Task SendAsync_RefreshThrows_ReturnsReconnectFailureWithoutRetry()
    {
        var fixture = new ClientFixture(Json(HttpStatusCode.OK, """{"error":-216,"message":"Access token is invalid"}"""))
        {
            RefreshException = new InvalidOperationException("refresh token invalid")
        };

        var result = await fixture.SendAsync();

        Assert.False(result.ApiSucceeded);
        Assert.Contains("kết nối lại Zalo OA", result.ErrorMessage);
        Assert.Equal(1, fixture.RefreshCount);
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task SendAsync_Http500_IsFailureEvenWhenBodySaysErrorZero()
    {
        var fixture = new ClientFixture(Json(HttpStatusCode.InternalServerError, """{"error":0,"message":"Success"}"""));

        var result = await fixture.SendAsync();

        Assert.False(result.HttpSucceeded);
        Assert.False(result.ApiSucceeded);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task SendAsync_InvalidJson_IsFailure()
    {
        var fixture = new ClientFixture(Json(HttpStatusCode.OK, "not-json"));

        var result = await fixture.SendAsync();

        Assert.False(result.ApiSucceeded);
        Assert.Contains("JSON không hợp lệ", result.ErrorMessage);
    }

    [Fact]
    public async Task SendAsync_UsesAccessTokenHeaderAndNeverBearerAuthorization()
    {
        var fixture = new ClientFixture(Json(HttpStatusCode.OK, """{"error":0,"message":"Success"}"""));

        await fixture.SendAsync();

        var request = Assert.Single(fixture.Handler.Requests);
        Assert.Equal("old-token", request.AccessToken);
        Assert.Equal(ZaloTextApiClient.GenerateAppSecretProof("old-token", fixture.AppSecret), request.AppSecretProof);
        Assert.Null(request.AuthorizationScheme);
        Assert.Null(request.AuthorizationParameter);
    }

    [Fact]
    public async Task SendAsync_InvalidAppSecretProof_IsFinalFailureWithoutTokenRefresh()
    {
        var fixture = new ClientFixture(Json(HttpStatusCode.OK, """{"error":-242,"message":"Invalid appsecret_proof provided in the API argument"}"""));

        var result = await fixture.SendAsync();

        Assert.False(result.ApiSucceeded);
        Assert.Equal(-242, result.ErrorCode);
        Assert.Equal(0, fixture.RefreshCount);
        Assert.Single(fixture.Handler.Requests);
        Assert.Contains(fixture.Logger.Messages, message => message.Contains("ProofGenerated: True", StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Logger.Messages, message =>
            message.Contains("old-token", StringComparison.Ordinal) ||
            message.Contains(fixture.AppSecret, StringComparison.Ordinal) ||
            message.Contains(fixture.Handler.Requests[0].AppSecretProof!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendAsync_ErrorCodeAsString_IsParsed()
    {
        var fixture = new ClientFixture(Json(HttpStatusCode.OK, """{"error":"0","message":"Success"}"""));

        var result = await fixture.SendAsync();

        Assert.True(result.ApiSucceeded);
    }

    [Fact]
    public async Task SendAsync_MissingErrorField_IsFailure()
    {
        var fixture = new ClientFixture(Json(HttpStatusCode.OK, """{"message":"Success"}"""));

        var result = await fixture.SendAsync();

        Assert.False(result.ApiSucceeded);
        Assert.NotNull(result.ErrorMessage);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class ClientFixture
    {
        private string _token = "old-token";

        public ClientFixture(params HttpResponseMessage[] responses)
        {
            Handler = new SequenceHandler(responses);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(value => value.CreateClient("ZaloOA")).Returns(new HttpClient(Handler));
            var settings = new Mock<IZaloSettingsService>();
            settings.SetupGet(value => value.Current).Returns(new ZaloOptions
            {
                AppSecret = AppSecret,
                ApiBaseUrl = "https://openapi.zalo.test",
                TextMessageEndpoint = "/v3.0/oa/message/cs"
            });
            Logger = new CapturingLogger<ZaloTextApiClient>();
            Client = new ZaloTextApiClient(settings.Object, factory.Object, Logger);
        }

        public ZaloTextApiClient Client { get; }
        public SequenceHandler Handler { get; }
        public string AppSecret { get; } = "test-app-secret";
        public CapturingLogger<ZaloTextApiClient> Logger { get; }
        public int RefreshCount { get; private set; }
        public Exception? RefreshException { get; set; }

        public Task<ZaloApiSendResponse> SendAsync() => Client.SendAsync(
            new Dictionary<string, string?> { ["user_id"] = "user-1" },
            "Hello",
            _ => Task.FromResult(_token),
            _ =>
            {
                RefreshCount++;
                if (RefreshException is not null)
                {
                    throw RefreshException;
                }

                _token = "new-token";
                return Task.CompletedTask;
            },
            new ZaloSendContext(10, 20, "RequestCreatedNotification"));
    }

    public sealed class SequenceHandler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Headers.TryGetValues("access_token", out var values) ? values.SingleOrDefault() : null,
                request.Headers.TryGetValues("appsecret_proof", out var proofValues) ? proofValues.SingleOrDefault() : null,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return _responses.Dequeue();
        }
    }

    public sealed record CapturedRequest(
        string? AccessToken,
        string? AppSecretProof,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? Body);

    public sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
