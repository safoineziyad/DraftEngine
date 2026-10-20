using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DraftEngine.AI.Exceptions;
using DraftEngine.AI.Services;
using DraftEngine.Core.Interfaces;
using DraftEngine.Core.Models;

namespace DraftEngine.Tests.AI;

/// <summary>
/// Unit tests for <see cref="OpenAiContractService"/>. A fake HttpMessageHandler returns canned
/// JSON so the real OpenAI API is never contacted.
/// </summary>
public class OpenAiContractServiceTests
{
    private const string ApiKey = "sk-test-fake";
    private const string InvalidKeyFriendlyMessage = "Your OpenAI API key is not valid.";
    private const string QuotaFriendlyMessage = "Your OpenAI account has no available credits.";
    private const string RateLimitFriendlyMessage = "Too many requests — please wait a moment and try again.";
    private const string NetworkFriendlyMessage = "Could not reach OpenAI. Check your internet connection.";
    private const string TimeoutFriendlyMessage = "The request took too long. Please try again.";

    // ---------------------------------------------------------------- successful path

    [Fact]
    public async Task GenerateClauseBodyAsync_ReturnsModelContent_OnSuccess()
    {
        var handler = RespondingWith(HttpStatusCode.OK, CompletionJson("Clause body text."));
        var (service, _) = CreateService(handler);

        var result = await service.GenerateClauseBodyAsync(
            new Clause { BodyTemplate = "Template" },
            new Dictionary<string, string> { ["PartyA"] = "Acme" });

        Assert.Equal("Clause body text.", result);
    }

    [Fact]
    public async Task Request_ContainsModelAndMessages_AndAuthorizationHeader()
    {
        var handler = RespondingWith(HttpStatusCode.OK, CompletionJson("OK"));
        var (service, client) = CreateService(handler);

        await service.GenerateClauseBodyAsync(new Clause { BodyTemplate = "Template" }, new Dictionary<string, string>());

        Assert.NotNull(handler.LastRequest);
        Assert.NotNull(handler.LastRequestBody);

        // HttpClient configuration (item 4 of the hardening spec).
        Assert.Equal(TimeSpan.FromSeconds(90), client.Timeout);
        Assert.Equal(new Uri("https://api.openai.com/v1/"), client.BaseAddress);
        Assert.Equal("api.openai.com", handler.LastRequest!.RequestUri!.Host);
        Assert.Contains("chat/completions", handler.LastRequest.RequestUri.AbsolutePath);

        // Authorization: Bearer <key>.
        var auth = handler.LastRequest.Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal(ApiKey, auth.Parameter);

        // Request JSON carries the model and the messages.
        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("gpt-4o-mini", document.RootElement.GetProperty("model").GetString());
        Assert.True(document.RootElement.GetProperty("messages").GetArrayLength() >= 2);
    }

    // ---------------------------------------------------------------- typed error mapping

    [Fact]
    public async Task Http401_ThrowsInvalidKeyException_WithFriendlyMessage_AndNoBodyLeak()
    {
        var body = $"{{\"error\":{{\"message\":\"Incorrect API key provided: {ApiKey}\",\"type\":\"invalid_request_error\",\"code\":\"invalid_api_key\"}}}}";
        var handler = RespondingWith(HttpStatusCode.Unauthorized, body);
        var (service, _) = CreateService(handler);

        var ex = await Assert.ThrowsAsync<OpenAiInvalidKeyException>(() =>
            service.GenerateClauseBodyAsync(new Clause { BodyTemplate = "T" }, new Dictionary<string, string>()));

        Assert.Equal(InvalidKeyFriendlyMessage, ex.FriendlyMessage);
        Assert.DoesNotContain("Incorrect API key", ex.FriendlyMessage);
        Assert.DoesNotContain("Incorrect API key", ex.Message);
        Assert.DoesNotContain(ApiKey, ex.Message);
        Assert.DoesNotContain(ApiKey, ex.FriendlyMessage);
    }

    [Fact]
    public async Task Http429_InsufficientQuota_ThrowsQuotaException_WithCreditsMessage()
    {
        var body = "{\"error\":{\"message\":\"You exceeded your current quota, please check your plan and billing details.\",\"type\":\"insufficient_quota\",\"code\":\"insufficient_quota\"}}";
        var handler = RespondingWith((HttpStatusCode)429, body);
        var (service, _) = CreateService(handler);

        var ex = await Assert.ThrowsAsync<OpenAiQuotaExceededException>(() =>
            service.GenerateClauseBodyAsync(new Clause { BodyTemplate = "T" }, new Dictionary<string, string>()));

        Assert.Equal(QuotaFriendlyMessage, ex.FriendlyMessage);
    }

