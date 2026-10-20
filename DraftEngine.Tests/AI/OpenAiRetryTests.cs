using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DraftEngine.AI.Exceptions;
using DraftEngine.AI.Services;
using DraftEngine.Core.Models;

namespace DraftEngine.Tests.AI;

/// <summary>
/// Unit tests for the transient-failure retry loop and the prompt-delimiting hardening of
/// <see cref="OpenAiContractService"/>. A scripted fake handler returns canned responses per
/// attempt and records every request, so the real OpenAI API is never contacted. Backoff is
/// configured with a 1 ms base delay to keep the suite fast.
/// </summary>
public class OpenAiRetryTests
{
    private const string ApiKey = "sk-test-fake";
    private const string UnavailableFriendlyMessage = "OpenAI is temporarily unavailable. Please try again later.";
    private const string InvalidKeyFriendlyMessage = "Your OpenAI API key is not valid.";
    private const string QuotaFriendlyMessage = "Your OpenAI account has no available credits.";

    // ---------------------------------------------------------------- retry succeeds

    [Fact]
    public async Task Http503_Twice_ThenSuccess_RetriesAndReturnsContent()
    {
        var handler = new ScriptedHttpMessageHandler(
            _ => Status(HttpStatusCode.ServiceUnavailable),
            _ => Status(HttpStatusCode.ServiceUnavailable),
            _ => Ok("Recovered."));
        var service = CreateService(handler);

        var result = await service.GenerateClauseBodyAsync(
            new Clause { BodyTemplate = "T" },
            new Dictionary<string, string>());

        Assert.Equal("Recovered.", result);
        Assert.Equal(3, handler.RequestCount); // initial attempt + 2 retries
    }

    [Fact]
    public async Task Http503_ThenSuccess_RetriesExactlyOnce()
    {
        var handler = new ScriptedHttpMessageHandler(
            _ => Status(HttpStatusCode.ServiceUnavailable),
            _ => Ok("Recovered."));
        var service = CreateService(handler);

        var result = await service.GenerateClauseBodyAsync(
            new Clause { BodyTemplate = "T" },
            new Dictionary<string, string>());

        Assert.Equal("Recovered.", result);
        Assert.Equal(2, handler.RequestCount); // assert the request count on the fake handler
    }

