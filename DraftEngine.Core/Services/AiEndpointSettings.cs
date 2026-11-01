using DraftEngine.Core.Interfaces;

namespace DraftEngine.Core.Services;

/// <summary>
/// Default OpenAI endpoint settings. Users can override with any OpenAI-compatible
/// server (Azure OpenAI, gateways, Ollama/LM Studio).
/// </summary>
public class AiEndpointSettings : IAiEndpointSettings
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o-mini";
    public string ApiKey { get; set; } = string.Empty;
    public bool IsCustomEndpoint { get; set; } = false;
}
