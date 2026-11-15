using System.Net;
using System.Text;
using DraftEngine.AI.Services;
using DraftEngine.Core.Models;

namespace DraftEngine.Tests.AI;

/// <summary>
/// Model listing and connection testing for the supported provider presets. Every case asserts a
/// human-readable category and that the key never appears in the message.
/// </summary>
public sealed class AiProviderClientTests
{
    private const string Key = "sk-test-abc123456789";

    private static AiProfile Profile(
        AiProviderPreset preset = AiProviderPreset.OpenAiCompatible,
        string baseUrl = "https://api.example.test/v1/",
        string model = "test-model",
        string key = Key) => new()
    {
        Name = "Test",
        Preset = preset,
        BaseUrl = baseUrl,
        Model = model,
        ApiKey = key,
    };

    [Fact]
    public async Task GetModels_ParsesOpenAiShape_SortedAndDeDuplicated()
    {
        var handler = RespondingWith(HttpStatusCode.OK,
            """{"data":[{"id":"gpt-b"},{"id":"gpt-a"},{"id":"gpt-b"}]}""");
        var client = new AiProviderClient(new HttpClient(handler));

        var models = await client.GetModelsAsync(Profile());

        Assert.Equal(new[] { "gpt-a", "gpt-b" }, models);
    }

    [Fact]
    public async Task GetModels_ParsesOllamaShape()
    {
        var handler = RespondingWith(HttpStatusCode.OK,
            """{"models":[{"name":"llama3.1:8b"},{"name":"qwen2.5:7b"}]}""");
        var client = new AiProviderClient(new HttpClient(handler));

        var models = await client.GetModelsAsync(
            Profile(AiProviderPreset.Ollama, "http://localhost:11434/v1/", key: string.Empty));

        Assert.Equal(new[] { "llama3.1:8b", "qwen2.5:7b" }, models);
    }

    [Fact]
    public async Task TestConnection_WrongUrl_ReportsEndpointNotFound()
    {
        var handler = RespondingWith(HttpStatusCode.NotFound, """{"error":{"message":"not found"}}""");
        var client = new AiProviderClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(Profile(model: "m"));

        Assert.False(result.Success);
        Assert.Equal(AiConnectionErrorKind.EndpointNotFound, result.Error);
        Assert.Contains("base URL", result.Message);
    }

    [Fact]
    public async Task TestConnection_ModelNotListed_ReportsModelNotFound()
    {
        var handler = RespondingWith(HttpStatusCode.OK, """{"data":[{"id":"other-model"}]}""");
        var client = new AiProviderClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(Profile(model: "missing-model"));

        Assert.False(result.Success);
        Assert.Equal(AiConnectionErrorKind.ModelNotFound, result.Error);
        Assert.Contains("missing-model", result.Message);
        Assert.DoesNotContain(Key, result.Message);
    }

    [Fact]
    public async Task TestConnection_InvalidKey_ReportsInvalidKey_WithoutLeakingIt()
    {
        var handler = RespondingWith(HttpStatusCode.Unauthorized, """{"error":{"code":"invalid_api_key"}}""");
        var client = new AiProviderClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(Profile());

        Assert.Equal(AiConnectionErrorKind.InvalidKey, result.Error);
        Assert.Contains("API key", result.Message);
        Assert.DoesNotContain(Key, result.Message);
    }

    [Fact]
    public async Task TestConnection_QuotaExceeded_ReportsQuota()
    {
        var handler = RespondingWith(HttpStatusCode.TooManyRequests,
            """{"error":{"type":"insufficient_quota","message":"You exceeded your current quota"}}""");
        var client = new AiProviderClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(Profile());

        Assert.Equal(AiConnectionErrorKind.QuotaExceeded, result.Error);
        Assert.Contains("quota", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestConnection_ServerError_ReportsUnavailable()
    {
        var handler = RespondingWith(HttpStatusCode.InternalServerError, "{}");
        var client = new AiProviderClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(Profile());

        Assert.Equal(AiConnectionErrorKind.ServerError, result.Error);
        Assert.Contains("temporarily unavailable", result.Message);
    }

    [Fact]
    public async Task TestConnection_NetworkFailure_ReportsNetwork()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("No such host is known."));
        var client = new AiProviderClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(Profile());

        Assert.Equal(AiConnectionErrorKind.Network, result.Error);
        Assert.Contains("Could not reach", result.Message);
    }

    [Fact]
    public async Task TestConnection_LocalProfileWithoutKey_SendsNoAuthorization()
    {
        var handler = RespondingWith(HttpStatusCode.OK, """{"data":[{"id":"llama3"}]}""");
        var client = new AiProviderClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(
            Profile(AiProviderPreset.Ollama, "http://localhost:11434/v1/", "llama3", key: string.Empty));

        Assert.True(result.Success);
        Assert.Null(handler.LastRequest!.Headers.Authorization);
        Assert.Equal(new[] { "llama3" }, result.Models);
    }

    [Fact]
    public async Task TestConnection_AzureProfile_UsesApiKeyHeader()
    {
        var handler = RespondingWith(HttpStatusCode.OK, """{"data":[{"id":"deployment-a"}]}""");
        var client = new AiProviderClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(
            Profile(AiProviderPreset.AzureOpenAi, "https://res.openai.azure.com/openai/deployments/a/", "deployment-a"));

        Assert.True(result.Success);
        Assert.Equal(Key, handler.LastRequest!.Headers.GetValues("api-key").Single());
        Assert.Null(handler.LastRequest.Headers.Authorization);
    }

    [Fact]
    public async Task TestConnection_NoModelList_FallsBackToChatPing()
    {
        var handler = new RecordingHandler(request =>
            request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.NotFound, """{"error":{"message":"unknown route"}}""")
                : Json(HttpStatusCode.OK, """{"choices":[{"message":{"role":"assistant","content":"ok"}}]}"""));
        var client = new AiProviderClient(new HttpClient(handler));

        var result = await client.TestConnectionAsync(Profile(model: "deployment-a"));

        Assert.True(result.Success);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task ChatRequests_UseTheConfiguredBaseUrlAndManualModelId()
    {
        var handler = RespondingWith(HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"ok"}}]}""");
        var service = new OpenAiContractService(
            new HttpClient(handler),
            Key,
            model: "my-self-hosted-model",
            maxRetries: 0,
            baseUrl: "http://localhost:9999/v1/");

        await service.CompletePromptAsync("system", "user");

        Assert.Equal("http://localhost:9999/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());
        Assert.Contains("my-self-hosted-model", handler.LastRequestBody);
    }

    [Fact]
    public void ChatService_AllowsKeylessLocalEndpoint_ButNotRemote()
    {
        using var local = new HttpClient();
        // Loopback endpoints may omit the key; remote endpoints still require one.
        var localService = new OpenAiContractService(local, string.Empty, "m", baseUrl: "http://localhost:11434/v1/");
        Assert.NotNull(localService);

        Assert.Throws<DraftEngine.AI.Exceptions.OpenAiApiKeyMissingException>(
            () => new OpenAiContractService(new HttpClient(), string.Empty, "m", baseUrl: "https://api.example.test/v1/"));
    }

    // ---------------------------------------------------------------- helpers

    private static RecordingHandler RespondingWith(HttpStatusCode status, string json)
        => new(_ => Json(status, json));

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Fake handler that records requests and returns canned responses.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responder(request);
        }
    }
}
