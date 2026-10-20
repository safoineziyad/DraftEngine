using System.Threading.Tasks;
using DraftEngine.Core.Interfaces;

namespace DraftEngine.App.Services.Settings;

/// <summary>
/// Loads and saves user application settings (OpenAI key/model, contract defaults).
/// The API key is encrypted at rest; the plaintext only ever exists in <see cref="Current"/>.
/// </summary>
public interface IAppSettingsService
{
    /// <summary>The settings currently in memory (API key decrypted). Never log this object.</summary>
    AppSettings Current { get; }

    /// <summary>Raised after settings are saved or reloaded, so views can refresh.</summary>
    event Action? SettingsChanged;

    /// <summary>Re-reads settings from disk, updates <see cref="Current"/> and raises <see cref="SettingsChanged"/>.</summary>
    Task<AppSettings> ReloadAsync();

    /// <summary>Persists the given settings (the API key is encrypted before writing) and raises <see cref="SettingsChanged"/>.</summary>
    Task SaveAsync(AppSettings settings);

    /// <summary>
    /// Creates an <see cref="IContractAiService"/> configured from the saved key and model.
    /// Returns null when no API key is configured. Callers must not persist the returned key.
    /// </summary>
    Task<IContractAiService?> CreateAiServiceAsync();

    /// <summary>
    /// Pure factory: builds an AI service for an arbitrary key/model without touching saved
    /// settings. Used by Settings to test a key the user just typed (before saving).
    /// Returns null when <paramref name="apiKey"/> is empty. The key is never logged.
    /// </summary>
    IContractAiService? CreateAiService(string apiKey, string model);
}
