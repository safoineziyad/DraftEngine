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

public class ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<Choice> Choices { get; set; } = new();

    [JsonPropertyName("error")]
    public OpenAiError? Error { get; set; }
}

public class Choice
{
    [JsonPropertyName("message")]
    public ChatCompletionMessage Message { get; set; } = new();
}

public class OpenAiError
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}
