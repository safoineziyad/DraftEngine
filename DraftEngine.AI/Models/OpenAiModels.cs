using System.Text.Json.Serialization;

namespace DraftEngine.AI.Models;

/// <summary>
/// OpenAI Chat Completion request models.
/// </summary>
public class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<ChatCompletionMessage> Messages { get; set; } = new();

    [JsonPropertyName("temperature")]
    public float Temperature { get; set; } = 0.2f;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 4096;
}

/// <summary>
/// A single chat message exchanged with the model. Named <c>ChatCompletionMessage</c>
/// (not <c>ChatMessage</c>) to avoid clashing with <c>DraftEngine.Core.Models.ChatMessage</c>.
/// </summary>
public class ChatCompletionMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// OpenAI Chat Completion response envelope: the list of generated choices and, on error
/// responses, the error payload returned by the API.
/// </summary>
public class ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<Choice> Choices { get; set; } = new();

    [JsonPropertyName("error")]
    public OpenAiError? Error { get; set; }
}

/// <summary>
/// A single generated completion choice; the assistant message carries the model output.
/// </summary>
public class Choice
{
    [JsonPropertyName("message")]
    public ChatCompletionMessage Message { get; set; } = new();
}

/// <summary>
/// Error payload returned by the OpenAI API inside a failed response body.
/// </summary>
public class OpenAiError
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}
