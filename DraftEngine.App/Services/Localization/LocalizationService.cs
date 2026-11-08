using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Windows;
using System.Windows.Media;
using Serilog;

namespace DraftEngine.App.Services.Localization;

/// <summary>
/// Implements <see cref="ILocalizationService"/> with resx-backed resources,
/// live switching, persisted preference, Windows language fallback, and
/// FlowDirection/RTL support. Never logs resource content — only key names.
/// </summary>
public class LocalizationService : ILocalizationService
{
    private static readonly ResourceManager ResourceManager = new(
        "DraftEngine.App.Resources.Strings",
        typeof(LocalizationService).GetTypeInfo().Assembly);

    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en");

    private static readonly SupportedLanguage[] Supported =
    [
        new("en", "English", false),
        new("fr", "Français", false),
        new("ar", "العربية", true),
        new("es", "Español", false)
    ];

    private readonly ILanguagePreferenceStore _store;
    private readonly Func<CultureInfo> _systemCultureProvider;
    private readonly Func<string, CultureInfo, string?>? _resourceLookup;

    private CultureInfo _culture = EnglishCulture;
    private string _currentLanguage = "en";

    /// <summary>Creates the service with an optional persistence store, system-culture probe and lookup hook.</summary>
    public LocalizationService(
        ILanguagePreferenceStore? store = null,
        Func<CultureInfo>? systemCultureProvider = null,
        Func<string, CultureInfo, string?>? resourceLookup = null)
    {
        _store = store ?? new RegistryLanguagePreferenceStore();
        _systemCultureProvider = systemCultureProvider ?? (() => CultureInfo.CurrentUICulture);
        _resourceLookup = resourceLookup;
    }

    /// <inheritdoc />
    public event EventHandler? LanguageChanged;

    /// <inheritdoc />
    public string CurrentLanguage => _currentLanguage;

    /// <inheritdoc />
    public CultureInfo Culture => _culture;

    /// <inheritdoc />
    public IReadOnlyList<SupportedLanguage> SupportedLanguages { get; } = Array.AsReadOnly(Supported);

    /// <inheritdoc />
    public NumberSubstitutionMethod DigitsMethod => NumberSubstitutionMethod.European;

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        var stored = _store.Load();
        var code = ResolveCode(stored) ?? ResolveCode(_systemCultureProvider().Name) ?? "en";
        Apply(code, persist: false);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ApplySystemLanguageAsync()
    {
        var code = ResolveCode(_systemCultureProvider().Name) ?? "en";
        return SetLanguageAsync(code);
    }

    /// <inheritdoc />
    public Task SetLanguageAsync(string languageCode)
    {
        var code = ResolveCode(languageCode);
        if (code is null)
        {
            // Unknown language: keep the current one but still notify so the picker
            // can snap back to a valid value.
            LanguageChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        Apply(code, persist: true);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public string GetString(string key) => GetString(key, Array.Empty<object>());

    /// <inheritdoc />
    public string GetString(string key, params object[] args)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        string? value = Lookup(key, _culture);

        // Fall back to the neutral/English resource when a translation is absent.
        if (string.IsNullOrEmpty(value) && !_culture.Equals(EnglishCulture))
        {
            value = Lookup(key, EnglishCulture);
        }

        if (string.IsNullOrEmpty(value))
        {
            // Log only the key name; never translated content or contract content.
            Log.Warning("Missing localization key: {Key}", key);
            return MissingKey(key);
        }

        if (args is { Length: > 0 })
        {
            try
            {
                return string.Format(_culture, value, args);
            }
            catch (FormatException)
            {
                // A malformed translation must not crash the UI; show the template.
                Log.Warning("Localization format mismatch for key: {Key}", key);
                return value;
            }
        }

        return value;
    }

    /// <inheritdoc />
    public FlowDirection GetFlowDirection() =>
        IsRtl() ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <inheritdoc />
    public bool IsRtl() => _culture.TextInfo.IsRightToLeft;

    private void Apply(string code, bool persist)
    {
        _currentLanguage = code;
        _culture = CultureInfo.GetCultureInfo(code);

        // CurrentCulture drives formatting (dates/numbers); CurrentUICulture drives
        // resource lookup. This method is synchronous on purpose: an async boundary
        // would isolate the culture change from the caller's execution context.
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _culture;
        CultureInfo.DefaultThreadCurrentCulture = _culture;
        CultureInfo.DefaultThreadCurrentUICulture = _culture;

        if (persist)
        {
            _store.Save(code);
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Normalizes a language tag to a supported two-letter code, or null.</summary>
    private static string? ResolveCode(string? languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return null;
        }

        var normalized = languageTag.Trim().Replace('_', '-').Split('-')[0].ToLowerInvariant();
        return Supported.Any(language => language.Code == normalized) ? normalized : null;
    }

    private string? Lookup(string key, CultureInfo culture)
    {
        try
        {
            return _resourceLookup is null
                ? ResourceManager.GetString(key, culture)
                : _resourceLookup(key, culture);
        }
        catch (MissingManifestResourceException)
        {
            return null;
        }
    }

    private static string MissingKey(string key)
    {
#if DEBUG
        return $"[{key}]";
#else
        return key;
#endif
    }
}