    [Fact]
    public async Task Http429_RateLimit_ThenSuccess_RetriesAndReturnsContent()
    {
        var handler = new ScriptedHttpMessageHandler(
            _ => Status((HttpStatusCode)429, "{\"error\":{\"message\":\"Rate limit reached\",\"type\":\"requests\"}}"),
            _ => Ok("Recovered."));
        var service = CreateService(handler);

        var result = await service.GenerateClauseBodyAsync(
            new Clause { BodyTemplate = "T" },
            new Dictionary<string, string>());

        Assert.Equal("Recovered.", result);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task RateLimit429_WithRetryAfterHeader_HonorsIt_AndStillSucceeds()
    {
        var retryAfter = Status((HttpStatusCode)429, "{\"error\":{\"message\":\"Rate limit reached\",\"type\":\"requests\"}}");
        retryAfter.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        var handler = new ScriptedHttpMessageHandler(
            _ => retryAfter,
            _ => Ok("Recovered."));
        var service = CreateService(handler);

        var result = await service.GenerateClauseBodyAsync(
            new Clause { BodyTemplate = "T" },
            new Dictionary<string, string>());

        Assert.Equal("Recovered.", result);
        Assert.Equal(2, handler.RequestCount);
    }

    // ---------------------------------------------------------------- no retry for permanent failures

    [Fact]
    public async Task Http401_DoesNotRetry_SendsExactlyOneRequest()
    {
        var handler = new ScriptedHttpMessageHandler(
            _ => Status(HttpStatusCode.Unauthorized,
                "{\"error\":{\"message\":\"Incorrect API key provided\",\"type\":\"invalid_request_error\",\"code\":\"invalid_api_key\"}}"));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<OpenAiInvalidKeyException>(() =>
            service.GenerateClauseBodyAsync(new Clause { BodyTemplate = "T" }, new Dictionary<string, string>()));

        Assert.Equal(InvalidKeyFriendlyMessage, ex.FriendlyMessage);
        Assert.Equal(1, handler.RequestCount); // 401 is permanent: never retried
    }

    [Fact]
    public async Task Http429_InsufficientQuota_DoesNotRetry_SendsExactlyOneRequest()
    {
        var handler = new ScriptedHttpMessageHandler(
            _ => Status((HttpStatusCode)429,
                "{\"error\":{\"message\":\"You exceeded your current quota, please check your plan and billing details.\",\"type\":\"insufficient_quota\",\"code\":\"insufficient_quota\"}}"));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<OpenAiQuotaExceededException>(() =>
            service.GenerateClauseBodyAsync(new Clause { BodyTemplate = "T" }, new Dictionary<string, string>()));

        Assert.Equal(QuotaFriendlyMessage, ex.FriendlyMessage);
        Assert.Equal(1, handler.RequestCount); // retrying exhausted credits is pointless
    }

    // ---------------------------------------------------------------- retry exhaustion

    [Fact]
    public async Task RetryExhaustion_StillThrowsTypedException_WithSameFriendlyMessage()
    {
        var handler = new ScriptedHttpMessageHandler(
            _ => Status(HttpStatusCode.ServiceUnavailable),
            _ => Status(HttpStatusCode.ServiceUnavailable),
            _ => Status(HttpStatusCode.ServiceUnavailable));
        var service = CreateService(handler, maxRetries: 2);

        var ex = await Assert.ThrowsAsync<OpenAiException>(() =>
            service.GenerateClauseBodyAsync(new Clause { BodyTemplate = "T" }, new Dictionary<string, string>()));

        Assert.Equal(UnavailableFriendlyMessage, ex.FriendlyMessage);
        Assert.Equal(3, handler.RequestCount); // 1 attempt + 2 retries, then the typed exception
    }

    [Fact]
    public async Task RetryExhaustion_OnRateLimit_StillThrowsRateLimitException()
    {
        var handler = new ScriptedHttpMessageHandler(
            _ => Status((HttpStatusCode)429, "{\"error\":{\"message\":\"Rate limit reached\",\"type\":\"requests\"}}"),
            _ => Status((HttpStatusCode)429, "{\"error\":{\"message\":\"Rate limit reached\",\"type\":\"requests\"}}"));
        var service = CreateService(handler, maxRetries: 1);

        var ex = await Assert.ThrowsAsync<OpenAiRateLimitException>(() =>
            service.GenerateClauseBodyAsync(new Clause { BodyTemplate = "T" }, new Dictionary<string, string>()));

        Assert.Equal("Too many requests — please wait a moment and try again.", ex.FriendlyMessage);
        Assert.Equal(2, handler.RequestCount);
    }

    // ---------------------------------------------------------------- prompt-injection delimiters

    [Fact]
    public async Task ApplyChatEditAsync_SerializesDelimiters_AroundContractAndUserInstruction()
    {
        var handler = new ScriptedHttpMessageHandler(_ => Ok("Edited contract."));
        var service = CreateService(handler);

        await service.ApplyChatEditAsync(
            new Contract { Title = "Test" },
            currentDocument: "The current contract text.",
            userInstruction: "Fix clause 2.");

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("<<<CONTRACT_DOCUMENT>>>", handler.LastRequestBody);
        Assert.Contains("<<<END_CONTRACT_DOCUMENT>>>", handler.LastRequestBody);
        Assert.Contains("The current contract text.", handler.LastRequestBody);
        Assert.Contains("<<<USER_INSTRUCTION>>>", handler.LastRequestBody);
        Assert.Contains("<<<END_USER_INSTRUCTION>>>", handler.LastRequestBody);
        Assert.Contains("Fix clause 2.", handler.LastRequestBody);
    }

    [Fact]
    public async Task GenerateClauseBodyAsync_SerializesDelimiters_AroundTemplateAndFieldValues()
    {
        var handler = new ScriptedHttpMessageHandler(_ => Ok("Clause body."));
        var service = CreateService(handler);

        await service.GenerateClauseBodyAsync(
            new Clause { BodyTemplate = "Template text." },
            new Dictionary<string, string> { ["PartyA"] = "Acme" });

        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("<<<CLAUSE_TEMPLATE>>>", handler.LastRequestBody);
        Assert.Contains("<<<END_CLAUSE_TEMPLATE>>>", handler.LastRequestBody);
        Assert.Contains("<<<FIELD_VALUES>>>", handler.LastRequestBody);
        Assert.Contains("<<<END_FIELD_VALUES>>>", handler.LastRequestBody);
    }

    // ---------------------------------------------------------------- header & logging safety across retries

    [Fact]
    public async Task Retries_EveryRequestKeepsAuthorizationHeader_AndLogsNeverContainTheKey()
    {
        var logs = new List<string>();
        var handler = new ScriptedHttpMessageHandler(
            _ => Status(HttpStatusCode.BadGateway),
            _ => Ok("Recovered."));
        var client = new HttpClient(handler);
        var service = new OpenAiContractService(
            client, ApiKey, log: logs.Add, maxRetries: 1, retryBaseDelay: TimeSpan.FromMilliseconds(1));

        await service.GenerateClauseBodyAsync(
            new Clause { BodyTemplate = "T" },
            new Dictionary<string, string>());

        Assert.Equal(2, handler.RequestCount);
        Assert.All(handler.Requests, request =>
        {
            var auth = request.Headers.Authorization;
            Assert.NotNull(auth);
            Assert.Equal("Bearer", auth!.Scheme);
            Assert.Equal(ApiKey, auth.Parameter);
        });
        Assert.NotEmpty(logs);
        Assert.All(logs, line => Assert.DoesNotContain(ApiKey, line));
    }

    // ---------------------------------------------------------------- helpers

    private static OpenAiContractService CreateService(HttpMessageHandler handler, int maxRetries = 2)
        => new(new HttpClient(handler), ApiKey, maxRetries: maxRetries, retryBaseDelay: TimeSpan.FromMilliseconds(1));

    private static HttpResponseMessage Ok(string content)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{{\"choices\":[{{\"message\":{{\"role\":\"assistant\",\"content\":\"{content}\"}}}}]}}",
                Encoding.UTF8,
                "application/json")
        };

    private static HttpResponseMessage Status(HttpStatusCode status, string body = "{}")
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>
    /// Fake handler serving one canned response per request (the last one repeats) and recording
    /// every request so tests can assert on the request count, headers and serialized bodies.
    /// </summary>
    private sealed class ScriptedHttpMessageHandler : HttpMessageHandler
    {
        private readonly List<Func<HttpRequestMessage, HttpResponseMessage>> _responders;

        public ScriptedHttpMessageHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responders)
            => _responders = responders.ToList();

        public List<HttpRequestMessage> Requests { get; } = new();
        public int RequestCount => Requests.Count;
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var index = Math.Min(Requests.Count - 1, _responders.Count - 1);
            return _responders[index](request);
        }
    }
}
