using System;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DraftEngine.AI.Exceptions;

/// <summary>
/// Translates HTTP status codes and transport exceptions into typed <see cref="OpenAiException"/>s
/// with user-safe messages. Raw response bodies are never surfaced to the UI, and any text that
/// might be logged is scrubbed of API keys.
/// </summary>
public static class OpenAiErrorMapper
{
    /// <summary>Replacement text used wherever an API key would otherwise appear.</summary>
    public const string RedactedApiKey = "sk-***REDACTED***";

    private static readonly Regex ApiKeyPattern = new(@"sk-[A-Za-z0-9_\-]{2,}", RegexOptions.Compiled);

    /// <summary>Maps a failed OpenAI HTTP response to a typed exception. Only safe metadata (status, error type/code) is kept.</summary>
    public static OpenAiException FromHttpError(HttpStatusCode statusCode, string? responseBody, string? apiKey)
    {
        var metadata = DescribeErrorMetadata(responseBody);
        var technical = Redact($"OpenAI API returned {(int)statusCode} ({statusCode}).{metadata}", apiKey);

        return (int)statusCode switch
        {
            401 => new OpenAiInvalidKeyException(technical),
            403 => new OpenAiException("Your OpenAI account does not have access to this model.", technical),
            404 => new OpenAiException("The configured AI model was not found. Check your AI settings.", technical),
            429 => LooksLikeQuotaError(responseBody)
                ? new OpenAiQuotaExceededException(technical)
                : new OpenAiRateLimitException(technical),
            >= 500 => new OpenAiException("OpenAI is temporarily unavailable. Please try again later.", technical),
            _ => new OpenAiException("OpenAI could not process the request. Please try again.", technical)
        };
    }

    /// <summary>Maps a transport, timeout or unexpected exception to a typed, user-safe OpenAI exception.</summary>
    public static OpenAiException FromException(Exception exception, string? apiKey)
    {
        return exception switch
        {
            OpenAiException openAi => openAi,
            TaskCanceledException => new OpenAiTimeoutException(Redact(exception.Message, apiKey)),
            OperationCanceledException => new OpenAiTimeoutException(Redact(exception.Message, apiKey)),
            TimeoutException => new OpenAiTimeoutException(Redact(exception.Message, apiKey)),
            SocketException => new OpenAiNetworkException(Redact(exception.Message, apiKey)),
            HttpRequestException => new OpenAiNetworkException(Redact(exception.Message, apiKey)),
            _ => new OpenAiException("Something went wrong while contacting OpenAI. Please try again.", Redact(exception.Message, apiKey))
        };
    }

    /// <summary>Replaces the configured key and any other sk-... token in <paramref name="text"/> with <see cref="RedactedApiKey"/>.</summary>
    public static string Redact(string? text, string? apiKey)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var redacted = text;
        if (!string.IsNullOrEmpty(apiKey))
            redacted = redacted.Replace(apiKey, RedactedApiKey, StringComparison.Ordinal);

        return ApiKeyPattern.Replace(redacted, RedactedApiKey);
    }

    /// <summary>Detects insufficient-quota / billing errors by inspecting only the error type, code and message internally.</summary>
    private static bool LooksLikeQuotaError(string? body)
    {
        if (!TryReadError(body, out var error)) return false;

        var haystack = $"{ReadString(error, "type")} {ReadString(error, "code")} {ReadString(error, "message")}"
            .ToLowerInvariant();

        return haystack.Contains("quota") || haystack.Contains("billing");
    }

    /// <summary>Extracts only the machine-readable error type/code for diagnostics — never the free-text message.</summary>
    private static string DescribeErrorMetadata(string? body)
    {
        if (!TryReadError(body, out var error)) return string.Empty;

        var type = ReadString(error, "type");
        var code = ReadString(error, "code");
        if (type.Length == 0 && code.Length == 0) return string.Empty;

        return $" error_type={type}; error_code={code}.";
    }

    private static bool TryReadError(string? body, out JsonElement error)
    {
        error = default;
        if (string.IsNullOrWhiteSpace(body)) return false;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("error", out var errorElement) ||
                errorElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // Clone detaches the element from the document so it stays usable after disposal.
            error = errorElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            error = default;
            return false;
        }
    }

    private static string ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
