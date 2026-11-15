using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DraftEngine.AI.Exceptions;
using DraftEngine.Core.Models;

namespace DraftEngine.AI.Services;

/// <summary>Machine-readable connection failure categories; the UI maps these to localized text.</summary>
public enum AiConnectionErrorKind
{
    None,
    InvalidKey,
    AccessDenied,
    QuotaExceeded,
    RateLimited,
    Network,
    EndpointNotFound,
    ModelNotFound,
    ServerError,
    Unknown,
}

/// <summary>Outcome of a connection test, including the models the endpoint advertised.</summary>
public sealed class AiConnectionResult
{
    public bool Success { get; init; }
    public AiConnectionErrorKind Error { get; init; }
    /// <summary>User-safe English sentence; never contains the key or a raw response body.</summary>
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> Models { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Talks to a provider's model-list endpoint (<c>GET {base}/models</c>) and verifies a model is
/// usable. Handles OpenAI-compatible <c>data[].id</c> and Ollama <c>models[].name</c> shapes.
/// Keys are never logged; failures surface only a category and a safe sentence.
/// </summary>
public sealed class AiProviderClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly Action<string>? _log;

    /// <summary>Creates the client. The caller owns the <see cref="HttpClient"/> (usually from IHttpClientFactory).</summary>
    public AiProviderClient(HttpClient httpClient, Action<string>? log = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _log = log;
    }

    /// <summary>Lists the models the endpoint advertises, sorted for display and de-duplicated.</summary>
    /// <exception cref="OpenAiException">A user-safe failure when the endpoint cannot be reached or refuses access.</exception>
    public async Task<IReadOnlyList<string>> GetModelsAsync(AiProfile profile, CancellationToken cancellationToken = default)
    {
        var result = await QueryModelsAsync(profile, cancellationToken);
        if (!result.Success)
        {
            throw new OpenAiException(result.Message);
        }

        return result.Models;
    }

    /// <summary>
    /// Tests a profile end to end: lists its models, verifies the selected model is advertised when
    /// the endpoint publishes a list, and falls back to a minimal chat request for providers that do
    /// not expose <c>/models</c> (Azure deployments). Never throws.
    /// </summary>
    public async Task<AiConnectionResult> TestConnectionAsync(
        AiProfile profile, string? model = null, CancellationToken cancellationToken = default)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));

        var effectiveModel = string.IsNullOrWhiteSpace(model) ? profile.Model?.Trim() ?? string.Empty : model.Trim();

        var models = await QueryModelsAsync(profile, cancellationToken);
        if (models.Success)
        {
            var listed = models.Models;
            if (effectiveModel.Length == 0)
            {
                return new AiConnectionResult
                {
                    Success = true,
                    Message = listed.Count == 0
                        ? "Connected. The endpoint did not list any models; enter a model id manually."
                        : $"Connected. {listed.Count} model(s) available.",
                    Models = listed,
                };
            }

            if (listed.Count > 0 && !listed.Contains(effectiveModel, StringComparer.OrdinalIgnoreCase))
            {
                return new AiConnectionResult
                {
                    Error = AiConnectionErrorKind.ModelNotFound,
                    Message = $"The endpoint is reachable, but the model \"{effectiveModel}\" is not in its model list. " +
                              "Pick one of the listed models or enter the exact id.",
                    Models = listed,
                };
            }

            return new AiConnectionResult
            {
                Success = true,
                Message = $"Connected with model \"{effectiveModel}\".",
                Models = listed,
            };
        }

        // A missing model list can be legitimate (Azure exposes models only per deployment) or a
        // wrong URL. Distinguish by trying one tiny chat request when a model is configured.
        var listError = models.Error;
        if (effectiveModel.Length == 0 && listError is AiConnectionErrorKind.EndpointNotFound or AiConnectionErrorKind.Unknown)
        {
            return new AiConnectionResult
            {
                Error = listError,
                Message = models.Message,
            };
        }

        if (listError is AiConnectionErrorKind.InvalidKey or AiConnectionErrorKind.AccessDenied or
            AiConnectionErrorKind.QuotaExceeded or AiConnectionErrorKind.RateLimited or
            AiConnectionErrorKind.Network or AiConnectionErrorKind.ServerError or
            AiConnectionErrorKind.ModelNotFound)
        {
            return new AiConnectionResult
            {
                Error = listError,
                Message = models.Message,
            };
        }

        var chat = await PingChatAsync(profile, effectiveModel, cancellationToken);
        return new AiConnectionResult
        {
            Success = chat.Success,
            Error = chat.Error,
            Message = chat.Message,
            Models = models.Models,
        };
    }

    /// <summary>Queries <c>models</c> and maps every failure to a category plus a safe sentence.</summary>
    private async Task<AiConnectionResult> QueryModelsAsync(AiProfile profile, CancellationToken cancellationToken)
    {
        Uri? baseUri;
        try
        {
            baseUri = BuildBaseUri(profile);
        }
        catch (UriFormatException)
        {
            return Fail(AiConnectionErrorKind.EndpointNotFound,
                "The base URL is not a valid http(s) address. Check the provider settings.");
        }

        if (baseUri is null)
        {
            return Fail(AiConnectionErrorKind.EndpointNotFound,
                "This provider needs a base URL, for example https://api.openai.com/v1/.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "models"));
        ApplyAuthentication(request, profile);

        _log?.Invoke($"GET {Sensitive(profile)}/models");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            using var response = await _httpClient.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                return Fail(ClassifyStatus(response.StatusCode, body),
                    MessageForStatus(response.StatusCode, body));
            }

            var models = ParseModels(body);
            _log?.Invoke($"models -> {(int)response.StatusCode} ({models.Count} entries)");
            return new AiConnectionResult { Success = true, Models = models };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail(AiConnectionErrorKind.Network,
                "The AI provider did not respond in time. Check the endpoint and your connection.");
        }
        catch (HttpRequestException ex)
        {
            _log?.Invoke($"models failed: {ex.GetType().Name}");
            return Fail(AiConnectionErrorKind.Network,
                "Could not reach the AI provider. Check the base URL and your network connection.");
        }
    }

    /// <summary>Minimal chat request used to verify deployments that do not publish a model list.</summary>
    private async Task<AiConnectionResult> PingChatAsync(AiProfile profile, string model, CancellationToken cancellationToken)
    {
        if (model.Length == 0)
        {
            return Fail(AiConnectionErrorKind.EndpointNotFound,
                "The endpoint does not publish a model list and no model id is set. Check the base URL or enter a model id.");
        }

        var baseUri = BuildBaseUri(profile)!;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "chat/completions"))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    model,
                    messages = new[] { new { role = "user", content = "ping" } },
                    max_tokens = 1,
                }, JsonOptions),
                Encoding.UTF8,
                "application/json"),
        };
        ApplyAuthentication(request, profile);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                return Fail(ClassifyStatus(response.StatusCode, body), MessageForStatus(response.StatusCode, body));
            }

            return new AiConnectionResult { Success = true, Message = $"Connected with model \"{model}\"." };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail(AiConnectionErrorKind.Network,
                "The AI provider did not respond in time. Check the endpoint and your connection.");
        }
        catch (HttpRequestException)
        {
            return Fail(AiConnectionErrorKind.Network,
                "Could not reach the AI provider. Check the base URL and your network connection.");
        }
    }

    private static Uri? BuildBaseUri(AiProfile profile)
    {
        var raw = profile.EffectiveBaseUrl;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!Uri.TryCreate(raw.EndsWith('/') ? raw : raw + "/", UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new UriFormatException("Base URL must be an absolute http(s) URL.");
        }

        return uri;
    }

    private static void ApplyAuthentication(HttpRequestMessage request, AiProfile profile)
    {
        var key = profile.ApiKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (profile.Preset == AiProviderPreset.AzureOpenAi)
        {
            request.Headers.TryAddWithoutValidation("api-key", key);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }
    }

    /// <summary>Reads model ids from either OpenAI (<c>data[].id</c>) or Ollama (<c>models[].name</c>) shapes.</summary>
    internal static IReadOnlyList<string> ParseModels(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var ids = new List<string>();

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in data.EnumerateArray())
                {
                    AddModel(ids, entry, "id");
                }
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("models", out var ollama) &&
                ollama.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in ollama.EnumerateArray())
                {
                    AddModel(ids, entry, "name");
                    AddModel(ids, entry, "id");
                }
            }

            return ids
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static void AddModel(List<string> ids, JsonElement entry, string property)
    {
        if (entry.ValueKind == JsonValueKind.Object &&
            entry.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString()))
        {
            ids.Add(value.GetString()!.Trim());
        }
    }

    private static AiConnectionErrorKind ClassifyStatus(HttpStatusCode status, string? body) => status switch
    {
        HttpStatusCode.Unauthorized => AiConnectionErrorKind.InvalidKey,
        HttpStatusCode.Forbidden => AiConnectionErrorKind.AccessDenied,
        HttpStatusCode.TooManyRequests => OpenAiErrorMapper.LooksLikeQuotaError(body)
            ? AiConnectionErrorKind.QuotaExceeded
            : AiConnectionErrorKind.RateLimited,
        HttpStatusCode.NotFound => LooksLikeModelError(body)
            ? AiConnectionErrorKind.ModelNotFound
            : AiConnectionErrorKind.EndpointNotFound,
        HttpStatusCode.BadRequest => LooksLikeModelError(body)
            ? AiConnectionErrorKind.ModelNotFound
            : AiConnectionErrorKind.EndpointNotFound,
        HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => AiConnectionErrorKind.ServerError,
        _ => AiConnectionErrorKind.Unknown,
    };

    private static string MessageForStatus(HttpStatusCode status, string? body) => ClassifyStatus(status, body) switch
    {
        AiConnectionErrorKind.InvalidKey => "The API key was rejected. Check the key for this provider.",
        AiConnectionErrorKind.AccessDenied => "This key does not have access to the endpoint or model.",
        AiConnectionErrorKind.QuotaExceeded => "The account is out of quota or billing is not set up.",
        AiConnectionErrorKind.RateLimited => "The provider is rate limiting requests. Try again shortly.",
        AiConnectionErrorKind.ModelNotFound => "The endpoint is reachable, but the selected model was not found.",
        AiConnectionErrorKind.EndpointNotFound =>
            "The endpoint was not found. Check the base URL — it usually ends with /v1/.",
        AiConnectionErrorKind.ServerError => "The AI provider is temporarily unavailable. Try again later.",
        _ => "The AI provider rejected the request. Check the base URL and settings.",
    };

    private static bool LooksLikeModelError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        return body.Contains("model", StringComparison.OrdinalIgnoreCase);
    }

    private static AiConnectionResult Fail(AiConnectionErrorKind kind, string message) =>
        new() { Error = kind, Message = message };

    /// <summary>Host/path only, for diagnostics — never the key and never query strings.</summary>
    private static string Sensitive(AiProfile profile)
    {
        var uri = profile.EffectiveBaseUrl;
        return string.IsNullOrWhiteSpace(uri) ? "(unset)" : uri.TrimEnd('/');
    }
}
