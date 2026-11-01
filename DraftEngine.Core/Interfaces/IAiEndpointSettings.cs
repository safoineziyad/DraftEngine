namespace DraftEngine.Core.Interfaces;

/// <summary>
/// Configurable OpenAI-compatible endpoint: base URL, model, and API key.
/// Supports Azure OpenAI, zero-retention gateways, and local models (Ollama/LM Studio).
/// OpenAI remains the default.
/// </summary>
public interface IAiEndpointSettings
{
    string BaseUrl { get; set; }
    string Model { get; set; }
    string ApiKey { get; set; }
    bool IsCustomEndpoint { get; set; }
}
