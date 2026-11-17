using System;

namespace DraftEngine.Core.Models;

/// <summary>
/// Kinds of OpenAI-compatible providers the app can talk to. Only the transport details differ
/// (base URL shape, authentication header); requests use the same chat-completions payload.
/// </summary>
public enum AiProviderPreset
{
    /// <summary>Any OpenAI-compatible endpoint (OpenAI itself, gateways, hosted proxies).</summary>
    OpenAiCompatible,

    /// <summary>Azure OpenAI: <c>api-key</c> header and a per-deployment path in the base URL.</summary>
    AzureOpenAi,

    /// <summary>Ollama, usually http://localhost:11434/v1/ — no key required.</summary>
    Ollama,

    /// <summary>LM Studio, usually http://localhost:1234/v1/ — no key required.</summary>
    LmStudio,

    /// <summary>Custom endpoint; behaves like <see cref="OpenAiCompatible"/> but has no default URL.</summary>
    Custom,
}

/// <summary>
/// One configured AI provider. <see cref="ApiKey"/> is plaintext in memory only; on disk it is
/// encrypted per profile. Profiles never carry pricing or hardcoded model names — the model id is
/// whatever the provider lists or the user types.
/// </summary>
public sealed class AiProfile
{
    /// <summary>Stable identifier used to remember the active profile.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>User-facing name, never sent to the provider.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Transport details for this provider.</summary>
    public AiProviderPreset Preset { get; set; } = AiProviderPreset.OpenAiCompatible;

    /// <summary>Configured base URL; empty means the preset's default.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Chat model id, always user- or provider-supplied.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>API key (plaintext in memory only; encrypted at rest). Empty for local servers.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>True when the preset normally runs without an API key.</summary>
    public bool AllowsMissingKey => Preset is AiProviderPreset.Ollama or AiProviderPreset.LmStudio;

    /// <summary>The base URL to call, falling back to the preset default when none is set.</summary>
    public string EffectiveBaseUrl
    {
        get
        {
            var configured = BaseUrl?.Trim();
            if (!string.IsNullOrEmpty(configured))
            {
                return configured;
            }

            return DefaultBaseUrl(Preset);
        }
    }

    /// <summary>Name of the header carrying the key: Azure uses <c>api-key</c>, everyone else Bearer.</summary>
    public string ApiKeyHeader => Preset == AiProviderPreset.AzureOpenAi ? "api-key" : "Authorization";

    /// <summary>Default base URL for a preset; empty for custom endpoints (the user must supply one).</summary>
    public static string DefaultBaseUrl(AiProviderPreset preset) => preset switch
    {
        AiProviderPreset.OpenAiCompatible => "https://api.openai.com/v1/",
        AiProviderPreset.Ollama => "http://localhost:11434/v1/",
        AiProviderPreset.LmStudio => "http://localhost:1234/v1/",
        _ => string.Empty,
    };

    /// <summary>Creates a deep copy (used so editing a profile does not mutate the saved one).</summary>
    public AiProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        Preset = Preset,
        BaseUrl = BaseUrl,
        Model = Model,
        ApiKey = ApiKey,
    };
}