    [Fact]
    public async Task Http429_RateLimit_ThrowsRateLimitException_WithFriendlyMessage()
    {
        var body = "{\"error\":{\"message\":\"Rate limit reached for gpt-4o-mini\",\"type\":\"requests\"}}";
        var handler = RespondingWith((HttpStatusCode)429, body);
        var (service, _) = CreateService(handler);

        var ex = await Assert.ThrowsAsync<OpenAiRateLimitException>(() =>
            service.GenerateClauseBodyAsync(new Clause { BodyTemplate = "T" }, new Dictionary<string, string>()));

        Assert.Equal(RateLimitFriendlyMessage, ex.FriendlyMessage);
    }

    [Fact]
    public async Task HttpRequestException_ThrowsNetworkException_WithConnectivityMessage()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("No such host is known."));
        var (service, _) = CreateService(handler);

        var ex = await Assert.ThrowsAsync<OpenAiNetworkException>(() =>
            service.GenerateClauseBodyAsync(new Clause { BodyTemplate = "T" }, new Dictionary<string, string>()));

        Assert.Equal(NetworkFriendlyMessage, ex.FriendlyMessage);
    }

    [Fact]
    public async Task SocketException_ThrowsNetworkException_WithConnectivityMessage()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new SocketException(11001));
        var (service, _) = CreateService(handler);

        var ex = await Assert.ThrowsAsync<OpenAiNetworkException>(() =>
            service.GenerateClauseBodyAsync(new Clause { BodyTemplate = "T" }, new Dictionary<string, string>()));

        Assert.Equal(NetworkFriendlyMessage, ex.FriendlyMessage);
    }

    [Fact]
    public async Task TaskCanceledException_ThrowsTimeoutException_WithFriendlyMessage()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new TaskCanceledException());
        var (service, _) = CreateService(handler);

        var ex = await Assert.ThrowsAsync<OpenAiTimeoutException>(() =>
            service.GenerateClauseBodyAsync(new Clause { BodyTemplate = "T" }, new Dictionary<string, string>()));

        Assert.Equal(TimeoutFriendlyMessage, ex.FriendlyMessage);
    }

    // ---------------------------------------------------------------- missing API key

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyApiKey_ThrowsOpenAiApiKeyMissingException(string? key)
    {
        var handler = RespondingWith(HttpStatusCode.OK, CompletionJson("OK"));

        var ex = Assert.Throws<OpenAiApiKeyMissingException>(() =>
            new OpenAiContractService(new HttpClient(handler), key!));

        Assert.Contains("Settings", ex.FriendlyMessage);
        Assert.Null(handler.LastRequest); // no HTTP call was attempted
    }

    // ---------------------------------------------------------------- connection test

    [Fact]
    public async Task TestConnectionDetailedAsync_Success_ReturnsTrue()
    {
        var handler = RespondingWith(HttpStatusCode.OK, CompletionJson("OK"));
        var (service, _) = CreateService(handler);

        var result = await service.TestConnectionDetailedAsync();

        Assert.True(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.True(await service.TestConnectionAsync());
    }

    [Fact]
    public async Task TestConnectionDetailedAsync_Http401_ReturnsFriendlyFailureReason()
    {
        var body = "{\"error\":{\"message\":\"Incorrect API key provided\",\"type\":\"invalid_request_error\",\"code\":\"invalid_api_key\"}}";
        var handler = RespondingWith(HttpStatusCode.Unauthorized, body);
        var (service, _) = CreateService(handler);

        var result = await service.TestConnectionDetailedAsync();

        Assert.False(result.Success);
        Assert.Equal(InvalidKeyFriendlyMessage, result.Message);
        Assert.False(await service.TestConnectionAsync());
    }

    // ---------------------------------------------------------------- secret redaction

    [Fact]
    public void Redact_ReplacesAnyApiKey_WithPlaceholder()
    {
        var redacted = OpenAiErrorMapper.Redact($"failure for {ApiKey} and sk-otherKey123", ApiKey);

        Assert.DoesNotContain(ApiKey, redacted);
        Assert.DoesNotContain("sk-otherKey123", redacted);
        Assert.Contains(OpenAiErrorMapper.RedactedApiKey, redacted);
    }

    [Fact]
    public async Task Logs_NeverContainTheApiKey()
    {
        var logs = new List<string>();
        var body = $"{{\"error\":{{\"message\":\"bad key {ApiKey}\",\"type\":\"invalid_request_error\",\"code\":\"invalid_api_key\"}}}}";
        var handler = RespondingWith(HttpStatusCode.Unauthorized, body);
        var client = new HttpClient(handler);
        var service = new OpenAiContractService(client, ApiKey, log: logs.Add);

        await Assert.ThrowsAsync<OpenAiInvalidKeyException>(() =>
            service.GenerateClauseBodyAsync(new Clause { BodyTemplate = "T" }, new Dictionary<string, string>()));

        Assert.NotEmpty(logs);
        Assert.All(logs, line => Assert.DoesNotContain(ApiKey, line));
    }

    // ---------------------------------------------------------------- assemble contract

    [Fact]
    public async Task AssembleContractAsync_UsesCustomOverrides_AndSkipsUnselectedAndEmptyClauses()
    {
        var handler = RespondingWith(HttpStatusCode.OK, CompletionJson("Polished contract text."));
        var (service, _) = CreateService(handler);
        var clauses = new List<ContractClause>
        {
            new() { Selected = true, CustomBodyOverride = "Custom clause text." },
            new() { Selected = false, CustomBodyOverride = "Unselected text." },
            new() { Selected = true, CustomBodyOverride = "   " }
        };

        var result = await service.AssembleContractAsync(new Contract { Title = "Test" }, clauses, new Dictionary<string, string>());

        Assert.Equal("Polished contract text.", result);
        Assert.Contains("Custom clause text.", handler.LastRequestBody);
        Assert.DoesNotContain("Unselected text.", handler.LastRequestBody);
    }

    [Fact]
    public async Task AssembleContractAsync_WithoutAnyOverride_Throws_AndSendsNoRequest()
    {
        var handler = RespondingWith(HttpStatusCode.OK, CompletionJson("should never be used"));
        var (service, _) = CreateService(handler);
        var clauses = new List<ContractClause> { new() { Selected = true, CustomBodyOverride = "" } };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AssembleContractAsync(new Contract(), clauses, new Dictionary<string, string>()));

        Assert.Null(handler.LastRequest); // the AI is never called with empty templates
    }

    [Fact]
    public async Task AssembleContractAsync_WithClauseBodies_UsesBody_WhenNoOverride()
    {
        var handler = RespondingWith(HttpStatusCode.OK, CompletionJson("Assembled text."));
        var (service, _) = CreateService(handler);
        var pairs = new List<(ContractClause ContractClause, Clause Clause)>
        {
            (new ContractClause { Selected = true }, new Clause { BodyTemplate = "Filled body text." }),
            (new ContractClause { Selected = true, CustomBodyOverride = "Override wins." }, new Clause { BodyTemplate = "Template never seen." }),
            (new ContractClause { Selected = false }, new Clause { BodyTemplate = "Unselected body." })
        };

        var result = await service.AssembleContractAsync(new Contract { Title = "Test" }, pairs, new Dictionary<string, string>());

        Assert.Equal("Assembled text.", result);
        Assert.Contains("Filled body text.", handler.LastRequestBody);
        Assert.Contains("Override wins.", handler.LastRequestBody);
        Assert.DoesNotContain("Unselected body.", handler.LastRequestBody);
        Assert.DoesNotContain("Template never seen.", handler.LastRequestBody);
    }

    // ---------------------------------------------------------------- helpers

    private static (OpenAiContractService Service, HttpClient Client) CreateService(FakeHttpMessageHandler handler, string apiKey = ApiKey)
    {
        var client = new HttpClient(handler);
        return (new OpenAiContractService(client, apiKey), client);
    }

    private static FakeHttpMessageHandler RespondingWith(HttpStatusCode status, string json)
        => new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    private static string CompletionJson(string content)
        => $"{{\"choices\":[{{\"message\":{{\"role\":\"assistant\",\"content\":\"{content}\"}}}}]}}";

    /// <summary>Fake handler returning canned responses and recording the last request for assertions.</summary>
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responder(request);
        }
    }
}
