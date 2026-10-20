using System;

namespace DraftEngine.AI.Exceptions;

/// <summary>
/// Base exception for OpenAI failures. <see cref="FriendlyMessage"/> always contains a
/// user-safe sentence that can be shown directly in the UI; the technical message never
/// contains response bodies, headers or the API key.
/// </summary>
public class OpenAiException : Exception
{
    /// <summary>Gets the user-safe sentence describing the failure.</summary>
    public string FriendlyMessage { get; }

    /// <summary>Creates an OpenAI exception with a user-safe message.</summary>
    public OpenAiException(string friendlyMessage, string? technicalMessage = null, Exception? innerException = null)
        : base(technicalMessage ?? friendlyMessage, innerException)
    {
        FriendlyMessage = friendlyMessage;
    }
}

/// <summary>Thrown when the configured OpenAI API key is null or empty. The UI uses this type to guide the user to Settings.</summary>
public class OpenAiApiKeyMissingException : OpenAiException
{
    /// <summary>Creates the exception with the default Settings guidance message.</summary>
    public OpenAiApiKeyMissingException(string? technicalMessage = null)
        : base("Your OpenAI API key is missing. Add it in Settings before using AI features.", technicalMessage)
    {
    }
}

/// <summary>Thrown when OpenAI rejects the API key (HTTP 401).</summary>
public class OpenAiInvalidKeyException : OpenAiException
{
    /// <summary>Creates the exception with the default invalid-key message.</summary>
    public OpenAiInvalidKeyException(string? technicalMessage = null)
        : base("Your OpenAI API key is not valid.", technicalMessage)
    {
    }
}

/// <summary>Thrown when the OpenAI account has no credits left (HTTP 429 with insufficient_quota / billing).</summary>
public class OpenAiQuotaExceededException : OpenAiException
{
    /// <summary>Creates the exception with the default out-of-credits message.</summary>
    public OpenAiQuotaExceededException(string? technicalMessage = null)
        : base("Your OpenAI account has no available credits.", technicalMessage)
    {
    }
}

/// <summary>Thrown when OpenAI rate-limits requests (HTTP 429, not quota related).</summary>
public class OpenAiRateLimitException : OpenAiException
{
    /// <summary>Creates the exception with the default rate-limit message.</summary>
    public OpenAiRateLimitException(string? technicalMessage = null)
        : base("Too many requests — please wait a moment and try again.", technicalMessage)
    {
    }
}

/// <summary>Thrown when the OpenAI endpoint cannot be reached (network, DNS or socket failure).</summary>
public class OpenAiNetworkException : OpenAiException
{
    /// <summary>Creates the exception with the default connectivity message.</summary>
    public OpenAiNetworkException(string? technicalMessage = null)
        : base("Could not reach OpenAI. Check your internet connection.", technicalMessage)
    {
    }
}

/// <summary>Thrown when a request to OpenAI takes too long.</summary>
public class OpenAiTimeoutException : OpenAiException
{
    /// <summary>Creates the exception with the default timeout message.</summary>
    public OpenAiTimeoutException(string? technicalMessage = null)
        : base("The request took too long. Please try again.", technicalMessage)
    {
    }
}
