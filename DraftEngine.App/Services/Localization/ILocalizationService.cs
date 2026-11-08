using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace DraftEngine.App.Services.Localization;

/// <summary>
/// Provides language/culture aware localizable strings with live switching,
/// FlowDirection awareness, and persistence. Core services return message keys,
/// not display text; the presentation layer resolves them through this service.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Current language code (e.g. "en", "fr", "ar", "es").</summary>
    string CurrentLanguage { get; }

    /// <summary>Culture used for formatting and text direction.</summary>
    CultureInfo Culture { get; }

    /// <summary>Supported languages with native names and RTL flag.</summary>
    IReadOnlyList<SupportedLanguage> SupportedLanguages { get; }

    /// <summary>Raised when the current language changes. UI should re-bind and FlowDirection-update.</summary>
    event EventHandler? LanguageChanged;

    /// <summary>Loads the persisted language, or the Windows language, or English.</summary>
    Task InitializeAsync();

    /// <summary>Switch to a specific language. Persists the choice and raises LanguageChanged.</summary>
    Task SetLanguageAsync(string languageCode);

    /// <summary>Switch to the Windows language if it is supported; otherwise fall back to English.</summary>
    Task ApplySystemLanguageAsync();

    /// <summary>Returns a localized string for the given key. Use from presentation layer only.</summary>
    string GetString(string key);

    /// <summary>Returns a localized string for the given key with placeholders filled in.</summary>
    string GetString(string key, params object[] args);

    /// <summary>Gets the FlowDirection for the current language. Arabic and Hebrew are RightToLeft.</summary>
    FlowDirection GetFlowDirection();

    /// <summary>Gets whether the current language is RTL.</summary>
    bool IsRtl();

    /// <summary>Digit substitution used for text (European/latin digits for Arabic).</summary>
    NumberSubstitutionMethod DigitsMethod { get; }
}

/// <summary>Describes a supported language with its native name and RTL status.</summary>
public record SupportedLanguage(string Code, string NativeName, bool IsRtl);

/// <summary>Persists the user's language choice between sessions.</summary>
public interface ILanguagePreferenceStore
{
    /// <summary>Returns the stored language code, or null when none was stored.</summary>
    string? Load();

    /// <summary>Stores the language code.</summary>
    void Save(string languageCode);
}

/// <summary>Registry-backed language preference (per Windows user, no database dependency).</summary>
public sealed class RegistryLanguagePreferenceStore : ILanguagePreferenceStore
{
    private const string KeyPath = @"Software\DraftEngine\Settings";
    private const string ValueName = "Language";

    /// <inheritdoc />
    public string? Load()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) as string;
        }
        catch
        {
            // Best-effort; a locked registry must never block startup.
            return null;
        }
    }

    /// <inheritdoc />
    public void Save(string languageCode)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(KeyPath);
            key?.SetValue(ValueName, languageCode);
        }
        catch
        {
            // Best-effort persistence; never block startup.
        }
    }
}
